import { useEffect, useRef, type RefObject } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { artworkImages } from './artwork-images'
import { preloadBackground } from './background-preload'
import { SettledBackgroundQueue } from './settled-background-queue'

const cardSelector = '[data-work-id], [data-rift-game]'
type Game = { workId: number; headerWorkId?: number | null }

/** Observe clipped hit targets, rather than the virtualizer's offscreen overscan rows. */
export function useVisibleBackgroundPreload(
  viewport: RefObject<HTMLElement | null>,
  games: readonly Game[],
  compositionId: string,
  fullscreen: boolean,
  enabled = true,
) {
  const client = useQueryClient()
  const current = useRef({ games, compositionId, fullscreen })
  current.current = { games, compositionId, fullscreen }
  const pending = useRef<SettledBackgroundQueue<Game> | null>(null)
  useEffect(() => {
    const root = viewport.current
    if (!enabled || !root || typeof IntersectionObserver === 'undefined') return
    const targets = new Set<HTMLElement>()
    const visible = new Set<HTMLElement>()
    const cache = artworkImages(client)
    const queue = new SettledBackgroundQueue<Game>({
      collect: () => {
        if (document.visibilityState === 'hidden') return []
        const byId = new Map(current.current.games.map((game) => [game.workId, game]))
        const collected = new Map<number, Game>()
        for (const card of visible) {
          if (!card.isConnected || card.closest('[inert], [hidden], [aria-hidden="true"]')) continue
          if (
            typeof card.checkVisibility === 'function' &&
            !card.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true })
          )
            continue
          const game = byId.get(Number(card.dataset.workId ?? card.dataset.riftGame))
          if (game) collected.set(game.workId, game)
        }
        return [...collected.values()]
      },
      busy: () => cache.pendingCount > 0,
      load: (game, signal) =>
        preloadBackground(
          client,
          {
            workId: game.workId,
            coverWorkId: game.headerWorkId ?? undefined,
            compositionId: current.current.compositionId,
            fullscreen: current.current.fullscreen,
            width: window.innerWidth,
            height: window.innerHeight,
            scale: window.devicePixelRatio || 1,
          },
          signal,
        ),
    })
    pending.current = queue
    let active = true
    const intersections = new IntersectionObserver(
      (entries) => {
        if (!active) return
        for (const entry of entries) {
          const card = entry.target as HTMLElement
          if (targets.has(card) && entry.isIntersecting && entry.intersectionRatio > 0) visible.add(card)
          else visible.delete(card)
        }
        queue.changed()
      },
      { threshold: [0, Number.EPSILON] },
    )
    const sync = () => {
      const next = new Set(root.querySelectorAll<HTMLElement>(cardSelector))
      let changed = false
      for (const card of targets)
        if (!next.has(card)) {
          targets.delete(card)
          visible.delete(card)
          intersections.unobserve(card)
          changed = true
        }
      for (const card of next)
        if (!targets.has(card)) {
          targets.add(card)
          intersections.observe(card)
          changed = true
        }
      return changed
    }
    const mutations = new MutationObserver((records) => {
      if (sync() || records.some((record) => record.type === 'attributes')) queue.changed()
    })
    mutations.observe(root, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ['data-work-id', 'data-rift-game', 'style', 'hidden', 'inert', 'aria-hidden'],
    })
    for (let ancestor = root.parentElement; ancestor; ancestor = ancestor.parentElement)
      mutations.observe(ancestor, {
        attributes: true,
        attributeFilter: ['hidden', 'inert', 'aria-hidden', 'style', 'class'],
      })
    const resize = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(queue.changed)
    resize?.observe(root)
    sync()
    // Search/filter controls sit outside the scrolling pane; capture also catches nested scrolls.
    const events = ['scroll', 'wheel', 'input', 'change', 'visibilitychange'] as const
    for (const event of events)
      document.addEventListener(event, queue.changed, { capture: true, passive: true })
    window.addEventListener('resize', queue.changed)
    queue.changed()
    return () => {
      active = false
      pending.current = null
      queue.dispose()
      intersections.disconnect()
      mutations.disconnect()
      resize?.disconnect()
      for (const event of events) document.removeEventListener(event, queue.changed, true)
      window.removeEventListener('resize', queue.changed)
    }
  }, [client, viewport, enabled])
  useEffect(() => pending.current?.changed(), [games, compositionId, fullscreen])
}
