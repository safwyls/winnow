import { QueryObserver, type QueryClient } from '@tanstack/react-query'
import { ArtworkCache, type OwnedArtwork } from './artwork-cache'

export const artworkFreshness = 120_000
export const artworkLifetime = 300_000
export type ArtworkKey = readonly ['artwork-image', string, string, number, string]
type Bytes = { source: string }
const owners = new WeakMap<QueryClient, ArtworkCache<OwnedArtwork>>()
const subscriptions = new WeakMap<QueryClient, () => void>()

export function artworkImages(client: QueryClient) {
  let cache = owners.get(client)
  if (cache) return cache
  cache = new ArtworkCache<OwnedArtwork>()
  owners.set(client, cache)
  const owned = cache
  const unsubscribe = client.getQueryCache().subscribe((event) => {
    const key = event.query.queryKey
    if (
      key[0] === 'artwork-image' &&
      key.length === 5 &&
      (event.type === 'removed' || (event.type === 'updated' && event.action.type === 'invalidate'))
    )
      owned.invalidate(JSON.stringify(key))
    if (event.type === 'removed' && client.getQueryCache().getAll().length === 0) {
      unsubscribe()
      if (owners.get(client) === owned) owners.delete(client)
      subscriptions.delete(client)
      void owned.close()
    }
  })
  subscriptions.set(client, unsubscribe)
  return cache
}

export async function closeArtworkImages(client: QueryClient) {
  const cache = owners.get(client)
  subscriptions.get(client)?.()
  subscriptions.delete(client)
  await cache?.close()
}

/** Cache bytes separately from decoded pixels so eviction need not repeat a backend read. */
export async function loadArtworkImage(client: QueryClient, key: ArtworkKey, signal: AbortSignal) {
  signal.throwIfAborted()
  const observer = new QueryObserver<Bytes>(client, {
    queryKey: key,
    gcTime: artworkLifetime,
    enabled: false,
  })
  const unsubscribe = observer.subscribe(() => {})
  try {
    const query = observer.getCurrentQuery()
    let source = query.state.data?.source
    if (!source || query.state.isInvalidated || Date.now() - query.state.dataUpdatedAt >= artworkFreshness) {
      const requestId = crypto.randomUUID().replaceAll('-', '')
      const cancel = () => {
        void window.winnow.cancelRequest?.(requestId).catch(() => undefined)
      }
      signal.addEventListener('abort', cancel, { once: true })
      try {
        source = (await window.winnow.artwork(key[1], key[2], key[3], requestId)) ?? undefined
        signal.throwIfAborted()
        if (!source) return null
        client.setQueryData<Bytes>(key, { source })
      } finally {
        signal.removeEventListener('abort', cancel)
      }
    }
    const pixels = await decodeArtworkImage(source, signal)
    if (!pixels && !signal.aborted) client.removeQueries({ queryKey: key, exact: true })
    return pixels
  } finally {
    unsubscribe()
  }
}

export async function decodeArtworkImage(source: string, signal: AbortSignal): Promise<OwnedArtwork | null> {
  signal.throwIfAborted()
  if (!source.startsWith('data:image/png;base64,')) return null
  const bytes = Uint8Array.from(atob(source.slice('data:image/png;base64,'.length)), (character) =>
    character.charCodeAt(0),
  )
  const url = URL.createObjectURL(new Blob([bytes], { type: 'image/png' }))
  const image = new Image()
  let released = false
  const dispose = () => {
    if (released) return
    released = true
    image.src = ''
    URL.revokeObjectURL(url)
  }
  signal.addEventListener('abort', dispose, { once: true })
  try {
    image.src = url
    await image.decode()
    signal.throwIfAborted()
    if (!image.naturalWidth || !image.naturalHeight) {
      dispose()
      return null
    }
    return { source: url, width: image.naturalWidth, height: image.naturalHeight, dispose }
  } catch {
    dispose()
    return null
  } finally {
    signal.removeEventListener('abort', dispose)
  }
}
