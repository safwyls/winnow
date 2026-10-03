import type { BackdropKey, BackdropPixels } from './avalon-backdrop-model'
import { decodeArtworkImage } from '../components/artwork-images'
import type { ArtworkSource } from '../components/artwork-decode'
import type { QueryClient } from '@tanstack/react-query'
import { artworkImages, loadArtworkImage, type ArtworkKey } from '../components/artwork-images'

/** Backdrops share the bounded image cache; each transition layer keeps its own lease. */
export async function loadCachedBackdropImage(
  client: QueryClient,
  key: BackdropKey,
  width: number,
  signal: AbortSignal,
): Promise<BackdropPixels | null> {
  signal.throwIfAborted()
  const imageKey: ArtworkKey = ['artwork-image', key.provider, key.id, width, 'asset']
  const lease = artworkImages(client).acquire(JSON.stringify(imageKey), (pending) =>
    loadArtworkImage(client, imageKey, pending),
  )
  const release = () => lease.release()
  signal.addEventListener('abort', release, { once: true })
  try {
    const pixels = lease.current ?? (await lease.ready)
    signal.throwIfAborted()
    if (!pixels) {
      release()
      return null
    }
    return { ...pixels, dispose: release }
  } catch (error) {
    release()
    throw error
  } finally {
    signal.removeEventListener('abort', release)
  }
}

/** Each visible transition layer owns its URL; aborted and detached layers release it immediately. */
export async function loadBackdropImage(
  key: BackdropKey,
  width: number,
  signal: AbortSignal,
): Promise<BackdropPixels | null> {
  signal.throwIfAborted()
  const requestId = crypto.randomUUID().replaceAll('-', '')
  const cancel = () => {
    void window.winnow.cancelRequest?.(requestId).catch(() => undefined)
  }
  signal.addEventListener('abort', cancel, { once: true })
  let source: ArtworkSource | null
  try {
    source = window.winnow.artworkBytes
      ? await window.winnow.artworkBytes(key.provider, key.id, width, requestId)
      : await window.winnow.artwork(key.provider, key.id, width, requestId)
  } finally {
    signal.removeEventListener('abort', cancel)
  }
  signal.throwIfAborted()
  return source ? decodeArtworkImage(source, signal) : null
}
