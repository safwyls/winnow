import { useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import type { ThemeContext } from '../../shared/theme'
import { request, storeLabel } from '../api/client'
import type { GameDetails, LibraryGame } from '../api/types'
import { hours } from '../components/primitives'

const openedEvent = 'winnow:avalon-preview-opened'
type Rating = GameDetails['ratings'][number]

export function compactAvalonRatings(ratings: Rating[]): string {
  return [
    ['igdb_users', 'IGDB'],
    ['igdb_critics', 'IGDB critics'],
    ['steam', 'Steam'],
  ]
    .flatMap(([source, label]) => {
      const rating = ratings.find((item) => item.source === source && item.hasFigure)
      if (!rating || rating.score == null || rating.ratingCount == null) return []
      return `${label}: ${source === 'steam' ? rating.label?.trim() || 'Unclassified' : Math.round(rating.score).toLocaleString()}`
    })
    .join(' · ')
}

export function useAvalonPreview(context: ThemeContext, game: LibraryGame, reason?: string) {
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
          reason={reason}
          target={target}
          close={() => setTarget(null)}
        />
      ) : null,
  }
}

function AvalonPreview({
  context,
  game,
  reason,
  target,
  close,
}: {
  context: ThemeContext
  game: LibraryGame
  reason?: string
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
  const width = Math.min(context.mode === 'fullscreen' ? 420 : 340, innerWidth / zoom - 16)
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
      const onLeft = bounds.right / zoom + actualWidth + 20 > viewportWidth
      const left = Math.max(
        8,
        Math.min(
          viewportWidth - actualWidth - 8,
          onLeft ? bounds.left / zoom - actualWidth - 12 : bounds.right / zoom + 12,
        ),
      )
      const top = Math.max(8, Math.min(bounds.top / zoom, viewportHeight - actualHeight - 8))
      const arrow = Math.max(16, Math.min(actualHeight - 24, bounds.top / zoom + 40 - top))
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
    <aside
      ref={bubble}
      role="tooltip"
      id={id}
      className="avalon-hover-preview"
      data-arrow-side={onLeft ? 'right' : 'left'}
      style={
        {
          width,
          maxHeight: innerHeight / zoom - 16,
          left,
          top,
          '--preview-arrow-top': `${arrow}px`,
        } as CSSProperties
      }
    >
      <Artwork workId={game.workId} hero />
      <div className="avalon-preview-copy" style={{ maxHeight: innerHeight / zoom - 50, overflow: 'hidden' }}>
        <h3>{game.title}</h3>
        <p className="avalon-preview-meta">
          {[...new Set(game.entries.map((entry) => storeLabel(entry.store)))].join(' / ')}
          {` · ${hours(game.playtimeMinutes)} played`}
        </p>
        {compact && <p className="avalon-preview-ratings">{compact}</p>}
        {reason && <p className="avalon-preview-reason">{reason}</p>}
        {game.summary && <p className="avalon-preview-summary">{game.summary}</p>}
      </div>
    </aside>,
    document.body,
  )
}
