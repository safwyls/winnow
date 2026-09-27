import type { QueryClient } from '@tanstack/react-query'

/** Wait for reads that predate this invalidation, then start fresh reads. */
export async function refreshSnapshots(client: QueryClient): Promise<void> {
  const inFlight = client.getQueryCache().findAll({ type: 'active', fetchStatus: 'fetching' })
  await Promise.allSettled(inFlight.map((query) => query.promise))
  await client.invalidateQueries({}, { cancelRefetch: false })
}

/** A change arriving during a snapshot read earns another refresh after that read settles. */
export class RefreshQueue {
  private dirty = false
  private running = false
  private disposed = false
  constructor(private readonly refresh: () => Promise<unknown>) {}
  request() {
    if (this.disposed) return
    this.dirty = true
    if (!this.running) void this.flush()
  }
  dispose() {
    this.disposed = true
  }
  private async flush() {
    this.running = true
    while (this.dirty && !this.disposed) {
      this.dirty = false
      try {
        await this.refresh()
      } catch {
        /* Query state owns the visible error. */
      }
    }
    this.running = false
  }
}
