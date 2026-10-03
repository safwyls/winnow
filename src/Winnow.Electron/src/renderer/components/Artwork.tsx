import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import { ArtworkPlaceholder } from './ArtworkPlaceholder'
import { CoverPadding } from './CoverPadding'
import { artworkWidth, type ArtworkLease, type OwnedArtwork } from './artwork-cache'
import { ApiError } from '../api/client'
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
const imageRetryDelays = [2_000, 10_000, 30_000]

export function useArtworkSelection(workId: number, hero = false, enabled = true) {
  return useQuery({
    queryKey: ['artwork', workId, hero],
    enabled,
    staleTime: artworkFreshness,
    gcTime: artworkLifetime,
    retry: (failures, error) =>
      failures < imageRetryDelays.length &&
      (!(error instanceof ApiError) || error.status === 0 || error.status === 429 || error.status >= 500),
    retryDelay: (attempt) => imageRetryDelays[attempt] ?? imageRetryDelays.at(-1)!,
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
        if (!result.ok) throw new ApiError(result.status, 'Artwork state could not be loaded.')
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
  const imageRead = useRef({ signature: '', pending: false, completedAt: 0 })
  const invalidatedImage = useRef(false)
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
    // A metadata refresh must not restart an unchanged download. Once pixels age out,
    // a successful selection read can revalidate them without requiring another resize.
    if (
      !invalidatedImage.current &&
      signature &&
      imageRead.current.signature === signature &&
      !imageRead.current.pending &&
      (!snapshot?.asset ||
        snapshot.asset === failure ||
        upgradeFailed ||
        Date.now() - imageRead.current.completedAt >= artworkFreshness)
    )
      setMeasureRetry((value) => value + 1)
  }, [state.data?.read])
  useLayoutEffect(
    () =>
      client.getQueryCache().subscribe((event) => {
        if (
          event.type === 'updated' &&
          event.action.type === 'invalidate' &&
          JSON.stringify(event.query.queryKey) === signature
        ) {
          invalidatedImage.current = true
          setMeasureRetry((value) => value + 1)
        }
      }),
    [client, signature],
  )
  useLayoutEffect(() => {
    invalidatedImage.current = false
    setUpgradeFailed(false)
    setSnapshot((previous) => (previous?.identity === identity ? previous : null))
    if (!imageKey) {
      return
    }
    let active = true,
      published = false
    let lease: ArtworkLease<OwnedArtwork>
    let retry: ReturnType<typeof setTimeout> | undefined
    const retained = snapshot?.identity === identity && snapshot.asset !== failure ? snapshot.asset : null
    const load = (attempt: number) => {
      imageRead.current = { ...imageRead.current, signature, pending: true }
      lease = cache.acquire(signature, (signal) => loadArtworkImage(client, imageKey, signal))
      leases.current.add(lease)
      published = false
      const publish = (asset: OwnedArtwork | null) => {
        if (!active) return
        imageRead.current = { signature, pending: false, completedAt: Date.now() }
        published = true
        if (!asset && retained) {
          lease.release()
          leases.current.delete(lease)
          setUpgradeFailed(true)
        } else {
          setSnapshot({ key: imageKey, identity, asset, lease })
          setFailure(null)
          setUpgradeFailed(false)
        }
        // A timeout or a busy backend must not leave a mounted cover empty until a
        // resize. Bound retries because a selected source can also have no image.
        if (!asset && attempt < imageRetryDelays.length)
          retry = setTimeout(() => load(attempt + 1), imageRetryDelays[attempt])
      }
      if (lease.current) publish(lease.current)
      else void lease.ready.then(publish)
    }
    load(0)
    return () => {
      active = false
      imageRead.current.pending = false
      clearTimeout(retry)
      if (!published) {
        lease.release()
        leases.current.delete(lease)
      }
    }
  }, [cache, client, signature, identity, view, measureRetry])
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
    (state.isPending && state.failureCount === 0) ||
    Boolean(key && (!width || !current)) ||
    Boolean(asset && !ready && !failed)
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
          : failed || (!asset && (state.isError || state.failureCount > 0 || Boolean(key)))
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
        // Bytes are already decoded and the virtualized surface owns visibility.
        // A second browser lazy-load gate can strand an image inside transformed covers.
        <img
          ref={attachImage}
          src={asset.source}
          alt=""
          loading="eager"
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
        (!asset || failed) && <ArtworkPlaceholder />
      )}
    </div>
  )
}
