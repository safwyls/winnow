import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { ThemeGamePreviewProps } from '../../shared/theme'
import { bucketLabel, hours } from './primitives'
import './game-preview.css'

const openedEvent = 'winnow:game-preview'

/** All measurements are viewport pixels; CSS zoom is accounted for at the portal boundary. */
export function previewPlacement(box: DOMRect, width: number, top: number, bottom: number, preferred = 340) {
  const gutter = 14,
    gap = 22
  const right = width - gutter - box.right - gap
  const left = box.left - gutter - gap
  const side = right >= preferred || right >= left ? 'right' : 'left'
  const available = side === 'right' ? right : left
  const docked = available < 240
  return {
    side: docked ? 'docked' : side,
    width: Math.max(0, docked ? Math.min(370, width - gutter * 2) : Math.min(preferred, available)),
    maxHeight: Math.max(0, (bottom - top) * (docked ? 0.55 : 1)),
  }
}

/** A read-only companion to any theme's game link, independent of the artwork renderer. */
export function GamePreview({
  game,
  reason,
  children,
  disabled = false,
  className = '',
}: ThemeGamePreviewProps) {
  const id = useId()
  const anchor = useRef<HTMLDivElement>(null),
    panel = useRef<HTMLDivElement>(null)
  const target = useRef<HTMLElement | null>(null)
  const showTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const hideTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const source = useRef<'pointer' | 'keyboard'>('pointer')
  const dismissed = useRef(false),
    pointerFocus = useRef(false)
  const [open, setOpen] = useState(false)
  const clearTimers = () => {
    clearTimeout(showTimer.current)
    clearTimeout(hideTimer.current)
  }
  const hide = () => {
    clearTimers()
    setOpen(false)
  }
  const show = (input: 'pointer' | 'keyboard') => {
    clearTimers()
    if (disabled || dismissed.current || document.querySelector('[role="dialog"]')) return
    target.current = anchor.current?.querySelector<HTMLElement>('button, a, [tabindex]') ?? anchor.current
    source.current = input
    window.dispatchEvent(new CustomEvent(openedEvent, { detail: id }))
    setOpen(true)
  }
  const leave = () => {
    clearTimers()
    if (source.current === 'keyboard' && anchor.current?.contains(document.activeElement)) return
    hideTimer.current = setTimeout(hide, 180)
  }
  const position = () => {
    const card = target.current,
      popup = panel.current
    if (!card || !popup || !card.isConnected || card.closest('[hidden]')) return hide()
    const box = card.getBoundingClientRect()
    const content = document.getElementById('main-content')?.getBoundingClientRect()
    const footer = document.querySelector('.app-footer')?.getBoundingClientRect()
    const top = Math.max(14, (content?.top ?? 0) + 14)
    const bottom = Math.min(window.innerHeight - 14, (footer?.top ?? window.innerHeight) - 14)
    if (box.bottom <= top || box.top >= bottom || bottom <= top) return hide()
    const zoom = Number.parseFloat(getComputedStyle(document.body).zoom) || 1
    const placement = previewPlacement(
      box,
      window.innerWidth,
      top,
      bottom,
      document.querySelector('.app-shell.fullscreen') ? 370 * zoom : 340 * zoom,
    )
    popup.dataset.placement = placement.side
    popup.style.width = `${placement.width / zoom}px`
    popup.style.maxHeight = `${placement.maxHeight / zoom}px`
    const { width, height } = popup.getBoundingClientRect()
    const x =
      placement.side === 'docked'
        ? (window.innerWidth - width) / 2
        : placement.side === 'right'
          ? box.right + 22
          : box.left - 22 - width
    const y =
      placement.side === 'docked'
        ? bottom - height
        : Math.max(top, Math.min(bottom - height, box.top + box.height * 0.38 - height / 2))
    popup.style.left = `${x / zoom}px`
    popup.style.top = `${y / zoom}px`
  }
  useLayoutEffect(() => {
    if (open) position()
  }, [open, game, reason])
  useEffect(() => {
    if (disabled) hide()
    return clearTimers
  }, [disabled])
  useEffect(() => {
    if (!open) return
    const card = target.current
    const previous = card?.getAttribute('aria-describedby')
    card?.setAttribute('aria-describedby', [previous, id].filter(Boolean).join(' '))
    const closeOther = (event: Event) => {
      if ((event as CustomEvent).detail !== id) hide()
    }
    const key = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        dismissed.current = true
        hide()
        event.preventDefault()
        event.stopImmediatePropagation()
      } else if (source.current === 'keyboard' && ['PageUp', 'PageDown'].includes(event.key)) {
        const popup = panel.current
        if (popup && popup.scrollHeight > popup.clientHeight) {
          popup.scrollTop += popup.clientHeight * (event.key === 'PageDown' ? 0.8 : -0.8)
          event.preventDefault()
          event.stopImmediatePropagation()
        }
      }
    }
    const scroll = (event: Event) => {
      if (event.target instanceof Node && panel.current?.contains(event.target)) return
      if (source.current === 'keyboard') position()
      else hide()
    }
    const observer = new ResizeObserver(position)
    if (card) observer.observe(card)
    const mutations = new MutationObserver(() => {
      if (card?.closest('[hidden]') || document.querySelector('[role="dialog"]')) hide()
    })
    mutations.observe(document.body, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['hidden'],
    })
    window.addEventListener(openedEvent, closeOther)
    window.addEventListener('keydown', key, true)
    window.addEventListener('blur', hide)
    window.addEventListener('resize', hide)
    document.addEventListener('scroll', scroll, true)
    document.addEventListener('visibilitychange', hide)
    return () => {
      if (previous) card?.setAttribute('aria-describedby', previous)
      else card?.removeAttribute('aria-describedby')
      observer.disconnect()
      mutations.disconnect()
      window.removeEventListener(openedEvent, closeOther)
      window.removeEventListener('keydown', key, true)
      window.removeEventListener('blur', hide)
      window.removeEventListener('resize', hide)
      document.removeEventListener('scroll', scroll, true)
      document.removeEventListener('visibilitychange', hide)
    }
  }, [open, id])
  return (
    <div
      ref={anchor}
      className={`winnow-game-preview-anchor ${className}`}
      onPointerEnter={(event) => {
        if (event.pointerType === 'touch') return
        clearTimers()
        if (!open) showTimer.current = setTimeout(() => show('pointer'), 180)
      }}
      onPointerLeave={() => {
        dismissed.current = false
        leave()
      }}
      onPointerDown={() => {
        pointerFocus.current = true
      }}
      onFocus={() => {
        if (!pointerFocus.current) show('keyboard')
        pointerFocus.current = false
      }}
      onBlur={() => {
        dismissed.current = false
        pointerFocus.current = false
        hide()
      }}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && showTimer.current) {
          clearTimers()
          dismissed.current = true
        }
      }}
    >
      {children}
      {open &&
        !disabled &&
        createPortal(
          <div
            id={id}
            role="tooltip"
            ref={panel}
            className="winnow-game-preview"
            onPointerEnter={clearTimers}
            onPointerLeave={leave}
          >
            <div className="winnow-game-preview-body">
              <span className="eyebrow">In your collection</span>
              <h3>{game.title}</h3>
              <div className="winnow-game-preview-meta">
                <span>{[...new Set(game.entries.map((entry) => entry.store))].join(' / ') || 'Library'}</span>
                <span>{game.playtimeMinutes ? `${hours(game.playtimeMinutes)} played` : 'Never played'}</span>
                {game.entries.some((entry) => entry.installed) && (
                  <span className="winnow-game-preview-installed">Installed</span>
                )}
              </div>
              {(reason || game.playtimeMinutes > 0 || bucketLabel(game.bucket) !== 'Never played') && (
                <p className="winnow-game-preview-reason">{reason ?? bucketLabel(game.bucket)}</p>
              )}
              <p className="winnow-game-preview-description">
                {game.summary?.trim() || 'No description available yet.'}
              </p>
              <div className="winnow-game-preview-hint">
                Select the cover to view game <span aria-hidden="true">↗</span>
              </div>
            </div>
          </div>,
          document.body,
        )}
    </div>
  )
}
