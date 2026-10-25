import { useContext, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { QueryClientContext } from '@tanstack/react-query'
import { resolveBackdropSelection } from './backdrop-selection'
import { loadBackdropImage, loadCachedBackdropImage } from './backdrop-image'
import { BackdropController, type BackdropFrame, type BackdropSize } from './backdrop-model'
import { useSystemReducedMotion } from '../useSystemReducedMotion'
import { shouldRefreshArtwork } from '../refresh'

const emptyFrame: BackdropFrame = { current: null, outgoing: null, progress: 1, loading: true }
export interface BackdropProps {
  workId: number
  coverWorkId?: number
  fullscreen?: boolean
  reducedMotion?: boolean
  className?: string
}

/** Selection, pixel ownership and refresh are shared; compositions own their visual treatment. */
export function useBackdrop({
  workId,
  coverWorkId,
  fullscreen = false,
  reducedMotion = false,
  fitWholeHero = true,
}: BackdropProps & { fitWholeHero?: boolean }) {
  const client = useContext(QueryClientContext)
  const element = useRef<HTMLDivElement>(null)
  const controller = useRef<BackdropController | null>(null)
  const [frame, setFrame] = useState(emptyFrame)
  const [size, setSize] = useState<BackdropSize>({ width: 0, height: 0, scale: 1, fullscreen })
  const systemReduced = useSystemReducedMotion()
  const [appReduced, setAppReduced] = useState(() =>
    document.documentElement.classList.contains('reduced-motion'),
  )
  useEffect(() => {
    const root = document.documentElement
    const update = () => setAppReduced(root.classList.contains('reduced-motion'))
    const observer = new MutationObserver(update)
    observer.observe(root, { attributes: true, attributeFilter: ['class'] })
    update()
    return () => observer.disconnect()
  }, [])
  const current = useRef({ workId, coverWorkId, size })
  const previousCover = useRef(coverWorkId)
  current.current = { workId, coverWorkId, size: { ...size, fullscreen, fitWholeHero } }
  useLayoutEffect(() => {
    const node = element.current!
    const resize = () => {
      const bounds = node.getBoundingClientRect()
      // Integer client dimensions can turn a 21:9 viewport into a narrower ratio under zoom.
      const layout = getComputedStyle(node)
      const width = Number.parseFloat(layout.width) || node.clientWidth,
        height = Number.parseFloat(layout.height) || node.clientHeight
      setSize((previous) =>
        width === previous.width &&
        height === previous.height &&
        previous.scale === devicePixelRatio * (bounds.width / width || 1)
          ? previous
          : { width, height, fullscreen, scale: devicePixelRatio * (bounds.width / width || 1) },
      )
    }
    resize()
    const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(resize) : undefined
    observer?.observe(node)
    window.addEventListener('resize', resize)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', resize)
    }
  }, [fullscreen])
  useEffect(() => {
    const value = new BackdropController({
      resolve: (id, ratio, signal) =>
        resolveBackdropSelection(id, ratio, current.current.coverWorkId, signal),
      image: client
        ? (key, width, signal) => loadCachedBackdropImage(client, key, width, signal)
        : loadBackdropImage,
      publish: setFrame,
      requestFrame: (callback) => requestAnimationFrame(callback),
      cancelFrame: (id) => cancelAnimationFrame(id),
    })
    controller.current = value
    let active = true,
      refreshQueued = false
    const refresh = () => {
      if (!active || refreshQueued) return
      refreshQueued = true
      // One cache invalidation can publish hundreds of query events. Revalidate once
      // after their leases retire, including invalidations arriving after the SSE event.
      queueMicrotask(() => {
        refreshQueued = false
        if (active) value.select(current.current.workId, current.current.size, true)
      })
    }
    const stopCache = client?.getQueryCache().subscribe((event) => {
      if (
        event.type === 'updated' &&
        event.action.type === 'invalidate' &&
        event.query.queryKey[0] === 'artwork-image' &&
        event.query.queryKey[4] === 'asset'
      )
        refresh()
    })
    const unsubscribe = window.winnow?.onEvent?.((event) => {
      if (
        event.kind === 'resync-required' ||
        event.kind === 'library.changed' ||
        event.kind === 'preferences.changed'
      ) {
        if (client && shouldRefreshArtwork(event))
          void client.invalidateQueries({ queryKey: ['artwork-image'], refetchType: 'none' })
        refresh()
      }
    })
    return () => {
      active = false
      stopCache?.()
      unsubscribe?.()
      value.dispose()
      controller.current = null
    }
  }, [client])
  useEffect(() => {
    controller.current?.setReducedMotion(reducedMotion || systemReduced || appReduced || !fullscreen)
  }, [reducedMotion, systemReduced, appReduced, fullscreen])
  useEffect(() => {
    controller.current?.select(
      workId,
      { ...size, fullscreen, fitWholeHero },
      previousCover.current !== coverWorkId,
    )
    previousCover.current = coverWorkId
  }, [workId, coverWorkId, size, fullscreen, fitWholeHero])

  return { element, frame, size: { ...size, fullscreen, fitWholeHero } }
}
