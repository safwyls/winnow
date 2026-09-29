import type { LibraryGame } from '../api/types'

export interface LibraryIdentityLink {
  parentWorkId: number
  childWorkId: number
  kind: string
  retractedAt?: string | null
}

export interface ExpansionMark {
  count: number
  unplayed: boolean
  text: string
}

export function parseExpansionGrouping(value: string | null | undefined): boolean {
  return value?.trim().toLowerCase() === 'true'
}

/** Fold only the presentation set, before search, facet cuts and list membership. */
export function projectLibraryGames(
  games: LibraryGame[],
  identityLinks: readonly LibraryIdentityLink[],
  enabled: boolean,
) {
  const marks = new Map<number, ExpansionMark>()
  if (!enabled) return { games, marks }
  const links = identityLinks.filter((link) => !link.retractedAt)
  const parents = new Map(
    links.filter((link) => link.kind === 'same_game').map((link) => [link.childWorkId, link.parentWorkId]),
  )
  function resolve(id: number) {
    const visited = new Set<number>()
    while (parents.has(id) && !visited.has(id)) {
      visited.add(id)
      id = parents.get(id)!
    }
    return id
  }
  const present = new Set(games.map((game) => game.workId))
  const folded = new Map<number, number>()
  for (const link of links) {
    if (link.kind !== 'expansion_of' || !present.has(link.childWorkId)) continue
    const parent = resolve(link.parentWorkId)
    if (parent !== link.childWorkId && present.has(parent)) folded.set(link.childWorkId, parent)
  }
  const visible = games.filter((game) => !folded.has(game.workId))
  const visibleIds = new Set(visible.map((game) => game.workId))
  for (const game of games) {
    const parent = folded.get(game.workId)
    if (parent === undefined || !visibleIds.has(parent)) continue
    const previous = marks.get(parent)
    const count = (previous?.count ?? 0) + 1
    const unplayed = !!previous?.unplayed || game.playtimeMinutes <= 0
    marks.set(parent, {
      count,
      unplayed,
      text: `Includes ${count} ${count === 1 ? 'expansion' : 'expansions'}${unplayed ? ', one of them never played' : ''}.`,
    })
  }
  return { games: folded.size ? visible : games, marks }
}
