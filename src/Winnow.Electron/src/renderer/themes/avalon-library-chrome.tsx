import { ChevronDown, ChevronUp, X } from 'lucide-react'
import type { ReactNode } from 'react'
import type { LibraryGame } from '../api/types'
import { bucketLabel } from '../components/primitives'
import { storeLabel } from '../api/client'
import type { useAvalonLists } from './avalon-list-state'
import {
  AVALON_FACET_GROUPS,
  avalonRuleOptions,
  type AvalonFactMap,
  type AvalonWorkspace,
} from './avalon-filters'
import './avalon-library-chrome.css'

export const MINIMUM_TILE_WIDTH = 108
export const MAXIMUM_TILE_WIDTH = 200
export const libraryBucketLabel = (key: string) => (key === 'bounced' ? 'Started' : bucketLabel(key))
export function reflectedDensity(value: number) {
  return Math.max(
    MINIMUM_TILE_WIDTH,
    Math.min(MAXIMUM_TILE_WIDTH, MINIMUM_TILE_WIDTH + MAXIMUM_TILE_WIDTH - value),
  )
}
export function libraryIdle(lastPlayedAt?: string | null, now = Date.now()) {
  if (!lastPlayedAt || !Number.isFinite(Date.parse(lastPlayedAt))) return '—'
  const days = Math.max(0, (now - Date.parse(lastPlayedAt)) / 86_400_000)
  if (days < 30) return `${Math.max(1, Math.floor(days))}d`
  const months = Math.floor(days / 30.4375)
  if (months < 12) return `${months}mo`
  const rest = months % 12
  return `${Math.floor(months / 12)}y${rest ? ` ${rest}mo` : ''}`
}
export function libraryPlaytime(minutes: number) {
  return minutes <= 0 ? '—' : minutes < 60 ? `${minutes}m` : `${Math.floor(minutes / 60)}h`
}
const columns = {
  Title: ['title', 'title-desc'],
  Playtime: ['time', 'time-low'],
  Idle: ['dormant', 'recent'],
} as const
export function LibraryColumnHeaders({ sort, change, width }: { sort: string; change(sort: string): void; width: number }) {
  function header(name: keyof typeof columns) {
    const [first, second] = columns[name]
    const active = sort === first || sort === second
    const ascending = name === 'Title' ? sort === first : sort === second
    return (
      <button
        aria-label={`Sort by ${name.toLowerCase()}`}
        aria-pressed={active}
        data-sort-direction={active ? (ascending ? 'ascending' : 'descending') : undefined}
        title={name === 'Idle' ? 'Time since you last played' : undefined}
        onClick={() => change(sort === first ? second : first)}
      >
        {name}
        {active && (ascending ? <ChevronUp aria-hidden size={12} /> : <ChevronDown aria-hidden size={12} />)}
      </button>
    )
  }
  return (
    <div className="avalon-record-header" role="group" aria-label="Library columns" style={{ width }}>
      <span aria-hidden />
      {header('Title')}
      <span>Store</span>
      <span>Status</span>
      {header('Playtime')}
      {header('Idle')}
    </div>
  )
}

type ListState = ReturnType<typeof useAvalonLists>
type Origin = 'context' | 'list' | 'unsaved' | 'user'
export interface LibraryCutChip {
  key: string
  label: string
  dimension: string
  origin: Origin
  accessibleName: string
  description: string
  remove(): void
}
const dimensions: Record<string, string> = {
  stores: 'STORE',
  buckets: 'BUCKET',
  genreIds: 'GENRE',
  themeIds: 'THEME',
  tagIds: 'STORE TAG',
  featureIds: 'FEATURE',
  controllerIds: 'CONTROLLER',
  gameModes: 'GAME MODE',
}
export function libraryCutChips(
  state: ListState,
  games: LibraryGame[],
  facts: AvalonFactMap,
  workspace?: AvalonWorkspace,
) {
  const chips: LibraryCutChip[] = [],
    saved = state.list?.isLive ? (state.base?.filter ?? state.list.filter ?? {}) : null
  const filter = state.filter
  function add(
    key: string,
    label: string,
    dimension: string,
    matchesSaved: boolean,
    remove: () => void,
    accessibleName = `Remove ${label} filter`,
    explicitOrigin?: Origin,
  ) {
    const origin = explicitOrigin ?? (saved ? (matchesSaved ? 'list' : 'unsaved') : 'user')
    const suffix =
      origin === 'context'
        ? ' — from context'
        : origin === 'list'
          ? ' — from this live list'
          : origin === 'unsaved'
            ? ' — yours, not saved to this list'
            : ''
    chips.push({
      key,
      label,
      dimension,
      origin,
      remove,
      accessibleName,
      description: `${dimension}: ${label}${suffix}`,
    })
  }
  if (state.list)
    add(
      'context',
      state.list.name,
      state.list.isLive ? 'LIVE LIST' : 'LIST',
      false,
      () => state.selectList('all'),
      'Leave this list',
      'context',
    )
  for (const key of ['buckets', ...AVALON_FACET_GROUPS.map(([key]) => key), 'stores']) {
    const selected = (filter[key] ?? []) as (string | number)[]
    if (!selected.length) continue
    const options = avalonRuleOptions(games, facts, workspace, filter, key)
    for (const value of selected) {
      const label =
        key === 'buckets'
          ? libraryBucketLabel(String(value))
          : key === 'stores'
            ? storeLabel(String(value))
            : (options.find((option) => option.value === value)?.label ?? String(value))
      add(
        `${key}:${value}`,
        label,
        dimensions[key],
        ((saved?.[key] ?? []) as unknown[]).some((item) =>
          key === 'stores' ? String(item).toLowerCase() === String(value).toLowerCase() : item === value,
        ),
        () =>
          key === 'buckets'
            ? state.selectBucket('all')
            : state.applyFilter({ ...filter, [key]: selected.filter((item) => item !== value) }),
      )
    }
  }
  for (const [key, dimension, name, label] of [
    ['installed', 'ON DISK', 'Remove installation filter', filter.installed ? 'Installed' : 'Not installed'],
    [
      'hasUnread',
      'UPDATES',
      'Remove update status filter',
      filter.hasUnread ? 'Unread updates' : 'No unread updates',
    ],
  ] as const)
    if (filter[key] != null)
      add(
        key,
        label,
        dimension,
        saved?.[key] === filter[key],
        () => state.applyFilter({ ...filter, [key]: null }),
        name,
      )
  if (filter.yearFrom != null || filter.yearTo != null)
    add(
      'year',
      filter.yearFrom != null && filter.yearTo != null
        ? filter.yearFrom === filter.yearTo
          ? `${filter.yearFrom}`
          : `${filter.yearFrom}–${filter.yearTo}`
        : filter.yearFrom != null
          ? `${filter.yearFrom} onwards`
          : `up to ${filter.yearTo}`,
      'RELEASE YEAR',
      saved?.yearFrom === filter.yearFrom && saved?.yearTo === filter.yearTo,
      () => state.applyFilter({ ...filter, yearFrom: null, yearTo: null }),
      'Remove release year filter',
    )
  if (filter.search?.trim())
    add(
      'search',
      filter.search.trim(),
      'SEARCH',
      saved?.search?.trim() === filter.search.trim(),
      () => state.setQuery(''),
      'Remove search filter',
    )
  return chips
}
export function LibraryCutBar({
  state,
  games,
  visible,
  facts,
  workspace,
  children,
}: {
  state: ListState
  games: LibraryGame[]
  visible: number
  facts: AvalonFactMap
  workspace?: AvalonWorkspace
  children?: ReactNode
}) {
  const chips = libraryCutChips(state, games, facts, workspace)
  if (!chips.length && visible === games.length) return null
  return (
    <section className="avalon-cut-bar" aria-label="Current library filters">
      <div className="avalon-cut-chips">
        {chips.map((chip) => (
          <button
            key={chip.key}
            data-filter-origin={chip.origin}
            aria-label={chip.accessibleName}
            aria-description={chip.description}
            title={chip.description}
            onClick={chip.remove}
          >
            {chip.origin === 'context' && <small>{chip.dimension}</small>}
            <span>{chip.label}</span>
            <X aria-hidden size={12} />
          </button>
        ))}
      </div>
      <span
        className="avalon-cut-count"
        aria-label={`${games.length.toLocaleString()} → ${visible.toLocaleString()}`}
      >
        <span>{games.length.toLocaleString()}</span>
        <span aria-hidden> → </span>
        <strong>{visible.toLocaleString()}</strong>
      </span>
      <div className="avalon-cut-actions">
        {children}
        <button
          onClick={() => {
            state.selectList('all')
            state.applyFilter({})
          }}
        >
          Clear filters
        </button>
      </div>
    </section>
  )
}
