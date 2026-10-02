import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import { ListFilter, Search, Settings2, X } from 'lucide-react'
import type { ThemeContext } from '../../../shared/theme'
import type { LibraryGame } from '../../api/types'
import { useLibrary } from '../../api/hooks'
import { bucketLabel, Empty } from '../../components/primitives'
import { LibraryTools } from '../../features/LibraryTools'
import { libraryScroll, useViewState } from '../../viewState'
import { filterGames } from './data'
import { RiftCover } from './Cover'
import { CoverPreview, type PreviewTarget } from './Preview'
import { useJourney } from './journey'

export function RiftLibrary(context: ThemeContext) {
  const journey = useJourney(),
    library = useLibrary()
  const prefix = `rift:library:${context.mode}`
  const [query, setQuery] = useViewState(`${prefix}:query`, '')
  const [bucket, setBucket] = useViewState(`${prefix}:bucket`, 'all')
  const [store, setStore] = useViewState(`${prefix}:store`, 'all')
  const [listId, setListId] = useViewState(`${prefix}:list`, 'all')
  const [sort, setSort] = useViewState(`${prefix}:sort`, 'title')
  const [tools, setTools] = useState(false),
    [filtersOpen, setFiltersOpen] = useState(false)
  const [preview, setPreview] = useState<PreviewTarget | null>(null)
  const currentPreview = useRef(preview)
  currentPreview.current = preview
  const timers = useRef<{ show?: ReturnType<typeof setTimeout> }>({})
  const dismissed = useRef<HTMLButtonElement | null>(null)
  const scroll = useRef<HTMLDivElement>(null),
    [size, setSize] = useState({ width: 1000, height: 700 })
  const scrollKey = `rift:${context.mode}`
  const games = useMemo(
    () => filterGames(context.games, library.data?.lists ?? [], { query, bucket, store, listId, sort }),
    [context.games, library.data?.lists, query, bucket, store, listId, sort],
  )
  const gap = 20
  const minimum =
    journey.coverSize === 'compact'
      ? 95
      : journey.coverSize === 'large'
        ? 195
        : context.mode === 'fullscreen'
          ? 130
          : size.width >= 1500
            ? 155
            : 110
  const columns = Math.max(2, Math.floor((size.width + gap) / (minimum + gap)))
  const rowHeight = ((size.width - (columns - 1) * gap) / columns) * 1.5 + gap
  const virtual = useVirtualizer({
    count: Math.ceil(games.length / columns),
    getScrollElement: () => scroll.current,
    initialOffset: () => libraryScroll.get(scrollKey) ?? 0,
    estimateSize: () => rowHeight,
    overscan: 2,
    onChange: (instance) => {
      if (instance.isScrolling) libraryScroll.set(scrollKey, instance.scrollOffset ?? 0)
    },
  })
  const close = useCallback((restore = false) => {
    clearTimeout(timers.current.show)
    const target = currentPreview.current?.card
    if (restore && target) {
      dismissed.current = target
      target.focus({ preventScroll: true })
    }
    setPreview(null)
  }, [])
  function queue(
    game: LibraryGame,
    card: HTMLButtonElement,
    keyboard: boolean,
    pointer?: { x: number; y: number },
  ) {
    if (card === dismissed.current || currentPreview.current?.card === card) return
    close()
    if (keyboard) setPreview({ game, card, keyboard })
    else
      timers.current.show = setTimeout(() => {
        if (card.isConnected) setPreview({ game, card, keyboard, pointer })
      }, 140)
  }
  useEffect(() => {
    const element = scroll.current
    if (!element) return
    const observer = new ResizeObserver(([entry]) => {
      setSize({ width: entry.contentRect.width, height: entry.contentRect.height })
      close()
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [tools, close])
  useEffect(() => {
    virtual.measure()
  }, [columns, rowHeight])
  const priorFilters = useRef([query, bucket, store, listId, sort].join('\0'))
  useEffect(() => {
    const filters = [query, bucket, store, listId, sort].join('\0')
    if (priorFilters.current !== filters) {
      virtual.scrollToOffset(0)
      libraryScroll.set(scrollKey, 0)
      priorFilters.current = filters
      close()
    }
  }, [query, bucket, store, listId, sort, close])
  useEffect(
    () => () => {
      clearTimeout(timers.current.show)
    },
    [],
  )
  function key(event: KeyboardEvent<HTMLButtonElement>, index: number) {
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
    close()
    const game = games[Math.max(0, Math.min(games.length - 1, next))]
    virtual.scrollToIndex(Math.floor(Math.max(0, Math.min(games.length - 1, next)) / columns), {
      align: 'auto',
    })
    requestAnimationFrame(() =>
      requestAnimationFrame(() =>
        scroll.current
          ?.querySelector<HTMLButtonElement>(`[data-rift-game="${game.workId}"]`)
          ?.focus({ preventScroll: true }),
      ),
    )
  }
  return (
    <div className="rift-library">
      <header className="rift-library-heading">
        <div>
          <span className="eyebrow">YOUR COMPLETE COLLECTION</span>
          <h1>Every world, within reach.</h1>
        </div>
        <button
          onClick={() => {
            close()
            setTools(!tools)
          }}
        >
          <Settings2 size={16} />
          {tools ? 'Close tools' : 'Manage library'}
        </button>
      </header>
      {tools ? (
        <div className="rift-manage">
          <LibraryTools mode={context.mode} onOpenGame={(workId) => journey.open(workId)} />
        </div>
      ) : (
        <>
          <div className="rift-library-toolbar">
            <label className="rift-search">
              <Search size={17} />
              <input
                data-library-search
                aria-label="Search games"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Find a world…"
              />
              {query && (
                <button aria-label="Clear search" onClick={() => setQuery('')}>
                  <X size={15} />
                </button>
              )}
            </label>
            <label>
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
            <label>
              Sort
              <select value={sort} onChange={(event) => setSort(event.target.value)}>
                <option value="title">Title</option>
                <option value="recent">Last played</option>
                <option value="time">Playtime</option>
              </select>
            </label>
            <button
              className="rift-filter-toggle"
              aria-expanded={filtersOpen}
              onClick={() => setFiltersOpen(!filtersOpen)}
            >
              <ListFilter size={16} />
              Filters
            </button>
          </div>
          <div className="rift-library-filters" data-open={filtersOpen || undefined}>
            <div className="rift-filter-lenses" role="group" aria-label="Library filters">
              {[
                { id: 'all', label: 'All games' },
                { id: 'installed', label: 'Installed' },
                ...[...new Set(context.games.map((game) => game.bucket))].map((id) => ({
                  id,
                  label: bucketLabel(id),
                })),
              ].map((item) => (
                <button key={item.id} aria-pressed={bucket === item.id} onClick={() => setBucket(item.id)}>
                  {item.label}
                </button>
              ))}
            </div>
            <label>
              Your lists
              <select value={listId} onChange={(event) => setListId(event.target.value)}>
                <option value="all">Every list</option>
                {library.data?.lists.map((list) => (
                  <option key={list.id} value={String(list.id)}>
                    {list.name}
                    {list.isLive ? ' · Live' : ''}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <div className="rift-gallery-heading">
            <span aria-live="polite">
              {games.length.toLocaleString()} {games.length === 1 ? 'WORLD' : 'WORLDS'}
              {query && ` MATCHING “${query}”`}
            </span>
            <span>Hover or focus to look inside</span>
          </div>
          <div
            ref={scroll}
            className="rift-gallery"
            onScroll={() => {
              if (scroll.current) libraryScroll.set(scrollKey, scroll.current.scrollTop)
              if (!currentPreview.current?.keyboard) close()
            }}
          >
            {context.loading ? (
              <div className="loading-state">
                <span className="loading-ring" />
                Opening your library…
              </div>
            ) : !context.games.length ? (
              <Empty title="Your next world belongs here.">
                <p>Connect a library in Settings, or add a game with Manage library.</p>
                <button onClick={() => context.setPage('settings')}>Open settings</button>
              </Empty>
            ) : !games.length ? (
              <Empty title="No worlds through this lens.">
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
                    data-index={row.index}
                    className="rift-gallery-row"
                    style={{
                      position: 'absolute',
                      top: 0,
                      left: 0,
                      width: '100%',
                      transform: `translateY(${row.start}px)`,
                      gridTemplateColumns: `repeat(${columns},minmax(0,1fr))`,
                      gap,
                    }}
                  >
                    {games.slice(row.index * columns, (row.index + 1) * columns).map((game, offset) => (
                      <RiftCover
                        key={game.workId}
                        game={game}
                        onClick={(event) => {
                          const source =
                            currentPreview.current?.game.workId === game.workId
                              ? document.querySelector<HTMLElement>('.rift-cover-preview')
                              : event.currentTarget
                          close()
                          journey.open(game.workId, source)
                        }}
                        onPointerEnter={(event) => {
                          if (event.pointerType !== 'touch')
                            queue(game, event.currentTarget, false, { x: event.clientX, y: event.clientY })
                        }}
                        onPointerLeave={(event) => {
                          if (dismissed.current === event.currentTarget) dismissed.current = null
                          if (
                            !currentPreview.current?.keyboard ||
                            document.activeElement !== event.currentTarget
                          )
                            close()
                        }}
                        onFocus={(event) => {
                          if (event.currentTarget.matches(':focus-visible'))
                            queue(game, event.currentTarget, true)
                        }}
                        onBlur={(event) => {
                          if (dismissed.current === event.currentTarget) dismissed.current = null
                          close()
                        }}
                        onKeyDown={(event) => key(event, row.index * columns + offset)}
                      />
                    ))}
                  </div>
                ))}
              </div>
            )}
          </div>
        </>
      )}
      {preview && <CoverPreview target={preview} fullscreen={context.mode === 'fullscreen'} close={close} />}
    </div>
  )
}
