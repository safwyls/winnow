import {
  forwardRef,
  useEffect,
  useId,
  useImperativeHandle,
  useLayoutEffect,
  useRef,
  useState,
  type ComponentPropsWithoutRef,
  type ReactNode,
} from 'react'
import { createPortal } from 'react-dom'
import type { ThemeContext } from '../../shared/theme'
import { request, storeLabel } from '../api/client'
import type { GameDetails, LibraryGame } from '../api/types'
import { detailIdle, detailPlaytime, receptionFigures } from '../features/details-facts'

const openedEvent = 'winnow:avalon-preview-opened'
type Rating = GameDetails['ratings'][number]

export function compactAvalonRatings(ratings: Rating[]): string {
  return receptionFigures(ratings)
    .map((figure) => `${figure.compactSource}: ${figure.compactValue}`)
    .join(' · ')
}

/** Artwork, surface and outline share the same geometry, including the pointer. */
export function previewBubbleOutline(width: number, height: number, arrowOnRight = false, offset = 40) {
  if (width <= 23 || height <= 13) return ''
  const left = arrowOnRight ? 0.5 : 10.5,
    right = width - (arrowOnRight ? 10.5 : 0.5)
  const top = 0.5,
    bottom = height - 0.5,
    radius = 6
  const half = Math.min(9, Math.max(0, (bottom - top - 2 * radius) / 2))
  const arrow = Math.max(
    top + radius + half,
    Math.min(bottom - radius - half, Number.isFinite(offset) ? offset : 40),
  )
  return [
    `M ${left + radius} ${top}`,
    `L ${right - radius} ${top}`,
    `Q ${right} ${top} ${right} ${top + radius}`,
    ...(arrowOnRight
      ? [`L ${right} ${arrow - half}`, `L ${width - 0.5} ${arrow}`, `L ${right} ${arrow + half}`]
      : []),
    `L ${right} ${bottom - radius}`,
    `Q ${right} ${bottom} ${right - radius} ${bottom}`,
    `L ${left + radius} ${bottom}`,
    `Q ${left} ${bottom} ${left} ${bottom - radius}`,
    ...(!arrowOnRight ? [`L ${left} ${arrow + half}`, `L 0.5 ${arrow}`, `L ${left} ${arrow - half}`] : []),
    `L ${left} ${top + radius}`,
    `Q ${left} ${top} ${left + radius} ${top}`,
    'Z',
  ].join(' ')
}

export const AvalonPreviewBubble = forwardRef<
  HTMLElement,
  ComponentPropsWithoutRef<'aside'> & {
    artwork?: ReactNode
    arrowOnRight?: boolean
    arrowOffset?: number
  }
>(function AvalonPreviewBubble(
  { artwork, arrowOnRight = false, arrowOffset = 40, children, className = '', ...props },
  ref,
) {
  const root = useRef<HTMLElement>(null)
  useImperativeHandle(ref, () => root.current!, [])
  const clip = useId().replaceAll(':', '')
  const [size, setSize] = useState({ width: 0, height: 0 })
  useLayoutEffect(() => {
    const measure = () => {
      const node = root.current
      if (!node) return
      const width = node.offsetWidth,
        height = node.offsetHeight
      setSize((previous) =>
        previous.width === width && previous.height === height ? previous : { width, height },
      )
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure)
    if (root.current) observer?.observe(root.current)
    return () => observer?.disconnect()
  }, [])
  const outline = previewBubbleOutline(size.width, size.height, arrowOnRight, arrowOffset)
  return (
    <aside
      {...props}
      ref={root}
      className={`avalon-hover-preview ${className}`}
      data-arrow-side={arrowOnRight ? 'right' : 'left'}
    >
      <svg className="avalon-preview-shape" width="100%" height="100%" aria-hidden="true">
        <defs>
          <clipPath id={clip}>
            <path d={outline} />
          </clipPath>
        </defs>
        <path d={outline} className="avalon-preview-fill" />
        <foreignObject x="0" y="0" width="100%" height="100%" clipPath={`url(#${clip})`}>
          <div className="avalon-preview-art">{artwork}</div>
        </foreignObject>
        <path d={outline} className="avalon-preview-outline" />
      </svg>
      <div className="avalon-preview-copy">{children}</div>
    </aside>
  )
})

export function useAvalonPreview(context: ThemeContext, game: LibraryGame, _reason?: string) {
  const identity = useId(),
    [target, setTarget] = useState<HTMLButtonElement | null>(null)
  const currentWork = useRef(game)
  useLayoutEffect(() => {
    if (currentWork.current !== game) {
      currentWork.current = game
      setTarget(null)
    }
  }, [game])
  useEffect(() => {
    const opened = (event: Event) => {
      if ((event as CustomEvent<string>).detail !== identity) setTarget(null)
    }
    window.addEventListener(openedEvent, opened)
    return () => window.removeEventListener(openedEvent, opened)
  }, [identity])
  return {
    open: (button: HTMLButtonElement) => {
      window.dispatchEvent(new CustomEvent(openedEvent, { detail: identity }))
      setTarget(button)
    },
    close: () => setTarget(null),
    preview:
      target && currentWork.current === game ? (
        <AvalonPreview
          key={game.workId}
          context={context}
          game={game}
          target={target}
          close={() => setTarget(null)}
        />
      ) : null,
  }
}

function AvalonPreview({
  context,
  game,
  target,
  close,
}: {
  context: ThemeContext
  game: LibraryGame
  target: HTMLButtonElement
  close(): void
}) {
  const id = useId(),
    [ratings, setRatings] = useState<Rating[]>([])
  const closeRef = useRef(close)
  closeRef.current = close
  useEffect(() => {
    let active = true
    const controller = new AbortController()
    void request<GameDetails>('game.details', { workId: game.workId }, undefined, controller.signal).then(
      (detail) => {
        if (active) setRatings(detail.ratings ?? [])
      },
      () => {},
    )
    return () => {
      active = false
      controller.abort()
    }
  }, [game.workId])
  useEffect(() => {
    const previous = target.getAttribute('aria-describedby')
    target.setAttribute('aria-describedby', previous ? `${previous} ${id}` : id)
    const hide = () => closeRef.current()
    const key = (event: KeyboardEvent) => {
      if (event.key === 'Escape') hide()
    }
    window.addEventListener('resize', hide)
    window.addEventListener('blur', hide)
    document.addEventListener('scroll', hide, true)
    document.addEventListener('visibilitychange', hide)
    document.addEventListener('keydown', key, true)
    return () => {
      if (previous) target.setAttribute('aria-describedby', previous)
      else target.removeAttribute('aria-describedby')
      window.removeEventListener('resize', hide)
      window.removeEventListener('blur', hide)
      document.removeEventListener('scroll', hide, true)
      document.removeEventListener('visibilitychange', hide)
      document.removeEventListener('keydown', key, true)
    }
  }, [target, id])
  const bubble = useRef<HTMLElement>(null)
  const zoom = Number.parseFloat(getComputedStyle(document.body).zoom) || 1
  const anchor = target.getBoundingClientRect()
  const rightSpace = innerWidth / zoom - anchor.right / zoom
  const available = rightSpace < 362 && anchor.left / zoom > rightSpace ? anchor.left / zoom : rightSpace
  const width =
    Math.min(Math.max(180, Math.min(320, available - 46)), Math.max(1, innerWidth / zoom - 58)) + 42
  const [placement, setPlacement] = useState({ left: 8, top: 8, onLeft: false, arrow: 24 })
  useLayoutEffect(() => {
    const place = () => {
      if (!bubble.current || !target.isConnected) return
      const bounds = target.getBoundingClientRect(),
        measured = bubble.current.getBoundingClientRect()
      const viewportWidth = innerWidth / zoom,
        viewportHeight = innerHeight / zoom
      const actualWidth = measured.width / zoom,
        actualHeight = measured.height / zoom
      const rightSpace = viewportWidth - bounds.right / zoom
      const onLeft = rightSpace < actualWidth + 8 && bounds.left / zoom > rightSpace
      const left = Math.max(
        8,
        Math.min(
          viewportWidth - actualWidth - 8,
          onLeft ? bounds.left / zoom - actualWidth : bounds.right / zoom,
        ),
      )
      const top = Math.max(8, Math.min(bounds.top / zoom, viewportHeight - actualHeight - 8))
      const arrow = (bounds.top + bounds.height / 2) / zoom - top
      setPlacement((previous) =>
        previous.left === left &&
        previous.top === top &&
        previous.onLeft === onLeft &&
        previous.arrow === arrow
          ? previous
          : { left, top, onLeft, arrow },
      )
    }
    place()
    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(place)
    if (bubble.current) observer.observe(bubble.current)
    return () => observer.disconnect()
  }, [target, zoom, width])
  const { left, top, onLeft, arrow } = placement
  const compact = compactAvalonRatings(ratings)
  const { Artwork } = context.components
  return createPortal(
    <AvalonPreviewBubble
      ref={bubble}
      role="tooltip"
      id={id}
      arrowOnRight={onLeft}
      arrowOffset={arrow}
      artwork={<Artwork workId={game.workId} hero />}
      style={{
        width,
        maxHeight: innerHeight / zoom - 16,
        left,
        top,
      }}
    >
      <h3>{game.title}</h3>
      <p className="avalon-preview-meta">
        {[...new Set(game.entries.map((entry) => storeLabel(entry.store)))].join(', ')}
        {' · '}
        {game.playtimeMinutes <= 0
          ? 'never opened'
          : `${detailPlaytime(game.playtimeMinutes)}${game.lastPlayedAt ? ` · idle ${detailIdle(game.lastPlayedAt)}` : ''}`}
      </p>
      {compact && (
        <p
          className="avalon-preview-ratings"
          role="group"
          aria-label={receptionFigures(ratings)
            .map((figure) => figure.automationName)
            .join(' ')}
        >
          {compact}
        </p>
      )}
      {game.summary && <p className="avalon-preview-summary">{game.summary}</p>}
    </AvalonPreviewBubble>,
    document.body,
  )
}
