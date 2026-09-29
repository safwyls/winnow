import type { GameList, LibraryGame } from '../../api/types'

export interface RiftFilters {
  query: string
  bucket: string
  store: string
  listId: string
  sort: string
}
export function filterGames(games: LibraryGame[], lists: GameList[], filters: RiftFilters): LibraryGame[] {
  const query = filters.query.trim().toLocaleLowerCase()
  const list = lists.find((item) => String(item.id) === filters.listId)
  return games
    .filter(
      (game) =>
        (!query || game.title.toLocaleLowerCase().includes(query)) &&
        (filters.bucket === 'all' ||
          (filters.bucket === 'installed'
            ? game.entries.some((entry) => entry.installed)
            : game.bucket === filters.bucket)) &&
        (filters.store === 'all' || game.entries.some((entry) => entry.store === filters.store)) &&
        (filters.listId === 'all' ||
          game.entries.some((entry) => list?.releaseIds.includes(entry.releaseId))),
    )
    .sort((a, b) =>
      filters.sort === 'time'
        ? b.playtimeMinutes - a.playtimeMinutes
        : filters.sort === 'recent'
          ? (b.lastPlayedAt ?? '').localeCompare(a.lastPlayedAt ?? '')
          : a.title.localeCompare(b.title),
    )
}

export function previewPlacement(
  card: { left: number; right: number; top: number; height: number },
  width: number,
  top: number,
  bottom: number,
  fullscreen = false,
) {
  const gutter = 18,
    gap = 20,
    preferred = fullscreen ? 430 : 400
  const right = width - card.right - gap - gutter,
    left = card.left - gap - gutter
  const side = right >= preferred || right >= left ? 'right' : 'left'
  const room = side === 'right' ? right : left,
    docked = room < 280
  const panelWidth = docked ? Math.min(400, width - gutter * 2) : Math.min(preferred, room)
  const panelHeight = Math.max(120, Math.min(fullscreen ? 440 : 420, bottom - top))
  return {
    width: panelWidth,
    height: panelHeight,
    placement: docked ? 'docked' : side,
    left: docked
      ? (width - panelWidth) / 2
      : side === 'right'
        ? card.right + gap
        : card.left - gap - panelWidth,
    top: docked
      ? bottom - panelHeight
      : Math.max(top, Math.min(bottom - panelHeight, card.top + card.height * 0.45 - panelHeight / 2)),
  }
}
