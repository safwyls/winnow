import { useEffect, useRef, useState } from 'react'
import type { LibraryFilter, LibraryGame } from '../api/types'
import { matchesBucket } from './avalon-data'
import { libraryBucketLabel } from './avalon-library-chrome'
import { AvalonFullscreenFilters } from './avalon-fullscreen-filters'
import { AnnouncedName } from '../components/AnnouncedName'
import { ChoiceMenu } from '../components/ChoiceMenu'
import {
  AVALON_FACET_GROUPS,
  avalonRuleOptions,
  avalonYearRange,
  matchesAvalonRules,
  type AvalonFactMap,
  type AvalonWorkspace,
} from './avalon-filters'

export function AvalonFilterPanel({
  filter,
  games,
  allGames = games,
  optionOrder,
  facts,
  workspace,
  fullscreen,
  browse,
  apply,
  close,
  editText,
}: {
  filter: LibraryFilter
  games: LibraryGame[]
  allGames?: LibraryGame[]
  optionOrder?: Map<string, (string | number)[]>
  facts: AvalonFactMap
  workspace?: AvalonWorkspace
  fullscreen: boolean
  browse?: { sort: string; bucket: string; manual: boolean }
  apply(filter: LibraryFilter, browse?: { sort: string; bucket: string }): void
  close(): void
  editText?(input: HTMLInputElement): void
}) {
  const [draft, setDraft] = useState(filter),
    [from, setFrom] = useState(String(filter.yearFrom ?? '')),
    [to, setTo] = useState(String(filter.yearTo ?? ''))
  const [error, setError] = useState('')
  const [draftSort, setDraftSort] = useState(browse?.sort ?? 'dormant')
  const [draftBucket, setDraftBucket] = useState(browse?.bucket ?? 'all')
  const ref = useRef<HTMLElement>(null),
    origin = useRef(document.activeElement as HTMLElement | null)
  const localOrder = useRef(new Map<string, (string | number)[]>())
  const order = optionOrder ?? localOrder.current
  const previousFilter = useRef(filter)
  useEffect(() => {
    if (fullscreen) return
    setDraft(filter)
    if (filter.yearFrom !== previousFilter.current.yearFrom) setFrom(String(filter.yearFrom ?? ''))
    if (filter.yearTo !== previousFilter.current.yearTo) setTo(String(filter.yearTo ?? ''))
    previousFilter.current = filter
  }, [filter, fullscreen])
  const valid = avalonYearRange(from, to)
  function update(next: LibraryFilter) {
    setDraft(next)
    if (!fullscreen) apply(next)
  }
  function year(nextFrom: string, nextTo: string) {
    setFrom(nextFrom)
    setTo(nextTo)
    const range = avalonYearRange(nextFrom, nextTo)
    setError(range ? '' : 'Enter years from 1000 to 9999, with the start no later than the end.')
    if (range) update({ ...draft, ...range })
  }
  useEffect(() => {
    ref.current?.querySelector<HTMLElement>('button, input, select')?.focus()
    return () => {
      origin.current?.focus()
    }
  }, [])
  const groups = [['stores', '', 'Stores'], ...AVALON_FACET_GROUPS] as const
  const population = browse ? games.filter((game) => matchesBucket(game, draftBucket)) : games
  const visibleGroups = groups.flatMap(([key, , label]) => {
    const options = avalonRuleOptions(population, facts, workspace, draft, key, allGames),
      selected = (draft[key] ?? []) as (string | number)[]
    if (
      !options.length ||
      (!selected.length &&
        options.length === 1 &&
        allGames.length > 0 &&
        allGames.every((game) =>
          matchesAvalonRules(game, { [key]: [options[0].value] }, facts.get(game.workId)),
        ))
    )
      return []
    const previous = order.get(key) ?? []
    const additions = options
      .filter((option) => !previous.includes(option.value))
      .sort((a, b) =>
        key === 'stores' || key === 'gameModes'
          ? a.label.localeCompare(b.label)
          : b.count - a.count || a.label.localeCompare(b.label),
      )
    const stable = [...previous, ...additions.map((option) => option.value)]
    order.set(key, stable)
    return [
      {
        key,
        label,
        selected,
        options: options.sort((a, b) => stable.indexOf(a.value) - stable.indexOf(b.value)),
      },
    ]
  })
  const datedYears = allGames.flatMap((game) =>
    game.firstReleaseYear == null ? [] : [game.firstReleaseYear],
  )
  if (fullscreen)
    return (
      <AvalonFullscreenFilters
        draft={draft}
        update={update}
        groups={visibleGroups}
        browse={{ sort: draftSort, bucket: draftBucket, manual: browse?.manual ?? false }}
        setBrowse={(next) => {
          setDraftSort(next.sort)
          setDraftBucket(next.bucket)
        }}
        from={from}
        to={to}
        showYears={Boolean(datedYears.length || from || to)}
        year={year}
        editText={editText}
        count={population.filter((game) => matchesAvalonRules(game, draft, facts.get(game.workId))).length}
        error={error}
        valid={Boolean(valid)}
        close={close}
        clear={() => {
          setFrom('')
          setTo('')
          setError('')
          setDraftBucket('all')
          update({})
        }}
        apply={() => {
          if (!valid) return
          if (browse) apply({ ...draft, ...valid }, { sort: draftSort, bucket: draftBucket })
          else apply({ ...draft, ...valid })
          close()
        }}
      />
    )
  return (
    <section
      ref={ref}
      role={fullscreen ? 'dialog' : 'region'}
      aria-modal={fullscreen || undefined}
      aria-label="Library filters"
      className={`avalon-filter-panel ${fullscreen ? 'fullscreen' : ''}`}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.preventDefault()
          event.stopPropagation()
          close()
        }
        if (fullscreen && event.key === 'Tab') {
          const controls = [
            ...(ref.current?.querySelectorAll<HTMLElement>(
              'button:not(:disabled), input:not(:disabled), select:not(:disabled), summary',
            ) ?? []),
          ]
          const first = controls[0],
            last = controls.at(-1)
          if (event.shiftKey && document.activeElement === first) {
            event.preventDefault()
            last?.focus()
          } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault()
            first?.focus()
          }
        }
      }}
    >
      <header>
        <h2>Filters</h2>
        <button onClick={close}>{fullscreen ? 'Cancel' : 'Close filters'}</button>
      </header>
      <div className="avalon-filter-fields">
        {browse && (
          <>
            <label>
              Collection
              <ChoiceMenu
                label="Collection"
                aria-label="Collection"
                value={draftBucket}
                onChange={setDraftBucket}
                options={[
                  { value: 'all', label: 'All games' },
                  ...[
                    ...new Set([
                      'never_played',
                      'bounced',
                      'active',
                      'stale_but_patched',
                      'derelict',
                      ...allGames.map((game) => game.bucket),
                      draftBucket,
                    ]),
                  ]
                    .filter((key) => key !== 'all')
                    .map((key) => ({ value: key, label: libraryBucketLabel(key) })),
                ]}
              />
            </label>
            <label>
              Sort
              <ChoiceMenu
                label="Sort"
                aria-label="Sort"
                value={draftSort}
                onChange={setDraftSort}
                options={[
                  { value: 'dormant', label: 'Dormant longest' },
                  { value: 'title', label: 'Name A–Z' },
                  { value: 'title-desc', label: 'Name Z–A' },
                  { value: 'recent', label: 'Last played' },
                  { value: 'time', label: 'Playtime high to low' },
                  { value: 'time-low', label: 'Playtime low to high' },
                  ...(browse.manual ? [{ value: 'list-order', label: 'List order' }] : []),
                ]}
              />
            </label>
          </>
        )}
        {(datedYears.length > 0 || from || to) && (
          <div className="avalon-filter-year">
            <label>
              From this year
              <input
                aria-label="From this year"
                inputMode="numeric"
                value={from}
                placeholder={datedYears.length ? String(Math.min(...datedYears)) : undefined}
                onChange={(event) => year(event.target.value, to)}
              />
            </label>
            <label>
              Up to this year
              <input
                aria-label="Up to this year"
                inputMode="numeric"
                value={to}
                placeholder={datedYears.length ? String(Math.max(...datedYears)) : undefined}
                onChange={(event) => year(from, event.target.value)}
              />
            </label>
          </div>
        )}
        {error && <p role="alert">{error}</p>}
        <label>
          Installation
          <ChoiceMenu
            label="Installation"
            aria-label="Installation"
            value={draft.installed == null ? '' : String(draft.installed)}
            onChange={(value) => update({ ...draft, installed: value ? value === 'true' : null })}
            options={[
              { value: '', label: 'Any installation' },
              { value: 'true', label: 'Installed' },
              { value: 'false', label: 'Not installed' },
            ]}
          />
        </label>
        {!visibleGroups.some((group) => group.key !== 'stores') && (
          <p className="muted">No game metadata is available to filter yet.</p>
        )}
        {visibleGroups.map(({ key, label, options, selected }) => {
          return (
            <details key={key} open={selected.length ? true : undefined}>
              <summary>
                {label}
                {selected.length ? ` · ${selected.length}` : ''}
              </summary>
              <div className="avalon-filter-options">
                {options.map((option) => (
                  <AnnouncedName
                    key={option.value}
                    name={`${option.label}, ${option.count} matching ${option.count === 1 ? 'title' : 'titles'}`}
                  >
                    <label>
                      <input
                        type="checkbox"
                        aria-label={`${option.label}, ${option.count} matching ${option.count === 1 ? 'title' : 'titles'}`}
                        disabled={option.count === 0 && !selected.includes(option.value)}
                        checked={selected.includes(option.value)}
                        onChange={(event) =>
                          update({
                            ...draft,
                            [key]: event.target.checked
                              ? [...selected, option.value]
                              : selected.filter((value) => value !== option.value),
                          })
                        }
                      />
                      <span>{option.label}</span>
                      <small>{option.count}</small>
                    </label>
                  </AnnouncedName>
                ))}
              </div>
            </details>
          )
        })}
      </div>
      <footer>
        <button
          onClick={() => {
            setFrom('')
            setTo('')
            setError('')
            setDraftBucket('all')
            update({})
          }}
        >
          Clear filters
        </button>
        {fullscreen && (
          <button
            data-controller-context
            className="primary"
            disabled={!valid}
            onClick={() => {
              if (browse) apply({ ...draft, ...valid }, { sort: draftSort, bucket: draftBucket })
              else apply({ ...draft, ...valid })
              close()
            }}
          >
            Apply filters
          </button>
        )}
      </footer>
    </section>
  )
}
