import { useEffect, useId, useRef, useState, type CSSProperties } from 'react'
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
  const currentWork = useRef(game.workId)
  useEffect(() => {
    if (currentWork.current !== game.workId) {
      currentWork.current = game.workId
      setTarget(null)
    }
  }, [game.workId])
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
      target && currentWork.current === game.workId ? (
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
    // The preload bridge has no request-abort message. This guard prevents late
    // metadata from reviving a closed, recycled, or detached preview.
    void request<GameDetails>('game.details', { workId: game.workId }).then(
      (detail) => {
        if (active) setRatings(detail.ratings ?? [])
      },
      () => {},
    )
    return () => {
      active = false
    }
  }, [game.workId])
  useEffect(() => {
    const previous = target.getAttribute('aria-describedby')
    target.setAttribute('aria-describedby', id)
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
  const zoom = Number.parseFloat(getComputedStyle(document.body).zoom) || 1
  const bounds = target.getBoundingClientRect()
  const width = Math.min(context.mode === 'fullscreen' ? 420 : 340, innerWidth / zoom - 32)
  const onLeft = bounds.right / zoom + width + 24 > innerWidth / zoom
  const left = Math.max(16, onLeft ? bounds.left / zoom - width - 12 : bounds.right / zoom + 12)
  const top = Math.max(16, Math.min(bounds.top / zoom, innerHeight / zoom - 280))
  const compact = compactAvalonRatings(ratings)
  const { Artwork } = context.components
  return createPortal(
    <aside
      role="tooltip"
      id={id}
      className="avalon-hover-preview"
      data-arrow-side={onLeft ? 'right' : 'left'}
      style={
        {
          width,
          left,
          top,
          '--preview-arrow-top': `${Math.max(24, bounds.top / zoom + 40 - top)}px`,
        } as CSSProperties
      }
    >
      <Artwork workId={game.workId} hero />
      <div className="avalon-preview-copy">
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
