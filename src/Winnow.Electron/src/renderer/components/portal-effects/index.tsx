import { useEffect, useRef, type ReactNode } from 'react'
import { useSystemReducedMotion } from '../../useSystemReducedMotion'
import { PortalController } from './controller'
import { normalizePortalOptions, type PortalOptions, type PortalPoint, type PortalRect } from './geometry'
import './portal-effects.css'

export * from './geometry'

export interface PortalSurfaceProps {
  children: ReactNode
  artwork?: ReactNode
  className?: string
  options?: Partial<PortalOptions>
  reducedMotion?: boolean
  active?: boolean
  /** Cursor position in local CSS pixels. It may sit just outside the reading plane. */
  origin?: PortalPoint
  /** Source portal bounds in local CSS pixels; expands to cover the entire surface. */
  expansion?: PortalRect
  onExpanded?: () => void
}

/** The parent supplies fixed dimensions, content and cached artwork; this owns only the reveal. */
export function PortalSurface({
  children,
  artwork,
  className = '',
  options,
  reducedMotion = false,
  active = true,
  origin,
  expansion,
  onExpanded,
}: PortalSurfaceProps) {
  const root = useRef<HTMLDivElement>(null)
  const art = useRef<HTMLDivElement>(null)
  const reveal = useRef<HTMLDivElement>(null)
  const scene = useRef<HTMLDivElement>(null)
  const content = useRef<HTMLDivElement>(null)
  const controller = useRef<PortalController | null>(null)
  const expandedCallback = useRef(onExpanded)
  expandedCallback.current = onExpanded
  const systemMotion = useSystemReducedMotion()
  const still = reducedMotion || systemMotion
  const normalized = normalizePortalOptions(options)
  const latestOptions = useRef(normalized)
  latestOptions.current = normalized
  useEffect(() => {
    if (!active || !root.current || !art.current || !scene.current || !content.current || !reveal.current)
      return
    const instance = new PortalController(
      {
        root: root.current,
        artwork: art.current,
        scene: scene.current,
        content: content.current,
        reveal: reveal.current,
      },
      {
        options: latestOptions.current,
        reducedMotion: still,
        origin,
        expansion,
        onExpanded: () => expandedCallback.current?.(),
      },
    )
    controller.current = instance
    return () => {
      controller.current = null
      instance.dispose()
    }
  }, [active, still, origin?.x, origin?.y, expansion?.x, expansion?.y, expansion?.width, expansion?.height])
  useEffect(() => {
    controller.current?.setOptions(normalized)
  }, [normalized.roundness, normalized.waviness, normalized.activity])
  return (
    <div
      ref={root}
      className={`winnow-portal-surface ${className}`}
      hidden={!active}
      data-portal-expansion={!!expansion}
    >
      <div ref={reveal} className="winnow-portal-reveal">
        <div className="winnow-portal-art-shadow" aria-hidden="true">
          <div ref={art} className="winnow-portal-art">
            {artwork}
          </div>
        </div>
        <div ref={content} className="winnow-portal-content">
          {children}
        </div>
      </div>
      <div ref={scene} className="winnow-portal-scene" aria-hidden="true" />
    </div>
  )
}
