import { QueryObserver, type QueryClient } from '@tanstack/react-query'
import { ArtworkCache, type OwnedArtwork } from './artwork-cache'
import { ArtworkDecoder } from './artwork-decode'
export { decodeArtworkImage } from './artwork-decode'

export const artworkFreshness = 120_000
export const artworkLifetime = 300_000
export type ArtworkKey = readonly ['artwork-image', string, string, number, string]
type Bytes = { source: string }
const owners = new WeakMap<QueryClient, ArtworkCache<OwnedArtwork>>()
const decoders = new WeakMap<QueryClient, ArtworkDecoder>()
const subscriptions = new WeakMap<QueryClient, () => void>()

export function artworkImages(client: QueryClient) {
  let cache = owners.get(client)
  if (cache) return cache
  cache = new ArtworkCache<OwnedArtwork>({ concurrent: 128 })
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
      decoders.get(client)?.close()
      decoders.delete(client)
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
  decoders.get(client)?.close()
  decoders.delete(client)
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
    const fetchSource = async () => {
      const requestId = crypto.randomUUID().replaceAll('-', '')
      const cancel = () => {
        void window.winnow.cancelRequest?.(requestId).catch(() => undefined)
      }
      signal.addEventListener('abort', cancel, { once: true })
      try {
        const source = (await window.winnow.artwork(key[1], key[2], key[3], requestId)) ?? undefined
        signal.throwIfAborted()
        return source
      } finally {
        signal.removeEventListener('abort', cancel)
      }
    }
    let source = query.state.data?.source
    if (!source || query.state.isInvalidated || Date.now() - query.state.dataUpdatedAt >= artworkFreshness)
      source = await fetchSource()
    if (!source) return null
    let decoder = decoders.get(client)
    if (!decoder) {
      decoder = new ArtworkDecoder()
      decoders.set(client, decoder)
    }
    const decoding = decoder.decode(source, signal, fetchSource, (current) =>
      client.setQueryData<Bytes>(key, { source: current }),
    )
    source = undefined
    const pixels = await decoding
    if (!pixels && !signal.aborted) client.removeQueries({ queryKey: key, exact: true })
    return pixels
  } finally {
    unsubscribe()
  }
}
