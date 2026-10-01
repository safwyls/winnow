import { describe, expect, it } from 'vitest'
import { SnapshotRefresh } from '../src/shared/snapshot-refresh'
import { recentGames } from '../src/main/jump-list'
import type { LibraryGame, Workspace } from '../src/renderer/api/types'

describe('native shell snapshots', () => {
  it('reads again after a preference changes while an older snapshot is pending', async () => {
    let complete!: () => void
    let source = 'false',
      applied = '',
      reads = 0
    const refresh = new SnapshotRefresh(async () => {
      const value = source
      if (++reads === 1)
        await new Promise<void>((resolve) => {
          complete = resolve
        })
      applied = value
    })
    const first = refresh.request()
    source = 'true'
    const second = refresh.request()
    refresh.request()
    complete()
    await Promise.all([first, second])
    expect(applied).toBe('true')
    expect(reads).toBe(2)
  })
  it('publishes only playable recent games in descending order with a ten-item limit', () => {
    const games = Array.from({ length: 15 }, (_, index) => ({
      workId: index + 1,
      title: `Game ${index + 1}`,
      bucket: 'active',
      playtimeMinutes: 10,
      lastPlayedAt: index === 14 ? null : new Date(Date.UTC(2026, 8, 28 - index)).toISOString(),
      entries: [
        {
          ownershipId: index + 1,
          releaseId: index + 1,
          workId: index + 1,
          title: `Game ${index + 1}`,
          store: 'steam',
          installed: index !== 0,
          playtimeMinutes: 10,
        },
      ],
    })) satisfies LibraryGame[]
    const workspace = {
      externalIds: games
        .slice(0, 13)
        .map((game) => ({ releaseId: game.workId, provider: 'steam', providerId: String(game.workId) })),
      pluginActions: {},
      epicLaunchKeys: {},
    } as Workspace
    expect(recentGames([...games].reverse(), workspace).map((game) => game.ownershipId)).toEqual([
      2, 3, 4, 5, 6, 7, 8, 9, 10, 11,
    ])
    expect(recentGames(games, workspace)[0].title).toBe('Game 2')
    expect(recentGames([{ ...games[1], workId: 1, headerWorkId: 2 }], workspace)[0]).toMatchObject({
      ownershipId: 2,
      workId: 1,
      coverWorkId: 2,
    })
    expect(recentGames(games.slice(13), workspace)).toEqual([])
  })
})
