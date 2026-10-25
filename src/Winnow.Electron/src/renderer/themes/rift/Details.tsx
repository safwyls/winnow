import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import type { ThemeContext } from '../../../shared/theme'
import { Artwork } from '../../components/Artwork'
import { PortalSurface } from '../../components/portal-effects'
import { useJourney } from './journey'

export function RiftDetails(context: ThemeContext) {
  const game = context.games.find((entry) => entry.workId === context.selectedWorkId)
  const journey = useJourney(),
    root = useRef<HTMLDivElement>(null)
  const [expansion, setExpansion] = useState<{ x: number; y: number; width: number; height: number } | null>(
    null,
  )
  const [expanded, setExpanded] = useState(false)
  useLayoutEffect(() => {
    if (!root.current) return
    const bounds = root.current.getBoundingClientRect(),
      zoom = bounds.width / root.current.clientWidth || 1
    const source =
      journey.origin.current?.workId === context.selectedWorkId ? journey.origin.current.rect : null
    setExpansion(
      source
        ? {
            x: (source.left - bounds.left) / zoom,
            y: (source.top - bounds.top) / zoom,
            width: source.width / zoom,
            height: source.height / zoom,
          }
        : { x: bounds.width / zoom / 2 - 40, y: bounds.height / zoom / 2 - 60, width: 80, height: 120 },
    )
    setExpanded(false)
  }, [context.selectedWorkId])
  const finish = useCallback(() => {
    setExpanded(true)
    journey.finish()
  }, [journey.finish])
  useLayoutEffect(() => {
    if (expanded)
      root.current?.querySelector<HTMLButtonElement>('.back-button')?.focus({ preventScroll: true })
  }, [expanded])
  return (
    <div ref={root} className="rift-details" data-expanded={expanded || undefined}>
      {expansion && (
        <PortalSurface
          key={context.selectedWorkId}
          options={journey.options}
          reducedMotion={journey.reducedMotion}
          expansion={expansion}
          onExpanded={finish}
        >
          <div className="rift-details-scroll" aria-busy={!expanded} inert={!expanded}>
            <div className="rift-detail-poster" aria-hidden="true">
              {context.selectedWorkId !== null && (
                <Artwork workId={game?.headerWorkId ?? context.selectedWorkId} eager />
              )}
            </div>
            {context.renderScreen('details')}
          </div>
        </PortalSurface>
      )}
    </div>
  )
}
