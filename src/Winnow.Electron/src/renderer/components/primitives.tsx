import { useEffect, useRef } from 'react'
import { ArrowUpRight, Clock3, HardDrive } from 'lucide-react'
import { Artwork } from './Artwork'
import type { ThemeGameCardProps } from '../../shared/theme'
import { ArtworkEffects } from './artwork-effects'
import { GamePreview } from './GamePreview'

export function hours(minutes: number) {
  return minutes < 60
    ? `${minutes}m`
    : `${(minutes / 60).toLocaleString(undefined, { maximumFractionDigits: 1 })}h`
}
export function bucketLabel(bucket: string) {
  return (
    (
      {
        never: 'Never played',
        never_played: 'Never played',
        unplayed: 'Never played',
        recent: 'Recently played',
        dormant: 'Away for a while',
        abandoned: 'Away for a while',
        active: 'In rotation',
        cooling: 'Taking a break',
        bounced: 'Bounced',
        retired: 'Retired',
        stale_but_patched: 'Patched since you played',
        derelict: 'Derelict',
      } as Record<string, string>
    )[bucket.toLowerCase()] ?? bucket.replace(/_/g, ' ')
  )
}

export function GameCard({
  game,
  reason,
  onOpen,
  presentation = 'landscape',
  effects,
  preview = 'inline',
}: ThemeGameCardProps) {
  const interaction = useRef<HTMLButtonElement>(null)
  return (
    <GamePreview game={game} reason={reason} disabled={preview !== 'flyout'}>
      <button
        ref={interaction}
        className="game-card"
        data-work-id={game.workId}
        data-presentation={presentation}
        data-preview={preview}
        onClick={onOpen}
        aria-label={`View ${game.title}`}
      >
        <ArtworkEffects interactionRef={interaction} effects={presentation === 'record' ? false : effects}>
          <Artwork workId={game.workId} />
        </ArtworkEffects>
        {(preview === 'inline' || preview === 'overlay') && (
          <div className="card-content">
            <div className="card-topline">
              <span>{game.entries[0]?.store ?? 'Library'}</span>
              <ArrowUpRight size={15} />
            </div>
            <h3 title={game.title}>{game.title}</h3>
            <p>{reason ?? bucketLabel(game.bucket)}</p>
            <div className="game-facts">
              <span>
                <Clock3 size={12} />
                {hours(game.playtimeMinutes)}
              </span>
              {game.entries.some((entry) => entry.installed) && (
                <span>
                  <HardDrive size={12} />
                  Installed
                </span>
              )}
            </div>
          </div>
        )}
      </button>
    </GamePreview>
  )
}

/** Surfacing requires a visible card in the focused window, rather than feed computation. */
export function Impression({
  releaseId,
  shelfId,
  children,
}: {
  releaseId: number
  shelfId: string
  children: React.ReactNode
}) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    let visible = false,
      sent = false
    const report = () => {
      if (!visible || sent || document.visibilityState !== 'visible' || !document.hasFocus()) return
      sent = true
      void window.winnow.request({ route: 'feedImpression', body: { releaseId, shelfId } })
    }
    const observer = new IntersectionObserver(
      ([entry]) => {
        visible = entry.isIntersecting && entry.intersectionRatio >= 0.5
        report()
      },
      { threshold: 0.5 },
    )
    if (ref.current) observer.observe(ref.current)
    window.addEventListener('focus', report)
    document.addEventListener('visibilitychange', report)
    return () => {
      observer.disconnect()
      window.removeEventListener('focus', report)
      document.removeEventListener('visibilitychange', report)
    }
  }, [releaseId, shelfId])
  return (
    <div ref={ref} className="impression">
      {children}
    </div>
  )
}

export function Empty({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="empty-state">
      <span className="eyebrow">A little space</span>
      <h2>{title}</h2>
      <div>{children}</div>
    </div>
  )
}
