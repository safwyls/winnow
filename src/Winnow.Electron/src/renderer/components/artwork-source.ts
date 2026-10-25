import { QueryObserver, type QueryClient } from '@tanstack/react-query'
import type { ArtworkSource } from './artwork-decode'
import type { ArtworkKey } from './artwork-images'

export type ArtworkBytes = { source: ArtworkSource }
const freshness = 120_000
const lifetime = 300_000
const maximumBytes = 32 * 1024 * 1024
const maximumEntries = 128
interface Pending {
  controller: AbortController
  readers: number
  ready: Promise<ArtworkSource | undefined>
}
interface WarmEntry {
  key: ArtworkKey
  source: ArtworkSource
  bytes: number
}
interface SourceOwner {
  pending: Map<string, Pending>
  warm: Map<string, WarmEntry>
  consumed: WeakSet<object>
  revisions: WeakMap<object, number>
}
const owners = new WeakMap<QueryClient, SourceOwner>()

function ownerFor(client: QueryClient) {
  const existing = owners.get(client)
  if (existing) return existing
  const owner: SourceOwner = {
    pending: new Map(),
    warm: new Map(),
    consumed: new WeakSet(),
    revisions: new WeakMap(),
  }
  owners.set(client, owner)
  const unsubscribe = client.getQueryCache().subscribe((event) => {
    const key = event.query.queryKey
    if (
      key[0] === 'artwork-image' &&
      (event.type === 'removed' || (event.type === 'updated' && event.action.type === 'invalidate'))
    ) {
      const signature = JSON.stringify(key)
      owner.pending.get(signature)?.controller.abort()
      owner.revisions.set(event.query, (owner.revisions.get(event.query) ?? 0) + 1)
      // Invalidated bytes still occupy memory until refreshed, consumed or evicted.
      if (event.type === 'removed') owner.warm.delete(signature)
    }
    if (event.type === 'removed' && client.getQueryCache().getAll().length === 0) {
      for (const pending of owner.pending.values()) pending.controller.abort()
      owner.warm.clear()
      owners.delete(client)
      unsubscribe()
    }
  })
  return owner
}

/** A foreground consumer takes over the ordinary query lifetime and pixel ownership. */
export function consumeArtworkSource(client: QueryClient, key: ArtworkKey) {
  const owner = ownerFor(client)
  const query = client.getQueryCache().find({ queryKey: key, exact: true })
  if (query) owner.consumed.add(query)
  owner.warm.delete(JSON.stringify(key))
}

/** Share a pending byte read without letting one consumer cancel another's request. */
export async function readArtworkSource(
  client: QueryClient,
  key: ArtworkKey,
  signal: AbortSignal,
): Promise<ArtworkSource | undefined> {
  signal.throwIfAborted()
  const owner = ownerFor(client)
  const signature = JSON.stringify(key)
  let pending = owner.pending.get(signature)
  if (pending?.controller.signal.aborted) {
    await pending.ready.catch(() => undefined)
    signal.throwIfAborted()
    return readArtworkSource(client, key, signal)
  }
  if (!pending) {
    const controller = new AbortController()
    const entry: Pending = { controller, readers: 0, ready: Promise.resolve(undefined) }
    owner.pending.set(signature, entry)
    entry.ready = (async () => {
      const requestId = crypto.randomUUID().replaceAll('-', '')
      const cancel = () => {
        void window.winnow.cancelRequest?.(requestId).catch(() => undefined)
      }
      controller.signal.addEventListener('abort', cancel, { once: true })
      try {
        const source = window.winnow.artworkBytes
          ? await window.winnow.artworkBytes(key[1], key[2], key[3], requestId)
          : await window.winnow.artwork(key[1], key[2], key[3], requestId)
        controller.signal.throwIfAborted()
        return source ?? undefined
      } finally {
        controller.signal.removeEventListener('abort', cancel)
        if (owner.pending.get(signature) === entry) owner.pending.delete(signature)
      }
    })()
    pending = entry
  }
  const reading = pending
  reading.readers++
  let released = false
  const release = () => {
    if (released) return
    released = true
    reading.readers--
    if (reading.readers === 0 && owner.pending.get(signature) === reading) reading.controller.abort()
  }
  signal.addEventListener('abort', release, { once: true })
  try {
    // Keep the caller's queue slot until main acknowledges cancellation.
    const source = await reading.ready
    signal.throwIfAborted()
    return source
  } finally {
    signal.removeEventListener('abort', release)
    release()
  }
}

/** Backend PNGs have already been decoded and validated. Speculation retains no browser pixels. */
export async function preloadArtworkSource(client: QueryClient, key: ArtworkKey, signal: AbortSignal) {
  signal.throwIfAborted()
  const observer = new QueryObserver<ArtworkBytes>(client, {
    queryKey: key,
    enabled: false,
    gcTime: lifetime,
  })
  const unsubscribe = observer.subscribe(() => {})
  try {
    const query = observer.getCurrentQuery()
    const owner = ownerFor(client)
    const revision = owner.revisions.get(query) ?? 0
    if (
      query.state.data?.source &&
      !query.state.isInvalidated &&
      Date.now() - query.state.dataUpdatedAt < freshness
    ) {
      const signature = JSON.stringify(key)
      const retained = owner.warm.get(signature)
      if (retained) {
        // Returning to a title makes it more relevant than offscreen warm entries.
        owner.warm.delete(signature)
        owner.warm.set(signature, retained)
      }
      return true
    }
    const source = await readArtworkSource(client, key, signal)
    signal.throwIfAborted()
    if (!source) return false
    if ((owner.revisions.get(query) ?? 0) !== revision) return false
    // A concurrent foreground decode publishes its own validated bytes and owns their lifetime.
    if (owner.consumed.has(query)) return true
    const bytes = typeof source === 'string' ? source.length * 2 : source.byteLength
    if (bytes > maximumBytes) return true
    client.setQueryData<ArtworkBytes>(key, { source })
    owner.warm.delete(JSON.stringify(key))
    owner.warm.set(JSON.stringify(key), { key, source, bytes })
    let retained = [...owner.warm.values()].reduce((sum, entry) => sum + entry.bytes, 0)
    for (const [signature, entry] of owner.warm) {
      if (retained <= maximumBytes && owner.warm.size <= maximumEntries) break
      owner.warm.delete(signature)
      retained -= entry.bytes
      const obsolete = client.getQueryCache().find({ queryKey: entry.key, exact: true })
      if (
        obsolete &&
        !owner.consumed.has(obsolete) &&
        client.getQueryData<ArtworkBytes>(entry.key)?.source === entry.source
      )
        client.removeQueries({ queryKey: entry.key, exact: true })
    }
    return true
  } finally {
    unsubscribe()
  }
}
