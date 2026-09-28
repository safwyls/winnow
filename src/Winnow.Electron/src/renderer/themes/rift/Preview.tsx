import { useEffect, useRef, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { ArrowUpRight, X } from 'lucide-react'
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
  hold,
  leave,
}: {
  target: PreviewTarget
  fullscreen: boolean
  close(restore?: boolean): void
  hold(): void
  leave(): void
}) {
  const journey = useJourney(),
    panel = useRef<HTMLDivElement>(null)
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
    const key = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopImmediatePropagation()
        close(true)
      }
      if (event.key !== 'Tab') return
      const actions = panel.current?.querySelectorAll<HTMLButtonElement>('button')
      if (!actions?.length) return
      if (event.target === target.card && !event.shiftKey) {
        event.preventDefault()
        actions[0].focus()
      } else if (event.target === actions[0] && event.shiftKey) {
        event.preventDefault()
        target.card.focus({ preventScroll: true })
      } else if (event.target === actions[actions.length - 1] && !event.shiftKey) {
        event.preventDefault()
        const cards = [...document.querySelectorAll<HTMLButtonElement>('.rift-gallery [data-rift-game]')]
        const next = cards[cards.indexOf(target.card) + 1]
        close()
        ;(next ?? document.querySelector<HTMLButtonElement>('[aria-label="Theme Studio"]'))?.focus()
      }
    }
    const focus = (event: FocusEvent) => {
      if (
        event.target instanceof Node &&
        !panel.current?.contains(event.target) &&
        !target.card.contains(event.target)
      )
        close()
    }
    const hide = () => close()
    document.addEventListener('keydown', key, true)
    document.addEventListener('focusin', focus)
    window.addEventListener('resize', hide)
    window.addEventListener('blur', hide)
    document.addEventListener('visibilitychange', hide)
    return () => {
      document.removeEventListener('keydown', key, true)
      document.removeEventListener('focusin', focus)
      window.removeEventListener('resize', hide)
      window.removeEventListener('blur', hide)
      document.removeEventListener('visibilitychange', hide)
    }
  }, [target, close])
  const origin = target.pointer
    ? { x: target.pointer.x / zoom - placement.left, y: target.pointer.y / zoom - placement.top }
    : undefined
  return createPortal(
    <div
      ref={panel}
      className={`rift-cover-preview ${fullscreen ? 'fullscreen' : ''}`}
      data-placement={placement.placement}
      data-compact={placement.width < 365 || placement.height < 490 || undefined}
      data-short={placement.height < 380 || undefined}
      role="region"
      aria-label={`${target.game.title} preview`}
      style={placement as CSSProperties}
      onPointerEnter={hold}
      onPointerLeave={leave}
      onFocusCapture={hold}
      onBlurCapture={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) leave()
      }}
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
          <div className="rift-preview-actions">
            <button className="primary" onClick={() => journey.open(target.game.workId, panel.current)}>
              View game <ArrowUpRight size={16} />
            </button>
            <button aria-label="Dismiss preview" onClick={() => close(true)}>
              <X size={16} />
            </button>
          </div>
        </div>
      </PortalSurface>
    </div>,
    document.body,
  )
}
