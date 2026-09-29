import { describe, expect, it } from 'vitest'
import { filterGames, previewPlacement } from '../src/renderer/themes/rift/data'
import type { LibraryGame } from '../src/renderer/api/types'

const game = (
  id: number,
  title: string,
  store: string,
  installed: boolean,
  bucket: string,
  playtimeMinutes = 0,
): LibraryGame => ({
  workId: id,
  title,
  bucket,
  playtimeMinutes,
  entries: [{ workId: id, ownershipId: id, releaseId: id * 10, title, store, installed, playtimeMinutes }],
})
const games = [
  game(1, 'The Forgotten World', 'steam', true, 'dormant', 120),
  game(2, 'A New Beginning', 'gog', false, 'never_played'),
  game(3, 'A Familiar Place', 'steam', false, 'active', 900),
]
const filters = { query: '', bucket: 'all', store: 'all', listId: 'all', sort: 'title' }

describe('Rift library selection', () => {
  it('combines query, bucket, store and list ownership without mutating the shared library', () => {
    const lists = [{ id: 9, name: 'Evenings', isLive: true, releaseIds: [10, 30], revision: '1' }]
    expect(
      filterGames(games, lists, {
        ...filters,
        query: '  forgotten ',
        bucket: 'installed',
        store: 'steam',
        listId: '9',
      }).map((item) => item.workId),
    ).toEqual([1])
    expect(filterGames(games, lists, { ...filters, bucket: 'never_played', listId: '9' })).toEqual([])
    expect(filterGames(games, lists, { ...filters, listId: 'removed' })).toEqual([])
    expect(games.map((item) => item.workId)).toEqual([1, 2, 3])
  })
  it('sorts playtime and recent activity while retaining never-played records', () => {
    expect(filterGames(games, [], { ...filters, sort: 'time' }).map((item) => item.workId)).toEqual([3, 1, 2])
    const recent = games.map((item) => ({
      ...item,
      lastPlayedAt:
        item.workId === 1 ? '2026-09-20T00:00:00Z' : item.workId === 3 ? '2026-08-01T00:00:00Z' : null,
    }))
    expect(filterGames(recent, [], { ...filters, sort: 'recent' }).map((item) => item.workId)).toEqual([
      1, 3, 2,
    ])
  })
})

describe('Rift portal preview placement', () => {
  it('opens beside the cover, flips at the right edge, and stays between chrome', () => {
    const right = previewPlacement({ left: 100, right: 280, top: 150, height: 270 }, 1280, 88, 740)
    expect(right.placement).toBe('right')
    expect(right.left).toBe(300)
    const left = previewPlacement({ left: 1060, right: 1240, top: 630, height: 270 }, 1280, 88, 740)
    expect(left.placement).toBe('left')
    expect(left.left + left.width).toBe(1040)
    expect(left.top + left.height).toBeLessThanOrEqual(740)
    expect(left.top).toBeGreaterThanOrEqual(88)
  })
  it('docks inside a narrow viewport at a fixed readable height', () => {
    const result = previewPlacement({ left: 190, right: 365, top: 310, height: 260 }, 390, 72, 644)
    expect(result.placement).toBe('docked')
    expect(result.width).toBe(354)
    expect(result.height).toBe(420)
    expect(result.left).toBe(18)
    expect(result.top + result.height).toBe(644)
  })
})
