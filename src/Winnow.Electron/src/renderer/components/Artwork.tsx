import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import { ImageOff } from 'lucide-react'
import { CoverPadding } from './CoverPadding'
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

export function useArtworkSelection(workId: number, hero = false, enabled = true) {
  return useQuery({
    queryKey: ['artwork', workId, hero],
    enabled,
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
}

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
  const [upgradeFailed, setUpgradeFailed] = useState(false)
  const [snapshot, setSnapshot] = useState<{
    key: ArtworkKey
    identity: string
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
      if (node.dataset.state === 'error' || node.dataset.retry === 'true')
        setMeasureRetry((value) => value + 1)
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
  const state = useArtworkSelection(workId, hero)
  const key = state.data?.selection?.current?.previewKey
  const identity = key ? JSON.stringify([view, key.provider, key.id, state.data!.selection!.revision]) : ''
  // A realized surface keeps its best size; recycling starts from the new surface's measured width.
  const requestWidth = Math.max(
    width,
    snapshot?.identity === identity && snapshot.asset && snapshot.asset !== failure ? snapshot.key[3] : 0,
  )
  const imageKey: ArtworkKey | null =
    key && requestWidth
      ? ['artwork-image', key.provider, key.id, requestWidth, state.data!.selection!.revision]
      : null
  const signature = imageKey ? JSON.stringify(imageKey) : ''
  useLayoutEffect(() => {
    setUpgradeFailed(false)
    setSnapshot((previous) => (previous?.identity === identity ? previous : null))
    if (!imageKey) {
      return
    }
    const lease = cache.acquire(signature, (signal) => loadArtworkImage(client, imageKey, signal))
    leases.current.add(lease)
    let active = true,
      published = false
    const retained = snapshot?.identity === identity && snapshot.asset !== failure ? snapshot.asset : null
    const publish = (asset: OwnedArtwork | null) => {
      if (!active) return
      published = true
      if (!asset && retained) {
        lease.release()
        leases.current.delete(lease)
        setUpgradeFailed(true)
        return
      }
      setSnapshot({ key: imageKey, identity, asset, lease })
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
  }, [cache, client, signature, state.data?.read, identity, view, measureRetry])
  useLayoutEffect(
    () => () => {
      if (snapshot) {
        snapshot.lease.release()
        leases.current.delete(snapshot.lease)
      }
    },
    [snapshot],
  )
  const current = Boolean(identity && snapshot?.identity === identity)
  const asset = current ? snapshot!.asset : null
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
      data-retry={upgradeFailed || undefined}
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
      {className.split(/\s+/).includes('artwork-edge-padding') && (
        <CoverPadding image={ready && !failed ? image.current : null} owner={asset} />
      )}
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
            client.removeQueries({ queryKey: snapshot!.key, exact: true })
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
