import type { LibraryResponse, Workspace } from './types'

const projections = new WeakMap<LibraryResponse, WeakMap<Workspace, LibraryResponse>>()

/** Presentation follows visible owned copies; identity and editable metadata stay canonical. */
export function projectLibraryHeaders(response: LibraryResponse, workspace?: Workspace): LibraryResponse {
  if (!workspace) return response
  const cached = projections.get(response)?.get(workspace)
  if (cached) return cached
  const preferences = workspace.preferredHeaderStores as Record<string, string | null> | undefined
  const works = new Map(workspace.works.map((work) => [work.id, work]))
  const parents = new Map(
    (
      (workspace.identityLinks ?? []) as {
        parentWorkId: number
        childWorkId: number
        kind: string
        retractedAt?: string | null
      }[]
    )
      .filter((link) => link.kind === 'same_game' && !link.retractedAt)
      .map((link) => [link.childWorkId, link.parentWorkId]),
  )
  const groupRoots = new Set<number>()
  for (let root of parents.values()) {
    const seen = new Set<number>()
    while (parents.has(root) && !seen.has(root)) {
      seen.add(root)
      root = parents.get(root)!
    }
    if (!parents.has(root)) groupRoots.add(root)
  }
  let changed = false
  const games = response.games.map((game) => {
    // Library and workspace refresh independently. An older workspace must not
    // reinterpret a newly grouped canonical snapshot before its links arrive.
    if (!groupRoots.has(game.workId)) return game
    const preferred = preferences?.[String(game.workId)]
    if (!game.entries.length || (!preferred && game.entries.every((entry) => entry.workId === game.workId)))
      return game
    const available = [...game.entries].sort(
      (a, b) => Number(b.workId === game.workId) - Number(a.workId === game.workId) || a.workId - b.workId,
    )
    const headerWorkId = (available.find((entry) => entry.store === preferred) ?? available[0])!.workId
    const entries = [...game.entries].sort((a, b) => {
      const own = Number(b.workId === headerWorkId) - Number(a.workId === headerWorkId)
      if (own) return own
      if (a.workId === headerWorkId) {
        const store = Number(b.store === preferred) - Number(a.store === preferred)
        if (store) return store
      }
      return a.workId - b.workId || a.ownershipId - b.ownershipId
    })
    changed = true
    return {
      ...game,
      headerWorkId,
      title: headerWorkId === game.workId ? game.title : (works.get(headerWorkId)?.name ?? game.title),
      entries,
    }
  })
  const result = changed ? { ...response, games } : response
  let byWorkspace = projections.get(response)
  if (!byWorkspace) projections.set(response, (byWorkspace = new WeakMap()))
  byWorkspace.set(workspace, result)
  return result
}
