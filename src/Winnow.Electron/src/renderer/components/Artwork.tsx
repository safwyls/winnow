import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import { ImageOff } from 'lucide-react'
import { artworkWidth, type ArtworkLease, type OwnedArtwork } from './artwork-cache'
import {
  artworkFreshness,
  artworkImages,
  artworkLifetime,
  loadArtworkImage,
  type ArtworkKey,
} from './artwork-images'

interface ArtState {
  current: { previewKey: { provider: string; id: string } } | null
  revision: string
}
let selectionRead = 0

export function Artwork({
  workId,
  hero = false,
  className = '',
  eager = false,
}: {
  workId: number
  hero?: boolean
  className?: string
  eager?: boolean
}) {
  const client = useQueryClient(),
    cache = artworkImages(client)
  const root = useRef<HTMLDivElement>(null),
    image = useRef<HTMLImageElement>(null)
  const leases = useRef(new Set<ArtworkLease<OwnedArtwork>>())
  const view = `${workId}:${hero}`
  const [width, setWidth] = useState(0)
  const [measureRetry, setMeasureRetry] = useState(0)
  const [snapshot, setSnapshot] = useState<{
    signature: string
    view: string
    asset: OwnedArtwork | null
    lease: ArtworkLease<OwnedArtwork>
  } | null>(null)
  const [loaded, setLoaded] = useState<{ view: string; source: string } | null>(null)
  const [failure, setFailure] = useState<OwnedArtwork | null>(null)
  const attachImage = useCallback((node: HTMLImageElement | null) => {
    if (!node) image.current?.removeAttribute('src')
    image.current = node
  }, [])
  useLayoutEffect(() => {
    const node = root.current!
    const measure = () => {
      const pixels = node.getBoundingClientRect().width * (window.devicePixelRatio || 1)
      if (pixels > 0) setWidth(artworkWidth(pixels))
      if (node.dataset.state === 'error') setMeasureRetry((value) => value + 1)
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure)
    observer?.observe(node)
    window.addEventListener('resize', measure)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [])
  // Clear DOM sources before dropping the last pixel hold, including an evicted visible image.
  useLayoutEffect(
    () => () => {
      image.current?.removeAttribute('src')
      for (const lease of leases.current) lease.release()
      leases.current.clear()
    },
    [],
  )
  const state = useQuery({
    queryKey: ['artwork', workId, hero],
    staleTime: artworkFreshness,
    gcTime: artworkLifetime,
    retry: false,
    queryFn: async ({ signal }) => {
      const requestId = crypto.randomUUID().replaceAll('-', '')
      const cancel = () => {
        void window.winnow.cancelRequest?.(requestId).catch(() => undefined)
      }
      signal.addEventListener('abort', cancel, { once: true })
      try {
        const result = await window.winnow.request<ArtState>({
          route: 'artworkState',
          params: { workId, slot: hero ? 'Hero' : 'Cover' },
          requestId,
        })
        signal.throwIfAborted()
        if (!result.ok) throw new Error('Artwork state could not be loaded.')
        return { selection: result.data ?? null, read: ++selectionRead }
      } finally {
        signal.removeEventListener('abort', cancel)
      }
    },
  })
  const key = state.data?.selection?.current?.previewKey
  const imageKey: ArtworkKey | null =
    key && width ? ['artwork-image', key.provider, key.id, width, state.data!.selection!.revision] : null
  const signature = imageKey ? JSON.stringify(imageKey) : ''
  useLayoutEffect(() => {
    if (!imageKey) {
      setSnapshot(null)
      return
    }
    const lease = cache.acquire(signature, (signal) => loadArtworkImage(client, imageKey, signal))
    leases.current.add(lease)
    let active = true,
      published = false
    const publish = (asset: OwnedArtwork | null) => {
      if (!active) return
      published = true
      setSnapshot({ signature, view, asset, lease })
      setFailure(null)
    }
    if (lease.current) publish(lease.current)
    else void lease.ready.then(publish)
    return () => {
      active = false
      if (!published) {
        lease.release()
        leases.current.delete(lease)
      }
    }
  }, [cache, client, signature, state.data?.read, view, measureRetry])
  useLayoutEffect(
    () => () => {
      if (snapshot) {
        snapshot.lease.release()
        leases.current.delete(snapshot.lease)
      }
    },
    [snapshot],
  )
  const current = snapshot?.signature === signature && snapshot?.view === view
  const asset = current ? snapshot.asset : null
  const failed = Boolean(asset && failure === asset)
  const ready = Boolean(asset && loaded?.view === view && loaded.source === asset.source)
  const loading =
    state.isPending || Boolean(key && (!width || !current)) || Boolean(asset && !ready && !failed)
  return (
    <div
      ref={root}
      className={`artwork ${className}`}
      aria-hidden="true"
      data-loading={loading || undefined}
      data-state={
        loading
          ? 'loading'
          : failed || (!asset && (state.isError || Boolean(key)))
            ? 'error'
            : asset
              ? 'ready'
              : 'missing'
      }
    >
      {asset && !failed && (
        <img
          ref={attachImage}
          src={asset.source}
          alt=""
          loading={eager ? 'eager' : 'lazy'}
          className={ready ? 'art-ready' : ''}
          onLoad={() => setLoaded({ view, source: asset.source })}
          onError={() => {
            setFailure(asset)
            setLoaded(null)
            client.removeQueries({ queryKey: imageKey!, exact: true })
          }}
        />
      )}
      {loading ? (
        <div className={`art-loading${hero ? ' art-loading-hero' : ''}`}>
          {hero && <span className="art-loading-orbit" />}
        </div>
      ) : (
        (!asset || failed) && (
          <div className="art-placeholder">
            <ImageOff size={24} strokeWidth={1} />
            <span>Artwork unavailable</span>
          </div>
        )
      )}
    </div>
  )
}
