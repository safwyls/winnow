import { useEffect, useId, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import type { LibraryGame } from '../../api/types'
import { Artwork } from '../../components/Artwork'
import { PortalSurface } from '../../components/portal-effects'
import { bucketLabel, hours } from '../../components/primitives'
import { previewPlacement } from './data'
import { storeNames, useJourney } from './journey'

export interface PreviewTarget {
  game: LibraryGame
  card: HTMLButtonElement
  keyboard: boolean
  pointer?: { x: number; y: number }
}
export function CoverPreview({
  target,
  fullscreen,
  close,
}: {
  target: PreviewTarget
  fullscreen: boolean
  close(restore?: boolean): void
}) {
  const journey = useJourney(),
    descriptionId = useId()
  const zoom = Number.parseFloat(getComputedStyle(document.body).zoom) || 1
  const card = target.card.getBoundingClientRect()
  const header = document.querySelector('.rift-bar')?.getBoundingClientRect()
  const footer = document.querySelector('.rift-footer')?.getBoundingClientRect()
  const placement = previewPlacement(
    { left: card.left / zoom, right: card.right / zoom, top: card.top / zoom, height: card.height / zoom },
    innerWidth / zoom,
    (header?.bottom ?? 60) / zoom + 12,
    (footer?.top ?? innerHeight - 44) / zoom - 12,
    fullscreen,
  )
  useEffect(() => {
    const previousDescription = target.card.getAttribute('aria-describedby')
    target.card.setAttribute('aria-describedby', descriptionId)
    const key = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopImmediatePropagation()
        close(true)
      }
    }
    const focus = (event: FocusEvent) => {
      if (event.target instanceof Node && !target.card.contains(event.target)) close()
    }
    const hide = () => close()
    document.addEventListener('keydown', key, true)
    document.addEventListener('focusin', focus)
    window.addEventListener('resize', hide)
    window.addEventListener('blur', hide)
    document.addEventListener('visibilitychange', hide)
    return () => {
      if (previousDescription) target.card.setAttribute('aria-describedby', previousDescription)
      else target.card.removeAttribute('aria-describedby')
      document.removeEventListener('keydown', key, true)
      document.removeEventListener('focusin', focus)
      window.removeEventListener('resize', hide)
      window.removeEventListener('blur', hide)
      document.removeEventListener('visibilitychange', hide)
    }
  }, [target, close, descriptionId])
  const origin = target.pointer
    ? { x: target.pointer.x / zoom - placement.left, y: target.pointer.y / zoom - placement.top }
    : undefined
  return createPortal(
    <div
      id={descriptionId}
      className={`rift-cover-preview ${fullscreen ? 'fullscreen' : ''}`}
      data-placement={placement.placement}
      data-compact={placement.width < 365 || placement.height < 420 || undefined}
      data-short={placement.height < 380 || undefined}
      role="tooltip"
      style={placement as CSSProperties}
    >
      <PortalSurface
        options={journey.options}
        reducedMotion={journey.reducedMotion || target.keyboard}
        origin={origin}
        artwork={<Artwork workId={target.game.workId} hero eager />}
      >
        <div className="rift-preview-reading">
          <span className="eyebrow">A WINDOW INTO THIS WORLD</span>
          <h3 className={target.game.title.length > 45 ? 'long-title' : ''}>{target.game.title}</h3>
          <div className="rift-meta">
            <span>{storeNames(target.game)}</span>
            <span>{hours(target.game.playtimeMinutes)} played</span>
          </div>
          <p className="rift-reason">{bucketLabel(target.game.bucket)}</p>
          <p className="rift-description">
            {target.game.summary ??
              'View the game to explore its artwork, activity, journal and library record.'}
          </p>
        </div>
      </PortalSurface>
    </div>,
    document.body,
  )
}
