import {
  createContext,
  useContext,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent,
  type MouseEvent,
  type ReactNode,
} from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useVirtualizer } from '@tanstack/react-virtual'
import {
  BookOpen,
  ChevronDown,
  ChevronUp,
  Compass,
  Expand,
  Grid2X2,
  List,
  Palette,
  Search,
  Settings2,
  X,
} from 'lucide-react'
import type { ThemeContext, ThemeDefinition } from '../../shared/theme'
import { themeSettingValues } from '../../shared/theme'
import type { LibraryGame, GameDetails } from '../api/types'
import { useLibrary, useWorkspace } from '../api/hooks'
import { request, storeLabel } from '../api/client'
import { bucketLabel, hours } from '../components/primitives'
import { CreateListButton, LibraryTools } from '../features/LibraryTools'
import { AddToListButton } from '../features/parity-list-prompt'
import { LiveListActions } from '../features/parity-live-actions'
import { ListOrderActions } from '../features/parity-list-actions'
import { libraryDefaultSort, useAvalonLists } from './avalon-list-state'
import { useLibraryProjection, type ExpansionMark } from '../features/parity-library-projection'
import '../features/parity-library-projection.css'
import { usePresentationPreferences } from '../features/SettingsPreferences'
import { clearViewState, libraryScroll, useViewState } from '../viewState'
import dragon from '../assets/dragon.svg'
import { avalonFilter, coverGrid, dormancy, matchesBucket } from './avalon-data'
import { AVALON_PALETTES, avalonPaletteStyle } from './avalon-palettes'
import {
  avalonFacts,
  matchesAvalonRules,
  type AvalonFactMap,
  type AvalonWorkspace,
} from './avalon-filters'
import { AvalonFilterPanel } from './avalon-filter-panel'
import { LibraryColumnHeaders, LibraryCutBar, libraryBucketLabel, libraryIdle, libraryPlaytime, reflectedDensity, MINIMUM_TILE_WIDTH, MAXIMUM_TILE_WIDTH } from './avalon-library-chrome'
import { useAvalonPreview } from './avalon-preview'
import './avalon.css'
import { useAvalonAppearance } from './avalon-appearance'
import { FeedFeedback, FeedHistory, FeedLaunch, FeedReason, useAvalonFeed } from './avalon-feed'
import type { FeedDeckShelf, FeedRow } from './avalon-feed-model'
import {
  AvalonDesktopShelf,
  AvalonHomeRow,
  AvalonRowViewport,
  useHomeRowGeometry,
} from './avalon-row-viewport'
import { revealShelfCover } from './avalon-row-motion'
import { homePageStart, homeShelfPosition } from './avalon-navigation'
import { AvalonFullscreenGrid } from './avalon-fullscreen-grid'
import { useSystemReducedMotion } from '../useSystemReducedMotion'
import { Details } from '../features/Details'
import { AvalonSearch } from './avalon-search'
import { AvalonBackdrop } from './avalon-backdrop'

const destinations = [
  { id: 'discover', label: 'For you', Icon: Compass },
  { id: 'library', label: 'Library', Icon: Grid2X2 },
  { id: 'journal', label: 'Activity', Icon: BookOpen },
  { id: 'settings', label: 'Settings', Icon: Settings2 },
] as const
const stateKey = (context: ThemeContext) => `avalon:library:${context.mode}`
const FactsContext = createContext<AvalonFactMap>(new Map())

function Collections({ context }: { context: ThemeContext }) {
  const library = useLibrary()
  const { bucket, listId, list, filter, selectBucket, selectList } = useAvalonLists(
    context.mode,
    library.data?.lists ?? [],
    Boolean(library.data),
  )
  const buckets = [
    ...new Set([
      'all',
      'installed',
      'never_played',
      'stale_but_patched',
      ...context.games.map((game) => game.bucket),
    ]),
  ]
  return (
    <div className="avalon-collections">
      <span className="avalon-label">Collections</span>
      <div className="avalon-buckets" role="group" aria-label="Library collections">
        {buckets.map((id) => (
          <button
            key={id}
            data-controller-tab={context.mode === 'fullscreen' || undefined}
            aria-pressed={context.page === 'library' && bucket === id && listId === 'all'}
            data-filter-rule={context.page === 'library' && list?.isLive && (id === 'installed' ? filter.installed === true : filter.buckets?.includes(id)) || undefined}
            onClick={() => {
              selectBucket(id)
              context.setPage('library')
            }}
          >
            <span>
              {id === 'stale_but_patched' && <i className="avalon-patch-pip" />}
              {id === 'all'
                ? 'All games'
                : id === 'installed'
                  ? 'Installed'
                  : id === 'stale_but_patched'
                    ? 'Patched'
                    : libraryBucketLabel(id)}
            </span>
            <small>{context.games.filter((game) => matchesBucket(game, id)).length.toLocaleString()}</small>
          </button>
        ))}
      </div>
      <label className="avalon-list-picker">
        My lists
        <select
          value={context.page === 'library' ? listId : 'all'}
          onChange={(event) => {
            selectList(event.target.value)
            context.setPage('library')
          }}
        >
          <option value="all">All games</option>
          {library.data?.lists.map((list) => (
            <option value={list.id} key={list.id}>
              {list.name}
              {list.isLive ? ' · Live' : ''}
            </option>
          ))}
        </select>
      </label>
      {context.page === 'library' && listId !== 'all' && (
        <button onClick={() => selectList('all')}>Close list</button>
      )}
      {!library.data?.lists.length && (
        <p className="muted">No lists yet. Make one to keep a few games together.</p>
      )}
      <CreateListButton mode={context.mode} />
    </div>
  )
}

export function AvalonShell(context: ThemeContext) {
  const fullscreen = context.mode === 'fullscreen'
  const projected = useLibraryProjection(context.games)
  const detailsModal = !fullscreen && context.page === 'details'
  const background = useRef<ReactNode>(null)
  if (!detailsModal) background.current = context.children
  const shellPage = detailsModal ? (context.previousPage ?? 'library') : context.page
  const appearance = useAvalonAppearance(context.profile, fullscreen, context.profileHydrated)
  const workspace = useWorkspace()
  const facts = useMemo(
    () => avalonFacts(context.games, workspace.data as AvalonWorkspace | undefined),
    [context.games, workspace.data],
  )
  const settings = themeSettingValues(avalon, context.profile)
  return (
    <div
      className={`avalon-shell ${context.mode}`}
      style={{
        ...avalonPaletteStyle(String(context.profile.settings.avalon?.palette ?? settings.palette)),
        ...appearance.style,
      }}
      data-pane-layout={appearance.appearance.layout}
      data-reduced-motion={context.profile.appearance.reducedMotion || undefined}
    >
      <header className="avalon-header">
        {fullscreen && context.page === 'details' ? (
          <button className="avalon-details-return" onClick={context.closeGame}>
            B · Back to{' '}
            {context.previousPage === 'search'
              ? 'Search'
              : (destinations.find((item) => item.id === context.previousPage)?.label ?? 'Library')}
          </button>
        ) : (
          <button
            className="avalon-brand"
            aria-label="Winnow home"
            onClick={() => context.setPage('discover')}
          >
            <span className="dragon-mark" style={{ maskImage: `url(${dragon})` }} />
            <strong>WINNOW</strong>
          </button>
        )}
        {fullscreen && context.page !== 'details' && (
          <nav className="avalon-navigation" aria-label="Main navigation">
            {destinations.map(({ id, label }) => (
              <button
                key={id}
                aria-current={shellPage === id ? 'page' : undefined}
                onClick={() => context.setPage(id)}
              >
                {label}
              </button>
            ))}
          </nav>
        )}
        <div className="avalon-utilities">
          <button
            aria-label="Search library"
            title="Search library (Ctrl+K)"
            onClick={() => {
              if (context.openSearch) {
                context.openSearch()
                return
              }
              context.setPage(fullscreen ? 'search' : 'library')
              if (!fullscreen)
                setTimeout(
                  () => document.querySelector<HTMLInputElement>('[data-library-search]')?.focus(),
                  0,
                )
            }}
          >
            <Search size={18} />
          </button>
          <button aria-label="Theme Studio" title="Theme Studio" onClick={() => context.setPage('studio')}>
            <Palette size={18} />
          </button>
          <button
            aria-label={fullscreen ? 'Leave fullscreen' : 'Enter fullscreen'}
            title="Fullscreen (F11)"
            onClick={context.toggleFullscreen}
          >
            {fullscreen ? <X size={18} /> : <Expand size={18} />}
          </button>
        </div>
      </header>
      {!fullscreen && (
        <aside className="avalon-rail">
          <div className="avalon-library-total">
            <strong>{projected.games.length.toLocaleString()}</strong>
            <span>games in your library</span>
          </div>
          <nav className="avalon-navigation" aria-label="Main navigation">
            {destinations.map(({ id, label, Icon }) => (
              <button
                key={id}
                aria-current={shellPage === id ? 'page' : undefined}
                onClick={() => context.setPage(id)}
              >
                <Icon size={17} />
                {label}
              </button>
            ))}
          </nav>
          <Collections context={{ ...context, page: shellPage, games: projected.games }} />
          <p className="avalon-rail-note">Your library has unread mail.</p>
        </aside>
      )}
      <main
        id="main-content"
        className="avalon-content"
        tabIndex={-1}
        onFocus={(event) => {
          if (!fullscreen || event.target !== event.currentTarget) return
          const content = event.currentTarget
          requestAnimationFrame(() => {
            if (document.activeElement === content)
              (
                content.querySelector<HTMLElement>('[data-initial-focus]') ??
                content.querySelector<HTMLElement>('.avalon-cover[data-selected="true"]') ??
                content.querySelector<HTMLElement>('[data-avalon-game], button, input')
              )?.focus({ preventScroll: true })
          })
        }}
      >
        <FactsContext.Provider value={facts}>
          {detailsModal ? background.current : context.children}
          {detailsModal && context.children}
        </FactsContext.Provider>
      </main>
      <footer className="avalon-footer">
        <span>
          {fullscreen ? 'Arrows to browse · Enter to view · Esc to go back' : 'Your library. Rediscovered.'}
        </span>
        <span>{fullscreen ? 'F11 · Back to desktop' : 'Ctrl+K · Search'}</span>
      </footer>
    </div>
  )
}

export function AvalonCover({
  context,
  game,
  reason,
  selected,
  onFocus,
  onKeyDown,
  onClick,
  onContextMenu,
  expansion,
}: {
  context: ThemeContext
  game: LibraryGame
  reason?: string
  selected?: boolean
  onFocus?: () => void
  onKeyDown?: (event: KeyboardEvent<HTMLButtonElement>) => void
  onClick?: (event: MouseEvent<HTMLButtonElement>) => void
  onContextMenu?: (event: MouseEvent<HTMLButtonElement>) => void
  expansion?: ExpansionMark
}) {
  const facts = useContext(FactsContext).get(game.workId)
  const hover = useAvalonPreview(context, game, reason)
  const { Artwork } = context.components
  const dim = themeSettingValues(avalon, context.profile).dimCovers
  const { saturation, brightness, hue } = dormancy(game.lastPlayedAt)
  const style = {
    '--avalon-dormancy': dim
      ? `saturate(${saturation}) hue-rotate(${hue}deg) brightness(${brightness})`
      : 'none',
  } as CSSProperties
  const patched =
    facts?.unread ??
    (game.bucket === 'stale_but_patched' && (game.playtimeMinutes > 0 || !!game.lastPlayedAt))
  const stores = [...new Set(game.entries.map((entry) => entry.store))]
  return (
    <>
      <button
        className="avalon-cover"
        data-avalon-game={game.workId}
        data-work-id={game.workId}
        data-selected={selected || undefined}
        style={style}
        aria-label={`View ${game.title}${patched ? ', patched since you played' : ''}${expansion ? `. ${expansion.text}` : ''}`}
        aria-description={reason}
        onMouseEnter={(event) => hover.open(event.currentTarget)}
        onMouseLeave={hover.close}
        onBlur={hover.close}
        onClick={(event) => {
          hover.close()
          if (onClick) onClick(event)
          else context.openGame(game.workId)
        }}
        onContextMenu={onContextMenu}
        onFocus={onFocus}
        onKeyDown={onKeyDown}
      >
        <Artwork workId={game.workId} />
        <span className="avalon-cover-fallback" aria-hidden="true">
          {game.title}
        </span>
        {patched && <span className="avalon-unread" title="Patched since you played" />}
        {expansion && (
          <span className="avalon-expansion-mark" aria-hidden="true" title={expansion.text}>
            +{expansion.count}
          </span>
        )}
        <span className="avalon-cover-caption" aria-hidden="true">
          <strong>{game.title}</strong>
          <span>{reason ? <FeedReason reason={reason} /> : `${hours(game.playtimeMinutes)} played`}</span>
          <span className="avalon-store-chips">
            {stores.map((store) => (
              <span key={store}>{storeLabel(store)}</span>
            ))}
          </span>
        </span>
      </button>
      {hover.preview}
    </>
  )
}

export function AvalonDiscover(context: ThemeContext) {
  const fullscreen = context.mode === 'fullscreen'
  const feedFailed = context.feedFailed || context.feed?.failed
  const { deck, shelves, handlers, refresh } = useAvalonFeed(context)
  const [shelfId, setShelfId] = useViewState(`avalon:home:${context.mode}:shelf`, '')
  const [column, setColumn] = useViewState(`avalon:home:${context.mode}:column`, 0)
  const [positions, setPositions] = useViewState<Record<string, number>>(
    `avalon:home:${context.mode}:positions`,
    {},
  )
  const [capacity, setCapacity] = useState(10)
  const shelfIndex = Math.max(
    0,
    shelves.findIndex((shelf) => shelf.id === shelfId),
  )
  const shelf = shelves[shelfIndex],
    rowIndex = Math.min(column, (shelf?.rows.length ?? 1) - 1),
    picked = shelf?.rows[rowIndex]
  const rowRef = useRef<HTMLDivElement>(null),
    focusPending = useRef(false),
    rowHadFocus = useRef(false)
  const homeRef = useRef<HTMLDivElement>(null)
  useHomeRowGeometry(
    homeRef,
    fullscreen && !!picked && !context.loading && (!context.feedLoading || !!context.feed),
    setCapacity,
  )
  const systemReducedMotion = useSystemReducedMotion()
  const { Artwork, Impression } = context.components
  useEffect(() => {
    if (shelf && shelf.id !== shelfId) setShelfId(shelf.id)
  }, [shelfId, shelf?.id])
  useEffect(() => {
    const clearFocus = () => {
      rowHadFocus.current = false
    }
    window.addEventListener('blur', clearFocus)
    return () => window.removeEventListener('blur', clearFocus)
  }, [])
  function changeShelf(next: number) {
    const target = shelves[Math.max(0, Math.min(shelves.length - 1, next))]
    if (!target) return
    if (fullscreen) focusPending.current = true
    if (target.id === shelf?.id) {
      focusCover()
      return
    }
    const position = fullscreen
      ? homeShelfPosition(rowIndex, positions[target.id] ?? 0, capacity, target.rows.length)
      : Math.min(column, target.rows.length - 1)
    setShelfId(target.id)
    setColumn(position)
    setPositions((saved) => ({ ...saved, [target.id]: position }))
  }
  function key(event: KeyboardEvent<HTMLButtonElement>, index: number, current: FeedDeckShelf) {
    if (!fullscreen) {
      let targetShelf = current,
        targetIndex = index
      if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
        targetShelf =
          shelves[
            Math.max(
              0,
              Math.min(shelves.length - 1, shelves.indexOf(current) + (event.key === 'ArrowDown' ? 1 : -1)),
            )
          ]
      } else if (event.key === 'ArrowLeft' || event.key === 'ArrowRight')
        targetIndex += event.key === 'ArrowRight' ? 1 : -1
      else return
      event.preventDefault()
      event.stopPropagation()
      targetIndex = Math.max(0, Math.min(targetShelf.rows.length - 1, targetIndex))
      const target = rowRef.current
        ?.querySelectorAll<HTMLElement>('.avalon-desktop-covers')
        [shelves.indexOf(targetShelf)]?.querySelectorAll<HTMLButtonElement>('[data-avalon-game]')[targetIndex]
      target?.focus({ preventScroll: true })
      target
        ?.closest('.avalon-shelf')
        ?.scrollIntoView?.({ block: 'nearest', inline: 'nearest', behavior: 'instant' })
      if (target) revealShelfCover(target)
      return
    }
    if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
      event.preventDefault()
      event.stopPropagation()
      focusPending.current = true
      changeShelf(shelfIndex + (event.key === 'ArrowDown' ? 1 : -1))
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      event.preventDefault()
      event.stopPropagation()
      focusPending.current = true
      const position = Math.max(
        0,
        Math.min((shelf?.rows.length ?? 1) - 1, index + (event.key === 'ArrowRight' ? 1 : -1)),
      )
      setColumn(position)
      setPositions((saved) => ({ ...saved, [current.id]: position }))
    }
  }
  function focusCover() {
    if (!fullscreen) return
    const cover = rowRef.current?.querySelector<HTMLButtonElement>(
      `[data-row-active="true"] [data-avalon-game="${picked?.game.workId}"]`,
    )
    if (!cover) return
    if (focusPending.current || rowHadFocus.current) cover?.focus({ preventScroll: true })
    focusPending.current = false
    revealShelfCover(cover)
  }
  useLayoutEffect(focusCover, [fullscreen, picked?.game.workId, shelf?.id, capacity])
  if (context.loading || (context.feedLoading && !context.feed))
    return (
      <div className="avalon-empty" role="status">
        <span className="dragon-mark" style={{ maskImage: `url(${dragon})` }} />
        <p>{context.loading ? 'Preparing your library…' : 'Building the feed…'}</p>
      </div>
    )
  if (!context.games.length)
    return (
      <div className="avalon-empty">
        <h1>Your library starts here.</h1>
        <p>Connect a store or add a game to see what is waiting for you.</p>
        <button onClick={() => context.setPage('settings')}>Open settings</button>
        <FeedHistory games={context.games} deck={deck} />
      </div>
    )
  if (!picked)
    return (
      <div className="avalon-empty">
        <h1>For you</h1>
        <p>
          {feedFailed
            ? 'Recommendations could not be loaded. Your library is still available.'
            : context.feed?.candidateCount
              ? 'Nothing to suggest right now. Your library is still here to explore.'
              : 'Recommendations appear here as Winnow learns about your library.'}
        </p>
        <button onClick={() => context.setPage('library')}>Browse library</button>
        {feedFailed && <button onClick={refresh}>Try again</button>}
        <FeedHistory games={context.games} deck={deck} />
      </div>
    )
  function card(current: FeedDeckShelf, entry: FeedRow, index: number) {
    const cover = (
      <AvalonCover
        context={context}
        game={entry.game}
        reason={entry.reason}
        selected={fullscreen && current.id === shelf?.id && index === rowIndex}
        onFocus={() => {
          if (fullscreen) {
            setColumn(index)
            setPositions((saved) => (saved[current.id] === index ? saved : { ...saved, [current.id]: index }))
          }
        }}
        onKeyDown={(event) => key(event, index, current)}
      />
    )
    return entry.releaseId === undefined || !current.feedback ? (
      <div className="avalon-home-card" key={entry.game.workId}>
        <div {...handlers(entry, 'cover')}>{cover}</div>
      </div>
    ) : (
      <Impression key={entry.game.workId} releaseId={entry.releaseId} shelfId={current.id}>
        <div {...handlers(entry, 'cover')}>{cover}</div>
      </Impression>
    )
  }
  if (!fullscreen)
    return (
      <div className="avalon-discover" ref={rowRef}>
        <header className="avalon-page-heading">
          <h1>For you</h1>
          <p>Something worth coming back to.</p>
        </header>
        <div className="avalon-feed-summary">
          {!!context.feed?.candidateCount && (
            <span>
              <span className="avalon-feed-date">{context.feed.candidateCount.toLocaleString()}</span> games
              scored
            </span>
          )}
          {context.feed && !!context.feed.candidateCount && !feedFailed && context.feed.confidence < 2 && (
            <span>
              {context.feed.confidence === 0
                ? 'Based on playtime and patch history. Improves as you play.'
                : 'Recorded sessions help refine your picks.'}
            </span>
          )}
          <FeedHistory games={context.games} deck={deck} />
        </div>
        {feedFailed && (
          <p className="error-banner" role="alert">
            Some recommendations could not be loaded.
            <button onClick={refresh}>Try again</button>
          </p>
        )}
        {shelves.map((current) => (
          <section className="avalon-shelf" key={current.id}>
            <header>
              <div className="avalon-shelf-label">
                <h2>{current.title}</h2>
                <span className="avalon-shelf-count" aria-label={`${current.rows.length} games`}>
                  {current.rows.length.toLocaleString()}
                </span>
              </div>
              <p>{current.blurb}</p>
            </header>
            <AvalonDesktopShelf id={current.id}>
              {current.rows.map((entry, index) => (
                <div key={entry.game.workId} {...handlers(entry, 'card')}>
                  {card(current, entry, index)}
                  <FeedLaunch context={context} row={entry} />
                  <FeedFeedback deck={deck} shelf={current} row={entry} />
                  <AddToListButton
                    games={[entry.game]}
                    mode={context.mode}
                    origin="feed"
                    label={`Add ${entry.game.title} to list…`}
                  />
                </div>
              ))}
            </AvalonDesktopShelf>
          </section>
        ))}
      </div>
    )
  return (
    <div
      className="avalon-home"
      ref={homeRef}
      onWheel={(event) => {
        if (Math.abs(event.deltaY) > 8) changeShelf(shelfIndex + Math.sign(event.deltaY))
      }}
    >
      <div className="avalon-home-backdrop" aria-hidden="true">
        <AvalonBackdrop workId={picked.game.workId} reducedMotion={context.profile.appearance.reducedMotion} />
      </div>
      <div className="avalon-home-hero" {...handlers(picked, 'hero')}>
        <div className="avalon-feed-summary">
          <FeedHistory games={context.games} deck={deck} />
          {feedFailed && (
            <span role="alert">
              Recommendations could not be refreshed. <button onClick={refresh}>Try again</button>
            </span>
          )}
        </div>
        <span className="avalon-label">{shelf.title}</span>
        <h1 title={picked.game.title}>{picked.game.title}</h1>
        <p title={picked.reason}>
          <FeedReason reason={picked.reason} />
        </p>
        <div className="avalon-hero-actions">
          <button onClick={() => context.openGame(picked.game.workId)}>View game</button>
          <FeedLaunch key={picked.game.workId} context={context} row={picked} />
          <FeedFeedback deck={deck} shelf={shelf} row={picked} />
          <AddToListButton games={[picked.game]} mode={context.mode} origin="feed" />
        </div>
      </div>
      <section className="avalon-home-shelf">
        <h2>{shelf.title}</h2>
        <AvalonRowViewport
          rows={shelves}
          first={shelfIndex}
          reducedMotion={context.profile.appearance.reducedMotion || systemReducedMotion}
          viewportRef={rowRef}
          onReady={focusCover}
        >
          {(current) => {
            const offset = homePageStart(
              current.id === shelf.id ? rowIndex : (positions[current.id] ?? 0),
              capacity,
              current.rows.length,
            )
            return (
              <AvalonHomeRow
                id={current.id}
                page={Math.floor(offset / capacity)}
                onFocusChange={(value) => {
                  rowHadFocus.current = value
                }}
              >
                {current.rows
                  .slice(offset, offset + capacity)
                  .map((entry, index) => card(current, entry, offset + index))}
              </AvalonHomeRow>
            )
          }}
        </AvalonRowViewport>
        <div className="avalon-shelf-navigation" aria-label="Recommendation shelves">
          <button
            tabIndex={-1}
            aria-label="Previous shelf"
            disabled={shelfIndex === 0}
            onClick={() => changeShelf(shelfIndex - 1)}
          >
            <ChevronUp />
          </button>
          {shelves.map((entry, index) => (
            <button
              tabIndex={-1}
              key={entry.id}
              data-controller-tab
              aria-label={`Show ${entry.title}`}
              aria-current={index === shelfIndex ? 'true' : undefined}
              onClick={() => changeShelf(index)}
            >
              <i />
            </button>
          ))}
          <button
            tabIndex={-1}
            aria-label="Next shelf"
            disabled={shelfIndex === shelves.length - 1}
            onClick={() => changeShelf(shelfIndex + 1)}
          >
            <ChevronDown />
          </button>
        </div>
      </section>
    </div>
  )
}

export function AvalonLibrary(context: ThemeContext) {
  const library = useLibrary(),
    fullscreen = context.mode === 'fullscreen',
    prefix = stateKey(context)
  const preferences = usePresentationPreferences()
  const projected = useLibraryProjection(context.games)
  const libraryGames = projected.games
  const systemReducedMotion = useSystemReducedMotion()
  const workspace = useWorkspace(),
    client = useQueryClient()
  const facts = useMemo(
    () => avalonFacts(libraryGames, workspace.data as AvalonWorkspace | undefined),
    [libraryGames, workspace.data],
  )
  const defaultSort = libraryDefaultSort(preferences.values.DefaultSort)
  const listState = useAvalonLists(
    context.mode,
    library.data?.lists ?? [],
    Boolean(library.data),
    preferences.loaded || preferences.values.DefaultSort !== undefined ? defaultSort : undefined,
  )
  const { query, setQuery, bucket, store, setStore, listId, savedSort, setSort, rules, setRules } = listState
  const [view, setView] = useViewState(`${prefix}:view`, 'grid')
  const [filterOrder] = useViewState(`${prefix}:filter-order`, new Map<string, (string | number)[]>())
  const sort = savedSort ?? defaultSort
  const [density, setDensity] = useViewState(`${prefix}:density`, 148),
    [tools, setTools] = useViewState(`${prefix}:tools`, false)
  const [selected, setSelected] = useViewState<number | null>(`${prefix}:selected`, null)
  const [selection, setSelection] = useViewState<number[]>(`${prefix}:selection`, [])
  const [filtersOpen, setFiltersOpen] = useState(false),
    [selectionError, setSelectionError] = useState(''),
    [selectionBusy, setSelectionBusy] = useState(false)
  const anchor = useRef<number | null>(null)
  const toolsReturn = useRef<{ workId: number | null; offset: number } | null>(null)
  const manageButton = useRef<HTMLButtonElement>(null)
  const toolsPanel = useRef<HTMLDivElement>(null)
  const scroll = useRef<HTMLDivElement>(null),
    [size, setSize] = useViewState(`${prefix}:viewport`, { width: 950, height: 660 })
  const games = useMemo(
    () =>
      avalonFilter(libraryGames, library.data?.lists ?? [], {
        query,
        bucket,
        store,
        listId: listState.list?.isLive ? 'all' : listId,
        sort,
      }).filter((game) => matchesAvalonRules(game, rules, facts.get(game.workId))),
    [libraryGames, library.data?.lists, query, bucket, store, listId, sort, rules, facts],
  )
  useEffect(() => {
    const visible = new Set(games.map((game) => game.workId))
    const retained = selection.filter((id) => visible.has(id))
    if (retained.length !== selection.length) setSelection(retained)
    if (!fullscreen && selected !== null && !visible.has(selected)) setSelected(retained[0] ?? null)
    if (anchor.current !== null && !visible.has(anchor.current)) anchor.current = null
  }, [games, selection, selected, fullscreen])
  useEffect(() => {
    if (fullscreen && !games.length) {
      setSelected(null)
      clearViewState(`${prefix}:rows`)
    }
  }, [fullscreen, games.length, prefix])
  const grid = coverGrid(size.width, size.height, density, fullscreen),
    listMode = !fullscreen && view === 'list'
  const columns = listMode ? 1 : grid.columns,
    rowHeight = listMode ? 44 : grid.rowHeight
  const virtual = useVirtualizer({
    count: Math.ceil(games.length / columns),
    getScrollElement: () => scroll.current,
    initialOffset: () => libraryScroll.get(prefix) ?? 0,
    estimateSize: () => rowHeight,
    overscan: 2,
  })
  useLayoutEffect(() => {
    let nextFrame = 0
    const frame = requestAnimationFrame(() => {
      if (tools) {
        toolsPanel.current?.querySelector<HTMLElement>('nav button, input, button')?.focus()
        return
      }
      const position = toolsReturn.current
      if (!position || !scroll.current) return
      scroll.current.scrollTop = position.offset
      virtual.scrollToOffset(position.offset)
      nextFrame = requestAnimationFrame(() => {
        const target = scroll.current?.querySelector<HTMLButtonElement>(
          `[data-avalon-game="${position.workId}"]`,
        )
        ;(target ?? manageButton.current)?.focus({ preventScroll: true })
        toolsReturn.current = null
      })
    })
    return () => {
      cancelAnimationFrame(frame)
      cancelAnimationFrame(nextFrame)
    }
  }, [tools, prefix])
  useEffect(() => {
    if (!scroll.current) return
    const observer = new ResizeObserver(([entry]) =>
      setSize({ width: entry.contentRect.width, height: entry.contentRect.height }),
    )
    observer.observe(scroll.current)
    return () => observer.disconnect()
  }, [tools, filtersOpen])
  useEffect(() => {
    virtual.measure()
  }, [rowHeight])
  const previousFilters = useRef([query, bucket, store, listId, sort, JSON.stringify(rules)].join('\0'))
  useEffect(() => {
    const next = [query, bucket, store, listId, sort, JSON.stringify(rules)].join('\0')
    if (previousFilters.current !== next) {
      virtual.scrollToOffset(0)
      libraryScroll.set(prefix, 0)
      previousFilters.current = next
    }
  }, [query, bucket, store, listId, sort, rules])
  function selectGame(event: MouseEvent<HTMLButtonElement>, game: LibraryGame, index: number) {
    if (event.ctrlKey || event.metaKey) {
      event.preventDefault()
      const next = selection.includes(game.workId)
        ? selection.filter((id) => id !== game.workId)
        : [...selection, game.workId]
      setSelection(next)
      setSelected(next.includes(game.workId) ? game.workId : (next.at(-1) ?? null))
    } else if (event.shiftKey && anchor.current !== null) {
      event.preventDefault()
      const first = games.findIndex((game) => game.workId === anchor.current)
      setSelection(
        games
          .slice(Math.max(0, Math.min(first, index)), Math.max(first, index) + 1)
          .map((game) => game.workId),
      )
    } else {
      setSelection([])
      setSelected(game.workId)
      context.openGame(game.workId)
    }
    anchor.current = game.workId
  }
  function contextGame(event: MouseEvent<HTMLButtonElement>, game: LibraryGame) {
    event.preventDefault()
    if (!selection.includes(game.workId)) {
      setSelection([game.workId])
      setSelected(game.workId)
    }
    requestAnimationFrame(() =>
      document.querySelector<HTMLElement>('.avalon-selection-actions button')?.focus(),
    )
  }
  async function selectionAction(kind: 'read' | 'derelict') {
    if (selectionBusy) return
    const selectedGames = games.filter((game) => selection.includes(game.workId))
    setSelectionBusy(true)
    setSelectionError('')
    let failures = 0
    try {
      if (kind === 'derelict')
        await request('library.derelict-exemptions', undefined, {
          workIds: selectedGames.filter((game) => game.bucket === 'derelict').map((game) => game.workId),
        })
      else
        for (const game of selectedGames) {
          const watermarks = new Map(facts.get(game.workId)?.watermarks)
          if (!facts.get(game.workId)?.unread) continue
          if (!watermarks.size) {
            failures++
            continue
          }
          try {
            const detail = await request<GameDetails>('game.details', { workId: game.workId })
            for (const [releaseId, watermark] of watermarks) {
              // Never acknowledge a build push newer than the library tile the user selected.
              const observedEventIds = detail.events
                .filter(
                  (event) =>
                    event.releaseId === releaseId &&
                    (event.kind !== 'build_push' || Date.parse(event.occurredAt) <= Date.parse(watermark)),
                )
                .map((event) => event.id)
              const response = await request<{ result: string }>(
                'updates.acknowledge',
                { releaseId },
                { observedEventIds },
              )
              if (!['Stored', 'NothingToDo'].includes(response.result)) failures++
            }
          } catch {
            failures++
          }
        }
      if (failures)
        setSelectionError(`${failures} update changes could not be saved. Open game details or try again.`)
    } catch (error) {
      setSelectionError(
        error instanceof Error ? error.message : 'The selection could not be updated. Try again.',
      )
    } finally {
      await client.invalidateQueries({ queryKey: ['api'] })
      setSelectionBusy(false)
    }
  }
  function key(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a') {
      event.preventDefault()
      event.stopPropagation()
      setSelection(games.map((game) => game.workId))
      return
    }
    if ((event.ctrlKey || event.metaKey) && event.key === ' ') {
      event.preventDefault()
      const id = games[index].workId
      const next = selection.includes(id) ? selection.filter((value) => value !== id) : [...selection, id]
      setSelection(next)
      setSelected(next.includes(id) ? id : (next.at(-1) ?? null))
      return
    }
    let next = index
    if (event.key === 'ArrowRight') next++
    else if (event.key === 'ArrowLeft') next--
    else if (event.key === 'ArrowDown') next += columns
    else if (event.key === 'ArrowUp') next -= columns
    else if (event.key === 'Home') next = event.ctrlKey ? 0 : Math.floor(index / columns) * columns
    else if (event.key === 'End')
      next = event.ctrlKey ? games.length - 1 : Math.floor(index / columns) * columns + columns - 1
    else return
    event.preventDefault()
    event.stopPropagation()
    next = Math.max(0, Math.min(games.length - 1, next))
    setSelected(games[next].workId)
    virtual.scrollToIndex(Math.floor(next / columns), { align: 'auto' })
    requestAnimationFrame(() =>
      requestAnimationFrame(() =>
        scroll.current
          ?.querySelector<HTMLButtonElement>(`[data-avalon-game="${games[next].workId}"]`)
          ?.focus({ preventScroll: true }),
      ),
    )
  }
  const { Artwork } = context.components
  return (
    <FactsContext.Provider value={facts}>
      <div className="avalon-library" data-filters-open={filtersOpen || undefined}>
        <header className="avalon-page-heading">
          <h1>Library</h1>
          <button
            ref={manageButton}
            aria-expanded={tools}
            onClick={() => {
              if (!tools) {
                const offset = scroll.current?.scrollTop ?? libraryScroll.get(prefix) ?? 0
                toolsReturn.current = { workId: selected, offset }
                libraryScroll.set(prefix, offset)
              }
              setTools(!tools)
            }}
          >
            <Settings2 size={16} />
            {tools ? 'Close tools' : 'Manage library'}
          </button>
        </header>
        {tools ? (
          <div
            ref={toolsPanel}
            className="avalon-library-tools"
            onKeyDown={(event) => {
              if (
                event.key === 'Escape' &&
                !event.defaultPrevented &&
                !(event.target as HTMLElement).closest('[role="dialog"], [role="alertdialog"]')
              ) {
                event.preventDefault()
                event.stopPropagation()
                setTools(false)
              }
            }}
          >
            <LibraryTools mode={context.mode} onOpenGame={context.openGame} />
          </div>
        ) : (
          <>
            {fullscreen && <Collections context={{ ...context, games: libraryGames }} />}
            <div className="avalon-toolbar">
              <button
                data-controller-context
                aria-expanded={filtersOpen}
                onClick={() => setFiltersOpen(!filtersOpen)}
              >
                Filters
              </button>
              <label className="avalon-search">
                <Search size={17} />
                <input
                  data-library-search
                  aria-label="Search games"
                  placeholder="Search your library…"
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                />
                {query && (
                  <button aria-label="Clear search" onClick={() => setQuery('')}>
                    <X size={15} />
                  </button>
                )}
              </label>
              {([...new Set(libraryGames.flatMap((game) => game.entries.map((entry) => entry.store)))].length > 1 || store !== 'all') && <label>
                Store
                <select aria-label="Store" value={store} onChange={(event) => setStore(event.target.value)}>
                  <option value="all">All stores</option>
                  {[...new Set(libraryGames.flatMap((game) => game.entries.map((entry) => entry.store)))].map(
                    (value) => (
                      <option key={value} value={value}>
                        {storeLabel(value)}
                      </option>
                    ),
                  )}
                </select>
              </label>}
              <label>
                Sort
                <select aria-label="Sort" value={sort} onChange={(event) => setSort(event.target.value)}>
                  <option value="dormant">Dormant longest</option>
                  <option value="title">Name A–Z</option>
                  <option value="title-desc">Name Z–A</option>
                  <option value="recent">Last played</option>
                  <option value="time">Playtime high to low</option>
                  <option value="time-low">Playtime low to high</option>
                  {listState.list && !listState.list.isLive && <option value="list-order">List order</option>}
                </select>
              </label>
              {!fullscreen && (
                <>
                  <div className="avalon-segmented" role="group" aria-label="Library view">
                    <button aria-label="Grid view" aria-pressed={!listMode} onClick={() => setView('grid')}>
                      <Grid2X2 size={17} />
                    </button>
                    <button aria-label="List view" aria-pressed={listMode} onClick={() => setView('list')}>
                      <List size={17} />
                    </button>
                  </div>
                  <label className="avalon-density">
                    Density
                    <input
                      aria-label="Density"
                      type="range"
                      min={MINIMUM_TILE_WIDTH}
                      max={MAXIMUM_TILE_WIDTH}
                      step={4}
                      value={reflectedDensity(density)}
                      onChange={(event) => setDensity(reflectedDensity(Number(event.target.value)))}
                    />
                  </label>
                </>
              )}
            </div>
            <LibraryCutBar state={listState} games={libraryGames} visible={games.length} facts={facts} workspace={workspace.data as AvalonWorkspace | undefined}>
              <LiveListActions state={listState} mode={context.mode} compact />
            </LibraryCutBar>
            {listState.list && !listState.list.isLive && (
              <ListOrderActions
                key={listState.list.id}
                list={listState.list}
                games={libraryGames}
                selected={
                  selection.length
                    ? games.filter((game) => selection.includes(game.workId))
                    : games.filter((game) => game.workId === selected)
                }
              />
            )}
            {
              <div
                className="avalon-selection-actions"
                role="group"
                aria-label="Selected games"
                style={{
                  visibility:
                    selection.length > 0 ||
                    (selected != null && games.some((game) => game.workId === selected))
                      ? 'visible'
                      : 'hidden',
                }}
              >
                <span>{selection.length || 1} selected</span>
                <AddToListButton
                  games={
                    selection.length
                      ? games.filter((game) => selection.includes(game.workId))
                      : games.filter((game) => game.workId === selected)
                  }
                  mode={context.mode}
                />
                {games.some((game) => selection.includes(game.workId) && facts.get(game.workId)?.unread) && (
                  <button disabled={selectionBusy} onClick={() => void selectionAction('read')}>
                    Mark as read
                  </button>
                )}
                {games.some((game) => selection.includes(game.workId) && game.bucket === 'derelict') && (
                  <button disabled={selectionBusy} onClick={() => void selectionAction('derelict')}>
                    Remove from Derelict
                  </button>
                )}
                {selection.length > 0 && (
                  <button
                    onClick={() => {
                      setSelection([])
                      if (!fullscreen) setSelected(null)
                    }}
                  >
                    Clear selection
                  </button>
                )}
              </div>
            }
            {selectionError && (
              <p role="alert" className="error-banner">
                {selectionError}
              </p>
            )}
            <p className="avalon-results-count" aria-live="polite">
              {games.length.toLocaleString()} {games.length === 1 ? 'game' : 'games'}
              {query && ` matching “${query}”`}
            </p>
            {listMode && <LibraryColumnHeaders sort={sort} change={setSort} width={size.width} />}
            <div className="avalon-library-results" inert={fullscreen && filtersOpen ? true : undefined}>
              <div
                ref={scroll}
                className="avalon-library-scroll"
                onKeyDownCapture={(event) => {
                  if (
                    (event.target as HTMLElement).closest('[data-avalon-game]') &&
                    [
                      'ArrowLeft',
                      'ArrowRight',
                      'ArrowUp',
                      'ArrowDown',
                      'Home',
                      'End',
                      'PageUp',
                      'PageDown',
                    ].includes(event.key)
                  )
                    setSelection([])
                }}
                onScroll={() => {
                  if (scroll.current) libraryScroll.set(prefix, scroll.current.scrollTop)
                }}
              >
                {context.loading ? (
                  <p role="status">Preparing your library…</p>
                ) : !games.length ? (
                  <div className="avalon-empty">
                    <h2>
                      {libraryGames.length ? 'No games match these filters.' : 'Your library starts here.'}
                    </h2>
                    <p>
                      {libraryGames.length
                        ? 'No titles match these filters. Drop one to widen the cut.'
                        : 'Connect a store in Settings, or add a game with Manage library.'}
                    </p>
                    {!libraryGames.length && <button onClick={() => context.setPage('settings')}>Open settings</button>}
                  </div>
                ) : fullscreen ? (
                  <AvalonFullscreenGrid
                    games={games}
                    columns={columns}
                    gap={grid.gap}
                    prefix={prefix}
                    selected={selected}
                    onSelected={setSelected}
                    reducedMotion={context.profile.appearance.reducedMotion || systemReducedMotion}
                    onKeyDown={key}
                  >
                    {(game, index, handlers) => (
                      <AvalonCover
                        key={game.workId}
                        context={context}
                        game={game}
                        expansion={projected.marks.get(game.workId)}
                        selected={
                          selection.length ? selection.includes(game.workId) : selected === game.workId
                        }
                        onClick={(event) => selectGame(event, game, index)}
                        onContextMenu={(event) => contextGame(event, game)}
                        onFocus={handlers.onFocus}
                        onKeyDown={handlers.onKeyDown}
                      />
                    )}
                  </AvalonFullscreenGrid>
                ) : (
                  <div style={{ height: virtual.getTotalSize(), position: 'relative' }}>
                    {virtual.getVirtualItems().map((row) => (
                      <div
                        key={row.key}
                        className={listMode ? 'avalon-record-row' : 'avalon-grid-row'}
                        style={{
                          position: 'absolute',
                          top: 0,
                          left: 0,
                          width: '100%',
                          transform: `translateY(${row.start}px)`,
                          gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
                          gap: grid.gap,
                          paddingBottom: listMode ? 0 : grid.gap,
                        }}
                      >
                        {games.slice(row.index * columns, (row.index + 1) * columns).map((game, index) =>
                          listMode ? (
                            <button
                              className="avalon-record"
                              key={game.workId}
                              data-avalon-game={game.workId}
                              data-work-id={game.workId}
                              aria-label={`View ${game.title}${projected.marks.has(game.workId) ? `. ${projected.marks.get(game.workId)!.text}` : ''}`}
                              aria-pressed={
                                selection.length ? selection.includes(game.workId) : selected === game.workId
                              }
                              onClick={(event) => selectGame(event, game, row.index * columns + index)}
                              onContextMenu={(event) => contextGame(event, game)}
                              onFocus={() => setSelected(game.workId)}
                              onKeyDown={(event) => key(event, row.index * columns + index)}
                            >
                              <Artwork workId={game.workId} />
                              <strong className="avalon-record-title">
                                {game.title}
                                {projected.marks.has(game.workId) && (
                                  <small title={projected.marks.get(game.workId)!.text}>
                                    +{projected.marks.get(game.workId)!.count}
                                  </small>
                                )}
                              </strong>
                              <span>
                                {[...new Set(game.entries.map((entry) => storeLabel(entry.store)))].join(
                                  ' / ',
                                )}
                              </span>
                              <span>{libraryBucketLabel(game.bucket)}</span>
                              <span>{libraryPlaytime(game.playtimeMinutes)}</span>
                              <span>{libraryIdle(game.lastPlayedAt)}</span>
                            </button>
                          ) : (
                            <AvalonCover
                              key={game.workId}
                              context={context}
                              game={game}
                              expansion={projected.marks.get(game.workId)}
                              selected={
                                selection.length ? selection.includes(game.workId) : selected === game.workId
                              }
                              onClick={(event) => selectGame(event, game, row.index * columns + index)}
                              onContextMenu={(event) => contextGame(event, game)}
                              onFocus={() => setSelected(game.workId)}
                              onKeyDown={(event) => key(event, row.index * columns + index)}
                            />
                          ),
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
            {filtersOpen && (
              <AvalonFilterPanel
                filter={{ ...rules, ...(store === 'all' ? {} : { stores: [store] }) }}
                allGames={libraryGames}
                optionOrder={filterOrder}
                games={avalonFilter(libraryGames, library.data?.lists ?? [], {
                  query,
                  bucket,
                  store: 'all',
                  listId: listState.list?.isLive ? 'all' : listId,
                  sort,
                })}
                facts={facts}
                workspace={workspace.data as AvalonWorkspace | undefined}
                fullscreen={fullscreen}
                apply={(next) => {
                  setStore('all')
                  setRules({ ...next, ...(rules.buckets ? { buckets: rules.buckets } : {}), ...(rules.hasUnread != null ? { hasUnread: rules.hasUnread } : {}) })
                }}
                close={() => setFiltersOpen(false)}
              />
            )}
          </>
        )}
      </div>
    </FactsContext.Provider>
  )
}

export const avalon: ThemeDefinition = {
  apiVersion: 1,
  id: 'avalon',
  name: 'Avalon',
  Shell: AvalonShell,
  Discover: AvalonDiscover,
  Library: AvalonLibrary,
  Search: AvalonSearch,
  Details: (context) =>
    context.selectedWorkId != null ? (
      <Details
        presentation="avalon"
        workId={context.selectedWorkId}
        mode={context.mode}
        onClose={context.closeGame}
      />
    ) : (
      <p>Choose a game from your library.</p>
    ),
  defaults: {
    appearance: {
      palette: 'winnow',
      accent: '#4DE8C2',
      font: 'modern',
      radius: 6,
      scrim: 70,
    },
    layout: { navigation: 'left', cardStyle: 'poster' },
  },
  settings: [
    {
      id: 'palette',
      label: 'Avalon palette',
      description: 'The original bundled Winnow palettes. Choose Studio colors to use your custom palette.',
      type: 'select',
      default: 'profile',
      options: [
        { value: 'profile', label: 'Studio colors' },
        ...AVALON_PALETTES.map(({ id, name }) => ({ value: id, label: name })),
      ],
    },
    {
      id: 'dimCovers',
      label: 'Dim dormant covers',
      description: 'Forgotten games fade gently. Hover or focus restores their color.',
      type: 'toggle',
      default: true,
    },
  ],
}
