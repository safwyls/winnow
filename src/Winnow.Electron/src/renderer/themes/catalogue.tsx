import { ArrowRight, ArrowUpRight, BookOpen, Hash } from 'lucide-react'
import type { ThemeContext, ThemeDefinition } from '../../shared/theme'
import { themeSettingValues } from '../../shared/theme'
import { Brand, Navigation, Utilities, AfterglowLibrary } from './afterglow'
import { Artwork } from '../components/Artwork'
import { Empty, Impression, hours } from '../components/primitives'

function CatalogueShell(context: ThemeContext) {
  return (
    <div className={`app-shell catalogue-shell ${context.mode}`}>
      <aside className="catalogue-rail">
        <Brand context={context} />
        <span className="eyebrow">The personal archive</span>
        <Navigation context={context} vertical />
        <div className="rail-note">
          <BookOpen size={28} strokeWidth={1} />
          <p>
            A collection is a conversation
            <br />
            with your past self.
          </p>
          <span>{context.games.length} games, all yours.</span>
        </div>
        <Utilities context={context} />
      </aside>
      <div className="catalogue-main">
        <header className="catalogue-masthead">
          <span>
            <Hash size={14} /> WINNOW / CATALOGUE
          </span>
          <span>{context.mode === 'fullscreen' ? 'FULLSCREEN EDITION' : 'PERSONAL EDITION'}</span>
        </header>
        <main id="main-content" tabIndex={-1} className="page-content">
          {context.children}
        </main>
      </div>
    </div>
  )
}
function CatalogueDiscover(context: ThemeContext) {
  const showArt = themeSettingValues(catalogue, context.profile).showArt
  if (context.loading) return <p className="loading-state">Opening the archive…</p>
  if (!context.games.length)
    return (
      <Empty title="A place for your collection.">
        <button onClick={() => context.setPage('settings')}>Connect a library</button>
      </Empty>
    )
  const shelves = context.feed?.shelves ?? []
  const ranked = shelves
    .flatMap((shelf) =>
      shelf.items.map((item) => ({
        item,
        shelf,
        game: context.games.find((game) => game.entries.some((entry) => entry.releaseId === item.releaseId)),
      })),
    )
    .filter((row) => row.game)
  return (
    <div className="catalogue-discover">
      <div className="catalogue-title">
        <span className="eyebrow">The index · {new Date().getFullYear()}</span>
        <h1>
          Good things,
          <br />
          <em>rediscovered.</em>
        </h1>
        <p>
          Notes from a library you’ve built over time.
          <br />
          Find a thread. Follow it somewhere.
        </p>
      </div>
      <div className="catalogue-ledger">
        <div className="ledger-head">
          <span>From your library</span>
          <span>Reason to return</span>
          <span>Time played</span>
        </div>
        {ranked.slice(0, 12).map(
          ({ game, item, shelf }, index) =>
            game && (
              <Impression key={`${shelf.id}-${item.releaseId}`} releaseId={item.releaseId} shelfId={shelf.id}>
                <button className="ledger-entry" onClick={() => context.openGame(game.workId)}>
                  <span className="ledger-number">{String(index + 1).padStart(2, '0')}</span>
                  {showArt && <Artwork workId={game.workId} />}
                  <span className="ledger-game">
                    <small>{shelf.title}</small>
                    <strong>{game.title}</strong>
                    <span>{game.entries[0]?.store}</span>
                  </span>
                  <p>{item.reason}</p>
                  <span className="ledger-time">{hours(game.playtimeMinutes)}</span>
                  <ArrowUpRight size={22} />
                </button>
              </Impression>
            ),
        )}
        {!ranked.length && <p>Your recommendations will appear here as Winnow learns about your library.</p>}
      </div>
      <button className="catalogue-explore" onClick={() => context.setPage('library')}>
        All {context.games.length} games <ArrowRight size={24} />
      </button>
    </div>
  )
}
function CatalogueLibrary(context: ThemeContext) {
  return (
    <div className="catalogue-library">
      <AfterglowLibrary {...context} />
    </div>
  )
}
export const catalogue: ThemeDefinition = {
  apiVersion: 1,
  id: 'catalogue',
  name: 'Catalogue',
  Shell: CatalogueShell,
  Discover: CatalogueDiscover,
  Library: CatalogueLibrary,
  settings: [
    {
      id: 'showArt',
      label: 'Artwork in the index',
      description: 'Switch between an illustrated ledger and a quiet text index.',
      type: 'toggle',
      default: true,
    },
  ],
}
