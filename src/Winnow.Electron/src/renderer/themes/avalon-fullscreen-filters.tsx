import { useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react'
import { CalendarDays, Filter, Grid2X2 } from 'lucide-react'
import type { LibraryFilter } from '../api/types'
import { libraryBucketDescription, libraryBucketLabel, LIBRARY_COLLECTIONS } from './avalon-library-chrome'
import accept from '../features/assets/xbox_button_a_outline.svg?raw'
import back from '../features/assets/xbox_button_b_outline.svg?raw'
import applyGlyph from '../features/assets/xbox_button_y_outline.svg?raw'
import './avalon-fullscreen-filters.css'

type Option = { value: string | number; label: string; count: number }
export type FullscreenFilterGroup = {
  key: string
  label: string
  selected: (string | number)[]
  options: Option[]
}
const headers: Record<string, string> = {
  genreIds: 'GENRE',
  themeIds: 'THEME',
  gameModes: 'GAME MODE',
  tagIds: 'STORE TAG',
  featureIds: 'FEATURES',
  controllerIds: 'CONTROLLER',
  stores: 'PLATFORM',
}
const sortOptions = [
  ['dormant', 'Dormant longest'],
  ['title', 'Name A–Z'],
  ['title-desc', 'Name Z–A'],
  ['recent', 'Last played'],
  ['time', 'Playtime high to low'],
  ['time-low', 'Playtime low to high'],
  ['list-order', 'List order'],
] as const

export function AvalonFullscreenFilters({
  draft,
  update,
  groups,
  browse,
  setBrowse,
  from,
  to,
  showYears,
  year,
  editText,
  count,
  error,
  valid,
  apply,
  clear,
  close,
}: {
  draft: LibraryFilter
  update(value: LibraryFilter): void
  groups: FullscreenFilterGroup[]
  browse: { bucket: string; sort: string; manual: boolean }
  setBrowse(value: { bucket: string; sort: string }): void
  from: string
  to: string
  showYears: boolean
  year(from: string, to: string): void
  editText?(input: HTMLInputElement): void
  count: number
  error: string
  valid: boolean
  apply(): void
  clear(): void
  close(): void
}) {
  const [page, setPage] = useState<string | null>(null)
  const panel = useRef<HTMLElement>(null),
    origin = useRef<HTMLElement | null>(null)
  const previousPage = useRef<string | null>(null)
  const fromInput = useRef<HTMLInputElement>(null),
    toInput = useRef<HTMLInputElement>(null)
  const ordered = Object.keys(headers).flatMap((key) => groups.filter((group) => group.key === key))
  const group = groups.find((item) => item.key === page)
  const installation = draft.installed == null ? 'Any' : draft.installed ? 'Installed' : 'Not installed'
  const title =
    page === 'collection'
      ? 'Collection'
      : page === 'sort'
        ? 'Sort'
        : page === 'installed'
          ? 'ON DISK'
          : page?.startsWith('year')
            ? 'Release year'
            : group
              ? headers[group.key]
              : 'Filter & sort'
  function available() {
    return [
      ...(panel.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled)') ?? []),
    ].filter((element) => !element.closest('[hidden]'))
  }
  function enter(next: string, button: HTMLElement) {
    origin.current = button
    setPage(next)
  }
  function returnToFilters() {
    setPage(null)
  }
  useLayoutEffect(() => {
    const target =
      previousPage.current && !page && origin.current?.isConnected ? origin.current : available()[0]
    target?.focus({ preventScroll: true })
    target?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
    previousPage.current = page
  }, [page])
  // Explicit rows match Browse and Refine, including when either column scrolls.
  function navigate(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'Escape') {
      event.preventDefault()
      event.stopPropagation()
      if (page) returnToFilters()
      else close()
      return
    }
    const controls = available()
    const current = document.activeElement as HTMLElement
    if (event.key === 'Tab') {
      const index = controls.indexOf(current)
      if ((event.shiftKey && index === 0) || (!event.shiftKey && index === controls.length - 1)) {
        event.preventDefault()
        controls[event.shiftKey ? controls.length - 1 : 0]?.focus()
      }
      return
    }
    if (
      !event.key.startsWith('Arrow') ||
      !(current instanceof HTMLButtonElement) ||
      !controls.includes(current)
    )
      return
    const rows = new Map<number, HTMLButtonElement[]>()
    for (const button of panel.current?.querySelectorAll<HTMLButtonElement>('button') ?? []) {
      if (button.closest('[hidden]')) continue
      const row = Number(button.dataset.filterRow)
      rows.set(row, [...(rows.get(row) ?? []), button])
    }
    const orderedRows = [...rows.entries()]
      .sort(([a], [b]) => a - b)
      .map(([, buttons]) =>
        buttons.sort((a, b) => Number(a.dataset.filterColumn) - Number(b.dataset.filterColumn)),
      )
    const row = orderedRows.findIndex((buttons) => buttons.includes(current))
    const column = orderedRows[row].indexOf(current)
    const horizontal = event.key === 'ArrowLeft' || event.key === 'ArrowRight'
    const direction = event.key === 'ArrowUp' || event.key === 'ArrowLeft' ? -1 : 1
    let nextRow = row,
      nextColumn = column
    let target: HTMLButtonElement | undefined
    // Source rows compact a lone right-column control to index zero.
    while (true) {
      if (horizontal) nextColumn += direction
      else {
        nextRow += direction
        if (!orderedRows[nextRow]) break
        nextColumn = Math.min(column, orderedRows[nextRow].length - 1)
      }
      const candidate = orderedRows[nextRow]?.[nextColumn]
      if (!candidate) break
      if (!candidate.disabled) {
        target = candidate
        break
      }
    }
    event.preventDefault()
    event.stopPropagation()
    target?.focus({ preventScroll: true })
    target?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
  }
  function yearButton(which: 'yearFrom' | 'yearTo', row: number) {
    const value = which === 'yearFrom' ? from : to
    return (
      <button
        data-filter-row={row}
        data-filter-column={0}
        onClick={(event) => {
          const input = which === 'yearFrom' ? fromInput.current : toInput.current
          if (editText && input) editText(input)
          else enter(which, event.currentTarget)
        }}
      >
        Release year {which === 'yearFrom' ? 'from' : 'to'} · {value || 'Any'}
      </button>
    )
  }
  const selections =
    page === 'collection'
      ? [
          ...LIBRARY_COLLECTIONS.map((item) => ({
            value: item.key,
            label: libraryBucketLabel(item.key),
            description: libraryBucketDescription(item.key),
          })),
          ...(!LIBRARY_COLLECTIONS.some((item) => item.key === browse.bucket)
            ? [
                {
                  value: browse.bucket,
                  label: libraryBucketLabel(browse.bucket),
                  description: libraryBucketDescription(browse.bucket),
                },
              ]
            : []),
        ]
      : page === 'sort'
        ? sortOptions
            .filter(([value]) => value !== 'list-order' || browse.manual)
            .map(([value, label]) => ({ value, label, description: '' }))
        : page === 'installed'
          ? [
              { value: '', label: 'Any installation', description: '' },
              { value: 'true', label: 'Installed', description: '' },
              { value: 'false', label: 'Not installed', description: '' },
            ]
          : []
  return (
    <section
      ref={panel}
      role="dialog"
      aria-modal="true"
      aria-label="Library filters"
      className="avalon-filter-panel fullscreen"
      data-filter-page={page ?? 'overview'}
      onKeyDown={navigate}
    >
      <header>
        <h2>{title}</h2>
        <p role="status" className="fullscreen-filter-count">
          {count.toLocaleString()} {count === 1 ? 'game' : 'games'}
        </p>
      </header>
      <div className="fullscreen-filter-columns" hidden={page !== null}>
        <div className="fullscreen-filter-browse">
          <h3>
            <Grid2X2 aria-hidden />
            Browse
          </h3>
          <button
            data-filter-row={0}
            data-filter-column={0}
            onClick={(e) => enter('collection', e.currentTarget)}
          >
            Collection · {libraryBucketLabel(browse.bucket)}
          </button>
          <button data-filter-row={1} data-filter-column={0} onClick={(e) => enter('sort', e.currentTarget)}>
            Sort · {sortOptions.find(([key]) => key === browse.sort)?.[1] ?? browse.sort}
          </button>
          {showYears && (
            <>
              <h3>
                <CalendarDays aria-hidden />
                Release year
              </h3>
              {yearButton('yearFrom', 2)}
              {yearButton('yearTo', 3)}
            </>
          )}
        </div>
        <div className="fullscreen-filter-refine">
          <h3>
            <Filter aria-hidden />
            Refine games
          </h3>
          {ordered.map((item, index) => (
            <button
              key={item.key}
              data-filter-row={index}
              data-filter-column={1}
              aria-label={`${headers[item.key]} · ${item.selected.length ? `${item.selected.length} selected` : 'Any'}`}
              onClick={(event) => enter(item.key, event.currentTarget)}
            >
              <span>{headers[item.key]}</span>
              <span className="fullscreen-filter-value" data-selected={item.selected.length > 0 || undefined}>
                {item.selected.length
                  ? item.options
                      .filter((option) => item.selected.includes(option.value))
                      .map((option) => option.label)
                      .join(', ')
                  : 'Any'}
              </span>
            </button>
          ))}
          <button
            data-filter-row={ordered.length}
            data-filter-column={1}
            aria-label={`ON DISK · ${installation}`}
            onClick={(e) => enter('installed', e.currentTarget)}
          >
            <span>ON DISK</span>
            <span className="fullscreen-filter-value" data-selected={draft.installed != null || undefined}>
              {installation}
            </span>
          </button>
          {!ordered.some((item) => item.key !== 'stores') && (
            <p className="fullscreen-filter-empty">No game metadata is available to filter yet.</p>
          )}
        </div>
      </div>
      {page && (
        <div className="fullscreen-filter-choices">
          {group
            ? group.options.map((option, index) => (
                <button
                  key={option.value}
                  data-filter-row={index}
                  data-filter-column={0}
                  aria-label={`${option.label}, ${option.count} matching ${option.count === 1 ? 'title' : 'titles'}`}
                  aria-pressed={group.selected.includes(option.value)}
                  disabled={!option.count && !group.selected.includes(option.value)}
                  onClick={() =>
                    update({
                      ...draft,
                      [group.key]: group.selected.includes(option.value)
                        ? group.selected.filter((value) => value !== option.value)
                        : [...group.selected, option.value],
                    })
                  }
                >
                  <span aria-hidden className="fullscreen-filter-check">
                    {group.selected.includes(option.value) ? '✓' : ''}
                  </span>
                  <span>{option.label}</span>
                  <span className="fullscreen-filter-option-count">{option.count}</span>
                </button>
              ))
            : selections.map((option, index) => (
                <button
                  key={option.value}
                  data-filter-row={index}
                  data-filter-column={0}
                  data-filter-choice={option.value}
                  aria-label={option.label}
                  title={option.description || undefined}
                  aria-description={option.description || undefined}
                  aria-pressed={
                    option.value ===
                    (page === 'collection'
                      ? browse.bucket
                      : page === 'sort'
                        ? browse.sort
                        : draft.installed == null
                          ? ''
                          : String(draft.installed))
                  }
                  onClick={() => {
                    if (page === 'collection') setBrowse({ ...browse, bucket: option.value })
                    else if (page === 'sort') setBrowse({ ...browse, sort: option.value })
                    else update({ ...draft, installed: option.value ? option.value === 'true' : null })
                    returnToFilters()
                  }}
                >
                  <span>{option.label}</span>
                  {option.description && <small>{option.description}</small>}
                </button>
              ))}
          {page.startsWith('year') && (
            <label>
              {page === 'yearFrom' ? 'From this year' : 'Up to this year'}
              <input
                inputMode="numeric"
                value={page === 'yearFrom' ? from : to}
                onChange={(event) =>
                  year(
                    page === 'yearFrom' ? event.target.value : from,
                    page === 'yearTo' ? event.target.value : to,
                  )
                }
              />
            </label>
          )}
          <button
            data-filter-row={Math.max(group?.options.length ?? 0, selections.length) + 1}
            data-filter-column={0}
            onClick={returnToFilters}
          >
            Done
          </button>
        </div>
      )}
      <div hidden>
        <input
          ref={fromInput}
          aria-label="From this year"
          tabIndex={-1}
          value={from}
          onChange={(event) => year(event.target.value, to)}
        />
        <input
          ref={toInput}
          aria-label="Up to this year"
          tabIndex={-1}
          value={to}
          onChange={(event) => year(from, event.target.value)}
        />
      </div>
      <footer>
        {error && <p role="alert">{error}</p>}
        <div className="fullscreen-filter-actions">
          <button
            data-filter-row={999}
            data-filter-column={0}
            data-controller-context
            className="primary"
            disabled={!valid}
            onClick={apply}
          >
            Apply filters
          </button>
          <button
            data-filter-row={999}
            data-filter-column={1}
            onClick={() => {
              clear()
              returnToFilters()
            }}
          >
            Clear filters
          </button>
          <button data-filter-row={999} data-filter-column={2} onClick={page ? returnToFilters : close}>
            {page ? 'Back to filters' : 'Cancel'}
          </button>
        </div>
        <div className="fullscreen-filter-hints">
          {[
            [accept, 'A', group ? 'Toggle' : 'Choose'],
            [back, 'B', page ? 'Back to filters' : 'Discard changes'],
            [applyGlyph, 'Y', 'Apply & return'],
          ].map(([art, key, label]) => (
            <span key={key}>
              <span
                aria-hidden="true"
                data-filter-glyph={key}
                dangerouslySetInnerHTML={{ __html: art.replace('<svg ', '<svg viewBox="8 8 48 48" ') }}
              />
              <span className="sr-only">{key} </span>
              {label}
            </span>
          ))}
        </div>
      </footer>
    </section>
  )
}
