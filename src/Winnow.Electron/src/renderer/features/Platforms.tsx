import { useState, type ReactNode } from 'react'
import { useLibrary } from '../api/hooks'
import type { LibraryGame, StoreConnections } from '../api/types'
import { steamConnectionState } from './steamConnection'
import { Notice } from './shared'
import './platforms.css'
import { useLibraryProjection } from './parity-library-projection'

/** Counts grouped titles before any screen search/filter. Repeated editions do not add another title. */
export function titlesByStore(games: readonly LibraryGame[]): Record<string, number> {
  const counts: Record<string, number> = {}
  for (const game of games)
    for (const store of new Set(game.entries.map((entry) => entry.store.toLowerCase())))
      counts[store] = (counts[store] ?? 0) + 1
  return counts
}

export function Platforms({
  snapshot,
  steam,
  epic,
}: {
  snapshot: StoreConnections
  steam(count?: number): ReactNode
  epic: ReactNode
}) {
  const [selected, setSelected] = useState('steam')
  const library = useLibrary()
  const projection = useLibraryProjection()
  const counts = library.data ? titlesByStore(projection.games) : undefined
  const attention: Record<string, boolean> = {
    steam: steamConnectionState(snapshot).attention,
    epic: !!snapshot.epic && !snapshot.epic.isLive,
    gog: false,
  }
  return (
    <section className="platform-settings" aria-label="Platforms">
      <nav className="tabs platform-tabs" aria-label="Platforms">
        {['steam', 'epic', 'gog'].map((platform) => (
          <button
            key={platform}
            aria-pressed={selected === platform}
            aria-controls={`platform-${platform}`}
            onClick={() => setSelected(platform)}
          >
            {platform.toUpperCase()}
            {attention[platform] && (
              <span className="platform-attention" aria-label="Needs attention">
                ●
              </span>
            )}
          </button>
        ))}
      </nav>
      {['steam', 'epic', 'gog'].map((platform) => (
        <div key={platform} id={`platform-${platform}`} hidden={selected !== platform}>
          {!!counts?.[platform] && (
            <p className="platform-title-count">
              {counts[platform].toLocaleString('en-US')} {counts[platform] === 1 ? 'game' : 'games'} in your
              library
            </p>
          )}
          {platform === 'steam' ? (
            steam(counts?.steam)
          ) : platform === 'epic' ? (
            epic
          ) : (
            <section className="feature-panel" aria-label="GOG connection">
              <h2>GOG</h2>
              <p
                className="connection-state"
                role="status"
                aria-label="Not needed"
                aria-live="polite"
                data-tone="live"
              >
                Not needed
              </p>
              <p className="reading-prose">
                Installed games and playtime are read from GOG Galaxy’s local files. There is nothing to sign
                into here.
              </p>
            </section>
          )}
        </div>
      ))}
      <Notice error={library.error} />
    </section>
  )
}
