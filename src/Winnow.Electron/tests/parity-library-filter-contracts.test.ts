import { describe, expect, it } from 'vitest'
import type { LibraryFilter, LibraryGame } from '../src/renderer/api/types'
import {
  avalonFacts,
  avalonRuleOptions,
  matchesAvalonRules,
  type AvalonWorkspace,
} from '../src/renderer/themes/avalon-filters'
import { reflectedDensity, libraryIdle, libraryPlaytime } from '../src/renderer/themes/avalon-library-chrome'

function matrix(rows: { title: string; genre: number; mode: string; bucket?: string; year?: number }[]) {
  const games: LibraryGame[] = rows.map((row, index) => ({
    workId: index + 1,
    title: row.title,
    firstReleaseYear: row.year,
    bucket: row.bucket ?? 'bounced',
    playtimeMinutes: 300,
    entries: [
      {
        workId: index + 1,
        releaseId: index + 1,
        ownershipId: index + 1,
        title: row.title,
        store: 'steam',
        installed: false,
        playtimeMinutes: 300,
      },
    ],
  }))
  const workspace: AvalonWorkspace = {
    facets: [
      { id: 1, kind: 'genre', name: 'RPG', slug: 'rpg' },
      { id: 2, kind: 'genre', name: 'Shooter', slug: 'shooter' },
      { id: 3, kind: 'genre', name: 'Strategy', slug: 'strategy' },
    ],
    releaseFacets: rows.map((row, index) => ({
      releaseId: index + 1,
      facetIds: [row.genre],
      gameModes: [row.mode],
    })),
  }
  const facts = avalonFacts(games, workspace)
  const titles = (filter: LibraryFilter) =>
    games.filter((game) => matchesAvalonRules(game, filter, facts.get(game.workId))).map((game) => game.title)
  const counts = (filter: LibraryFilter) =>
    new Map(
      avalonRuleOptions(games, facts, workspace, filter, 'genreIds').map((option) => [
        option.label,
        option.count,
      ]),
    )
  return { titles, counts }
}
describe('Library source filter matrices', () => {
  it('widens one selected genre to the union of two genres', () => {
    const { titles } = matrix([
      { title: 'Disco Elysium', genre: 1, mode: 'single' },
      { title: 'Hades', genre: 2, mode: 'single' },
      { title: 'Civilization VI', genre: 3, mode: 'single' },
    ])
    expect(titles({ genreIds: [1] })).toEqual(['Disco Elysium'])
    expect(titles({ genreIds: [1, 2] })).toEqual(['Disco Elysium', 'Hades'])
  })
  it('narrows independent genre and player-mode groups and includes the rail as another AND term', () => {
    const { titles } = matrix([
      { title: 'Deep Rock Galactic', genre: 2, mode: 'co_op' },
      { title: 'DOOM', genre: 2, mode: 'single', bucket: 'never_played' },
      { title: 'Overcooked', genre: 1, mode: 'co_op' },
    ])
    expect(titles({ genreIds: [2], gameModes: ['co_op'] })).toEqual(['Deep Rock Galactic'])
    expect(titles({ genreIds: [2] })).toEqual(['Deep Rock Galactic', 'DOOM'])
    expect(titles({ genreIds: [2], buckets: ['bounced'] })).toEqual(['Deep Rock Galactic'])
  })
  it('counts the result each option would leave while lifting only its own selected group', () => {
    const { titles, counts } = matrix([
      { title: 'Co-op RPG', genre: 1, mode: 'co_op' },
      { title: 'Solo RPG', genre: 1, mode: 'single' },
      { title: 'Solo shooter', genre: 2, mode: 'single' },
    ])
    expect(counts({}).get('RPG')).toBe(2)
    expect(counts({ gameModes: ['single'] }).get('RPG')).toBe(1)
    expect(counts({ gameModes: ['single'] }).get('Shooter')).toBe(1)
    const selected = { gameModes: ['single'], genreIds: [1] }
    expect(titles(selected)).toEqual(['Solo RPG'])
    expect(counts(selected).get('Shooter')).toBe(1)
    expect(counts(selected).get('RPG')).toBe(1)
    expect(titles({ ...selected, genreIds: [1, 2] })).toEqual(['Solo RPG', 'Solo shooter'])
  })
  it('excludes undated games only while a release-year bound is active', () => {
    const { titles } = matrix([
      { title: 'Dated', genre: 1, mode: 'single', year: 2015 },
      { title: 'Undated', genre: 1, mode: 'single' },
    ])
    expect(titles({ yearFrom: 2010, yearTo: 2020 })).toEqual(['Dated'])
    expect(titles({})).toEqual(['Dated', 'Undated'])
  })
})
describe('Library row facts and density boundaries', () => {
  it('clamps mirrored widths to the original range and preserves the midpoint', () => {
    expect([0, 108, 154, 200, 999].map(reflectedDensity)).toEqual([200, 200, 154, 108, 108])
  })
  it('prints the original compact playtime and idle columns including absent and future dates', () => {
    expect([0, 1, 59, 60, 119, 120].map(libraryPlaytime)).toEqual(['—', '1m', '59m', '1h', '1h', '2h'])
    const now = Date.parse('2026-09-29T00:00:00Z')
    const idle = (days: number) => libraryIdle(new Date(now - days * 86400000).toISOString(), now)
    expect([-2, 0, 29, 31, 365.25, 395.6875].map(idle)).toEqual(['1d', '1d', '29d', '1mo', '1y', '1y 1mo'])
    expect(libraryIdle(null, now)).toBe('—')
  })
})
