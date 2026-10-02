import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import type { ArtworkLease, OwnedArtwork } from './artwork-cache'
import { artworkImages, loadArtworkImage, type ArtworkKey } from './artwork-images'

/** A selected asset has the same visible lease lifetime as a work's selected cover. */
export function ArtworkAsset({
  asset,
  width,
  alt,
}: {
  asset: { provider: string; id: string }
  width: number
  alt: string
}) {
  const client = useQueryClient(),
    cache = artworkImages(client)
  const identity = JSON.stringify([asset.provider, asset.id, width])
  const image = useRef<HTMLImageElement | null>(null)
  const leases = useRef(new Set<ArtworkLease<OwnedArtwork>>())
  const queryKey = useRef('')
  const [revision, setRevision] = useState(0)
  const [snapshot, setSnapshot] = useState<{
    identity: string
    pixels: OwnedArtwork | null
    lease?: ArtworkLease<OwnedArtwork>
  } | null>(null)
  const attach = useCallback((node: HTMLImageElement | null) => {
    if (!node) image.current?.removeAttribute('src')
    image.current = node
  }, [])
  useLayoutEffect(
    () =>
      client.getQueryCache().subscribe((event) => {
        if (
          event.type === 'updated' &&
          event.action.type === 'invalidate' &&
          JSON.stringify(event.query.queryKey) === queryKey.current
        )
          setRevision((value) => value + 1)
      }),
    [client],
  )
  useLayoutEffect(
    () => () => {
      image.current?.removeAttribute('src')
      for (const lease of leases.current) lease.release()
      leases.current.clear()
    },
    [],
  )
  useLayoutEffect(() => {
    let active = true
    let published = false
    let lease: ArtworkLease<OwnedArtwork> | undefined
    setSnapshot((previous) => (previous?.identity === identity ? previous : null))
    const retained = snapshot?.identity === identity ? snapshot.pixels : null
    const publish = (pixels: OwnedArtwork | null) => {
      if (!active) return
      published = true
      if (!pixels && lease?.failed && retained) {
        lease?.release()
        if (lease) leases.current.delete(lease)
      } else setSnapshot({ identity, pixels, lease })
    }
    async function load() {
      let id = asset.id
      if (asset.provider.startsWith('plugin-')) {
        const url = new URL(id)
        if (url.protocol !== 'https:' || url.username || url.password || url.hash || url.port) {
          publish(null)
          return
        }
        id = Array.from(
          new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(url.href))),
        )
          .map((byte) => byte.toString(16).padStart(2, '0'))
          .join('')
      }
      if (!active) return
      const key: ArtworkKey = ['artwork-image', asset.provider, id, width, 'asset']
      queryKey.current = JSON.stringify(key)
      lease = cache.acquire(queryKey.current, (signal) => loadArtworkImage(client, key, signal))
      leases.current.add(lease)
      if (lease.current) publish(lease.current)
      else publish(await lease.ready)
    }
    void load().catch(() => publish(null))
    return () => {
      active = false
      if (lease && !published) {
        lease.release()
        leases.current.delete(lease)
      }
    }
  }, [asset.provider, asset.id, width, identity, client, cache, revision])
  useLayoutEffect(
    () => () => {
      if (snapshot?.lease) {
        snapshot.lease.release()
        leases.current.delete(snapshot.lease)
      }
    },
    [snapshot],
  )
  const current = snapshot?.identity === identity ? snapshot : null
  return current?.pixels ? (
    <img key={identity} ref={attach} src={current.pixels.source} alt={alt} />
  ) : (
    <span className="art-placeholder">{current ? 'Screenshot unavailable' : 'Loading screenshot…'}</span>
  )
}
