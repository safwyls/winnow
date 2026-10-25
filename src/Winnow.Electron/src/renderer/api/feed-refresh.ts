import type { QueryClient } from '@tanstack/react-query'

const primaryKey = ['api', 'feed.get'] as const
const coordinators = new WeakMap<QueryClient, FeedRefresh>()

/** Library invalidations queue one replay behind the current scoring pass. */
class FeedRefresh {
  private listeners = new Set<() => void>()
  private dirty = false
  private running = false
  private version = 0
  private lifetime = 0
  pending = false
  epoch = 0
  constructor(private client: QueryClient) {}
  snapshot = () => this.version
  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
      queueMicrotask(() => {
        if (this.listeners.size) return
        this.lifetime++
        this.dirty = false
        this.running = false
        this.pending = false
        void this.client.cancelQueries({ queryKey: primaryKey, exact: true })
        void this.client.cancelQueries({ queryKey: ['api', 'feed.supplement'] })
      })
    }
  }
  private publish() {
    this.version++
    for (const listener of this.listeners) listener()
  }
  invalidate() {
    this.epoch++
    void this.client.invalidateQueries({ queryKey: primaryKey, exact: true, refetchType: 'none' })
    void this.client.cancelQueries({ queryKey: ['api', 'feed.supplement'] })
    this.dirty = true
    this.pending = true
    this.publish()
    if (!this.running) void this.replay()
  }
  private async replay() {
    this.running = true
    const lifetime = this.lifetime
    try {
      const current = this.client.getQueryCache().find({ queryKey: primaryKey, exact: true })
      if (current?.state.fetchStatus === 'fetching') await current.promise?.catch(() => {})
      while (this.dirty && lifetime === this.lifetime) {
        this.dirty = false
        const query = this.client.getQueryCache().find({ queryKey: primaryKey, exact: true })
        if (!query?.isActive()) break
        await this.client.refetchQueries(
          { queryKey: primaryKey, exact: true, type: 'active' },
          { cancelRefetch: false },
        )
      }
    } finally {
      if (lifetime === this.lifetime) {
        this.running = false
        this.pending = false
        this.publish()
      }
    }
  }
}

export function feedRefresh(client: QueryClient) {
  let coordinator = coordinators.get(client)
  if (!coordinator) coordinators.set(client, (coordinator = new FeedRefresh(client)))
  return coordinator
}

export function invalidateFeed(client: QueryClient) {
  feedRefresh(client).invalidate()
}
