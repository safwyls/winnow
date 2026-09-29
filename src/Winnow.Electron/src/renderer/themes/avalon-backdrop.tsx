import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { request } from '../api/client'
import { useSystemReducedMotion } from '../useSystemReducedMotion'
import { loadBackdropImage } from './avalon-backdrop-image'
import {
  AvalonBackdropController,
  backdropGeometry,
  backdropKey,
  type BackdropFrame,
  type BackdropLayer,
  type BackdropSelection,
  type BackdropSize,
} from './avalon-backdrop-model'
import './avalon-backdrop.css'

const emptyFrame: BackdropFrame = { current: null, outgoing: null, progress: 1, loading: true }

export function AvalonBackdrop({
  workId,
  fullscreen = true,
  cinematic = false,
  reducedMotion = false,
  className = '',
}: {
  workId: number
  fullscreen?: boolean
  cinematic?: boolean
  reducedMotion?: boolean
  className?: string
}) {
  const element = useRef<HTMLDivElement>(null)
  const controller = useRef<AvalonBackdropController | null>(null)
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
  const current = useRef({ workId, size })
  current.current = { workId, size: { ...size, fullscreen } }
  useLayoutEffect(() => {
    const node = element.current!
    const resize = () => {
      const bounds = node.getBoundingClientRect()
      const width = node.clientWidth,
        height = node.clientHeight
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
    const value = new AvalonBackdropController({
      resolve: (id, ratio, signal) =>
        request<BackdropSelection>('artwork.backdrop', { workId: id, aspectRatio: ratio }, undefined, signal),
      image: loadBackdropImage,
      publish: setFrame,
      requestFrame: (callback) => requestAnimationFrame(callback),
      cancelFrame: (id) => cancelAnimationFrame(id),
    })
    controller.current = value
    const unsubscribe = window.winnow?.onEvent?.((event) => {
      if (
        event.kind === 'resync-required' ||
        event.kind === 'library.changed' ||
        event.kind === 'preferences.changed'
      )
        value.select(current.current.workId, current.current.size, true)
    })
    return () => {
      unsubscribe?.()
      value.dispose()
      controller.current = null
    }
  }, [])
  useEffect(() => {
    controller.current?.setReducedMotion(reducedMotion || systemReduced || appReduced || !fullscreen)
  }, [reducedMotion, systemReduced, appReduced, fullscreen])
  useEffect(() => {
    controller.current?.select(workId, { ...size, fullscreen })
  }, [workId, size, fullscreen])

  function layer(value: BackdropLayer | null, outgoing: boolean) {
    if (!value) return null
    const geometry = backdropGeometry(
      value.candidate,
      { ...size, fullscreen },
      value.pixels.width / value.pixels.height,
    )
    return (
      <div
        className="avalon-backdrop-layer"
        data-outgoing={outgoing || undefined}
        data-key={backdropKey(value.candidate.key)}
        data-fallback={value.fallback || undefined}
        style={{ opacity: outgoing ? 1 : frame.progress }}
      >
        <div
          className="avalon-backdrop-art"
          data-fitted={geometry.fitted || undefined}
          style={{ width: geometry.width, height: geometry.height, left: geometry.left }}
        >
          <img src={value.pixels.source} alt="" />
          <div className="avalon-backdrop-veil" />
        </div>
      </div>
    )
  }
  return (
    <div
      ref={element}
      className={`avalon-backdrop ${className}`}
      aria-hidden="true"
      data-fullscreen={fullscreen || undefined}
      data-cinematic={cinematic || undefined}
      data-loading={frame.loading || undefined}
      data-crossfading={Boolean(frame.outgoing) || undefined}
      data-state={frame.current ? 'ready' : frame.loading ? 'loading' : 'missing'}
    >
      {layer(frame.outgoing, true)}
      {layer(frame.current, false)}
      <div className="avalon-backdrop-reading-veil" />
    </div>
  )
}
