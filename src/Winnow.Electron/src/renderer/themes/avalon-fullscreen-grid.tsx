import {
  useEffect,
  useImperativeHandle,
  useLayoutEffect,
  useMemo,
  useRef,
  type KeyboardEvent,
  type ReactNode,
  type Ref,
} from 'react'
import type { LibraryGame } from '../api/types'
import { useViewState } from '../viewState'
import { AvalonRowViewport } from './avalon-row-viewport'
import {
  initialGridPosition,
  moveGridRows,
  reconcileGrid,
  selectGrid,
  type AvalonGridPosition,
} from './avalon-navigation'

export interface AvalonGridHandle {
  focusFirst(): void
  focusSelected(): void
  moveRows(delta: number): void
}
export type AvalonSavedGrid = AvalonGridPosition & {
  collections?: Record<string, AvalonGridPosition>
}

export function AvalonFullscreenGrid({
  games,
  columns,
  gap,
  prefix,
  collection,
  selected,
  onSelected,
  reducedMotion,
  onKeyDown,
  onTopBoundary,
  controls,
  children,
}: {
  games: LibraryGame[]
  columns: number
  gap: number
  prefix: string
  collection?: string
  selected: number | null
  onSelected(id: number | null): void
  reducedMotion: boolean
  onKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number): void
  onTopBoundary?(): void
  controls?: Ref<AvalonGridHandle>
  children(
    game: LibraryGame,
    index: number,
    handlers: { onFocus(): void; onKeyDown(event: KeyboardEvent<HTMLButtonElement>): void },
  ): ReactNode
}) {
  const root = useRef<HTMLDivElement>(null),
    viewport = useRef<HTMLDivElement>(null)
  const [stored, saveStored] = useViewState<AvalonSavedGrid>(`${prefix}:rows`, {
    ...initialGridPosition(),
    selectedId: selected,
  })
  const saved = collection ? (stored.collections?.[collection] ?? initialGridPosition()) : stored
  function save(next: AvalonGridPosition) {
    saveStored((previous) =>
      collection ? { ...next, collections: { ...previous.collections, [collection]: next } } : next,
    )
  }
  const ids = useMemo(() => games.map((game) => game.workId), [games])
  const position = reconcileGrid(saved, columns, ids)
  const rows = useMemo(
    () =>
      Array.from({ length: Math.ceil(games.length / columns) }, (_, index) => {
        const cards = games.slice(index * columns, (index + 1) * columns)
        return { id: cards.map((game) => game.workId).join(':'), cards, index }
      }),
    [games, columns],
  )
  const pendingFocus = useRef(false),
    hadFocus = useRef(false)
  const current = useRef({ position, ids, save, onSelected })
  current.current = { position, ids, save, onSelected }
  function focusSelection() {
    const cover = viewport.current?.querySelector<HTMLButtonElement>(
      `[data-row-active="true"] [data-avalon-game="${position.selectedId}"]`,
    )
    if (cover && (pendingFocus.current || hadFocus.current)) {
      cover.focus({ preventScroll: true })
      pendingFocus.current = false
    }
  }
  function apply(next: AvalonGridPosition, focus = true) {
    if (focus) pendingFocus.current = true
    save(next)
    onSelected(next.selectedId)
  }
  useImperativeHandle(controls, () => ({
    focusFirst() {
      apply(selectGrid(position, position.firstRow * columns, ids))
      // A selected first card does not produce a state change on repeated activation.
      viewport.current
        ?.querySelector<HTMLButtonElement>('[data-row-active="true"] [data-avalon-game]')
        ?.focus({ preventScroll: true })
    },
    focusSelected() {
      pendingFocus.current = true
      focusSelection()
    },
    moveRows(delta) {
      apply(moveGridRows(position, delta, ids))
    },
  }))
  useLayoutEffect(() => {
    if (JSON.stringify(saved) !== JSON.stringify(position)) save(position)
    if (position.selectedId !== selected) onSelected(position.selectedId)
    focusSelection()
  }, [position.firstRow, position.selectedIndex, position.selectedId, columns, ids, selected, collection])
  useEffect(() => {
    const clear = () => {
      hadFocus.current = false
      pendingFocus.current = false
    }
    window.addEventListener('blur', clear)
    return () => window.removeEventListener('blur', clear)
  }, [])
  useEffect(() => {
    const element = root.current!
    const wheel = (event: WheelEvent) => {
      if (Math.abs(event.deltaY) <= 8) return
      event.preventDefault()
      event.stopPropagation()
      const active = current.current,
        next = moveGridRows(active.position, Math.sign(event.deltaY), active.ids)
      pendingFocus.current = true
      active.save(next)
      active.onSelected(next.selectedId)
    }
    element.addEventListener('wheel', wheel, { passive: false })
    return () => element.removeEventListener('wheel', wheel)
  }, [])
  function key(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    if ((event.ctrlKey || event.metaKey) && !['Home', 'End'].includes(event.key)) {
      onKeyDown(event, index)
      return
    }
    const base = selectGrid(position, index, ids)
    if (event.key === 'ArrowUp' && index < columns && onTopBoundary) {
      event.preventDefault()
      event.stopPropagation()
      apply(base, false)
      onTopBoundary()
      return
    }
    let next: AvalonGridPosition
    if (event.key === 'ArrowUp' || event.key === 'ArrowDown')
      next = moveGridRows(base, event.key === 'ArrowUp' ? -1 : 1, ids)
    else if (event.key === 'PageUp' || event.key === 'PageDown')
      next = moveGridRows(base, event.key === 'PageUp' ? -2 : 2, ids)
    else if (event.key === 'ArrowLeft' || event.key === 'ArrowRight')
      next = selectGrid(base, index + (event.key === 'ArrowLeft' ? -1 : 1), ids)
    else if (event.key === 'Home')
      next = selectGrid(base, event.ctrlKey ? 0 : Math.floor(index / columns) * columns, ids)
    else if (event.key === 'End')
      next = selectGrid(
        base,
        event.ctrlKey ? ids.length - 1 : Math.floor(index / columns) * columns + columns - 1,
        ids,
      )
    else {
      onKeyDown(event, index)
      return
    }
    event.preventDefault()
    event.stopPropagation()
    apply(next)
  }
  return (
    <div
      ref={root}
      className="avalon-fullscreen-grid"
      data-selected-id={position.selectedId ?? undefined}
      onFocusCapture={() => {
        hadFocus.current = true
      }}
      onBlurCapture={(event) => {
        if (event.relatedTarget instanceof Node && !event.currentTarget.contains(event.relatedTarget))
          hadFocus.current = false
      }}
    >
      <AvalonRowViewport
        rows={rows}
        first={position.firstRow}
        visible={2}
        reducedMotion={reducedMotion}
        viewportRef={viewport}
        onReady={focusSelection}
      >
        {(row) => (
          <div
            className="avalon-grid-row avalon-fullscreen-grid-row"
            style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`, gap }}
          >
            {row.cards.map((game, local) => {
              const index = row.index * columns + local
              return children(game, index, {
                onFocus: () => apply(selectGrid(position, index, ids), false),
                onKeyDown: (event) => key(event, index),
              })
            })}
          </div>
        )}
      </AvalonRowViewport>
    </div>
  )
}
