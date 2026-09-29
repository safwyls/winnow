import type { LibraryGame, Workspace } from '../renderer/api/types'
import { primaryAction } from '../shared/game-actions'

export function recentGames(games: LibraryGame[], workspace: Workspace) {
  return games
    .filter((game) => game.lastPlayedAt)
    .sort((left, right) => String(right.lastPlayedAt).localeCompare(String(left.lastPlayedAt)))
    .flatMap((game) => {
      const entry = game.entries.find((value) => primaryAction(value, workspace) === 'Play')
      return entry ? [{ ownershipId: entry.ownershipId, title: game.title }] : []
    })
    .slice(0, 10)
}
