import { useRef, type ButtonHTMLAttributes } from 'react'
import type { LibraryGame } from '../../api/types'
import { Artwork } from '../../components/Artwork'
import { ArtworkEffects } from '../../components/artwork-effects'

export function RiftCover({
  game,
  className = '',
  ...props
}: { game: LibraryGame } & ButtonHTMLAttributes<HTMLButtonElement>) {
  const interaction = useRef<HTMLButtonElement>(null)
  return (
    <button
      {...props}
      ref={interaction}
      className={`rift-cover ${className}`}
      data-rift-game={game.workId}
      aria-label={props['aria-label'] ?? `View ${game.title}`}
    >
      <ArtworkEffects interactionRef={interaction}>
        <Artwork workId={game.headerWorkId ?? game.workId} />
        <span className="rift-cover-fallback" aria-hidden="true">
          {game.title}
        </span>
      </ArtworkEffects>
    </button>
  )
}
