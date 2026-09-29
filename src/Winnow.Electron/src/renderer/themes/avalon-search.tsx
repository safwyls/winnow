import { useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import type { ThemeContext } from '../../shared/theme'
import type { LibraryGame } from '../api/types'
import { useViewState } from '../viewState'
import { useSystemReducedMotion } from '../useSystemReducedMotion'
import { useLibraryProjection } from '../features/parity-library-projection'
import { AvalonCover } from './avalon'
import { coverGrid } from './avalon-data'
import { AvalonFullscreenGrid, type AvalonGridHandle } from './avalon-fullscreen-grid'
import { initialGridPosition, reconcileGrid } from './avalon-navigation'
import './avalon-search.css'

export const AVALON_SEARCH_STATE = 'avalon:search:fullscreen'

/** Search starts from the entire visible library, independently of the Library's filters and sort. */
export function searchGames(games: LibraryGame[], query: string): LibraryGame[] {
  const text = query.trim().toLocaleLowerCase()
  return games.filter((game) => game.title.toLocaleLowerCase().includes(text))
}

export function AvalonSearch(context: ThemeContext) {
  const projection = useLibraryProjection(context.games)
  const [query, setQuery] = useViewState(`${AVALON_SEARCH_STATE}:query`, '')
  const [selected, setSelected] = useViewState<number | null>(`${AVALON_SEARCH_STATE}:selected`, null)
  const [inResults, setInResults] = useViewState(`${AVALON_SEARCH_STATE}:in-results`, false)
  const [position, setPosition] = useViewState(`${AVALON_SEARCH_STATE}:rows`, initialGridPosition())
  const [size, setSize] = useState({ width: 950, height: 660 })
  const input = useRef<HTMLInputElement>(null),
    edit = useRef<HTMLButtonElement>(null),
    show = useRef<HTMLButtonElement>(null),
    results = useRef<HTMLDivElement>(null),
    controls = useRef<AvalonGridHandle>(null)
  const systemReducedMotion = useSystemReducedMotion()
  const games = useMemo(() => searchGames(projection.games, query), [projection.games, query])
  const layout = coverGrid(size.width, size.height, 148, true)
  const rows = Math.ceil(games.length / layout.columns)
  useLayoutEffect(() => {
    if (!games.length) {
      setSelected(null)
      setPosition(reconcileGrid(position, layout.columns, []))
    }
  }, [games.length, layout.columns])

  useLayoutEffect(() => {
    const element = results.current!
    const resize = () => setSize({ width: element.clientWidth, height: element.clientHeight })
    resize()
    const observer = new ResizeObserver(resize)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])
  useLayoutEffect(() => {
    const frame = requestAnimationFrame(() => {
      if (input.current?.closest('.avalon-search-page')?.contains(document.activeElement)) return
      if (inResults && games.length) controls.current?.focusSelected()
      else edit.current?.focus({ preventScroll: true })
    })
    return () => cancelAnimationFrame(frame)
  }, [])

  function enterSearch() {
    setInResults(false)
    if (input.current) {
      input.current.focus({ preventScroll: true })
      context.editText?.(input.current)
    }
  }
  function pageKey(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'PageDown' || event.key === 'PageUp') {
      event.preventDefault()
      event.stopPropagation()
      controls.current?.moveRows(event.key === 'PageDown' ? 2 : -2)
    }
  }
  function headerKey(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'ArrowDown' && games.length) {
      event.preventDefault()
      event.stopPropagation()
      controls.current?.focusSelected()
    }
  }
  return (
    <section className="avalon-search-page" data-controller-page onKeyDown={pageKey} aria-label="Search">
      <div className="avalon-search-heading">
        <h1>Search</h1>
        <button onClick={() => context.closeSearch?.()}>B · Back</button>
      </div>
      <div className="avalon-search-inputs" onKeyDown={headerKey} onFocus={() => setInResults(false)}>
        <input
          ref={input}
          type="search"
          aria-label="Search games"
          placeholder="Search games"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
        />
        <button
          ref={edit}
          data-controller-context
          data-initial-focus={!inResults || undefined}
          onClick={enterSearch}
        >
          Enter search
        </button>
        <button ref={show} disabled={!games.length} onClick={() => controls.current?.focusFirst()}>
          Go to results
        </button>
      </div>
      <p className="avalon-search-count" role="status">
        {games.length.toLocaleString()} games
      </p>
      <div className="avalon-search-results" ref={results}>
        {context.loading ? (
          <p role="status">Preparing your library…</p>
        ) : !games.length ? (
          <p className="avalon-search-empty">No games match. Try a different title.</p>
        ) : (
          <AvalonFullscreenGrid
            games={games}
            columns={layout.columns}
            gap={layout.gap}
            prefix={AVALON_SEARCH_STATE}
            selected={selected}
            onSelected={setSelected}
            reducedMotion={context.profile.appearance.reducedMotion || systemReducedMotion}
            controls={controls}
            onKeyDown={() => {}}
            onTopBoundary={() => show.current?.focus()}
          >
            {(game, _index, handlers) => (
              <AvalonCover
                key={game.workId}
                context={context}
                game={game}
                expansion={projection.marks.get(game.workId)}
                selected={game.workId === selected}
                onFocus={() => {
                  setInResults(true)
                  handlers.onFocus()
                }}
                onKeyDown={handlers.onKeyDown}
              />
            )}
          </AvalonFullscreenGrid>
        )}
      </div>
      <footer className="avalon-search-hints">
        <span>A · Choose &nbsp; B · Back &nbsp; Y · Enter search</span>
        {!!rows && (
          <span>
            LT / RT · Rows {position.firstRow + 1}–{Math.min(position.firstRow + 2, rows)} / {rows}
          </span>
        )}
      </footer>
    </section>
  )
}
