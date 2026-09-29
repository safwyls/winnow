import type { QueryClient } from '@tanstack/react-query'
import type { BackendEvent } from '../shared/bridge'
import { request } from './api/client'
import type { JournalResponse } from './api/types'
import { patchJournalCaches } from './features/activity-model'

export async function refreshJournalSnapshot(client: QueryClient, sessionId: number): Promise<void> {
  const reads = client.getQueryCache().findAll({ fetchStatus: 'fetching', predicate: query =>
    ['activity.query', 'game.details', 'journal.get'].includes(String(query.queryKey[1])) })
  await Promise.allSettled(reads.map(query => query.promise))
  const saved = await request<JournalResponse>('journal.get', { sessionId })
  patchJournalCaches(client, sessionId, saved)
}

/** Selection revisions do not fingerprint image bytes. Explicit changes and resyncs bypass reuse. */
export function shouldRefreshArtwork(event: Pick<BackendEvent, 'kind' | 'resource'>): boolean {
  return event.kind === 'resync-required' || /^works\/\d+\/artwork(?:\/|$)/.test(event.resource ?? '')
}

/** Wait for reads that predate this invalidation, then start fresh reads. */
export async function refreshSnapshots(
  client: QueryClient,
  options: { artwork?: boolean } = {},
): Promise<void> {
  const inFlight = client.getQueryCache().findAll({ type: 'active', fetchStatus: 'fetching' })
  await Promise.allSettled(inFlight.map((query) => query.promise))
  await client.invalidateQueries(
    { predicate: (query) => query.queryKey[0] !== 'artwork-image' || Boolean(options.artwork) },
    { cancelRefetch: false },
  )
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
