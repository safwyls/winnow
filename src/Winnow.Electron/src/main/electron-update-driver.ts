import { spawn, type StdioOptions } from 'node:child_process'
import { createReadStream, readFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { isAbsolute } from 'node:path'
import { AppImageUpdater, NsisUpdater } from 'electron-updater'
import { CancellationToken, type UpdateInfo } from 'builder-util-runtime'
import type { UpdateDriver, UpdateRelease } from './application-updater'
import { electronAssetName, newerRelease, updateRepository, validateUpdateFiles } from './update-policy'
import { manualRelease } from './manual-update-release'
import { UpdateHttpExecutor } from './update-http'
import {
  isAppImageInstallation,
  matchesWindowsInstallation,
  readWindowsInstallDirectory,
} from './installation-policy'

export async function verifyStagedUpdate(paths: string[], file: { sha512: string; size?: number }) {
  if (paths.length !== 1) throw new Error('Invalid installer staging.')
  const hash = createHash('sha512')
  let size = 0
  for await (const chunk of createReadStream(paths[0])) {
    size += chunk.length
    if (size > (file.size ?? 4 * 1024 ** 3)) throw new Error('The staged installer size changed.')
    hash.update(chunk)
  }
  if ((file.size !== undefined && size !== file.size) || hash.digest('base64') !== file.sha512)
    throw new Error('The staged installer failed checksum verification.')
}

export function spawnInstaller(
  command: string,
  args: string[],
  env = process.env,
  stdio: StdioOptions = 'ignore',
): Promise<boolean> {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { env, stdio, detached: true, windowsHide: true })
    child.once('error', reject)
    child.once('spawn', () => {
      child.unref()
      resolve(true)
    })
  })
}
export class ManagedNsisUpdater extends NsisUpdater {
  async discardDownload() {
    await this.downloadedUpdateHelper?.clear()
  }
}
export class ConfirmedAppImageUpdater extends AppImageUpdater {
  async discardDownload() {
    await this.downloadedUpdateHelper?.clear()
  }
  private launches: Promise<boolean>[] = []
  protected override spawnLog(
    command: string,
    args: string[] = [],
    env?: NodeJS.ProcessEnv,
    stdio: StdioOptions = 'ignore',
  ): Promise<boolean> {
    const launched = spawnInstaller(command, args, env, stdio)
    // The upstream installer does not await its launch. Retain that result before allowing the app to quit.
    void launched.catch(() => {})
    this.launches.push(launched)
    return launched
  }
  async installConfirmed() {
    this.launches = []
    if (!this.install(true, true)) throw new Error('Update installation was refused.')
    await Promise.all(this.launches)
  }
}
export interface NativeUpdateAdapter {
  autoDownload: boolean
  autoInstallOnAppQuit: boolean
  allowPrerelease: boolean
  allowDowngrade: boolean
  channel: string | null
  disableWebInstaller: boolean
  disableDifferentialDownload: boolean
  checkForUpdates(): Promise<{ isUpdateAvailable: boolean; updateInfo: UpdateInfo } | null>
  discardDownload(): Promise<void>
  downloadUpdate(token: CancellationToken): Promise<string[]>
  on(event: string, listener: (...args: any[]) => void): unknown
  removeListener(event: string, listener: (...args: any[]) => void): unknown
}
export function createUpdateDriver(options: {
  native: NativeUpdateAdapter
  version: string
  platform: string
  arch: string
  supported: boolean
  install(paths: string[]): Promise<void>
  quit(): void
  manualCheck?: (includeBeta: boolean, signal?: AbortSignal) => Promise<UpdateRelease | null>
  check?: (signal?: AbortSignal) => ReturnType<NativeUpdateAdapter['checkForUpdates']>
  verify?: typeof verifyStagedUpdate
}): UpdateDriver {
  const native = options.native
  native.autoDownload = false
  native.autoInstallOnAppQuit = false
  native.disableWebInstaller = true
  native.disableDifferentialDownload = true
  native.allowDowngrade = false
  // Errors are reported through the action promise; an unhandled EventEmitter error must not terminate Winnow.
  native.on('error', () => {})
  let release: UpdateRelease | null = null,
    downloaded: string[] = []
  let verifiedFile: { sha512: string; size?: number } | null = null
  const discard = async () => {
    downloaded = []
    await native.discardDownload()
  }
  return {
    supported: options.supported,
    discard,
    async check(includeBeta, signal) {
      if (!options.supported && options.manualCheck) return options.manualCheck(includeBeta, signal)
      const previousRelease = release,
        previousDownload = downloaded
      release = null
      downloaded = []
      native.channel = includeBeta ? 'beta' : 'latest'
      native.allowPrerelease = includeBeta
      native.allowDowngrade = false
      const result = await (options.check ? options.check(signal) : native.checkForUpdates())
      signal?.throwIfAborted()
      if (
        !result?.isUpdateAvailable ||
        !newerRelease(result.updateInfo.version, options.version, includeBeta)
      ) {
        release = null
        downloaded = []
        return null
      }
      const info = result.updateInfo
      validateUpdateFiles(info, options.platform, options.arch)
      const name = electronAssetName(info.version, options.platform, options.arch)
      // Keep the native provider's resolved metadata, but remove other architectures before it selects a file.
      const architectureFiles = info.files.filter((file) => file.url.split('/').at(-1) === name)
      info.files.splice(0, info.files.length, ...architectureFiles)
      const file = architectureFiles[0]
      if (
        previousRelease?.version === info.version &&
        previousDownload.length &&
        (verifiedFile?.sha512 !== file.sha512 || verifiedFile.size !== file.size)
      ) {
        await discard()
        throw new Error('The staged update metadata changed. Check again before downloading.')
      }
      verifiedFile = { sha512: file.sha512, size: file.size }
      if (previousRelease?.version === info.version) downloaded = previousDownload
      release = {
        version: info.version,
        releaseUrl: `${updateRepository}/releases/tag/v${info.version}`,
        downloadUrl: `${updateRepository}/releases/download/v${info.version}/${name}`,
      }
      return release
    },
    async download(requested, signal, progress) {
      if (!options.supported || requested.version !== release?.version)
        throw new Error('Check for updates again before downloading.')
      downloaded = []
      const token = new CancellationToken(),
        cancel = () => token.cancel()
      const listener = (value: { percent: number }) => {
        if (Number.isFinite(value.percent)) progress(value.percent)
      }
      signal.addEventListener('abort', cancel, { once: true })
      if (signal.aborted) cancel()
      native.on('download-progress', listener)
      try {
        const paths = await native.downloadUpdate(token)
        if (signal.aborted || !paths.length || paths.some((path) => !isAbsolute(path)))
          throw new Error('Update download was cancelled or invalid.')
        downloaded = paths
      } catch (error) {
        await discard()
        throw error
      } finally {
        signal.removeEventListener('abort', cancel)
        native.removeListener('download-progress', listener)
      }
    },
    async install() {
      if (!options.supported || !release || !downloaded.length || !verifiedFile)
        throw new Error('Download and verify the update before installing.')
      try {
        await (options.verify ?? verifyStagedUpdate)(downloaded, verifiedFile)
        await options.install([...downloaded])
      } catch (error) {
        await discard()
        throw error
      }
      downloaded = []
      options.quit()
    },
  }
}

export function electronUpdateDriver(options: {
  version: string
  packaged: boolean
  appName: string
  quit(): void
}): UpdateDriver {
  const linux = process.platform === 'linux'
  const native = linux ? new ConfirmedAppImageUpdater() : new ManagedNsisUpdater()
  const executor = new UpdateHttpExecutor()
  Object.assign(native, { httpExecutor: executor })
  native.setFeedURL({ provider: 'github', owner: 'safwyls', repo: 'winnow' })
  native.logger = null
  let osRelease: string | null = null
  if (linux) {
    try {
      osRelease = readFileSync('/etc/os-release', 'utf8')
    } catch {
      /* An unknown distribution requires manual updates. */
    }
  }
  const supported =
    options.packaged &&
    process.arch === 'x64' &&
    (process.platform === 'win32'
      ? !process.env.PORTABLE_EXECUTABLE_DIR &&
        matchesWindowsInstallation(
          process.execPath,
          readWindowsInstallDirectory('nsis'),
          'nsis',
          options.appName,
        )
      : linux && isAppImageInstallation(process.env.APPIMAGE, osRelease))
  return createUpdateDriver({
    native,
    version: options.version,
    platform: process.platform,
    arch: process.arch,
    supported,
    manualCheck: (includeBeta, signal) =>
      manualRelease(options.version, includeBeta, process.platform, process.arch, signal),
    check: (signal) => executor.duringCheck(signal, () => native.checkForUpdates()),
    async install(paths) {
      if (linux) {
        process.env.WINNOW_APPIMAGE_UPDATED = '1'
        await (native as ConfirmedAppImageUpdater).installConfirmed()
      } else {
        await spawnInstaller(paths[0], ['--updated', '/S', '--force-run'])
      }
    },
    quit: options.quit,
  })
}
