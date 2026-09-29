import { describe, expect, it } from 'vitest'
import type { FeedSnapshot, LibraryGame } from '../src/renderer/api/types'
import {
  AVALON_COLORS,
  avalonFilter,
  avalonShelves,
  coverGrid,
  dormancy,
} from '../src/renderer/themes/avalon-data'
import { readFileSync } from 'node:fs'
import { PALETTES } from '../src/shared/theme'

const game = (id: number, extra: Partial<LibraryGame> = {}): LibraryGame => ({
  workId: id,
  title: `Game ${id}`,
  bucket: 'never_played',
  playtimeMinutes: 0,
  entries: [
    {
      ownershipId: id,
      releaseId: id,
      workId: id,
      title: `Game ${id}`,
      store: 'Steam',
      installed: false,
      playtimeMinutes: 0,
    },
  ],
  ...extra,
})

describe('Avalon presentation facts', () => {
  it('keeps the house palette synchronized with the original tokens', () => {
    expect(PALETTES.winnow).toMatchObject(AVALON_COLORS)
    const tokens = readFileSync(new URL('../../Winnow.App/Themes/tokens.axaml', import.meta.url), 'utf8')
    const names = {
      background: 'Ground',
      surface: 'Surface',
      raised: 'SurfaceRaised',
      text: 'Text',
      muted: 'TextDim',
      line: 'Line',
      cool: 'Azure',
    }
    for (const [key, name] of Object.entries(names))
      expect(tokens).toContain(
        `<Color x:Key="${name}Color">${AVALON_COLORS[key as keyof typeof AVALON_COLORS]}</Color>`,
      )
  })

  it.each([
    [0, 1],
    [6, 0.72],
    [12, 0.5],
    [24, 0.34],
    [36, 0.22],
    [60, 0.22],
  ])('matches the original dormancy knot at %s months', (months, saturation) => {
    const now = Date.parse('2026-09-28T00:00:00Z')
    const result = dormancy(new Date(now - months * 30.4375 * 86400000).toISOString(), now)
    expect(result.saturation).toBeCloseTo(saturation)
    expect(result.brightness).toBeGreaterThanOrEqual(0.68)
  })
  it('handles unknown, invalid and future play dates without making artwork unreadable', () => {
    expect(dormancy(null)).toEqual({ saturation: 0.22, brightness: 0.68, hue: -6 })
    expect(dormancy('invalid')).toEqual(dormancy(null))
    expect(dormancy('2100-01-01')).toEqual({ saturation: 1, brightness: 1, hue: -0 })
  })
  it('keeps recent play nearly vivid and four-year-old games at the original dormancy floor', () => {
    const now = Date.parse('2026-09-29T00:00:00Z')
    expect(dormancy('2026-09-26T00:00:00Z', now).saturation).toBeGreaterThan(0.9)
    expect(dormancy('2022-09-29T00:00:00Z', now).saturation).toBe(0.22)
  })

  it('fills desktop vacancies from the reserve and includes the fullscreen reserve without duplicates', () => {
    const games = Array.from({ length: 10 }, (_, index) => game(index + 1))
    const item = (id: number) => ({
      ownershipId: id,
      releaseId: id,
      title: `Game ${id}`,
      reason: 'A reason.',
    })
    const feed: FeedSnapshot = {
      candidateCount: 12,
      confidence: 1,
      failed: false,
      shelves: [
        {
          id: 'feed',
          title: 'For you',
          blurb: '',
          supportsFeedback: true,
          items: [item(99), item(1), item(1), item(2), item(3)],
          reserve: [4, 5, 6, 7, 8, 9, 10].map(item),
        },
      ],
    }
    expect(avalonShelves(games, feed, false)[0].rows.map((row) => row.game.workId)).toEqual([1, 2, 3, 4, 5])
    expect(avalonShelves(games, feed, true)[0].rows.map((row) => row.game.workId)).toEqual([
      1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
    ])
    for (const fullscreen of [false, true]) {
      const shelf = avalonShelves(games, feed, fullscreen)[0]
      for (const row of [...shelf.rows, ...(shelf.reserve ?? [])])
        expect(row.game).toBe(games.find((game) => game.workId === row.game.workId))
      expect(avalonShelves([], feed, fullscreen)).toEqual([])
    }
  })
  it('puts recent play first without feedback or feed impressions', () => {
    const games = [
      game(1, { lastPlayedAt: '2026-07-01T00:00:00Z' }),
      game(2, { lastPlayedAt: '2026-09-01T00:00:00Z' }),
      game(3),
    ]
    const [recent] = avalonShelves(games, undefined, false)
    expect(recent.id).toBe('recently-played')
    expect(recent.feedback).toBe(false)
    expect(recent.rows.map((row) => row.game.workId)).toEqual([2, 1])
    expect(recent.rows.every((row) => row.releaseId === undefined)).toBe(true)
  })

  it('does not duplicate the backend unscored recently_played shelf', () => {
    const games = [game(1, { lastPlayedAt: '2026-09-01T00:00:00Z' })]
    const shelves = avalonShelves(
      games,
      {
        candidateCount: 1,
        confidence: 1,
        failed: false,
        shelves: [
          {
            id: 'recently_played',
            title: 'Recently played',
            blurb: '',
            supportsFeedback: false,
            reserve: [],
            items: [{ ownershipId: 1, releaseId: 1, title: 'Game 1', reason: 'Ready to play.' }],
          },
        ],
      },
      true,
    )
    expect(shelves).toHaveLength(1)
    expect(shelves[0].rows[0].releaseId).toBe(1)
    expect(shelves[0].rows[0].reason).toBe('Ready to play.')
  })

  it.each([false, true])(
    'preserves source shelf order, pitches, reasons and recent capacity with fullscreen=%s',
    (fullscreen) => {
      const games = Array.from({ length: 12 }, (_, index) => game(index + 1, { lastPlayedAt: '2026-09-01' }))
      const items = games
        .slice()
        .reverse()
        .map((game) => ({
          ownershipId: game.workId,
          releaseId: game.workId,
          title: game.title,
          reason: `Last played on ${game.workId} September 2026.`,
        }))
      const feed: FeedSnapshot = {
        candidateCount: 0,
        confidence: 0,
        failed: false,
        shelves: [
          {
            id: 'recently_played',
            title: 'Recently played',
            blurb: 'Your latest games.',
            supportsFeedback: false,
            items: items.slice(0, 6),
            reserve: items.slice(6),
          },
          {
            id: 'recommended',
            title: 'Recommended',
            blurb: 'A different pitch.',
            supportsFeedback: true,
            items: [items[0]],
            reserve: [],
          },
        ],
      }
      const [recent, recommended] = avalonShelves(games, feed, fullscreen)
      expect(recent.id).toBe('recently_played')
      expect(recent.blurb).toBe('Your latest games.')
      expect(recent.feedback).toBe(false)
      expect(recent.rows.map((row) => row.releaseId)).toEqual(
        items.slice(0, fullscreen ? 12 : 5).map((item) => item.releaseId),
      )
      expect(recent.rows.map((row) => row.reason)).toEqual(
        items.slice(0, fullscreen ? 12 : 5).map((item) => item.reason),
      )
      expect(recent.reserve?.map((row) => row.releaseId)).toEqual(
        fullscreen ? [] : items.slice(5).map((item) => item.releaseId),
      )
      expect(recommended).toMatchObject({
        id: 'recommended',
        title: 'Recommended',
        blurb: 'A different pitch.',
        feedback: true,
      })
      expect(recommended.rows).toHaveLength(1)
    },
  )

  it('keeps omitted or empty shelves absent even when the library has recent play', () => {
    const games = [game(1, { lastPlayedAt: '2026-09-01' })]
    const shelves = avalonShelves(
      games,
      {
        candidateCount: 1,
        confidence: 2,
        failed: false,
        shelves: [{ id: 'empty', title: 'Empty', blurb: '', items: [], reserve: [], supportsFeedback: true }],
      },
      false,
    )
    expect(shelves).toEqual([])
  })

  it('intersects search, installation, stores and lists across linked releases', () => {
    const a = game(1, { title: 'Alpha' }),
      b = game(2, { title: 'Bravo' })
    a.entries.push({ ...a.entries[0], releaseId: 50, store: 'GOG', installed: true })
    const lists = [{ id: 7, name: 'Weekend', isLive: false, releaseIds: [50], revision: 'one' }]
    const filters = { query: ' ALP ', bucket: 'installed', store: 'GOG', listId: '7', sort: 'title' }
    expect(avalonFilter([a, b], lists, filters)).toEqual([a])
    expect(avalonFilter([a, b], lists, { ...filters, listId: 'missing' })).toEqual([])
    expect([a, b].map((entry) => entry.title)).toEqual(['Alpha', 'Bravo'])
  })
  it.each([
    ['title', [1, 2, 3]],
    ['title-desc', [3, 2, 1]],
    ['time', [3, 1, 2]],
    ['recent', [2, 1, 3]],
  ])('sorts %s deterministically without changing the library snapshot', (sort, ids) => {
    const games = [
      game(3, { playtimeMinutes: 100 }),
      game(1, { lastPlayedAt: '2026-01-01' }),
      game(2, { lastPlayedAt: '2026-02-01' }),
    ]
    expect(
      avalonFilter(games, [], {
        query: '',
        bucket: 'all',
        store: 'all',
        listId: 'all',
        sort: String(sort),
      }).map((entry) => entry.workId),
    ).toEqual(ids)
    expect(games[0].workId).toBe(3)
  })
  it('reflows portrait covers as density and available width change', () => {
    expect(coverGrid(1000, 800, 108, false).columns).toBeGreaterThan(coverGrid(1000, 800, 200, false).columns)
    expect(coverGrid(20, 100, 148, false).columns).toBe(1)
    const tv = coverGrid(1600, 600, 148, true)
    expect(tv.rowHeight * 2 - tv.gap).toBeLessThanOrEqual(640)
  })
})
