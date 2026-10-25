import type { StartedBackend } from './lifecycle'
import { scrubStartupDiagnostic } from './startup-failure'

export class BackendStartupFailure extends Error {
  constructor(
    public readonly exitCode: 2 | 3,
    diagnostic: string,
  ) {
    super(diagnostic || `The Winnow backend could not start (exit ${exitCode}).`)
    this.name = 'BackendStartupFailure'
  }
}

export interface StartupBackendPorts {
  healthy(): Promise<boolean>
  running(): Promise<boolean>
  start(): Promise<StartedBackend>
  signal: AbortSignal
  now?: () => number
  wait?: (milliseconds: number, signal: AbortSignal) => Promise<void>
  timeoutMs?: number
}

/** A closing native window must not leave its load promise or initial preferences holding startup open. */
export async function waitForStartupOperation<T>(
  operation: () => Promise<T>,
  signal: AbortSignal,
): Promise<T> {
  signal.throwIfAborted()
  let aborted!: () => void
  const cancelled = new Promise<never>((_resolve, reject) => {
    aborted = () => reject(signal.reason)
    signal.addEventListener('abort', aborted, { once: true })
  })
  try {
    // Promise.race observes a late rejection even after cancellation has won.
    const pending = Promise.resolve().then(() => {
      signal.throwIfAborted()
      return operation()
    })
    return await Promise.race([pending, cancelled])
  } finally {
    signal.removeEventListener('abort', aborted)
  }
}

/** Activations may arrive before the initial backend decision; they cannot bypass it. */
export class StartupWindowGate {
  private ready = false
  private pending = false
  constructor(private readonly show: () => void) {}
  request = () => {
    if (this.ready) this.show()
    else this.pending = true
  }
  resolve(): void {
    this.ready = true
    if (this.pending) {
      this.pending = false
      this.show()
    }
  }
}

function wait(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const aborted = () => {
      clearTimeout(timer)
      signal.removeEventListener('abort', aborted)
      reject(signal.reason)
    }
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', aborted)
      resolve()
    }, milliseconds)
    signal.addEventListener('abort', aborted, { once: true })
    if (signal.aborted) aborted()
  })
}

/** Fatal owned startup is resolved before constructing a library window; later disconnects stay recoverable. */
export async function prepareBackendStartup(ports: StartupBackendPorts): Promise<string | undefined> {
  const now = ports.now ?? Date.now,
    pause = ports.wait ?? wait,
    deadline = now() + (ports.timeoutMs ?? 45_000)
  ports.signal.throwIfAborted()
  if (await ports.healthy()) return
  ports.signal.throwIfAborted()
  let child: StartedBackend | undefined
  let outcome: { code: number | null } | undefined
  if (!(await ports.running())) {
    ports.signal.throwIfAborted()
    try {
      child = await ports.start()
    } catch (error) {
      ports.signal.throwIfAborted()
      return scrubStartupDiagnostic(
        error instanceof Error
          ? error.message
          : 'Could not start the backend. This window will reconnect automatically.',
      )
    }
    void child.exited.then((code) => {
      outcome = { code }
    })
  }
  while (true) {
    ports.signal.throwIfAborted()
    // Another frontend may win backend ownership. Its healthy endpoint outranks our losing companion.
    if (await ports.healthy()) return
    ports.signal.throwIfAborted()
    if (outcome) {
      if (outcome.code === 2 || outcome.code === 3)
        throw new BackendStartupFailure(outcome.code, child!.diagnostic())
      return `The Winnow backend stopped before connecting (exit ${outcome.code ?? 'unknown'}). This window will reconnect automatically.`
    }
    if (now() >= deadline)
      return 'The backend has not connected after 45 seconds. Check that the Winnow backend started successfully; this window will keep trying. Your edits have not been sent.'
    await pause(Math.min(100, Math.max(0, deadline - now())), ports.signal)
  }
}
