import { setTimeout as delay } from 'node:timers/promises'
import { backendResponds, discoverBackend, startBackend } from './lifecycle'
import type { Discovery } from './transport'

export interface BackendServicePorts {
  discover(): Promise<Discovery>
  shutdown(connection: Discovery): Promise<void>
  waitForExit(connection: Discovery): Promise<void>
  attachOrStart(): Promise<void>
}
export class ShutdownRefused extends Error {}

/** An update keeps the lifecycle lock until recovery or process exit. */
export class BackendServiceLifecycle {
  private active = false
  private stopping: Discovery | null = null
  constructor(private readonly ports: BackendServicePorts) {}
  private async stop() {
    const connection = await this.ports.discover()
    this.stopping = connection
    try {
      await this.ports.shutdown(connection)
    } catch (failure) {
      if (failure instanceof ShutdownRefused) this.stopping = null
      throw failure
    }
    await this.ports.waitForExit(connection)
    this.stopping = null
  }
  async stopForUpdate(): Promise<void> {
    if (this.active) throw new Error('The library service is already restarting.')
    this.active = true
    try {
      await this.stop()
    } catch (failure) {
      await this.recover()
      throw failure
    }
  }
  async recover(): Promise<void> {
    if (!this.active) return
    try {
      if (this.stopping) {
        await this.ports.waitForExit(this.stopping)
        this.stopping = null
      }
      await this.ports.attachOrStart()
    } finally {
      this.active = false
    }
  }
  async restart(): Promise<void> {
    if (this.active) throw new Error('The library service is already restarting.')
    this.active = true
    try {
      await this.stop()
    } finally {
      await this.recover()
    }
  }
}

export function createBackendServiceLifecycle(
  options: Parameters<typeof startBackend>[0],
): BackendServiceLifecycle {
  const discover = () => discoverBackend(options.dataDirectory)
  const running = (id: number) => {
    try {
      process.kill(id, 0)
      return true
    } catch (failure) {
      if ((failure as NodeJS.ErrnoException).code === 'ESRCH') return false
      throw new Error('The library service process could not be inspected.')
    }
  }
  async function verify(connection: Discovery) {
    const response = await fetch(new URL('/api/v1/health', connection.address), {
      headers: { Authorization: `Bearer ${connection.token}` },
      redirect: 'error',
      signal: AbortSignal.timeout(10_000),
    })
    const health = response.ok ? ((await response.json()) as { epoch?: string }) : null
    if (!health || health.epoch !== connection.epoch)
      throw new Error('The library service changed. Try again.')
  }
  return new BackendServiceLifecycle({
    discover: async () => {
      const connection = await discover()
      await verify(connection)
      return connection
    },
    shutdown: async (connection) => {
      const response = await fetch(new URL('/api/v1/lifecycle/shutdown', connection.address), {
        method: 'POST',
        headers: { Authorization: `Bearer ${connection.token}`, 'Content-Type': 'application/json' },
        body: '{}',
        redirect: 'error',
        signal: AbortSignal.timeout(15_000),
      })
      if (!response.ok) throw new ShutdownRefused('The library service could not accept the restart request.')
    },
    waitForExit: async (connection) => {
      const deadline = Date.now() + 30_000
      while (running(connection.processId)) {
        if (Date.now() >= deadline)
          throw new Error(
            'The library service is still stopping. Close other Winnow windows, then try again.',
          )
        await delay(100)
      }
    },
    attachOrStart: async () => {
      if (await backendResponds(discover)) return
      let existing = false
      try {
        existing = running((await discover()).processId)
      } catch (failure) {
        if (
          (failure as NodeJS.ErrnoException).code !== 'ENOENT' &&
          !(
            failure instanceof Error && failure.message === 'The backend has not published its connection yet'
          )
        )
          throw failure
      }
      if (!existing)
        await startBackend({
          ...options,
          args: options.args.some((arg) => arg === '--no-sync' || arg === '--seed-sample')
            ? ['--no-sync']
            : [],
        })
      const deadline = Date.now() + 60_000
      while (!(await backendResponds(discover))) {
        if (Date.now() >= deadline)
          throw new Error('The library service did not reconnect. Close and reopen Winnow.')
        await delay(250)
      }
    },
  })
}
