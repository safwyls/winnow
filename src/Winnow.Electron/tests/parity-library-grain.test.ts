import { describe, expect, it } from 'vitest'
import type { LibraryGame } from '../src/renderer/api/types'
import {
  parseExpansionGrouping,
  projectLibraryGames,
  type LibraryIdentityLink,
} from '../src/renderer/features/parity-library-grain'
import { avalonFilter } from '../src/renderer/themes/avalon-data'

const game = (id: number, minutes: number, store = 'steam'): LibraryGame => ({
  workId: id,
  title: `Game ${id}`,
  playtimeMinutes: minutes,
  lastPlayedAt: minutes ? '2026-01-01T00:00:00Z' : null,
  bucket: minutes ? 'active' : 'never_played',
  entries: [
    {
      workId: id,
      ownershipId: id,
      releaseId: id * 10,
      title: `Game ${id}`,
      store,
      installed: false,
      playtimeMinutes: minutes,
    },
  ],
})
const link = (parent: number, child: number, kind = 'expansion_of'): LibraryIdentityLink => ({
  parentWorkId: parent,
  childWorkId: child,
  kind,
})

describe('Library expansion grain', () => {
  it.each([
    [null, false],
    ['', false],
    ['   ', false],
    ['yes please', false],
    ['false', false],
    ['true', true],
    [' True ', true],
  ] as const)('defaults stored %s to grouping=%s and round trips its formatted value', (value, expected) => {
    expect(parseExpansionGrouping(value)).toBe(expected)
    expect(parseExpansionGrouping(String(expected))).toBe(expected)
  })
  it('keeps an unlinked library and disabled expansion links tile for tile with the original row identities', () => {
    const games = [game(1, 12000), game(2, 0)]
    for (const result of [
      projectLibraryGames(games, [], true),
      projectLibraryGames(games, [link(1, 2)], false),
    ]) {
      expect(result.games).toBe(games)
      expect(result.games.map((row) => row.entries.length)).toEqual([1, 1])
      expect(result.games[1]!.bucket).toBe('never_played')
      expect(result.games[1]!.playtimeMinutes).toBe(0)
      expect(result.marks.size).toBe(0)
    }
  })
  it.each([0, 45])(
    'folds a pack with %s minutes and carries its count without adding its stores dates bucket or playtime to the base',
    (minutes) => {
      const games = [
        game(1, 12000),
        { ...game(2, minutes, 'epic'), lastPlayedAt: '2026-09-29T00:00:00Z' },
        game(3, 8000),
      ]
      const before = structuredClone(games)
      const result = projectLibraryGames(games, [link(1, 2)], true)
      expect(result.games).toEqual([games[0], games[2]])
      expect(result.games[0]).toBe(games[0])
      expect(result.games[0]!.playtimeMinutes).toBe(12000)
      expect(result.games[0]!.lastPlayedAt).toBe('2026-01-01T00:00:00Z')
      expect(result.games[0]!.bucket).toBe('active')
      expect(result.games[0]!.entries.map((entry) => entry.store)).toEqual(['steam'])
      expect(result.marks.get(1)).toEqual({
        count: 1,
        unplayed: minutes === 0,
        text: minutes === 0 ? 'Includes 1 expansion, one of them never played.' : 'Includes 1 expansion.',
      })
      expect(games).toEqual(before)
      expect(games[1]!.playtimeMinutes).toBe(minutes)
    },
  )
  it('keeps a pack whose base is absent and ignores variants and retracted relationships', () => {
    const games = [game(1, 12000), game(2, 0), game(3, 0), game(4, 0)]
    const result = projectLibraryGames(
      games,
      [link(99, 2), link(1, 3, 'variant_of'), { ...link(1, 4), retractedAt: '2026-09-01' }],
      true,
    )
    expect(result.games).toBe(games)
    expect(result.marks.size).toBe(0)
  })
  it('resolves a merged base before folding and counts owned packs once with unplayed wording', () => {
    const games = [game(1, 12000), game(2, 30), game(3, 0)]
    const result = projectLibraryGames(
      games,
      [link(1, 10, 'same_game'), link(10, 11, 'same_game'), link(11, 2), link(1, 3)],
      true,
    )
    expect(result.games).toEqual([games[0]])
    expect(result.marks.get(1)).toEqual({
      count: 2,
      unplayed: true,
      text: 'Includes 2 expansions, one of them never played.',
    })
  })
  it('folds before search store bucket and manual-list cuts without changing the underlying recommendation rows', () => {
    const games = [game(1, 12000), game(2, 0, 'epic'), game(3, 0)]
    const snapshot = structuredClone(games)
    const { games: projected } = projectLibraryGames(games, [link(1, 2)], true)
    const defaults = { query: '', bucket: 'all', store: 'all', listId: 'all', sort: 'title' }
    const lists = [{ id: 5, name: 'Pack only', isLive: false, releaseIds: [20], revision: 'one' }]
    expect(avalonFilter(projected, lists, { ...defaults, query: 'Game 2' })).toEqual([])
    expect(avalonFilter(projected, lists, { ...defaults, store: 'epic' })).toEqual([])
    expect(avalonFilter(projected, lists, { ...defaults, bucket: 'never_played' })).toEqual([games[2]])
    expect(avalonFilter(projected, lists, { ...defaults, listId: '5' })).toEqual([])
    expect(games).toEqual(snapshot)
    expect(games.filter((game) => game.bucket === 'never_played').map((game) => game.workId)).toEqual([2, 3])
  })
})
