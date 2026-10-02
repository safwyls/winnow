import type { LibraryFilter, LibraryGame } from '../api/types'
import { storeLabel } from '../api/client'

export interface AvalonWorkspace {
  facets?: { id: number; kind: string; slug: string; name: string }[]
  releaseFacets?: { releaseId: number; facetIds: number[]; gameModes: string[] }[]
  buckets?: {
    releaseId: number
    resolvedWorkId: number
    majorUpdateAt?: string | null
    game: { unreadUpdateCount: number; majorUpdateAt?: string | null }
  }[]
}
export interface AvalonGameFacts {
  facetIds: Set<number>
  gameModes: Set<string>
  unread: boolean
  unreadCount: number
  watermarks: Map<number, string>
}
export type AvalonFactMap = Map<number, AvalonGameFacts>
export const AVALON_FACET_GROUPS = [
  ['genreIds', 'genre', 'Genres'],
  ['themeIds', 'theme', 'Themes'],
  ['tagIds', 'tag', 'Tags'],
  ['featureIds', 'feature', 'Features'],
  ['controllerIds', 'controller', 'Controller support'],
  ['gameModes', 'game_mode', 'Player modes'],
] as const

export function avalonFacts(games: LibraryGame[], workspace?: AvalonWorkspace): AvalonFactMap {
  const releases = new Map(workspace?.releaseFacets?.map((row) => [row.releaseId, row]))
  const buckets = new Map<number, NonNullable<AvalonWorkspace['buckets']>>()
  for (const row of workspace?.buckets ?? [])
    buckets.set(row.resolvedWorkId, [...(buckets.get(row.resolvedWorkId) ?? []), row])
  return new Map(
    games.map((game) => {
      const facts: AvalonGameFacts = {
        facetIds: new Set(),
        gameModes: new Set(),
        unread: false,
        unreadCount: 0,
        watermarks: new Map(),
      }
      for (const entry of game.entries) {
        for (const id of releases.get(entry.releaseId)?.facetIds ?? []) facts.facetIds.add(id)
        for (const mode of releases.get(entry.releaseId)?.gameModes ?? []) facts.gameModes.add(mode)
      }
      const rows = buckets.get(game.workId)
      facts.unread =
        (game.playtimeMinutes > 0 || !!game.lastPlayedAt) &&
        (rows ? rows.some((row) => row.game.unreadUpdateCount > 0) : game.bucket === 'stale_but_patched')
      // Each ownership carries the same work aggregate. Store copies are not additive.
      facts.unreadCount = facts.unread
        ? Math.max(0, ...(rows ?? []).map((row) => row.game.unreadUpdateCount))
        : 0
      for (const row of rows ?? [])
        if (
          row.majorUpdateAt &&
          (!facts.watermarks.has(row.releaseId) ||
            Date.parse(row.majorUpdateAt) > Date.parse(facts.watermarks.get(row.releaseId)!))
        )
          facts.watermarks.set(row.releaseId, row.majorUpdateAt)
      return [game.workId, facts]
    }),
  )
}
const values = (filter: LibraryFilter, key: string): (string | number)[] =>
  Array.isArray(filter[key]) ? (filter[key] as (string | number)[]) : []
export function matchesAvalonRules(
  game: LibraryGame,
  filter: LibraryFilter,
  facts?: AvalonGameFacts,
): boolean {
  if (
    filter.search?.trim() &&
    !game.title.toLocaleLowerCase().includes(filter.search.trim().toLocaleLowerCase())
  )
    return false
  if (
    filter.stores?.length &&
    !game.entries.some((entry) =>
      filter.stores!.some((store) => store.toLocaleLowerCase() === entry.store.toLocaleLowerCase()),
    )
  )
    return false
  if (filter.buckets?.length && !filter.buckets.includes(game.bucket)) return false
  if (
    typeof filter.installed === 'boolean' &&
    filter.installed !== game.entries.some((entry) => entry.installed)
  )
    return false
  if (typeof filter.hasUnread === 'boolean' && filter.hasUnread !== (facts?.unread ?? false)) return false
  if (
    typeof filter.yearFrom === 'number' &&
    (game.firstReleaseYear == null || game.firstReleaseYear < filter.yearFrom)
  )
    return false
  if (
    typeof filter.yearTo === 'number' &&
    (game.firstReleaseYear == null || game.firstReleaseYear > filter.yearTo)
  )
    return false
  for (const [key] of AVALON_FACET_GROUPS) {
    const selected = values(filter, key)
    if (
      selected.length &&
      !selected.some((value) =>
        key === 'gameModes' ? facts?.gameModes.has(String(value)) : facts?.facetIds.has(Number(value)),
      )
    )
      return false
  }
  return true
}
export function avalonRuleOptions(
  games: LibraryGame[],
  facts: AvalonFactMap,
  workspace: AvalonWorkspace | undefined,
  filter: LibraryFilter,
  key: string,
  choicesGames: LibraryGame[] = games,
) {
  const group = AVALON_FACET_GROUPS.find(([field]) => field === key)
  const selected = values(filter, key)
  const choices: { value: string | number; label: string; count: number; missing?: boolean }[] =
    key === 'stores'
      ? [...new Set(choicesGames.flatMap((game) => game.entries.map((entry) => entry.store)))].map(
          (value) => ({
            value,
            label: storeLabel(value),
            count: 0,
          }),
        )
      : key === 'buckets'
        ? [...new Set(choicesGames.map((game) => game.bucket))].map((value) => ({
            value,
            label: value.replaceAll('_', ' '),
            count: 0,
          }))
        : (workspace?.facets ?? [])
            .filter(
              (facet) =>
                facet.kind === group?.[1] &&
                (selected.includes(key === 'gameModes' ? facet.slug : facet.id) ||
                  choicesGames.some((game) =>
                    key === 'gameModes'
                      ? facts.get(game.workId)?.gameModes.has(facet.slug)
                      : facts.get(game.workId)?.facetIds.has(facet.id),
                  )),
            )
            .map((facet) => ({
              value: key === 'gameModes' ? facet.slug : facet.id,
              label: facet.name,
              count: 0,
            }))
  for (const value of selected)
    if (!choices.some((choice) => choice.value === value))
      choices.push({ value, label: `Unavailable saved filter (${value})`, count: 0, missing: true })
  const remaining = { ...filter, [key]: [] }
  const population = games.filter((game) => matchesAvalonRules(game, remaining, facts.get(game.workId)))
  const counts = new Map<string | number, number>()
  for (const game of population) {
    const attributes =
      key === 'stores'
        ? new Set(game.entries.map((entry) => entry.store))
        : key === 'buckets'
          ? new Set([game.bucket])
          : key === 'gameModes'
            ? facts.get(game.workId)?.gameModes
            : facts.get(game.workId)?.facetIds
    for (const value of attributes ?? []) counts.set(value, (counts.get(value) ?? 0) + 1)
  }
  for (const choice of choices) choice.count = counts.get(choice.value) ?? 0
  return choices.sort((a, b) => a.label.localeCompare(b.label))
}
export function avalonYearRange(
  from: string,
  to: string,
): { yearFrom: number | null; yearTo: number | null } | null {
  const parse = (text: string) =>
    !text.trim() ? null : /^\d{4}$/.test(text.trim()) ? Number(text.trim()) : NaN
  const start = parse(from),
    end = parse(to)
  if (
    (start !== null && (!Number.isInteger(start) || start < 1000 || start > 9999)) ||
    (end !== null && (!Number.isInteger(end) || end < 1000 || end > 9999)) ||
    (start !== null && end !== null && start > end)
  )
    return null
  return { yearFrom: start, yearTo: end }
}
