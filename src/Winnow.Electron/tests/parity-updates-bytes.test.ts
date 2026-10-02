import { afterEach, describe, expect, it, vi } from 'vitest'
import { createServer, request, type Server, type RequestOptions } from 'node:http'
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { createHash } from 'node:crypto'
import { CancellationToken, type UpdateInfo } from 'builder-util-runtime'
import type { AppAdapter } from 'electron-updater/out/AppAdapter'
import { Provider } from 'electron-updater/out/providers/Provider'
import {
  createUpdateDriver,
  ManagedNsisUpdater,
  ConfirmedAppImageUpdater,
} from '../src/main/electron-update-driver'
import { electronAssetName } from '../src/main/update-policy'
import { UpdateHttpExecutor } from '../src/main/update-http'
import { ApplicationUpdater } from '../src/main/application-updater'
import { quitDrain } from '../src/main/quit-drain'

const payload = Buffer.from('isolated test installer bytes; never executed')
const checksum = createHash('sha512').update(payload).digest('base64')
const metadata: UpdateInfo = {
  version: '2.0.0',
  releaseDate: '2026-09-29',
  path: '',
  sha512: '',
  files: [{ url: 'Winnow-Electron-2.0.0-win-x64-Setup.exe', sha512: checksum, size: payload.length }],
}
const resources: { directory: string; server: Server }[] = []
afterEach(async () => {
  vi.unstubAllEnvs()
  for (const { directory, server } of resources.splice(0)) {
    server.closeAllConnections()
    await new Promise<void>((resolve) => server.close(() => resolve()))
    await rm(directory, { recursive: true, force: true })
  }
})
class FixtureTransport extends UpdateHttpExecutor {
  urls: string[] = []
  constructor(private port: number) {
    super()
  }
  // Only the socket destination is substituted. The production downloader, redirect gate,
  // stream checksum, cancellation token and installer cache still execute.
  override createRequest(options: RequestOptions, callback: (response: any) => void): Electron.ClientRequest {
    this.urls.push(`${options.protocol}//${options.hostname}${options.path}`)
    const native = request(
      {
        hostname: '127.0.0.1',
        port: this.port,
        path: options.path,
        method: options.method,
        headers: options.headers,
      },
      callback,
    )
    return native as unknown as Electron.ClientRequest
  }
}
class FixtureProvider extends Provider<UpdateInfo> {
  constructor(
    private info: UpdateInfo,
    executor: FixtureTransport,
  ) {
    super({ platform: 'win32', executor, isUseMultipleRangeRequest: false })
  }
  async getLatestVersion() {
    return this.info
  }
  resolveFiles(info: UpdateInfo) {
    return info.files.map((file) => ({
      info: file,
      url: new URL(`https://github.com/safwyls/winnow/releases/download/v${info.version}/${file.url}`),
    }))
  }
}
class FixtureUpdater extends ManagedNsisUpdater {
  constructor(
    adapter: AppAdapter,
    private info: UpdateInfo,
    executor: FixtureTransport,
  ) {
    super(undefined, adapter)
    Object.assign(this, { httpExecutor: executor })
    this.updateInfoAndProvider = { info, provider: new FixtureProvider(info, executor) }
  }
  override async checkForUpdates() {
    return { isUpdateAvailable: true, updateInfo: this.info, versionInfo: this.info }
  }
}
class FixtureAppImageUpdater extends ConfirmedAppImageUpdater {
  constructor(
    adapter: AppAdapter,
    private info: UpdateInfo,
    executor: FixtureTransport,
  ) {
    super(undefined, adapter)
    Object.assign(this, { httpExecutor: executor })
    this.updateInfoAndProvider = { info, provider: new FixtureProvider(info, executor) }
  }
  override async checkForUpdates() {
    return { isUpdateAvailable: true, updateInfo: this.info, versionInfo: this.info }
  }
}
async function fixture(platform: 'win32' | 'linux', version = '2.0.0') {
  const directory = await mkdtemp(join(tmpdir(), 'winnow-update-bytes-'))
  let mode: 'valid' | 'corrupt' | 'paused' | 'foreign' | 'github' = 'valid',
    entered!: () => void
  let started = new Promise<void>((resolve) => {
    entered = resolve
  })
  const server = createServer((req, res) => {
    if (mode === 'foreign') {
      res.writeHead(302, { location: 'https://attacker.invalid/payload.exe' })
      res.end()
      return
    }
    if (mode === 'github' && !req.url?.startsWith('/approved')) {
      res.writeHead(302, { location: 'https://release-assets.githubusercontent.com/approved' })
      res.end()
      return
    }
    res.writeHead(200, { 'content-length': payload.length })
    if (mode === 'paused') {
      res.write(payload.subarray(0, 5))
      entered()
      return
    }
    res.end(mode === 'corrupt' ? Buffer.alloc(payload.length) : payload)
  })
  resources.push({ directory, server })
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
  const address = server.address()
  if (!address || typeof address === 'string') throw new Error('No fixture port')
  const config = join(directory, 'app-update.yml')
  await writeFile(config, 'updaterCacheDirName: fixture-updater\n')
  const adapter: AppAdapter = {
    version: '1.0.0',
    name: 'Fixture',
    isPackaged: true,
    appUpdateConfigPath: config,
    userDataPath: directory,
    baseCachePath: directory,
    whenReady: async () => {},
    relaunch: () => {},
    quit: () => {},
    onQuit: () => {},
  }
  const info = { ...structuredClone(metadata), version },
    fileName = electronAssetName(version, platform, 'x64')
  info.files[0] = { ...info.files[0], url: fileName }
  if (platform === 'linux') {
    const current = join(directory, 'current.AppImage')
    await writeFile(current, 'test-owned current AppImage; never executed')
    vi.stubEnv('APPIMAGE', current)
  }
  const executor = new FixtureTransport(address.port),
    native =
      platform === 'linux'
        ? new FixtureAppImageUpdater(adapter, info, executor)
        : new FixtureUpdater(adapter, info, executor)
  native.logger = null
  const install = vi.fn(async (_paths: string[]) => {}),
    quit = vi.fn()
  const driver = createUpdateDriver({
    native,
    version: '1.0.0',
    platform,
    arch: 'x64',
    supported: true,
    install,
    quit,
  })
  const pending = join(directory, 'fixture-updater', 'pending')
  return {
    driver,
    native,
    install,
    quit,
    executor,
    pending,
    fileName,
    setMode(value: typeof mode) {
      mode = value
      started = new Promise<void>((resolve) => {
        entered = resolve
      })
      return started
    },
  }
}
describe.each(['win32', 'linux'] as const)('%s verified native update byte lifecycle', (platform) => {
  it.each(['local', 'remote'] as const)(
    '%s beta opt-out clears verified prerelease files and rejects that release afterward',
    async (source) => {
      const value = await fixture(platform, '2.0.0-beta.2')
      const preferences = { automatic: true, includeBeta: true }
      const updater = new ApplicationUpdater({
        driver: value.driver,
        version: '1.0.0',
        packaged: true,
        preferences: async () => ({ ...preferences }),
        savePreference: async (name, flag) => {
          preferences[name === 'AutomaticUpdates' ? 'automatic' : 'includeBeta'] = flag
        },
        stopBackend: async () => {},
        recoverBackend: async () => {},
        prepareRestart: async () => {},
        clearRestart: async () => {},
        openDownload: async () => {},
      })
      await updater.check()
      expect(updater.snapshot).toMatchObject({ canRestart: true, availableVersion: '2.0.0-beta.2' })
      expect(await readFile(join(value.pending, value.fileName))).toEqual(payload)
      if (source === 'local') {
        await updater.setPreference('AutomaticUpdates', false)
        await updater.setPreference('IncludeBetaUpdates', false)
      } else {
        preferences.automatic = false
        preferences.includeBeta = false
        await updater.refreshPreferences()
      }
      expect(await readdir(value.pending)).toEqual([])
      expect(updater.snapshot).toMatchObject({
        canRestart: false,
        includeBeta: false,
        availableVersion: null,
      })
      await updater.check()
      expect(updater.snapshot).toMatchObject({
        canRestart: false,
        canDownload: false,
        availableVersion: null,
      })
      await updater.restart()
      expect(value.install).not.toHaveBeenCalled()
      await updater.dispose()
    },
  )
  it('rechecks staged bytes before installer handoff and discards a changed payload', async () => {
    const value = await fixture(platform),
      release = await value.driver.check(false)
    await value.driver.download(release!, new AbortController().signal, () => {})
    await writeFile(join(value.pending, value.fileName), Buffer.alloc(payload.length))
    await expect(value.driver.install()).rejects.toThrow('checksum')
    expect(await readdir(value.pending)).toEqual([])
    expect(value.install).not.toHaveBeenCalled()
    expect(value.quit).not.toHaveBeenCalled()
  })
  it('stages checksum-verified bytes and installs only after an explicit restart command', async () => {
    const { driver, install, pending, quit, fileName } = await fixture(platform)
    const stopBackend = vi.fn(async () => {})
    const updater = new ApplicationUpdater({
      driver,
      version: '1.0.0',
      packaged: true,
      preferences: async () => ({ automatic: true, includeBeta: false }),
      savePreference: async () => {},
      stopBackend,
      recoverBackend: async () => {},
      prepareRestart: async () => {},
      clearRestart: async () => {},
      openDownload: async () => {},
    })
    await updater.check()
    expect(updater.snapshot).toMatchObject({ canRestart: true, progress: 100 })
    expect(await readFile(join(pending, fileName))).toEqual(payload)
    expect(install).not.toHaveBeenCalled()
    expect(stopBackend).not.toHaveBeenCalled()
    await updater.restart()
    expect(install).toHaveBeenCalledOnce()
    expect(install.mock.invocationCallOrder[0]).toBeGreaterThan(stopBackend.mock.invocationCallOrder[0])
    expect(quit).toHaveBeenCalledOnce()
    await updater.dispose()
  })
  it('removes corrupt bytes and cache metadata before a successful retry', async () => {
    const value = await fixture(platform),
      release = await value.driver.check(false)
    value.setMode('corrupt')
    await expect(value.driver.download(release!, new AbortController().signal, () => {})).rejects.toThrow(
      /checksum/i,
    )
    expect(await readdir(value.pending)).toEqual([])
    await expect(value.driver.install()).rejects.toThrow('Download and verify')
    value.setMode('valid')
    await value.driver.download(release!, new AbortController().signal, () => {})
    expect(await readFile(join(value.pending, value.fileName))).toEqual(payload)
  })
  it('cancels a partial native stream and deletes bytes before allowing retry', async () => {
    const value = await fixture(platform),
      release = await value.driver.check(false),
      abort = new AbortController()
    const started = value.setMode('paused')
    const download = value.driver.download(release!, abort.signal, () => {})
    const refused = expect(download).rejects.toThrow(/cancel/i)
    await started
    await vi.waitFor(async () =>
      expect((await readdir(value.pending)).some((name) => name.startsWith('temp-'))).toBe(true),
    )
    abort.abort()
    await refused
    expect(await readdir(value.pending)).toEqual([])
    await expect(value.driver.install()).rejects.toThrow('Download and verify')
    value.setMode('valid')
    await value.driver.download(release!, new AbortController().signal, () => {})
    expect(await readFile(join(value.pending, value.fileName))).toEqual(payload)
  })
  it('discards installer-owned files and prevents installation after a channel change', async () => {
    const value = await fixture(platform),
      release = await value.driver.check(false)
    await value.driver.download(release!, new AbortController().signal, () => {})
    await value.driver.discard()
    expect(await readdir(value.pending)).toEqual([])
    await expect(value.driver.install()).rejects.toThrow('Download and verify')
  })
  it('clears staging after a failed installer handoff and leaves the app open', async () => {
    const value = await fixture(platform),
      release = await value.driver.check(false)
    await value.driver.download(release!, new AbortController().signal, () => {})
    value.install.mockRejectedValue(new Error('spawn refused'))
    await expect(value.driver.install()).rejects.toThrow('spawn refused')
    expect(await readdir(value.pending)).toEqual([])
    expect(value.quit).not.toHaveBeenCalled()
  })
  it('rejects a foreign redirect before making its request and removes temporary bytes', async () => {
    const value = await fixture(platform),
      release = await value.driver.check(false)
    value.setMode('foreign')
    await expect(value.driver.download(release!, new AbortController().signal, () => {})).rejects.toThrow(
      'left GitHub',
    )
    expect(value.executor.urls).toHaveLength(1)
    expect(value.executor.urls[0]).toContain('https://github.com/')
    expect(await readdir(value.pending)).toEqual([])
  })
  it('follows the official release storage redirect while still verifying bytes', async () => {
    const value = await fixture(platform),
      release = await value.driver.check(false)
    value.setMode('github')
    await value.driver.download(release!, new AbortController().signal, () => {})
    expect(value.executor.urls).toHaveLength(2)
    expect(value.executor.urls[1]).toBe('https://release-assets.githubusercontent.com/approved')
    expect(await readFile(join(value.pending, value.fileName))).toEqual(payload)
  })
  it('cancels a native metadata request before it returns a response', async () => {
    const value = await fixture(platform),
      abort = new AbortController()
    const started = value.setMode('paused')
    const check = value.executor.duringCheck(abort.signal, () =>
      value.executor.request(
        { protocol: 'https:', hostname: 'api.github.com', path: '/metadata' },
        new CancellationToken(),
      ),
    )
    const refused = expect(check).rejects.toThrow(/cancel/i)
    await started
    abort.abort()
    await refused
  })
  it('prevents repeated quit events until update shutdown has drained and then permits quit', async () => {
    let release!: () => void
    const stop = vi.fn(
        () =>
          new Promise<void>((resolve) => {
            release = resolve
          }),
      ),
      quit = vi.fn(),
      event = { preventDefault: vi.fn() }
    const drain = quitDrain(stop, quit)
    expect(drain(event)).toBe(true)
    expect(drain(event)).toBe(true)
    expect(stop).toHaveBeenCalledOnce()
    expect(quit).not.toHaveBeenCalled()
    release()
    await vi.waitFor(() => expect(quit).toHaveBeenCalledOnce())
    expect(drain(event)).toBe(false)
    expect(event.preventDefault).toHaveBeenCalledTimes(2)
  })
})
