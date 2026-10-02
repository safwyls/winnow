import { EventEmitter } from 'node:events'
import { describe, expect, it, vi } from 'vitest'
import { resolve } from 'node:path'
import type { UpdateInfo } from 'builder-util-runtime'
vi.mock('electron-updater', () => ({ AppImageUpdater: class {}, NsisUpdater: class {} }))
const spawn = vi.hoisted(() => vi.fn())
vi.mock('node:child_process', () => ({ spawn }))
import {
  createUpdateDriver,
  spawnInstaller,
  type NativeUpdateAdapter,
} from '../src/main/electron-update-driver'

function fixture() {
  const events = new EventEmitter()
  const metadata: UpdateInfo = {
    version: '2.0.0',
    releaseDate: '2026-09-28T12:00:00Z',
    path: '',
    sha512: '',
    files: [
      {
        url: 'Winnow-Electron-2.0.0-win-x64-Setup.exe',
        sha512: Buffer.alloc(64).toString('base64'),
        size: 200,
      },
    ],
  }
  const native: NativeUpdateAdapter = {
    autoDownload: true,
    autoInstallOnAppQuit: true,
    allowPrerelease: true,
    allowDowngrade: true,
    channel: null,
    disableWebInstaller: false,
    disableDifferentialDownload: false,
    checkForUpdates: vi.fn(async () => ({ isUpdateAvailable: true, updateInfo: metadata })),
    downloadUpdate: vi.fn(async () => [resolve('verified-cache-update.exe')]),
    discardDownload: vi.fn(async () => {}),
    on: events.on.bind(events),
    removeListener: events.removeListener.bind(events),
  }
  const install = vi.fn(async () => {}),
    quit = vi.fn()
  const driver = createUpdateDriver({
    native,
    version: '1.0.0',
    platform: 'win32',
    arch: 'x64',
    supported: true,
    verify: vi.fn(async () => {}),
    install,
    quit,
  })
  return { driver, native, metadata, install, quit, events }
}
describe('native Electron update adapter', () => {
  it('revokes staging when a checked version changes its checksum', async () => {
    const { driver, native, metadata, install } = fixture()
    const release = await driver.check(false)
    await driver.download(release!, new AbortController().signal, () => {})
    metadata.files[0] = { ...metadata.files[0], sha512: Buffer.alloc(64, 1).toString('base64') }
    await expect(driver.check(false)).rejects.toThrow('metadata changed')
    expect(native.discardDownload).toHaveBeenCalledOnce()
    await expect(driver.install()).rejects.toThrow('Download and verify')
    expect(install).not.toHaveBeenCalled()
  })
  it('disables automatic installation and downgrades while selecting only matching Electron artifacts', async () => {
    const { driver, native, metadata } = fixture()
    metadata.files.unshift({ ...metadata.files[0], url: 'Winnow-Electron-2.0.0-win-arm64-Setup.exe' })
    const release = await driver.check(true)
    expect(native).toMatchObject({
      autoDownload: false,
      autoInstallOnAppQuit: false,
      allowPrerelease: true,
      allowDowngrade: false,
      channel: 'beta',
      disableWebInstaller: true,
    })
    expect(metadata.files).toHaveLength(1)
    expect(release?.downloadUrl).toContain('win-x64-Setup.exe')
  })
  it('rejects legacy or foreign metadata before starting any download', async () => {
    const { driver, native, metadata } = fixture()
    metadata.files[0].url = 'Winnow-2.0.0-Setup.exe'
    await expect(driver.check(false)).rejects.toThrow('Electron update')
    expect(native.downloadUpdate).not.toHaveBeenCalled()
    await expect(driver.install()).rejects.toThrow('Download and verify')
  })
  it('maps native progress and cancellation and rejects a late completed download', async () => {
    const { driver, native, events, install } = fixture(),
      abort = new AbortController(),
      progress = vi.fn()
    vi.mocked(native.downloadUpdate).mockImplementation(async (token) => {
      events.emit('download-progress', { percent: 20 })
      abort.abort()
      expect(token.cancelled).toBe(true)
      return [resolve('verified-cache-update.exe')]
    })
    const release = await driver.check(false)
    await expect(driver.download(release!, abort.signal, progress)).rejects.toThrow('cancelled')
    expect(progress).toHaveBeenCalledWith(20)
    expect(events.listenerCount('download-progress')).toBe(0)
    await expect(driver.install()).rejects.toThrow('Download and verify')
    expect(install).not.toHaveBeenCalled()
  })
  it('keeps verified staging unavailable after native checksum failure and supports retry', async () => {
    const { driver, native, install, quit } = fixture(),
      release = await driver.check(false)
    vi.mocked(native.downloadUpdate).mockRejectedValueOnce(new Error('sha512 mismatch'))
    await expect(driver.download(release!, new AbortController().signal, () => {})).rejects.toThrow(
      'sha512 mismatch',
    )
    await expect(driver.install()).rejects.toThrow('Download and verify')
    await driver.download(release!, new AbortController().signal, () => {})
    await driver.install()
    expect(install).toHaveBeenCalledWith([resolve('verified-cache-update.exe')])
    expect(quit).toHaveBeenCalledOnce()
  })
  it('does not quit on failed handoff and waits for a confirmed installer launch', async () => {
    const { driver, install, quit } = fixture(),
      release = await driver.check(false)
    await driver.download(release!, new AbortController().signal, () => {})
    install.mockRejectedValue(new Error('launch denied'))
    await expect(driver.install()).rejects.toThrow('launch denied')
    expect(quit).not.toHaveBeenCalled()
  })
  it('handles emitted updater errors without an unhandled EventEmitter exception', () => {
    const { events } = fixture()
    expect(() => events.emit('error', new Error('offline'))).not.toThrow()
  })
  it('invalid metadata after a valid check revokes the previous download authorization', async () => {
    const { driver, metadata, native } = fixture(),
      previous = await driver.check(false)
    metadata.files[0].url = 'Winnow-2.0.0-Setup.exe'
    await expect(driver.check(false)).rejects.toThrow('Electron update')
    await expect(driver.download(previous!, new AbortController().signal, () => {})).rejects.toThrow(
      'Check for updates again',
    )
    expect(native.downloadUpdate).not.toHaveBeenCalled()
  })
  it('confirms spawn rather than trusting a process ID and keeps the window hidden', async () => {
    const child = Object.assign(new EventEmitter(), { pid: 5, unref: vi.fn() })
    spawn.mockReturnValue(child)
    const launched = spawnInstaller('verified-installer.exe', ['--updated', '/S', '--force-run'])
    let complete = false
    void launched.then(() => {
      complete = true
    })
    await Promise.resolve()
    expect(complete).toBe(false)
    child.emit('spawn')
    await launched
    expect(child.unref).toHaveBeenCalledOnce()
    expect(spawn).toHaveBeenLastCalledWith(
      'verified-installer.exe',
      ['--updated', '/S', '--force-run'],
      expect.objectContaining({ windowsHide: true, detached: true, stdio: 'ignore' }),
    )
  })
  it('rejects asynchronous spawn failure before a handoff can quit Winnow', async () => {
    const child = Object.assign(new EventEmitter(), { unref: vi.fn() })
    spawn.mockReturnValue(child)
    const launched = spawnInstaller('verified-installer.exe', [])
    child.emit('error', new Error('EACCES'))
    await expect(launched).rejects.toThrow('EACCES')
    expect(child.unref).not.toHaveBeenCalled()
  })
})
