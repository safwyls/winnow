import type { FeedSnapshot, GameList, LibraryGame } from '../api/types'

export const AVALON_COLORS = {
  background: '#0F1C1E',
  surface: '#16282A',
  raised: '#1D3437',
  text: '#F0EDE7',
  muted: '#8FA5A0',
  line: '#2B4A4C',
  cool: '#57A8F0',
} as const

/** Matches the piecewise saturation curve in Winnow.App.Services.Dormancy. */
export function dormancy(lastPlayedAt: string | null | undefined, now = Date.now()) {
  const played = lastPlayedAt ? Date.parse(lastPlayedAt) : NaN
  const months = Number.isFinite(played) ? Math.max(0, (now - played) / (30.4375 * 86400000)) : 36
  const ramp = [
    [0, 1],
    [6, 0.72],
    [12, 0.5],
    [24, 0.34],
    [36, 0.22],
  ]
  let saturation = 0.22
  for (let i = 1; i < ramp.length; i++) {
    if (months <= ramp[i][0]) {
      const [m0, s0] = ramp[i - 1],
        [m1, s1] = ramp[i]
      saturation = s0 + (s1 - s0) * ((months - m0) / (m1 - m0))
      break
    }
  }
  const vivid = (saturation - 0.22) / 0.78
  return { saturation, brightness: 0.68 + 0.32 * vivid, hue: -6 * (1 - vivid) }
}

export interface AvalonShelf {
  id: string
  title: string
  blurb: string
  feedback: boolean
  rows: { game: LibraryGame; releaseId?: number; reason: string }[]
  reserve?: { game: LibraryGame; releaseId?: number; reason: string }[]
}

export function avalonShelves(
  games: LibraryGame[],
  feed: FeedSnapshot | undefined,
  fullscreen: boolean,
): AvalonShelf[] {
  const result: AvalonShelf[] = []
  const recent = games
    .filter((game) => game.lastPlayedAt && Number.isFinite(Date.parse(game.lastPlayedAt)))
    .sort((a, b) => Date.parse(b.lastPlayedAt!) - Date.parse(a.lastPlayedAt!))
    .slice(0, fullscreen ? 10 : 5)
  if (!feed && recent.length)
    result.push({
      id: 'recently-played',
      title: 'Recently played',
      blurb: 'Pick up where you left off.',
      feedback: false,
      rows: recent.map((game) => ({ game, reason: 'Pick up where you left off.' })),
    })
  const byRelease = new Map(
    games.flatMap((game) => game.entries.map((entry) => [entry.releaseId, game] as const)),
  )
  for (const shelf of feed?.shelves ?? []) {
    const seen = new Set<number>()
    const rows: AvalonShelf['rows'] = []
    for (const item of [...shelf.items, ...shelf.reserve]) {
      const game = byRelease.get(item.releaseId)
      if (!game || seen.has(game.workId)) continue
      seen.add(game.workId)
      rows.push({ game, releaseId: item.releaseId, reason: item.reason })
    }
    if (rows.length)
      result.push({
        id: shelf.id,
        title: shelf.title,
        blurb: shelf.blurb,
        feedback: shelf.supportsFeedback,
        rows: fullscreen ? rows : rows.slice(0, 5),
        reserve: fullscreen ? [] : rows.slice(5),
      })
  }
  return result
}

export interface AvalonFilters {
  query: string
  bucket: string
  store: string
  listId: string
  sort: string
}
export function matchesBucket(game: LibraryGame, bucket: string) {
  return (
    bucket === 'all' ||
    (bucket === 'installed' ? game.entries.some((entry) => entry.installed) : game.bucket === bucket)
  )
}
export function avalonFilter(games: LibraryGame[], lists: GameList[], filters: AvalonFilters) {
  const query = filters.query.trim().toLocaleLowerCase()
  const list = lists.find((item) => String(item.id) === filters.listId)
  return games
    .filter(
      (game) =>
        (!query || game.title.toLocaleLowerCase().includes(query)) &&
        matchesBucket(game, filters.bucket) &&
        (filters.store === 'all' || game.entries.some((entry) => entry.store === filters.store)) &&
        (filters.listId === 'all' ||
          game.entries.some((entry) => list?.releaseIds.includes(entry.releaseId))),
    )
    .sort((a, b) => {
      const title = a.title.localeCompare(b.title) || a.workId - b.workId
      if (filters.sort === 'list-order' && list && !list.isLive) {
        const position = (game: LibraryGame) =>
          Math.min(
            ...game.entries
              .map((entry) => list.releaseIds.indexOf(entry.releaseId))
              .filter((index) => index >= 0),
          )
        return position(a) - position(b) || title
      }
      if (filters.sort === 'title-desc') return -title
      if (filters.sort === 'time') return b.playtimeMinutes - a.playtimeMinutes || title
      if (filters.sort === 'time-low') return a.playtimeMinutes - b.playtimeMinutes || title
      if (filters.sort === 'recent')
        return (b.lastPlayedAt ?? '').localeCompare(a.lastPlayedAt ?? '') || title
      if (filters.sort === 'dormant')
        return (a.lastPlayedAt ?? '').localeCompare(b.lastPlayedAt ?? '') || title
      return title
    })
}

export function coverGrid(width: number, height: number, preferred: number, fullscreen: boolean) {
  const gap = fullscreen ? 24 : 16
  const target = fullscreen ? Math.max(80, Math.min(240, (height - gap) / 3)) : preferred
  const columns = Math.max(1, (fullscreen ? Math.ceil : Math.floor)((width + gap) / (target + gap)))
  const coverWidth = Math.max(1, (width - (columns - 1) * gap) / columns)
  return { columns, gap, rowHeight: coverWidth * 1.5 + gap }
}
