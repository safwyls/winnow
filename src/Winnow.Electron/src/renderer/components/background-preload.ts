import type { QueryClient } from '@tanstack/react-query'
import { preloadArtworkSource } from './artwork-source'
import { backdropCandidates, backdropWidth, type BackdropSize } from './backdrop-model'
import { resolveBackdropSelection } from './backdrop-selection'

export interface BackgroundPreloadTarget {
  workId: number
  coverWorkId?: number
  compositionId: string
  fullscreen: boolean
  width: number
  height: number
  scale: number
}

export async function preloadBackground(
  client: QueryClient,
  target: BackgroundPreloadTarget,
  signal: AbortSignal,
): Promise<void> {
  signal.throwIfAborted()
  if (target.width <= 0 || target.height <= 0 || target.scale <= 0 || target.workId <= 0) return
  const size = detailsBackdropSize(target)
  const selection = await resolveBackdropSelection(
    target.workId,
    size.width / size.height,
    target.coverWorkId,
    signal,
  )
  for (const candidate of backdropCandidates(selection)) {
    signal.throwIfAborted()
    try {
      if (
        await preloadArtworkSource(
          client,
          [
            'artwork-image',
            candidate.key.provider,
            candidate.key.id,
            backdropWidth(candidate, size),
            'asset',
          ],
          signal,
        )
      )
        return
    } catch {
      signal.throwIfAborted()
      // Preserve foreground fallback order when one source is unavailable.
    }
  }
}

/** Match the already loaded Details CSS, including user zoom and the desktop dialog cap. */
export function detailsBackdropSize(target: BackgroundPreloadTarget): BackdropSize {
  const zoom = Number.parseFloat(getComputedStyle(document.body).zoom) || 1
  if (target.compositionId !== 'avalon') {
    const rift = target.compositionId === 'rift'
    const content = document.querySelector<HTMLElement>(rift ? '.rift-content' : '.page-content')
    let width = target.width / zoom
    if (content) {
      const style = getComputedStyle(content)
      width =
        content.clientWidth -
        (Number.parseFloat(style.paddingLeft) || 0) -
        (Number.parseFloat(style.paddingRight) || 0)
    }
    return {
      width: Math.min(rift ? 1500 : 1300, width),
      height: rift
        ? target.width <= 600
          ? 450
          : Math.min(680, target.height * 0.8)
        : Math.max(220, Math.min(420, target.width * 0.32)),
      scale: target.scale * zoom,
      fullscreen: target.fullscreen,
      fitWholeHero: false,
    }
  }
  if (target.fullscreen)
    return {
      width: target.width / zoom,
      height: target.height / zoom,
      scale: target.scale * zoom,
      fullscreen: true,
    }
  const shell = document.querySelector('.avalon-shell')
  if (shell) {
    const probe = document.createElement('div')
    probe.className = 'avalon-details desktop'
    probe.style.cssText = 'visibility:hidden;pointer-events:none;z-index:-1;'
    shell.append(probe)
    const layout = getComputedStyle(probe)
    const width = Number.parseFloat(layout.width) - 2
    const height = Number.parseFloat(layout.height) - 2
    const bounds = probe.getBoundingClientRect()
    const scale = target.scale * (bounds.width / (width + 2) || zoom)
    probe.remove()
    if (width > 0 && height > 0) return { width, height, scale, fullscreen: false }
  }
  const narrow = target.width <= 850
  return {
    width:
      (narrow
        ? target.width - 32
        : Math.min(target.width - 80, Math.min(1582, Math.max(860, target.width * 0.5)))) - 2,
    height:
      (narrow ? target.height - 32 : Math.min(target.height - 80, Math.max(720, target.height * 0.667))) - 2,
    scale: target.scale * zoom,
    fullscreen: false,
  }
}
