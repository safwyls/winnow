import { useEffect, useMemo, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useVirtualizer } from '@tanstack/react-virtual'
import { motion, useReducedMotion } from 'motion/react'
import {
  ArrowRight,
  ArrowUpRight,
  BookOpen,
  Compass,
  ChevronLeft,
  ChevronRight,
  Expand,
  Grid2X2,
  List,
  Maximize2,
  Palette,
  Pause,
  Play,
  Search,
  Settings2,
  Sparkles,
  X,
} from 'lucide-react'
import type { ThemeContext, ThemeDefinition } from '../../shared/theme'
import type { LibraryGame } from '../api/types'
import { useLibrary } from '../api/hooks'
import { Artwork } from '../components/Artwork'
import { Empty, GameCard, Impression, bucketLabel, hours } from '../components/primitives'
import { LibraryTools } from '../features/LibraryTools'
import dragon from '../assets/dragon.svg'
import { useViewState, libraryScroll } from '../viewState'
import { useHeroRotation } from '../useHeroRotation'

const destinations = [
  { id: 'discover', label: 'Discover', Icon: Compass },
  { id: 'library', label: 'Library', Icon: Grid2X2 },
  { id: 'journal', label: 'Journal', Icon: BookOpen },
] as const
export function Navigation({ context, vertical = false }: { context: ThemeContext; vertical?: boolean }) {
  return (
    <nav className={`primary-nav ${vertical ? 'vertical' : ''}`} aria-label="Main navigation">
      {destinations.map(({ id, label, Icon }) => (
        <button
          key={id}
          onClick={() => context.setPage(id)}
          aria-current={context.page === id ? 'page' : undefined}
        >
          <Icon size={17} />
          {label}
        </button>
      ))}
    </nav>
  )
}
export function Brand({ context }: { context: ThemeContext }) {
  return (
    <button className="brand" onClick={() => context.setPage('discover')} aria-label="Winnow home">
      <span className="dragon-mark" style={{ maskImage: `url(${dragon})` }} />
      <span>
        winnow<span className="brand-edition">Afterglow</span>
      </span>
    </button>
  )
}
export function Utilities({ context }: { context: ThemeContext }) {
  return (
    <div className="utilities">
      <button
        title="Search library (Ctrl+K)"
        aria-label="Search library"
        onClick={() => {
          context.setPage('library')
          setTimeout(() => document.querySelector<HTMLInputElement>('[data-library-search]')?.focus(), 0)
        }}
      >
        <Search size={18} />
      </button>
      <button title="Theme Studio" aria-label="Theme Studio" onClick={() => context.setPage('studio')}>
        <Palette size={18} />
      </button>
      <button
        title="Fullscreen (F11)"
        aria-label={context.mode === 'fullscreen' ? 'Leave fullscreen' : 'Enter fullscreen'}
        onClick={context.toggleFullscreen}
      >
        {context.mode === 'fullscreen' ? <X size={18} /> : <Expand size={18} />}
      </button>
      <button title="Settings" aria-label="Settings" onClick={() => context.setPage('settings')}>
        <Settings2 size={18} />
      </button>
    </div>
  )
}
export function AfterglowShell(context: ThemeContext) {
  return (
    <div className={`app-shell afterglow-shell ${context.mode}`}>
      <header className="app-header">
        <Brand context={context} />
        <Navigation context={context} />
        <Utilities context={context} />
      </header>
      <main id="main-content" tabIndex={-1} className="page-content">
        {context.children}
      </main>
      <AppFooter context={context} />
    </div>
  )
}
export function AppFooter({ context }: { context: ThemeContext }) {
  return (
    <footer className="app-footer">
      <span>Your library. Another possibility.</span>
      <span>
        {context.games.length.toLocaleString()} games <span className="footer-dot">·</span>{' '}
        {context.mode === 'fullscreen'
          ? 'Arrows to move · Enter to view · Esc to go back'
          : 'Ctrl+K to find something'}
      </span>
    </footer>
  )
}
function gameFor(context: ThemeContext, releaseId: number) {
  return context.games.find((game) => game.entries.some((entry) => entry.releaseId === releaseId))
}
export function AfterglowDiscover(context: ThemeContext) {
  const queryClient = useQueryClient()
  const [selection, setSelection] = useViewState(`discover:${context.mode}:selection`, 0)
  const [feedback, setFeedback] = useState<{ releaseId: number; kind: number; title: string } | null>(null)
  const [message, setMessage] = useState('')
  const [pending, setPending] = useState(false)
  const shelves = context.feed?.shelves ?? []
  const firstShelf = shelves.find((shelf) => shelf.supportsFeedback) ?? shelves[0]
  const firstItems = (firstShelf?.items ?? []).filter((item) => gameFor(context, item.releaseId)).slice(0, 6)
  const heroIndex = Math.min(selection, Math.max(0, firstItems.length - 1))
  const picked = firstItems[heroIndex]
  const hero = picked ? gameFor(context, picked.releaseId) : context.games[0]
  const systemReducedMotion = useReducedMotion()
  const reducedMotion = context.profile.appearance.reducedMotion || Boolean(systemReducedMotion)
  const rotation = useHeroRotation({
    count: firstItems.length,
    index: heroIndex,
    onSelect: setSelection,
    reducedMotion,
    enabled: !pending && !context.loading && !context.profile.layout.hiddenSections.includes('hero'),
  })
  const returning = [...context.games]
    .filter((game) => game.lastPlayedAt)
    .sort((a, b) => (b.lastPlayedAt ?? '').localeCompare(a.lastPlayedAt ?? ''))
    .slice(0, 8)
  useEffect(() => {
    if (context.mode !== 'fullscreen') return
    const key = (event: KeyboardEvent) => {
      if ((event.target as HTMLElement).closest('input,textarea,select,button')) return
      if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
        event.preventDefault()
        rotation.select(heroIndex + (event.key === 'ArrowRight' ? 1 : -1))
      }
      if (event.key === 'Enter' && hero) context.openGame(hero.workId)
    }
    document.addEventListener('keydown', key)
    return () => document.removeEventListener('keydown', key)
  }, [context.mode, hero, heroIndex, rotation.select])
  const dismiss = async (kind: number) => {
    if (!picked || pending) return
    setPending(true)
    setMessage('')
    try {
      const result = await window.winnow.request<{ saved: boolean }>({
        route: 'feedFeedback',
        body: { releaseId: picked.releaseId, kind },
      })
      if (!result.ok || !result.data?.saved) throw Error(result.message ?? 'Feedback could not be saved.')
      setFeedback({ releaseId: picked.releaseId, kind, title: picked.title })
      await queryClient.invalidateQueries({ queryKey: ['api', 'feed.get'] })
    } catch (error) {
      setMessage(String(error))
    } finally {
      setPending(false)
    }
  }
  const undo = async () => {
    if (!feedback) return
    setPending(true)
    try {
      const result = await window.winnow.request({
        route: 'feedRevoke',
        body: { releaseId: feedback.releaseId, kind: feedback.kind },
      })
      if (!result.ok) throw Error(result.message)
      setFeedback(null)
      await queryClient.invalidateQueries({ queryKey: ['api', 'feed.get'] })
    } catch (e) {
      setMessage(String(e))
    } finally {
      setPending(false)
    }
  }
  if (context.loading)
    return (
      <div className="loading-state">
        <span className="loading-ring" />
        Opening your library…
      </div>
    )
  if (!context.games.length)
    return (
      <Empty title="Your next chapter starts here.">
        <p>Connect a library in Settings, or add a game by hand in Library.</p>
        <button className="primary" onClick={() => context.setPage('settings')}>
          Open settings <ArrowRight size={16} />
        </button>
      </Empty>
    )
  const sections: Record<string, React.ReactNode> = {
    hero: hero && (
      <section
        className="hero-layout"
        key="hero"
        ref={rotation.ref}
        aria-roledescription="carousel"
        aria-label="Featured recommendations"
        onMouseEnter={rotation.onMouseEnter}
        onMouseLeave={rotation.onMouseLeave}
        onFocusCapture={rotation.onFocusCapture}
        onBlurCapture={rotation.onBlurCapture}
      >
        <div className="hero-stage">
          <Artwork key={`art-${hero.workId}`} workId={hero.workId} hero eager />
          <div className="hero-scrim" />
          {firstItems.length > 1 && (
            <div className="hero-controls" role="group" aria-label="Recommendation controls">
              <button aria-label="Previous recommendation" onClick={() => rotation.select(heroIndex - 1)}>
                <ChevronLeft size={17} />
              </button>
              <span
                className="hero-position"
                aria-live={rotation.rotating ? 'off' : 'polite'}
                aria-atomic="true"
              >
                <span className="sr-only">Recommendation </span>
                {heroIndex + 1}
                <span aria-hidden="true"> / </span>
                <span className="sr-only"> of </span>
                {firstItems.length}
              </span>
              <button aria-label="Next recommendation" onClick={() => rotation.select(heroIndex + 1)}>
                <ChevronRight size={17} />
              </button>
              {!reducedMotion && (
                <button
                  className="hero-rotation"
                  onClick={rotation.togglePaused}
                  aria-label={rotation.paused ? 'Resume rotation' : 'Pause rotation'}
                  title={rotation.paused ? 'Resume rotation' : 'Pause rotation'}
                >
                  {rotation.paused ? <Play size={14} /> : <Pause size={14} />}
                </button>
              )}
            </div>
          )}
          <motion.div
            className="hero-copy"
            key={`copy-${hero.workId}`}
            initial={reducedMotion ? false : { opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: reducedMotion ? 0 : 0.35 }}
          >
            <span className="eyebrow">{firstShelf?.title ?? 'From your collection'}</span>
            <h2>{hero.title}</h2>
            <p className="hero-reason">
              {picked?.reason ?? hero.summary ?? 'Make a little room for something you own.'}
            </p>
            <div className="hero-actions">
              <button className="primary" onClick={() => context.openGame(hero.workId)}>
                View game <ArrowUpRight size={18} />
              </button>
              {picked && firstShelf?.supportsFeedback && (
                <>
                  <button className="glass-button" disabled={pending} onClick={() => void dismiss(1)}>
                    Not now
                  </button>
                  <button className="glass-button" disabled={pending} onClick={() => void dismiss(0)}>
                    Not interested
                  </button>
                </>
              )}
            </div>
            <div className="hero-meta">
              <span>
                {hero.entries
                  .map((entry) => entry.store)
                  .filter((store, i, all) => all.indexOf(store) === i)
                  .join(' / ')}
              </span>
              <span>{hero.firstReleaseYear ?? 'In your library'}</span>
              <span>{hours(hero.playtimeMinutes)} played</span>
            </div>
          </motion.div>
          {picked && firstShelf && (
            <Impression
              releaseId={picked.releaseId}
              shelfId={firstShelf.id}
              enabled={firstShelf.supportsFeedback}
            >
              <span className="hero-impression-sentinel" />
            </Impression>
          )}
          <span className="hero-caption">One more world worth opening.</span>
        </div>
        {context.mode === 'fullscreen' ? (
          <div className="focus-filmstrip" role="group" aria-label="Recommendations">
            {firstItems.slice(0, 6).map((item, index) => (
              <button
                key={item.releaseId}
                className={index === heroIndex ? 'selected' : ''}
                aria-pressed={index === heroIndex}
                onClick={() => rotation.select(index)}
                onDoubleClick={() => {
                  const game = gameFor(context, item.releaseId)
                  if (game) context.openGame(game.workId)
                }}
              >
                <span className="film-number">{String(index + 1).padStart(2, '0')}</span>
                {item.title}
                <ArrowUpRight size={15} />
              </button>
            ))}
          </div>
        ) : (
          <aside className="discovery-aside">
            <span className="eyebrow">A small invitation</span>
            <h2>
              There’s more
              <br />
              <em>in here.</em>
            </h2>
            <p>You already own the beginning of your next adventure.</p>
            <div className="aside-count">
              {context.games.filter((game) => game.bucket === 'never_played').length}
              <span>worlds still waiting</span>
            </div>
            <button className="text-button" onClick={() => context.setPage('library')}>
              Explore your collection <ArrowRight size={16} />
            </button>
          </aside>
        )}
      </section>
    ),
    returning: returning.length > 0 && (
      <section className="returning-section" key="returning">
        <div className="section-heading">
          <div>
            <span className="eyebrow">Still on your mind?</span>
            <h2>Pick up the thread.</h2>
          </div>
          <button className="text-button" onClick={() => context.setPage('journal')}>
            Your journal <ArrowRight size={16} />
          </button>
        </div>
        <div className="returning-grid">
          {returning.map((game) => (
            <button key={game.workId} onClick={() => context.openGame(game.workId)}>
              <Artwork workId={game.headerWorkId ?? game.workId} />
              <span>
                <strong>{game.title}</strong>
                <small>
                  {hours(game.playtimeMinutes)} played ·{' '}
                  {game.lastPlayedAt
                    ? new Date(game.lastPlayedAt).toLocaleDateString(undefined, {
                        month: 'short',
                        day: 'numeric',
                      })
                    : ''}
                </small>
              </span>
              <ArrowUpRight size={18} />
            </button>
          ))}
        </div>
      </section>
    ),
    shelves: (
      <div key="shelves" className="feed-shelves">
        {shelves.map((shelf) => (
          <section key={shelf.id} className="feed-shelf">
            <div className="section-heading">
              <div>
                <span className="eyebrow">A different way in</span>
                <h2>{shelf.title}</h2>
              </div>
              <p>{shelf.blurb}</p>
            </div>
            <div className="card-grid">
              {shelf.items.slice(0, context.mode === 'fullscreen' ? 6 : 8).map((item) => {
                const game = gameFor(context, item.releaseId)
                return game ? (
                  <Impression
                    key={item.releaseId}
                    releaseId={item.releaseId}
                    shelfId={shelf.id}
                    enabled={shelf.supportsFeedback}
                  >
                    <GameCard
                      game={game}
                      reason={item.reason}
                      presentation={context.profile.layout.cardStyle}
                      effects={false}
                      preview={context.profile.layout.cardStyle === 'record' ? 'inline' : 'overlay'}
                      onOpen={() => context.openGame(game.workId)}
                    />
                  </Impression>
                ) : null
              })}
            </div>
          </section>
        ))}
      </div>
    ),
  }
  return (
    <div className="discover-page">
      <div className="page-heading">
        <div>
          <span className="eyebrow">A library of possibilities</span>
          <h1>What draws you in?</h1>
        </div>
        <div className="date-stamp">
          {new Date().toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' })}
          <span>
            <Sparkles size={13} />{' '}
            {context.feed?.confidence === 0 ? 'Getting to know your library' : 'Picked from what you own'}
          </span>
        </div>
      </div>
      {context.feed?.failed && (
        <p className="status-banner" role="status">
          Recommendations are unavailable. You can still explore your library.
        </p>
      )}
      {message && (
        <p className="error-banner" role="alert">
          {message}
        </p>
      )}
      {feedback && (
        <div className="receipt" role="status">
          {feedback.kind === 1
            ? `We’ll leave ${feedback.title} for later.`
            : `We won’t recommend ${feedback.title}.`}
          <button onClick={() => void undo()} disabled={pending}>
            Undo
          </button>
        </div>
      )}
      {context.profile.layout.discoverSections
        .filter((section) => !context.profile.layout.hiddenSections.includes(section))
        .map((section) => sections[section])}
    </div>
  )
}

export function AfterglowLibrary({ quietCards = true, ...context }: ThemeContext & { quietCards?: boolean }) {
  const library = useLibrary()
  const [query, setQuery] = useViewState(`library:${context.mode}:query`, ''),
    [bucket, setBucket] = useViewState(`library:${context.mode}:bucket`, 'all'),
    [store, setStore] = useViewState(`library:${context.mode}:store`, 'all'),
    [listId, setListId] = useViewState(`library:${context.mode}:list`, 'all'),
    [sort, setSort] = useViewState(`library:${context.mode}:sort`, 'title'),
    [view, setView] = useViewState<'grid' | 'list'>(`library:${context.mode}:view`, 'grid'),
    [tools, setTools] = useState(false)
  const scrollRef = useRef<HTMLDivElement>(null)
  const games = useMemo(
    () =>
      context.games
        .filter(
          (game) =>
            (!query || game.title.toLocaleLowerCase().includes(query.toLocaleLowerCase())) &&
            (bucket === 'all' ||
              (bucket === 'unplayed'
                ? game.bucket === 'never_played'
                : bucket === 'installed'
                  ? game.entries.some((entry) => entry.installed)
                  : game.bucket === bucket)) &&
            (store === 'all' || game.entries.some((entry) => entry.store === store)) &&
            (listId === 'all' ||
              game.entries.some((entry) =>
                library.data?.lists
                  .find((list) => String(list.id) === listId)
                  ?.releaseIds.includes(entry.releaseId),
              )),
        )
        .sort((a, b) =>
          sort === 'time'
            ? b.playtimeMinutes - a.playtimeMinutes
            : sort === 'recent'
              ? (b.lastPlayedAt ?? '').localeCompare(a.lastPlayedAt ?? '')
              : a.title.localeCompare(b.title),
        ),
    [context.games, query, bucket, store, listId, sort, library.data],
  )
  const listMode = view === 'list' || context.profile.layout.cardStyle === 'record'
  const [gridWidth, setGridWidth] = useState(1000)
  const [gridHeight, setGridHeight] = useState(800)
  const columns = Math.max(
    gridWidth >= 280 ? 2 : 1,
    Math.floor(gridWidth / (context.mode === 'fullscreen' ? 210 : 190)),
    // Keep a focused cover's caption inside the scroll pane in short windows.
    quietCards && context.profile.layout.cardStyle === 'poster'
      ? Math.ceil((gridWidth + 22) / (Math.max(110, (gridHeight - 28) / 1.5) + 22))
      : 1,
  )
  useEffect(() => {
    const element = scrollRef.current
    if (!element) return
    const observer = new ResizeObserver(([entry]) => {
      setGridWidth(entry.contentRect.width)
      setGridHeight(entry.contentRect.height)
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [])
  const rowSize = listMode ? 1 : columns
  const virtual = useVirtualizer({
    count: Math.ceil(games.length / rowSize),
    getScrollElement: () => scrollRef.current,
    initialOffset: () => libraryScroll.get(context.mode) ?? 0,
    onChange: (instance) => {
      if (instance.isScrolling) libraryScroll.set(context.mode, instance.scrollOffset ?? 0)
    },
    estimateSize: () =>
      listMode
        ? context.mode === 'fullscreen'
          ? 110
          : 78
        : context.profile.layout.cardStyle === 'poster'
          ? ((gridWidth - (columns - 1) * 22) / columns) * 1.5 + 28
          : 188,
    overscan: 3,
  })
  const previousFilters = useRef([query, bucket, store, listId, sort].join('\0'))
  useEffect(() => {
    virtual.measure()
  }, [columns, context.profile.layout.cardStyle, listMode])
  useEffect(() => {
    const filters = [query, bucket, store, listId, sort].join('\0')
    if (previousFilters.current !== filters) {
      virtual.scrollToOffset(0)
      libraryScroll.set(context.mode, 0)
      previousFilters.current = filters
    }
  }, [query, bucket, store, listId, sort])
  return (
    <div className="library-page">
      <div className="page-heading">
        <div>
          <span className="eyebrow">Collected over time</span>
          <h1>Your collection.</h1>
        </div>
        <button onClick={() => setTools(!tools)} className="secondary">
          {tools ? 'Close tools' : 'Manage library'} <Settings2 size={16} />
        </button>
      </div>
      {tools && <LibraryTools mode={context.mode} onOpenGame={context.openGame} />}
      <div className="library-toolbar" hidden={tools}>
        <label className="search-field">
          <Search size={18} />
          <input
            data-library-search
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Find a game…"
            aria-label="Search games"
          />
          {query && (
            <button aria-label="Clear search" onClick={() => setQuery('')}>
              <X size={15} />
            </button>
          )}
        </label>
        <label className="select-label">
          Source
          <select value={store} onChange={(event) => setStore(event.target.value)}>
            <option value="all">All stores</option>
            {[...new Set(context.games.flatMap((game) => game.entries.map((entry) => entry.store)))].map(
              (name) => (
                <option key={name}>{name}</option>
              ),
            )}
          </select>
        </label>
        <label className="select-label">
          Sort
          <select value={sort} onChange={(event) => setSort(event.target.value)}>
            <option value="title">Title</option>
            <option value="recent">Last played</option>
            <option value="time">Playtime</option>
          </select>
        </label>
        <div className="segmented">
          <button aria-label="Grid view" aria-pressed={!listMode} onClick={() => setView('grid')}>
            <Grid2X2 size={17} />
          </button>
          <button aria-label="List view" aria-pressed={listMode} onClick={() => setView('list')}>
            <List size={17} />
          </button>
        </div>
      </div>
      <div className="library-body" hidden={tools}>
        <aside className="library-index">
          <span className="eyebrow">Browse</span>
          {[
            { id: 'all', label: 'All games' },
            { id: 'unplayed', label: 'Never played' },
            { id: 'installed', label: 'Installed' },
            ...[...new Set(context.games.map((game) => game.bucket))]
              .filter((value) => !['unplayed', 'never', 'never_played'].includes(value))
              .map((value) => ({ id: value, label: bucketLabel(value) })),
          ].map((item) => (
            <button
              key={item.id}
              className={bucket === item.id ? 'selected' : ''}
              onClick={() => setBucket(item.id)}
            >
              {item.label}
              {item.id === 'all' && <span>{context.games.length}</span>}
            </button>
          ))}
          <span className="eyebrow lists-label">Your lists</span>
          <button className={listId === 'all' ? 'selected' : ''} onClick={() => setListId('all')}>
            Every list
          </button>
          {library.data?.lists.map((list) => (
            <button
              key={list.id}
              className={listId === String(list.id) ? 'selected' : ''}
              onClick={() => setListId(String(list.id))}
            >
              {list.name}
              {list.isLive && <span>Live</span>}
            </button>
          ))}
        </aside>
        <section className="library-results">
          <p className="results-count" aria-live="polite">
            {games.length.toLocaleString()} {games.length === 1 ? 'game' : 'games'}
            {query && ` matching “${query}”`}
          </p>
          <div className={`library-scroll ${listMode ? 'records' : 'grid'}`} ref={scrollRef}>
            {games.length === 0 ? (
              <Empty title="Nothing here just yet.">
                <p>Try another search or clear your filters.</p>
                <button
                  onClick={() => {
                    setQuery('')
                    setBucket('all')
                    setStore('all')
                    setListId('all')
                  }}
                >
                  Clear filters
                </button>
              </Empty>
            ) : (
              <div style={{ height: virtual.getTotalSize(), position: 'relative' }}>
                {virtual.getVirtualItems().map((row) => (
                  <div
                    key={row.key}
                    ref={virtual.measureElement}
                    data-index={row.index}
                    className={listMode ? 'virtual-record' : 'virtual-grid-row'}
                    style={{
                      position: 'absolute',
                      top: 0,
                      left: 0,
                      width: '100%',
                      transform: `translateY(${row.start}px)`,
                      gridTemplateColumns: `repeat(${columns},minmax(0,1fr))`,
                    }}
                  >
                    {games.slice(row.index * rowSize, row.index * rowSize + rowSize).map((game) =>
                      listMode ? (
                        <button
                          className="game-record"
                          key={game.workId}
                          onClick={() => context.openGame(game.workId)}
                        >
                          <Artwork workId={game.headerWorkId ?? game.workId} />
                          <span className="record-title">
                            <strong>{game.title}</strong>
                            <small>{game.entries.map((entry) => entry.store).join(' / ')}</small>
                          </span>
                          <span className="record-bucket">{bucketLabel(game.bucket)}</span>
                          <span>{hours(game.playtimeMinutes)}</span>
                          <ArrowUpRight size={17} />
                        </button>
                      ) : (
                        <GameCard
                          key={game.workId}
                          game={game}
                          presentation={context.profile.layout.cardStyle}
                          effects={quietCards ? false : undefined}
                          preview={quietCards ? 'overlay' : 'flyout'}
                          onOpen={() => context.openGame(game.workId)}
                        />
                      ),
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        </section>
      </div>
    </div>
  )
}

export const afterglow: ThemeDefinition = {
  apiVersion: 1,
  id: 'afterglow',
  name: 'Afterglow',
  defaults: {
    appearance: { palette: 'afterglow', accent: '#efad80', font: 'editorial', radius: 18, scrim: 55 },
    layout: { navigation: 'top' },
  },
  Shell: AfterglowShell,
  Discover: AfterglowDiscover,
  Library: AfterglowLibrary,
}
