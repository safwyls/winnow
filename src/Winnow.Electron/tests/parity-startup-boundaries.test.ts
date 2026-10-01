import { afterEach, describe, expect, it, vi } from 'vitest'
import { EventEmitter } from 'node:events'
import { PassThrough } from 'node:stream'
import { dirname, resolve } from 'node:path'
import {
  BackendStartupFailure,
  prepareBackendStartup,
  StartupWindowGate,
  waitForStartupOperation,
  type StartupBackendPorts,
} from '../src/main/startup-backend'
import { reportStartupFailure } from '../src/main/startup-failure'
import { backendResponds, startBackend } from '../src/main/lifecycle'
import { SnapshotRefresh } from '../src/shared/snapshot-refresh'
import { WindowTrayController } from '../src/main/window-tray'

const native = vi.hoisted(() => ({ spawn: vi.fn(), access: vi.fn(), mkdir: vi.fn() }))
vi.mock('node:child_process', () => ({ spawn: native.spawn }))
vi.mock('node:fs/promises', async (original) => ({
  ...(await original<typeof import('node:fs/promises')>()),
  access: native.access,
  mkdir: native.mkdir,
}))
afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.unstubAllEnvs()
  native.spawn.mockReset()
  native.access.mockReset()
  native.mkdir.mockReset()
  vi.useRealTimers()
})

function fixture() {
  const lifetime = new AbortController()
  let time = 0
  let connected = false
  let complete!: (code: number | null) => void
  const child = {
    processId: 123,
    exited: new Promise<number | null>((resolve) => {
      complete = resolve
    }),
    diagnostic: () => 'Winnow backend failed to start: this database does not support the newer schema.',
  }
  const ports: StartupBackendPorts = {
    healthy: vi.fn(async () => connected),
    running: vi.fn(async () => false),
    start: vi.fn(async () => child),
    signal: lifetime.signal,
    now: () => time,
    wait: vi.fn(async (milliseconds) => {
      time += milliseconds
    }),
    timeoutMs: 300,
  }
  return {
    ports,
    lifetime,
    complete,
    child,
    connect: () => {
      connected = true
    },
  }
}

describe('native-ready frontend startup and external backend ownership', () => {
  it('a rejected primary renderer load reaches the exit-three boundary with its useful diagnostic', async () => {
    const lifetime = new AbortController(),
      surface = vi.fn(),
      remove = vi.spyOn(lifetime.signal, 'removeEventListener')
    let failure: unknown
    try {
      await waitForStartupOperation(async () => {
        throw Error('ERR_CONNECTION_REFUSED loading the primary renderer')
      }, lifetime.signal)
    } catch (error) {
      failure = error
    }
    expect(reportStartupFailure(failure, { surface, log: vi.fn() })).toBe(3)
    expect(surface).toHaveBeenCalledExactlyOnceWith(
      'Winnow could not start',
      expect.stringContaining('ERR_CONNECTION_REFUSED'),
    )
    expect(remove).toHaveBeenCalledWith('abort', expect.any(Function))
  })
  it.each(['Window closed', 'Startup closed'])(
    '%s cancels a pending primary renderer load and consumes its late failure without an alert',
    async (reason) => {
      const lifetime = new AbortController(),
        surface = vi.fn(),
        remove = vi.spyOn(lifetime.signal, 'removeEventListener')
      let reject!: (error: Error) => void
      const load = vi.fn(
        () =>
          new Promise<void>((_resolve, rejected) => {
            reject = rejected
          }),
      )
      const pending = waitForStartupOperation(load, lifetime.signal)
      await vi.waitFor(() => expect(load).toHaveBeenCalledOnce())
      lifetime.abort(new DOMException(reason, 'AbortError'))
      const error = await pending.catch((failure: unknown) => failure)
      expect(reportStartupFailure(error, { surface, log: vi.fn() })).toBe(0)
      expect(surface).not.toHaveBeenCalled()
      reject(Error('ERR_ABORTED from the closed webContents'))
      await Promise.resolve()
      expect(remove).toHaveBeenCalledWith('abort', expect.any(Function))
    },
  )
  it('held initial preferences apply saved fullscreen before renderer loading without scheduling a second refresh', async () => {
    let release!: () => void
    const values = { StartInFullscreen: 'true' }
    const read = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          release = resolve
        }),
    )
    const refresh = new SnapshotRefresh(read)
    const firstRead = refresh.request()
    const window = {
      setFullScreen: vi.fn(),
      show: vi.fn(),
      hide: vi.fn(),
      focus: vi.fn(),
      isMinimized: () => false,
      restore: vi.fn(),
      setSkipTaskbar: vi.fn(),
    }
    const tray = new WindowTrayController({ background: false, window: () => window, createIcon: vi.fn() })
    const load = vi.fn(async () => {
      expect(window.setFullScreen).toHaveBeenCalledExactlyOnceWith(true)
    })
    const opening = (async () => {
      await waitForStartupOperation(() => firstRead, new AbortController().signal)
      tray.preferences(values)
      await load()
    })()
    await Promise.resolve()
    expect(load).not.toHaveBeenCalled()
    expect(read).toHaveBeenCalledOnce()
    release()
    await opening
    expect(read).toHaveBeenCalledOnce()
    expect(load).toHaveBeenCalledOnce()
  })
  it('the initial preference wait uses only the remaining startup deadline and clears its listener', async () => {
    const signal = AbortSignal.timeout(50),
      remove = vi.spyOn(signal, 'removeEventListener')
    const pending = waitForStartupOperation(() => new Promise<void>(() => {}), signal)
    const outcome = pending.catch((error: unknown) => error)
    expect(await outcome).toMatchObject({ name: 'TimeoutError' })
    expect(remove).toHaveBeenCalledWith('abort', expect.any(Function))
  })
  it('quitting aborts the actual health fetch without waiting for its timeout', async () => {
    const lifetime = new AbortController()
    let received: AbortSignal | undefined
    vi.stubGlobal(
      'fetch',
      vi.fn(
        (_url, options) =>
          new Promise((_resolve, reject) => {
            received = options.signal
            received!.addEventListener('abort', () => reject(received!.reason), { once: true })
          }),
      ),
    )
    const pending = backendResponds(
      async () => ({
        address: 'http://127.0.0.1:1234',
        token: 'fixture-token',
        epoch: 'fixture-epoch',
        processId: 123,
        apiVersion: '1',
      }),
      lifetime.signal,
    )
    await vi.waitFor(() => expect(received).toBeDefined())
    lifetime.abort(new DOMException('Closed', 'AbortError'))
    await expect(pending).resolves.toBe(false)
    expect(received!.aborted).toBe(true)
  })
  it('queues activation while fatal startup is pending instead of constructing a library window', async () => {
    const f = fixture(),
      show = vi.fn(),
      gate = new StartupWindowGate(show)
    vi.mocked(f.ports.wait!).mockImplementation(async () => {
      gate.request()
      gate.request()
      f.complete(3)
    })
    await expect(prepareBackendStartup(f.ports)).rejects.toMatchObject({ exitCode: 3 })
    expect(show).not.toHaveBeenCalled()
    const ready = fixture()
    vi.mocked(ready.ports.wait!).mockImplementation(async () => {
      gate.request()
      ready.connect()
    })
    await prepareBackendStartup(ready.ports)
    gate.resolve()
    expect(show).toHaveBeenCalledOnce()
  })
  it('reuses a healthy external backend without another companion or startup waiter', async () => {
    const f = fixture()
    f.connect()
    await expect(prepareBackendStartup(f.ports)).resolves.toBeUndefined()
    expect(f.ports.running).not.toHaveBeenCalled()
    expect(f.ports.start).not.toHaveBeenCalled()
    expect(f.ports.wait).not.toHaveBeenCalled()
  })
  it('waits for a live published backend without initializing its workers again', async () => {
    const f = fixture()
    vi.mocked(f.ports.running).mockResolvedValue(true)
    vi.mocked(f.ports.wait!).mockImplementation(async () => f.connect())
    await expect(prepareBackendStartup(f.ports)).resolves.toBeUndefined()
    expect(f.ports.start).not.toHaveBeenCalled()
  })
  it('starts one companion and constructs no library until its initial health succeeds', async () => {
    const f = fixture(),
      window = vi.fn()
    vi.mocked(f.ports.wait!).mockImplementation(async () => {
      expect(window).not.toHaveBeenCalled()
      f.connect()
    })
    await prepareBackendStartup(f.ports)
    window()
    expect(f.ports.start).toHaveBeenCalledOnce()
    expect(window).toHaveBeenCalledOnce()
    f.complete(3)
    await Promise.resolve()
    expect(window).toHaveBeenCalledOnce()
  })
  it.each([2, 3] as const)(
    'an owned startup exit %s preserves its status and useful diagnostic before any library window',
    async (code) => {
      const f = fixture(),
        window = vi.fn(),
        surface = vi.fn()
      f.complete(code)
      let failure: unknown
      try {
        await prepareBackendStartup(f.ports)
        window()
      } catch (error) {
        failure = error
      }
      expect(failure).toBeInstanceOf(BackendStartupFailure)
      expect(window).not.toHaveBeenCalled()
      expect(
        reportStartupFailure(failure, {
          surface,
          log: vi.fn(),
          exitCode: (failure as BackendStartupFailure).exitCode,
        }),
      ).toBe(code)
      expect(surface).toHaveBeenCalledWith(
        'Winnow could not start',
        expect.stringContaining('does not support'),
      )
    },
  )
  it('a concurrent healthy owner takes precedence over our companion ownership refusal', async () => {
    const f = fixture()
    f.complete(3)
    vi.mocked(f.ports.start).mockImplementation(async () => {
      f.connect()
      return f.child
    })
    await expect(prepareBackendStartup(f.ports)).resolves.toBeUndefined()
    expect(f.ports.start).toHaveBeenCalledOnce()
  })
  it('keeps a missing companion and the bounded initial timeout recoverable', async () => {
    const missing = fixture()
    vi.mocked(missing.ports.start).mockRejectedValue(Error('Backend companion was not found. token=private'))
    await expect(prepareBackendStartup(missing.ports)).resolves.toBe(
      'Backend companion was not found. [redacted-secret]',
    )
    const slow = fixture()
    await expect(prepareBackendStartup(slow.ports)).resolves.toContain('will keep trying')
    expect(slow.ports.start).toHaveBeenCalledOnce()
    expect(slow.ports.wait).toHaveBeenCalledTimes(3)
  })
  it('quitting during the real startup timer cancels cleanly and removes its abort listener', async () => {
    const f = fixture(),
      remove = vi.spyOn(f.lifetime.signal, 'removeEventListener')
    delete f.ports.wait
    delete f.ports.now
    const pending = prepareBackendStartup(f.ports)
    await vi.waitFor(() => expect(f.ports.healthy).toHaveBeenCalledTimes(2))
    f.lifetime.abort(new DOMException('Closed', 'AbortError'))
    await expect(pending).rejects.toMatchObject({ name: 'AbortError' })
    expect(remove).toHaveBeenCalledWith('abort', expect.any(Function))
    const surface = vi.fn()
    expect(reportStartupFailure(f.lifetime.signal.reason, { surface, log: vi.fn() })).toBe(0)
    expect(surface).not.toHaveBeenCalled()
  })
})

it('the real spawn boundary keeps backend configuration at its executable root and bounds, redacts and unreferences stderr', async () => {
  const backend = resolve('isolated-backend', 'Winnow.Backend.dll'),
    directory = resolve('isolated-data')
  vi.stubEnv('WINNOW_BACKEND_PATH', backend)
  const stderr = Object.assign(new PassThrough(), { unref: vi.fn() })
  const child = Object.assign(new EventEmitter(), { pid: 321, stderr, unref: vi.fn() })
  native.spawn.mockImplementation(() => {
    queueMicrotask(() => child.emit('spawn'))
    return child
  })
  const started = await startBackend({
    appPath: resolve('frontend'),
    resourcesPath: resolve('frontend/resources'),
    packaged: true,
    dataDirectory: directory,
    args: ['--no-sync'],
  })
  expect(native.spawn).toHaveBeenCalledWith('dotnet', [backend, '--data-dir', directory, '--no-sync'], {
    cwd: dirname(backend),
    detached: true,
    windowsHide: true,
    stdio: ['ignore', 'ignore', 'pipe'],
  })
  expect(child.unref).toHaveBeenCalledOnce()
  expect(stderr.unref).toHaveBeenCalledOnce()
  stderr.write('x'.repeat(50_000))
  stderr.write('\nUnsupported schema. api_key=private-secret\n')
  child.emit('close', 3)
  await expect(started.exited).resolves.toBe(3)
  expect(started.diagnostic().length).toBeLessThanOrEqual(32768)
  expect(started.diagnostic()).toContain('Unsupported schema.')
  expect(started.diagnostic()).not.toContain('private-secret')
  stderr.destroy()
})
