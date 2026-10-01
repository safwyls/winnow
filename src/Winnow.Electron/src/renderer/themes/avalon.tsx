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
  ChartNoAxesCombined,
  Compass,
  Expand,
  Grid2X2,
  GitMerge,
  List,
  Palette,
  Search,
  Settings2,
  X,
} from 'lucide-react'
import type { ThemeContext, ThemeDefinition } from '../../shared/theme'
import { avalonPaletteId, themeSettingValues } from '../../shared/theme'
import type { LibraryGame, GameDetails } from '../api/types'
import { useLibrary, useWorkspace } from '../api/hooks'
import { request, storeLabel } from '../api/client'
import { bucketLabel } from '../components/primitives'
import { CreateListButton, LibraryTools, ListEditor } from '../features/LibraryTools'
import { SortMenu } from '../components/SortMenu'
import { AddToListButton, orderedLists } from '../features/parity-list-prompt'
import { LiveListActions } from '../features/parity-live-actions'
import { ListOrderActions } from '../features/parity-list-actions'
import { libraryDefaultSort, useAvalonLists } from './avalon-list-state'
import { useLibraryProjection } from '../features/parity-library-projection'
import '../features/parity-library-projection.css'
import { usePresentationPreferences } from '../features/SettingsPreferences'
import { DisplayPreferences } from '../features/DisplayPreferences'
import { libraryScroll, useViewState } from '../viewState'
import dragon from '../assets/dragon.svg'
import { avalonFilter, coverGrid, coverWallExtent, dormancy, matchesBucket } from './avalon-data'
import { AVALON_PALETTES, avalonPaletteStyle } from './avalon-palettes'
import { avalonFacts, matchesAvalonRules, type AvalonFactMap, type AvalonWorkspace } from './avalon-filters'
import { AvalonFilterPanel } from './avalon-filter-panel'
import { AvalonAction } from './avalon-actions'
import { AvalonBrowseSpine } from './avalon-browse-spine'
import { AvalonCollectionLists } from './avalon-collection-lists'
import { AvalonRailFooter } from './avalon-rail-footer'
import { ownershipStores, ownershipDescription } from './avalon-store-marks'
import {
  LibraryColumnHeaders,
  LibraryCutBar,
  libraryBucketLabel,
  libraryBucketDescription,
  LIBRARY_COLLECTIONS,
  libraryCutChips,
  libraryIdle,
  libraryPlaytime,
  reflectedDensity,
  MINIMUM_TILE_WIDTH,
  MAXIMUM_TILE_WIDTH,
} from './avalon-library-chrome'
import { useAvalonPreview } from './avalon-preview'
import { AvalonCoverWorkspace, AvalonDesktopCover, type AvalonCoverProps } from './avalon-desktop-cover'
import './avalon.css'
import { useAvalonAppearance } from './avalon-appearance'
import { FeedFeedback, FeedHistory, FeedLaunch, FeedReason, useAvalonFeed } from './avalon-feed'
import { AvalonFeedCard } from './avalon-feed-card'
import type { FeedDeckShelf, FeedRow } from './avalon-feed-model'
import {
  AvalonDesktopShelf,
  AvalonHomeRow,
  AvalonRowViewport,
  useHomeRowGeometry,
} from './avalon-row-viewport'
import { revealShelfCover } from './avalon-row-motion'
import { homePageStart, homeShelfPosition, initialGridPosition } from './avalon-navigation'
import { AvalonFullscreenGrid, type AvalonGridHandle, type AvalonSavedGrid } from './avalon-fullscreen-grid'
import { FullscreenStatus } from '../components/FullscreenStatus'
import { RootBumper, SectionLabel, SectionNavigation } from '../components/SectionNavigation'
import { AvalonShelfIndicator } from './avalon-shelf-indicator'
import { useSystemReducedMotion } from '../useSystemReducedMotion'
import { Details } from '../features/Details'
import { AvalonSearch } from './avalon-search'
import { AvalonBackdrop } from './avalon-backdrop'
import { AvalonAmbientBackdrop } from './avalon-ambient-backdrop'
import { AvalonLibraryPanel } from './avalon-library-panel'
import { UpdateCaption } from '../features/Updates'
import { LibraryHideConfirmation } from '../features/LibraryHideConfirmation'
import { unreadLabel } from './avalon-unread'

const destinations = [
  { id: 'discover', label: 'For you', Icon: Compass },
  { id: 'library', label: 'Library', Icon: Grid2X2 },
  { id: 'journal', label: 'Activity', Icon: BookOpen },
  { id: 'settings', label: 'Settings', Icon: Settings2 },
] as const
const desktopDestinations = [
  destinations[0],
  { id: 'merges', label: 'Merges', Icon: GitMerge },
  { id: 'stats', label: 'STATS', Icon: ChartNoAxesCombined },
  ...destinations.slice(1).filter((item) => item.id !== 'settings'),
] as const
const stateKey = (context: ThemeContext) => `avalon:library:${context.mode}`
const librarySortOptions = [
  { value: 'dormant', label: 'Dormant longest' },
  { value: 'title', label: 'Name A–Z' },
  { value: 'title-desc', label: 'Name Z–A' },
  { value: 'recent', label: 'Last played' },
  { value: 'time', label: 'Playtime high to low' },
  { value: 'time-low', label: 'Playtime low to high' },
] as const
const FactsContext = createContext<AvalonFactMap>(new Map())

function Collections({ context, fullscreenTools }: { context: ThemeContext; fullscreenTools?: ReactNode }) {
  const library = useLibrary()
  const [, setTools] = useViewState(`${stateKey(context)}:tools`, false)
  const { bucket, listId, list, filter, selectBucket, selectList } = useAvalonLists(
    context.mode,
    library.data?.lists ?? [],
    Boolean(library.data),
  )
  const activeBucket =
    context.mode === 'fullscreen' && bucket === 'all' && filter.installed === true ? 'installed' : bucket
  const buckets = [
    ...new Set([
      'all',
      'installed',
      'never_played',
      'stale_but_patched',
      ...(context.mode === 'desktop'
        ? [
            ...LIBRARY_COLLECTIONS.map((collection) => collection.key),
            ...context.games.map((game) => game.bucket),
          ]
        : []),
    ]),
  ]
  return (
    <div className="avalon-collections">
      <span className="avalon-label">Collections</span>
      <SectionNavigation fullscreen={context.mode === 'fullscreen'}>
        <div className="avalon-buckets" role="group" aria-label="Library collections">
          {buckets.map((id) => (
            <button
              key={id}
              title={libraryBucketDescription(id)}
              aria-description={libraryBucketDescription(id)}
              aria-label={
                id === 'stale_but_patched'
                  ? `Patched, ${context.games.filter((game) => matchesBucket(game, id)).length.toLocaleString()} games with unread updates`
                  : undefined
              }
              data-controller-tab={context.mode === 'fullscreen' || undefined}
              aria-pressed={context.page === 'library' && activeBucket === id && listId === 'all'}
              data-filter-rule={
                (context.page === 'library' &&
                  list?.isLive &&
                  (id === 'installed' ? filter.installed === true : filter.buckets?.includes(id))) ||
                undefined
              }
              onClick={() => {
                selectBucket(id)
                setTools(false)
                context.setPage('library')
              }}
            >
              <span>
                {id === 'stale_but_patched' && <i className="avalon-patch-pip" />}
                {context.mode === 'fullscreen' ? (
                  <SectionLabel>
                    {id === 'all'
                      ? 'All games'
                      : id === 'installed'
                        ? 'Installed'
                        : id === 'stale_but_patched'
                          ? 'Patched'
                          : libraryBucketLabel(id)}
                  </SectionLabel>
                ) : id === 'all' ? (
                  'All games'
                ) : id === 'installed' ? (
                  'Installed'
                ) : id === 'stale_but_patched' ? (
                  'Patched'
                ) : (
                  libraryBucketLabel(id)
                )}
              </span>
              <small>{context.games.filter((game) => matchesBucket(game, id)).length.toLocaleString()}</small>
            </button>
          ))}
        </div>
      </SectionNavigation>
      {fullscreenTools ?? (
        <>
          {context.mode === 'desktop' ? (
            <AvalonCollectionLists
              lists={library.data?.lists ?? []}
              games={context.games}
              selected={context.page === 'library' ? listId : null}
              select={(id) => {
                selectList(id)
                setTools(false)
                context.setPage('library')
              }}
            />
          ) : (
            <label className="avalon-list-picker">
              My lists
              <select
                value={context.page === 'library' ? listId : 'all'}
                onChange={(event) => {
                  selectList(event.target.value)
                  setTools(false)
                  context.setPage('library')
                }}
              >
                <option value="all">All games</option>
                {orderedLists(library.data?.lists ?? []).map((list) => (
                  <option value={list.id} key={list.id}>
                    {list.name}
                    {list.isLive ? ' · Live' : ''}
                  </option>
                ))}
              </select>
            </label>
          )}
          {context.mode === 'fullscreen' && context.page === 'library' && listId !== 'all' && (
            <button onClick={() => selectList('all')}>Close list</button>
          )}
          {!library.data?.lists.length && (
            <p className="muted">No lists yet. Choose New list below to create a static or live list.</p>
          )}
          {context.mode === 'fullscreen' && <CreateListButton mode={context.mode} />}
        </>
      )}
    </div>
  )
}

export function AvalonShell(context: ThemeContext) {
  const fullscreen = context.mode === 'fullscreen'
  const [activityPanel, setActivityPanel] = useViewState('desktop:journal:panel', 'history')
  const [statisticsSection, setStatisticsSection] = useViewState('desktop:stats:section', 'gameplay')
  const [librarySelected] = useViewState<number | null>('avalon:library:fullscreen:selected', null)
  const [libraryToolsOpen] = useViewState('avalon:library:fullscreen:tools', false)
  const [libraryFiltersOpen] = useViewState('avalon:library:fullscreen:filters-open', false)
  const projected = useLibraryProjection(context.games)
  const detailsModal = !fullscreen && context.page === 'details'
  const background = useRef<ReactNode>(null)
  const content = useRef<HTMLElement>(null)
  const detailsScroll = useRef<{ element: HTMLElement; top: number } | null>(null)
  useLayoutEffect(() => {
    if (detailsModal) {
      const element = content.current?.querySelector<HTMLElement>('.avalon-library-scroll')
      if (element && !detailsScroll.current) detailsScroll.current = { element, top: element.scrollTop }
      return
    }
    const saved = detailsScroll.current
    detailsScroll.current = null
    if (!saved?.element.isConnected) return
    // The retained Library may receive layout or focus changes under Details.
    // Restore its opening position after the modal's focus restoration as well.
    saved.element.scrollTop = saved.top
    const frame = requestAnimationFrame(() => {
      if (saved.element.isConnected) saved.element.scrollTop = saved.top
    })
    return () => cancelAnimationFrame(frame)
  }, [detailsModal])
  if (!detailsModal) background.current = context.children
  const shellPage = detailsModal ? (context.previousPage ?? 'library') : context.page
  const appearance = useAvalonAppearance(context.profile, fullscreen, context.profileHydrated)
  const workspace = useWorkspace()
  const facts = useMemo(
    () => avalonFacts(context.games, workspace.data as AvalonWorkspace | undefined),
    [context.games, workspace.data],
  )
  return (
    <div
      className={`avalon-shell ${context.mode}`}
      style={{
        ...avalonPaletteStyle(avalonPaletteId(context.profile)),
        ...appearance.style,
      }}
      data-pane-layout={appearance.appearance.layout}
      data-reduced-motion={context.profile.appearance.reducedMotion || undefined}
    >
      {fullscreen && (shellPage === 'journal' || shellPage === 'settings') && (
        <AvalonAmbientBackdrop page={shellPage} />
      )}
      {fullscreen &&
        shellPage === 'library' &&
        librarySelected !== null &&
        !libraryToolsOpen &&
        !libraryFiltersOpen && (
          <div className="avalon-library-backdrop" aria-hidden="true">
            <AvalonBackdrop
              workId={librarySelected}
              reducedMotion={context.profile.appearance.reducedMotion}
            />
          </div>
        )}
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
            <RootBumper button="LB" />
            {destinations.map(({ id, label }) => (
              <button
                key={id}
                aria-current={shellPage === id ? 'page' : undefined}
                onClick={() => context.setPage(id)}
              >
                {label}
              </button>
            ))}
            <RootBumper button="RB" />
          </nav>
        )}
        <div className="avalon-utilities">
          {fullscreen && <FullscreenStatus />}
          <div className="avalon-utility-actions">
            <UpdateCaption mode={context.mode} />
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
        </div>
      </header>
      {!fullscreen && (
        <aside className="avalon-rail">
          <div className="avalon-rail-scroll">
            <div className="avalon-library-total">
              <strong>{projected.games.length.toLocaleString()}</strong>
              <span>games in your library</span>
            </div>
            <nav className="avalon-navigation" aria-label="Main navigation">
              {desktopDestinations.map(({ id, label, Icon }) => (
                <button
                  key={id}
                  aria-current={
                    id === 'stats'
                      ? shellPage === 'journal' &&
                        activityPanel === 'summary' &&
                        statisticsSection === 'spending'
                        ? 'page'
                        : undefined
                      : shellPage === id &&
                          !(
                            id === 'journal' &&
                            activityPanel === 'summary' &&
                            statisticsSection === 'spending'
                          )
                        ? 'page'
                        : undefined
                  }
                  title={
                    id === 'merges'
                      ? 'Entries that might be one game, and what you have rolled up'
                      : undefined
                  }
                  onClick={() => {
                    if (id === 'stats') {
                      setActivityPanel('summary')
                      setStatisticsSection('spending')
                      context.setPage('journal')
                    } else {
                      if (id === 'journal') setActivityPanel('history')
                      context.setPage(id)
                    }
                  }}
                >
                  <Icon size={17} />
                  {label}
                </button>
              ))}
            </nav>
            <Collections context={{ ...context, page: shellPage, games: projected.games }} />
            <p className="avalon-rail-note">Your library has unread mail.</p>
          </div>
          <AvalonRailFooter context={{ ...context, page: shellPage, games: projected.games }} />
        </aside>
      )}
      <main
        ref={content}
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
        <AvalonCoverWorkspace.Provider value={workspace.data}>
          <FactsContext.Provider value={facts}>
            {detailsModal ? background.current : context.children}
            {detailsModal && context.children}
          </FactsContext.Provider>
        </AvalonCoverWorkspace.Provider>
      </main>
      <footer className="avalon-footer">
        <span>
          {fullscreen
            ? `Arrows to browse · Enter to view${shellPage === 'library' ? ' · Y · Library options' : ''} · Esc to go back`
            : 'Your library. Rediscovered.'}
        </span>
        <span>
          {fullscreen
            ? shellPage === 'discover'
              ? 'LT / RT  Shelf'
              : 'F11 · Back to desktop'
            : 'Ctrl+K · Search'}
        </span>
      </footer>
    </div>
  )
}

export function AvalonCover(props: AvalonCoverProps) {
  const workspace = useContext(AvalonCoverWorkspace)
  const facts =
    useContext(FactsContext).get(props.game.workId) ??
    avalonFacts([props.game], workspace as AvalonWorkspace | undefined).get(props.game.workId)
  const { game, context } = props
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
  const unreadCount = facts?.unreadCount ?? 0
  if (context.mode === 'fullscreen')
    return <AvalonFullscreenCover {...props} patched={patched} unreadCount={unreadCount} />
  // A reused grid slot must release the outgoing tile's press, focus and preview.
  return (
    <AvalonDesktopCover
      key={game.workId}
      {...props}
      patched={patched}
      unreadCount={unreadCount}
      style={style}
      workspace={workspace}
    />
  )
}

function AvalonFullscreenCover({
  context,
  game,
  reason,
  selected,
  onFocus,
  onKeyDown,
  onClick,
  onContextMenu,
  expansion,
  patched,
  unreadCount,
}: AvalonCoverProps & { patched: boolean; unreadCount: number }) {
  const hover = useAvalonPreview(context, game, reason)
  const { Artwork } = context.components
  const dim = themeSettingValues(avalon, context.profile).dimCovers
  const { saturation, brightness, hue } = dormancy(game.lastPlayedAt)
  const style = {
    '--avalon-dormancy': dim
      ? `saturate(${saturation}) hue-rotate(${hue}deg) brightness(${brightness})`
      : 'none',
  } as CSSProperties
  const stores = ownershipStores(game)
  return (
    <>
      <button
        className="avalon-cover"
        data-avalon-game={game.workId}
        data-work-id={game.workId}
        data-selected={selected || undefined}
        style={style}
        aria-label={`View ${game.title}${unreadLabel(patched, unreadCount)}${ownershipDescription(game)}${expansion ? `. ${expansion.text}` : ''}`}
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
        <Artwork
          workId={game.workId}
          className={context.mode === 'fullscreen' ? 'artwork-edge-padding' : undefined}
        />
        <span className="avalon-cover-fallback" aria-hidden="true">
          {game.title}
        </span>
        {patched && <span className="avalon-unread" title="Patched since you played" />}
        {stores.length > 1 && (
          <span
            className="avalon-store-initials"
            aria-hidden="true"
            title={stores.map((store) => store.label).join(', ')}
          >
            {stores.map((store) => (
              <span key={store.key}>{store.initial}</span>
            ))}
          </span>
        )}
        {expansion && (
          <span className="avalon-expansion-mark" aria-hidden="true" title={expansion.text}>
            +{expansion.count}
          </span>
        )}
        <span className="avalon-cover-caption" aria-hidden="true">
          <strong>{game.title}</strong>
          <span>
            {reason ? <FeedReason reason={reason} /> : `${libraryPlaytime(game.playtimeMinutes)} played`}
          </span>
          <span className="avalon-store-chips">
            {stores.map((store) => (
              <span key={store.key} title={store.label}>
                {store.badge}
              </span>
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
    const cover = !fullscreen ? (
      <AvalonFeedCard
        context={context}
        deck={deck}
        shelf={current}
        row={entry}
        onKeyDown={(event) => key(event, index, current)}
      />
    ) : (
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
              <div className="avalon-shelf-label" title={current.blurb}>
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
        <AvalonBackdrop
          workId={picked.game.workId}
          reducedMotion={context.profile.appearance.reducedMotion}
        />
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
        <AvalonShelfIndicator
          titles={shelves.map((entry) => entry.title)}
          selected={shelfIndex}
          onSelect={changeShelf}
        />
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
  const collection =
    listId !== 'all' ? `list:${listId}` : bucket === 'all' && rules.installed === true ? 'installed' : bucket
  const [, saveGrid] = useViewState<AvalonSavedGrid>(`${prefix}:rows`, initialGridPosition())
  const gridControls = useRef<AvalonGridHandle>(null)
  const previousCollection = useRef(collection)
  useEffect(() => {
    const changed = previousCollection.current !== collection
    previousCollection.current = collection
    if (!fullscreen || !changed) return
    const frame = requestAnimationFrame(() => gridControls.current?.focusSelected())
    return () => cancelAnimationFrame(frame)
  }, [fullscreen, collection])
  const [selection, setSelection] = useViewState<number[]>(`${prefix}:selection`, [])
  const [panel, setPanel] = useState<'options' | 'lists' | null>(null)
  const { filtersOpen, setFiltersOpen } = listState
  function restoreBrowseFocus() {
    requestAnimationFrame(() => {
      if (document.querySelector('[role="dialog"], [role="alertdialog"]')) return
      if (gridControls.current) gridControls.current.focusSelected()
      else
        document
          .querySelector<HTMLButtonElement>('.avalon-buckets [aria-pressed="true"], .avalon-buckets button')
          ?.focus()
    })
  }
  const [selectionError, setSelectionError] = useState(''),
    [selectionBusy, setSelectionBusy] = useState(false)
  const [hideTargets, setHideTargets] = useState<number[] | null>(null)
  const hideOrigin = useRef<HTMLElement | null>(null)
  function closeHide(completed: boolean) {
    setHideTargets(null)
    if (fullscreen) {
      if (completed) restoreBrowseFocus()
      else setPanel('options')
    } else
      requestAnimationFrame(() => {
        if (!completed) {
          if (hideOrigin.current?.isConnected) hideOrigin.current.focus({ preventScroll: true })
          return
        }
        const viewport = scroll.current?.getBoundingClientRect()
        const remaining = [
          ...(scroll.current?.querySelectorAll<HTMLButtonElement>('[data-avalon-game]') ?? []),
        ].find((button) => {
          const bounds = button.getBoundingClientRect()
          return (
            !hideTargets?.includes(Number(button.dataset.avalonGame)) &&
            viewport &&
            bounds.bottom > viewport.top &&
            bounds.top < viewport.bottom &&
            bounds.width > 0
          )
        })
        ;(
          remaining ??
          document.querySelector<HTMLElement>('[data-library-search]') ??
          manageButton.current
        )?.focus({ preventScroll: true })
      })
  }
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
      saveGrid((previous) => ({
        ...initialGridPosition(),
        collections: { ...previous.collections, [collection]: initialGridPosition() },
      }))
    }
  }, [fullscreen, games.length, prefix, collection])
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
    if (fullscreen) {
      setPanel('options')
      return
    }
    requestAnimationFrame(() =>
      document.querySelector<HTMLElement>('.avalon-selection-actions button')?.focus(),
    )
  }
  async function selectionAction(kind: 'read' | 'derelict') {
    if (selectionBusy) return
    const selectedGames = games.filter((game) =>
      selection.length ? selection.includes(game.workId) : game.workId === selected,
    )
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
  const toolbar = (
    <div className="avalon-toolbar">
      {!fullscreen && (
        <button aria-expanded={filtersOpen} onClick={() => setFiltersOpen(!filtersOpen)}>
          Filters
        </button>
      )}
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
      {([...new Set(libraryGames.flatMap((game) => game.entries.map((entry) => entry.store)))].length > 1 ||
        store !== 'all') && (
        <label>
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
        </label>
      )}
      {fullscreen ? (
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
      ) : (
        <SortMenu
          value={sort}
          options={
            listState.list && !listState.list.isLive
              ? [...librarySortOptions, { value: 'list-order', label: 'List order' }]
              : librarySortOptions
          }
          onChange={setSort}
        />
      )}
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
          <DisplayPreferences />
        </>
      )}
    </div>
  )
  const browseActions = (
    <>
      <LibraryCutBar
        state={listState}
        games={libraryGames}
        visible={games.length}
        facts={facts}
        workspace={workspace.data as AvalonWorkspace | undefined}
      >
        <LiveListActions
          state={listState}
          mode={context.mode}
          onClosed={fullscreen ? () => setPanel(null) : undefined}
          compact
          nameSuggestion={libraryCutChips(
            listState,
            libraryGames,
            facts,
            workspace.data as AvalonWorkspace | undefined,
          )
            .filter((chip) => chip.origin !== 'context')
            .slice(0, 2)
            .map((chip) => chip.label)
            .join(' · ')
            .slice(0, 200)}
        />
      </LibraryCutBar>
      {listState.list &&
        !listState.list.isLive &&
        games.some((game) =>
          selection.length ? selection.includes(game.workId) : game.workId === selected,
        ) && (
          <ListOrderActions
            key={listState.list.id}
            list={listState.list}
            onCommitted={fullscreen ? () => setPanel(null) : undefined}
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
              selection.length > 0 || (selected != null && games.some((game) => game.workId === selected))
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
            onClosed={fullscreen ? () => setPanel(null) : undefined}
          />
          {games.some(
            (game) =>
              (selection.length ? selection.includes(game.workId) : game.workId === selected) &&
              facts.get(game.workId)?.unread,
          ) && (
            <button disabled={selectionBusy} onClick={() => void selectionAction('read')}>
              Mark as read
            </button>
          )}
          {bucket === 'derelict' &&
            games.some(
              (game) =>
                (selection.length ? selection.includes(game.workId) : game.workId === selected) &&
                game.bucket === 'derelict',
            ) && (
              <button disabled={selectionBusy} onClick={() => void selectionAction('derelict')}>
                Remove from Derelict
              </button>
            )}
          <button
            disabled={selectionBusy}
            onClick={(event) => {
              const targets = games
                .filter((game) =>
                  selection.length ? selection.includes(game.workId) : game.workId === selected,
                )
                .map((game) => game.workId)
              if (!targets.length) return
              hideOrigin.current = event.currentTarget
              setHideTargets(targets)
              if (fullscreen) setPanel(null)
            }}
          >
            {selection.length > 1 ? `Hide ${selection.length} games…` : 'Hide game…'}
          </button>
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
    </>
  )
  const { Artwork } = context.components
  return (
    <AvalonCoverWorkspace.Provider value={workspace.data}>
      <FactsContext.Provider value={facts}>
        <div className="avalon-library" data-list-id={listId} data-filters-open={filtersOpen || undefined}>
          {hideTargets && (
            <LibraryHideConfirmation
              mode={context.mode}
              workIds={hideTargets}
              close={() => closeHide(false)}
              hidden={() => closeHide(true)}
            />
          )}
          <header className="avalon-page-heading">
            <h1>
              {fullscreen && !tools
                ? (listState.list?.name ??
                  (collection === 'installed'
                    ? 'Installed games'
                    : bucket === 'all'
                      ? 'Your library'
                      : libraryBucketLabel(bucket)))
                : 'Library'}
            </h1>
            {fullscreen && !tools ? (
              <div className="avalon-fullscreen-library-summary">
                <span className="avalon-results-count" aria-live="polite">
                  {games.length.toLocaleString()} {games.length === 1 ? 'game' : 'games'}
                </span>
                <span>
                  ·{' '}
                  {
                    (
                      {
                        dormant: 'Dormant longest',
                        title: 'Name A–Z',
                        'title-desc': 'Name Z–A',
                        recent: 'Last played',
                        time: 'Playtime high to low',
                        'time-low': 'Playtime low to high',
                        'list-order': 'List order',
                      } as Record<string, string>
                    )[sort]
                  }
                </span>
              </div>
            ) : (
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
            )}
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
              {fullscreen && (
                <Collections
                  context={{ ...context, games: libraryGames }}
                  fullscreenTools={
                    <div className="avalon-fullscreen-library-actions">
                      <button aria-pressed={listId !== 'all'} onClick={() => setPanel('lists')}>
                        My lists
                      </button>
                      <button aria-expanded={filtersOpen} onClick={() => setFiltersOpen(true)}>
                        Filter &amp; sort
                      </button>
                      <button
                        data-controller-context
                        aria-expanded={panel === 'options'}
                        onClick={() => setPanel('options')}
                      >
                        More
                      </button>
                    </div>
                  }
                />
              )}
              {!fullscreen && toolbar}
              <div className="avalon-library-browse">
                {!fullscreen && browseActions}
                {!fullscreen && (
                  <p className="avalon-results-count" aria-live="polite">
                    {games.length.toLocaleString()} {games.length === 1 ? 'game' : 'games'}
                    {query && ` matching “${query}”`}
                  </p>
                )}
                {listMode && games.length > 0 && !context.loading && (
                  <LibraryColumnHeaders sort={sort} change={setSort} width={size.width} />
                )}
                <div
                  className="avalon-library-results"
                  inert={fullscreen && (filtersOpen || panel !== null) ? true : undefined}
                >
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
                          {libraryGames.length
                            ? 'No games match these filters.'
                            : 'Your library starts here.'}
                        </h2>
                        <p>
                          {libraryGames.length
                            ? query.trim()
                              ? `No titles match “${query.trim()}”.`
                              : 'No titles match these filters. Drop one to widen the cut.'
                            : 'Connect a store in Settings, or add a game with Manage library.'}
                        </p>
                        {!libraryGames.length && (
                          <button onClick={() => context.setPage('settings')}>Open settings</button>
                        )}
                      </div>
                    ) : fullscreen ? (
                      <AvalonFullscreenGrid
                        games={games}
                        columns={columns}
                        gap={grid.gap}
                        prefix={prefix}
                        collection={collection}
                        controls={gridControls}
                        selected={selected}
                        onSelected={setSelected}
                        reducedMotion={context.profile.appearance.reducedMotion || systemReducedMotion}
                        onKeyDown={key}
                        onTopBoundary={() =>
                          document
                            .querySelector<HTMLButtonElement>(
                              '.avalon-buckets [aria-pressed="true"], .avalon-buckets button',
                            )
                            ?.focus()
                        }
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
                      <div
                        className="avalon-wall"
                        style={{
                          height: listMode
                            ? virtual.getTotalSize()
                            : coverWallExtent(games.length, columns, grid.coverHeight, grid.gap),
                          position: 'relative',
                        }}
                      >
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
                              gridTemplateColumns: listMode
                                ? '1fr'
                                : `repeat(${columns}, ${grid.coverWidth}px)`,
                              gridAutoRows: listMode ? undefined : grid.coverHeight,
                              gap: grid.gap,
                              paddingBottom:
                                listMode || (row.index + 1) * columns >= games.length ? 0 : grid.gap,
                            }}
                          >
                            {games.slice(row.index * columns, (row.index + 1) * columns).map((game, index) =>
                              listMode ? (
                                <button
                                  className="avalon-record"
                                  key={game.workId}
                                  data-avalon-game={game.workId}
                                  data-work-id={game.workId}
                                  aria-label={`View ${game.title}${unreadLabel(facts.get(game.workId)?.unread ?? false, facts.get(game.workId)?.unreadCount ?? 0)}${ownershipDescription(game)}${projected.marks.has(game.workId) ? `. ${projected.marks.get(game.workId)!.text}` : ''}`}
                                  aria-pressed={
                                    selection.length
                                      ? selection.includes(game.workId)
                                      : selected === game.workId
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
                                  <span className="avalon-record-stores">
                                    {ownershipStores(game).map((store) => (
                                      <span className="avalon-store-chip" key={store.key}>
                                        {store.label}
                                      </span>
                                    ))}
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
                                    selection.length
                                      ? selection.includes(game.workId)
                                      : selected === game.workId
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
                {!fullscreen && !context.loading && (
                  <AvalonBrowseSpine
                    games={games}
                    sort={sort}
                    scroll={scroll}
                    jump={(index) => virtual.scrollToIndex(Math.floor(index / columns), { align: 'start' })}
                  />
                )}
              </div>
              {fullscreen && (
                <AvalonLibraryPanel
                  kind={panel}
                  close={() => setPanel(null)}
                  restoreFocus={restoreBrowseFocus}
                >
                  {panel === 'lists' ? (
                    <>
                      {orderedLists(library.data?.lists ?? []).map((list) => {
                        const count = libraryGames.filter((game) =>
                          list.isLive
                            ? matchesAvalonRules(game, list.filter ?? {}, facts.get(game.workId))
                            : game.entries.some((entry) => list.releaseIds.includes(entry.releaseId)),
                        ).length
                        return (
                          <button
                            key={list.id}
                            data-avalon-list={list.id}
                            aria-pressed={listId === String(list.id)}
                            onClick={() => {
                              if (listId !== String(list.id)) listState.selectList(String(list.id))
                              setPanel(null)
                            }}
                          >
                            {list.name} · {count.toLocaleString()} games{list.isLive ? ' · Live list' : ''}
                          </button>
                        )
                      })}
                      {!library.data?.lists.length && (
                        <p>No lists yet. Create a list to keep games together.</p>
                      )}
                      <CreateListButton mode={context.mode} onClosed={() => setPanel(null)} />
                    </>
                  ) : (
                    <>
                      <AvalonAction label="My lists" icon={List} onChoose={() => setPanel('lists')} />
                      <AvalonAction
                        label="Filter & sort"
                        icon={Settings2}
                        onChoose={() => {
                          setPanel(null)
                          setFiltersOpen(true)
                        }}
                      />
                      <AvalonAction
                        label="Search"
                        icon={Search}
                        onChoose={() => {
                          setPanel(null)
                          if (context.openSearch) context.openSearch()
                          else context.setPage('search')
                        }}
                      />
                      <CreateListButton mode={context.mode} onClosed={() => setPanel(null)} />
                      <AvalonAction
                        label="Manage library"
                        icon={Settings2}
                        onChoose={() => {
                          toolsReturn.current = { workId: selected, offset: scroll.current?.scrollTop ?? 0 }
                          setPanel(null)
                          setTools(true)
                        }}
                      />
                      {listState.list && (
                        <button
                          onClick={() => {
                            listState.selectList('all')
                            setPanel(null)
                          }}
                        >
                          Close list
                        </button>
                      )}
                      {listState.list && (
                        <ListEditor
                          key={listState.list.id}
                          list={listState.list}
                          actionsOnly
                          onClosed={() => setPanel(null)}
                        />
                      )}
                      {listState.list && !listState.list.isLive && sort !== 'list-order' && (
                        <button
                          onClick={() => {
                            setSort('list-order')
                            setPanel(null)
                          }}
                        >
                          Show list order
                        </button>
                      )}
                      {browseActions}
                      <details>
                        <summary>Current search and sort</summary>
                        {toolbar}
                      </details>
                    </>
                  )}
                </AvalonLibraryPanel>
              )}
              {filtersOpen && (
                <AvalonFilterPanel
                  editText={context.editText}
                  filter={{
                    ...rules,
                    ...(store === 'all' ? {} : { stores: [store] }),
                    ...(fullscreen && bucket === 'installed' ? { installed: true } : {}),
                  }}
                  browse={
                    fullscreen
                      ? {
                          sort,
                          bucket: bucket === 'installed' ? 'all' : bucket,
                          manual: Boolean(listState.list && !listState.list.isLive),
                        }
                      : undefined
                  }
                  allGames={libraryGames}
                  optionOrder={filterOrder}
                  games={avalonFilter(libraryGames, library.data?.lists ?? [], {
                    query,
                    bucket: fullscreen ? 'all' : bucket,
                    store: 'all',
                    listId: listState.list?.isLive ? 'all' : listId,
                    sort,
                  })}
                  facts={facts}
                  workspace={workspace.data as AvalonWorkspace | undefined}
                  fullscreen={fullscreen}
                  apply={(next, browse) => {
                    setStore('all')
                    if (browse) {
                      setSort(browse.sort)
                      listState.setBucket(browse.bucket)
                      setRules(next)
                      return
                    }
                    setRules({
                      ...next,
                      ...(rules.buckets ? { buckets: rules.buckets } : {}),
                      ...(rules.hasUnread != null ? { hasUnread: rules.hasUnread } : {}),
                    })
                  }}
                  close={() => {
                    setFiltersOpen(false)
                    if (fullscreen) restoreBrowseFocus()
                  }}
                />
              )}
            </>
          )}
        </div>
      </FactsContext.Provider>
    </AvalonCoverWorkspace.Provider>
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
        editText={context.editText}
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
      default: 'winnow',
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
