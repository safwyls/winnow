import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApplicationUpdater, type UpdatePorts, type UpdateRelease } from '../src/main/application-updater'
import { BackendServiceLifecycle, ShutdownRefused } from '../src/main/backend-service'
import { newerRelease, releaseVersion, validateUpdateFiles } from '../src/main/update-policy'
import { clearUpdateResume, prepareUpdateResume, restoreUpdateResume } from '../src/main/update-resume'
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const release: UpdateRelease = {
  version: '2.0.0',
  releaseUrl: 'https://github.com/safwyls/winnow/releases/tag/v2.0.0',
  downloadUrl:
    'https://github.com/safwyls/winnow/releases/download/v2.0.0/Winnow-Electron-2.0.0-win-x64-Setup.exe',
}
const deferred = <T>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { resolve, promise }
}
const instances: ApplicationUpdater[] = []
function fixture(overrides: Partial<UpdatePorts> = {}) {
  const preferences = { automatic: false, includeBeta: false }
  const ports: UpdatePorts = {
    version: '1.0.0',
    packaged: true,
    driver: {
      supported: true,
      check: vi.fn(async () => release),
      download: vi.fn(async (_release, _signal, progress) => {
        progress(60)
      }),
      install: vi.fn(async () => {}),
      discard: vi.fn(async () => {}),
    },
    preferences: vi.fn(async () => ({ ...preferences })),
    savePreference: vi.fn(async (key, value) => {
      preferences[key === 'AutomaticUpdates' ? 'automatic' : 'includeBeta'] = value
    }),
    stopBackend: vi.fn(async () => {}),
    recoverBackend: vi.fn(async () => {}),
    prepareRestart: vi.fn(async () => {}),
    clearRestart: vi.fn(async () => {}),
    openDownload: vi.fn(async () => {}),
    ...overrides,
  }
  const updater = new ApplicationUpdater(ports)
  instances.push(updater)
  return { updater, ports, preferences }
}
afterEach(() => {
  instances.splice(0).forEach((value) => value.dispose())
  vi.useRealTimers()
})

describe('Electron application update lifecycle parity', () => {
  it('stages a background download but requires explicit restart after backend shutdown', async () => {
    const { updater, ports, preferences } = fixture()
    preferences.automatic = true
    await updater.check()
    expect(updater.snapshot).toMatchObject({ canRestart: true, progress: 100, busy: false })
    expect(ports.driver.install).not.toHaveBeenCalled()
    expect(ports.stopBackend).not.toHaveBeenCalled()
    await updater.restart()
    expect(ports.stopBackend).toHaveBeenCalledOnce()
    expect(ports.prepareRestart).toHaveBeenCalledWith('2.0.0')
    expect(vi.mocked(ports.stopBackend).mock.invocationCallOrder[0]).toBeLessThan(
      vi.mocked(ports.prepareRestart).mock.invocationCallOrder[0],
    )
    expect(vi.mocked(ports.prepareRestart).mock.invocationCallOrder[0]).toBeLessThan(
      vi.mocked(ports.driver.install).mock.invocationCallOrder[0],
    )
    await updater.restart()
    expect(ports.driver.install).toHaveBeenCalledOnce()
  })
  it('does not restart when download verification fails and allows a retry', async () => {
    const { updater, ports } = fixture()
    vi.mocked(ports.driver.download).mockRejectedValueOnce(new Error('checksum mismatch'))
    await updater.check()
    await updater.updateAndRestart()
    expect(updater.snapshot).toMatchObject({ canRestart: false, canDownload: true, busy: false })
    expect(ports.stopBackend).not.toHaveBeenCalled()
    expect(ports.driver.install).not.toHaveBeenCalled()
    await updater.updateAndRestart()
    expect(ports.driver.install).toHaveBeenCalledOnce()
  })
  it('recovers the backend after installer preparation failure and retains recovery guidance during checks', async () => {
    const { updater, ports } = fixture()
    vi.mocked(ports.prepareRestart).mockRejectedValue(new Error('disk full'))
    await updater.check()
    await updater.updateAndRestart()
    expect(ports.driver.install).not.toHaveBeenCalled()
    expect(ports.clearRestart).toHaveBeenCalledOnce()
    expect(ports.recoverBackend).toHaveBeenCalledOnce()
    expect(updater.snapshot.recoveryStatus).toContain('previous update')
    await updater.check()
    expect(updater.snapshot.recoveryStatus).toContain('previous update')
  })
  it('keeps the application open and permits redownload when handoff fails', async () => {
    const { updater, ports } = fixture()
    vi.mocked(ports.driver.install).mockRejectedValue(new Error('spawn failed'))
    await updater.check()
    await updater.updateAndRestart()
    expect(updater.snapshot).toMatchObject({ canRestart: false, canDownload: true })
    expect(ports.recoverBackend).toHaveBeenCalledOnce()
  })
  it('never prepares an installer when backend shutdown fails', async () => {
    const { updater, ports } = fixture()
    vi.mocked(ports.stopBackend).mockRejectedValue(new Error('shutdown failed'))
    await updater.check()
    await updater.updateAndRestart()
    expect(ports.prepareRestart).not.toHaveBeenCalled()
    expect(ports.driver.install).not.toHaveBeenCalled()
    expect(ports.recoverBackend).not.toHaveBeenCalled()
  })
  it('cancels a download and leaves retry available without accepting a late success', async () => {
    const { updater, ports } = fixture(),
      downloading = deferred<void>()
    vi.mocked(ports.driver.download).mockImplementation(async (_release, signal, progress) => {
      progress(30)
      await downloading.promise
      expect(signal.aborted).toBe(true)
    })
    await updater.check()
    const operation = updater.download()
    await Promise.resolve()
    expect(updater.snapshot).toMatchObject({ canCancel: true, progress: 30 })
    updater.cancel()
    downloading.resolve()
    await operation
    expect(updater.snapshot).toMatchObject({ canCancel: false, canRestart: false, canDownload: true })
  })
  it('turning automatic updates off prevents an in-flight check from downloading', async () => {
    const { updater, ports, preferences } = fixture(),
      checked = deferred<UpdateRelease | null>()
    preferences.automatic = true
    vi.mocked(ports.driver.check).mockReturnValue(checked.promise)
    const checking = updater.check()
    await Promise.resolve()
    await Promise.resolve()
    const changed = updater.setPreference('AutomaticUpdates', false)
    checked.resolve(release)
    await Promise.all([checking, changed])
    expect(ports.driver.download).not.toHaveBeenCalled()
    expect(updater.snapshot.automatic).toBe(false)
    expect(ports.savePreference).toHaveBeenCalledWith('AutomaticUpdates', false)
  })
  it('remote channel changes discard staged prereleases and never silently restart', async () => {
    const { updater, ports, preferences } = fixture()
    preferences.includeBeta = true
    await updater.check()
    await updater.download()
    preferences.includeBeta = false
    await updater.refreshPreferences()
    expect(ports.driver.discard).toHaveBeenCalledOnce()
    expect(updater.snapshot).toMatchObject({
      includeBeta: false,
      canRestart: false,
      canDownload: false,
      availableVersion: null,
    })
    await updater.restart()
    expect(ports.driver.install).not.toHaveBeenCalled()
  })
  it('disabled automatic updates persist while manual download remains available', async () => {
    const { updater, ports } = fixture()
    await updater.setPreference('AutomaticUpdates', false)
    await updater.check()
    expect(ports.driver.download).not.toHaveBeenCalled()
    await updater.download()
    expect(updater.snapshot.canRestart).toBe(true)
  })
  it('awaits a paused manual check on disposal without publishing or downloading after shutdown', async () => {
    const { updater, ports, preferences } = fixture(),
      entered = deferred<void>(),
      releaseCheck = deferred<void>()
    preferences.automatic = true
    vi.mocked(ports.driver.check).mockImplementation(async (_beta, signal) => {
      entered.resolve()
      await releaseCheck.promise
      expect(signal?.aborted).toBe(true)
      return release
    })
    const listener = vi.fn()
    updater.subscribe(listener)
    const check = updater.check()
    await entered.promise
    const calls = listener.mock.calls.length
    let drained = false
    const stop = updater.dispose().then(() => {
      drained = true
    })
    await Promise.resolve()
    expect(drained).toBe(false)
    releaseCheck.resolve()
    await Promise.all([check, stop])
    expect(listener).toHaveBeenCalledTimes(calls)
    expect(ports.driver.download).not.toHaveBeenCalled()
  })
  it('disabling automatic updates aborts a blocked metadata request and completes without a response', async () => {
    const { updater, ports, preferences } = fixture(),
      entered = deferred<void>()
    preferences.automatic = true
    vi.mocked(ports.driver.check).mockImplementation(
      (_beta, signal) =>
        new Promise((_resolve, reject) => {
          signal!.addEventListener('abort', () => reject(signal!.reason), { once: true })
          entered.resolve()
        }),
    )
    const check = updater.check()
    await entered.promise
    await updater.setPreference('AutomaticUpdates', false)
    await check
    expect(updater.snapshot).toMatchObject({ automatic: false, busy: false, canRestart: false })
    expect(ports.driver.download).not.toHaveBeenCalled()
  })
  it('local channel changes discard installer staging before another check is authorized', async () => {
    const { updater, ports } = fixture()
    await updater.check()
    await updater.download()
    await updater.setPreference('IncludeBetaUpdates', true)
    expect(ports.driver.discard).toHaveBeenCalledOnce()
    expect(updater.snapshot).toMatchObject({ includeBeta: true, canRestart: false, canDownload: false })
    await updater.restart()
    expect(ports.driver.install).not.toHaveBeenCalled()
  })
  it('unsupported installations expose browser links and never download or install', async () => {
    const { updater, ports, preferences } = fixture()
    ports.driver.supported = false
    preferences.automatic = true
    await updater.check()
    await updater.download()
    await updater.action('manual-download')
    expect(ports.openDownload).toHaveBeenCalledWith(release.downloadUrl)
    expect(ports.driver.download).not.toHaveBeenCalled()
    expect(updater.snapshot).toMatchObject({ canDownload: false, canRestart: false })
  })
  it.each(['1.0.0-dev.1', '1.0.0-ci.12'])(
    'development version %s never checks or stages',
    async (version) => {
      const { updater, ports } = fixture({ version })
      await updater.check()
      expect(ports.driver.check).not.toHaveBeenCalled()
    },
  )
  it('failed checks report failure rather than up to date', async () => {
    const { updater, ports } = fixture()
    vi.mocked(ports.driver.check).mockRejectedValue(new Error('503'))
    await updater.check()
    expect(updater.snapshot.status).toContain('Could not complete')
    expect(updater.snapshot.status).not.toContain('up to date')
  })
  it('schedules automatic checks after startup and stops all timers on disposal', async () => {
    vi.useFakeTimers()
    const { updater, ports, preferences } = fixture()
    preferences.automatic = true
    await updater.initialize()
    await vi.advanceTimersByTimeAsync(19_999)
    expect(ports.driver.check).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(ports.driver.check).toHaveBeenCalledOnce()
    updater.dispose()
    await vi.advanceTimersByTimeAsync(6 * 60 * 60_000)
    expect(ports.driver.check).toHaveBeenCalledOnce()
  })
})

describe('Electron update release policy', () => {
  it.each([
    ['1.0.0-alpha.2', '1.0.0-alpha.10'],
    ['1.0.0-beta', '1.0.0'],
    ['1.0.0', '1.0.1'],
    ['1.9.9', '2.0.0'],
    ['1.0.0-beta.9', '1.0.0-beta.x'],
  ])('orders %s before %s', (earlier, later) => {
    expect(newerRelease(later, earlier, true)).toBe(true)
    expect(newerRelease(earlier, later, true)).toBe(false)
  })
  it.each(['1.2', 'v1.2.3', '01.2.3', '1.2.3-beta.01', '1.2.3-', '1.2.3+meta', '../2.0.0'])(
    'rejects malformed version %s',
    (value) => expect(releaseVersion(value)).toBeNull(),
  )
  it('stable rejects prereleases and every channel rejects downgrades and CI artifacts', () => {
    expect(newerRelease('2.0.0-beta.1', '1.0.0', false)).toBe(false)
    expect(newerRelease('1.0.0', '2.0.0-beta.1', true)).toBe(false)
    expect(newerRelease('2.0.0-ci.1', '1.0.0', true)).toBe(false)
  })
  it('accepts exact Electron platform metadata and rejects legacy or foreign artifacts and missing digests', () => {
    const file = {
      url: 'Winnow-Electron-2.0.0-win-x64-Setup.exe',
      sha512: Buffer.alloc(64).toString('base64'),
      size: 200,
    }
    expect(() => validateUpdateFiles({ version: '2.0.0', files: [file] }, 'win32', 'x64')).not.toThrow()
    for (const patch of [
      { url: 'Winnow-2.0.0-Setup.exe' },
      { url: `https://evil.example/${file.url}` },
      { sha512: '' },
      { size: -1 },
    ])
      expect(() =>
        validateUpdateFiles({ version: '2.0.0', files: [{ ...file, ...patch }] }, 'win32', 'x64'),
      ).toThrow()
    expect(() => validateUpdateFiles({ version: '2.0.0', files: [file] }, 'win32', 'arm64')).toThrow()
  })
})

describe('backend service shutdown and recovery parity', () => {
  function service() {
    const connection = {
      processId: 1234,
      address: 'http://127.0.0.1:12345',
      token: 'test',
      epoch: 'test',
      apiVersion: '1',
    }
    const ports = {
      discover: vi.fn(async () => connection),
      shutdown: vi.fn(async () => {}),
      waitForExit: vi.fn(async () => {}),
      attachOrStart: vi.fn(async () => {}),
    }
    return { lifecycle: new BackendServiceLifecycle(ports), ports }
  }
  it('waits for confirmed process exit before attaching or starting a provider restart', async () => {
    const { lifecycle, ports } = service(),
      exit = deferred<void>()
    ports.waitForExit.mockReturnValue(exit.promise)
    const restart = lifecycle.restart()
    await Promise.resolve()
    await Promise.resolve()
    expect(ports.attachOrStart).not.toHaveBeenCalled()
    await expect(lifecycle.stopForUpdate()).rejects.toThrow('already restarting')
    exit.resolve()
    await restart
    expect(ports.attachOrStart).toHaveBeenCalledOnce()
  })
  it('an update holds the lifecycle lock until installer failure recovery', async () => {
    const { lifecycle, ports } = service()
    await lifecycle.stopForUpdate()
    await expect(lifecycle.restart()).rejects.toThrow('already restarting')
    expect(ports.attachOrStart).not.toHaveBeenCalled()
    await lifecycle.recover()
    expect(ports.attachOrStart).toHaveBeenCalledOnce()
    await lifecycle.restart()
  })
  it('a refused shutdown reconnects without waiting for a process that never stopped', async () => {
    const { lifecycle, ports } = service()
    ports.shutdown.mockRejectedValueOnce(new ShutdownRefused('refused'))
    await expect(lifecycle.stopForUpdate()).rejects.toThrow('refused')
    expect(ports.waitForExit).not.toHaveBeenCalled()
    expect(ports.attachOrStart).toHaveBeenCalledOnce()
    await lifecycle.restart()
  })
  it('an uncertain shutdown waits again before recovery instead of starting a competing backend', async () => {
    const { lifecycle, ports } = service()
    ports.shutdown.mockRejectedValue(new Error('timeout'))
    await expect(lifecycle.stopForUpdate()).rejects.toThrow('timeout')
    expect(ports.waitForExit).toHaveBeenCalledOnce()
    expect(ports.waitForExit.mock.invocationCallOrder[0]).toBeLessThan(
      ports.attachOrStart.mock.invocationCallOrder[0],
    )
  })
})

describe('installer restart context', () => {
  const directories: string[] = []
  afterEach(() => {
    directories.splice(0).forEach((path) => rmSync(path, { recursive: true, force: true }))
  })
  const fixture = () => {
    const root = mkdtempSync(join(tmpdir(), 'winnow-update-test-'))
    directories.push(root)
    return { root, path: join(root, 'resume.json') }
  }
  it('restores only the isolated data directory and no-sync once after a matching Windows installer launch', () => {
    const { root, path } = fixture()
    prepareUpdateResume(path, { version: '2.0.0', dataDirectory: root, noSync: true }, 100)
    expect(
      restoreUpdateResume(path, {
        version: '2.0.0',
        args: ['Winnow.exe', '--updated'],
        appImageUpdated: false,
        now: 101,
      }).args,
    ).toEqual(['Winnow.exe', '--updated', '--data-dir', root, '--no-sync'])
    expect(existsSync(path)).toBe(false)
  })
  it('restores AppImage context without replaying sample seed or arbitrary arguments', () => {
    const { path } = fixture()
    writeFileSync(
      path,
      JSON.stringify({ version: '2.0.0', created: 100, noSync: true, args: ['--seed-sample'] }),
    )
    expect(
      restoreUpdateResume(path, { version: '2.0.0', args: ['Winnow'], appImageUpdated: true, now: 101 }).args,
    ).toEqual(['Winnow', '--no-sync'])
  })
  it('ignores an unrequested marker and refuses expired mismatched or conflicting restart context', () => {
    const { root, path } = fixture()
    prepareUpdateResume(path, { version: '2.0.0', dataDirectory: root, noSync: true }, 100)
    expect(
      restoreUpdateResume(path, { version: '2.0.0', args: ['Winnow'], appImageUpdated: false }).args,
    ).toEqual(['Winnow'])
    expect(existsSync(path)).toBe(true)
    for (const options of [
      { version: '1.0.0', now: 101, args: ['--updated'] },
      { version: '2.0.0', now: 8_000_000, args: ['--updated'] },
      { version: '2.0.0', now: 101, args: ['--updated', '--data-dir', root] },
    ]) {
      prepareUpdateResume(path, { version: '2.0.0', dataDirectory: root, noSync: true }, 100)
      expect(() => restoreUpdateResume(path, { ...options, appImageUpdated: false })).toThrow(
        'could not be restored',
      )
      expect(existsSync(path)).toBe(false)
    }
    clearUpdateResume(path)
  })
})
