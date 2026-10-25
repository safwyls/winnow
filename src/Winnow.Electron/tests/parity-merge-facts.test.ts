import { describe, expect, it } from 'vitest'
import { buildMergeCards, includeMergeRow } from '../src/renderer/features/parity-merge-model'
import {
  mergeIdle,
  mergePlaytime,
  mergeRollup,
  mergeRowDetail,
} from '../src/renderer/features/parity-merge-facts'
import { mergeFixture } from './parity-merge-fixtures'

const now = Date.parse('2026-09-01T00:00:00Z')
function factsFixture() {
  return mergeFixture() as ReturnType<typeof mergeFixture> & {
    workspace: {
      buckets: { playtimeMinutes: number; lastPlayedAt: string | null; bucket: string }[]
      ownerships: {
        id: number
        releaseId: number
        store: string
        installed: boolean
        acquiredAt?: string | null
      }[]
    }
  }
}
describe('merge library facts', () => {
  it('reads played and unplayed row hours and idle facts from workspace and rolls them up', () => {
    const review = factsFixture()
    review.workspace.buckets[0]!.playtimeMinutes = 300
    review.workspace.buckets[0]!.lastPlayedAt = new Date(now - 100 * 86400000).toISOString()
    review.workspace.buckets[1]!.playtimeMinutes = 0
    const card = buildMergeCards(review)[0]!,
      [played, unplayed] = card.rows
    expect([mergePlaytime(played!), mergeIdle(played!, now), played!.minutes, played!.unread]).toEqual([
      '5h',
      '3mo',
      300,
      false,
    ])
    expect(mergeRowDetail(played!)).toContain('5h')
    expect([mergePlaytime(unplayed!), mergeIdle(unplayed!, now)]).toEqual(['0h', 'never'])
    expect(mergeRowDetail(unplayed!)).toContain('never opened')
    expect(mergeRollup(card)).toMatch(/^5h rolled up · 2 entries/)
  })
  it('distinguishes unplayed games from packs without their own playtime', () => {
    const review = factsFixture()
    review.candidates = []
    review.workspace.buckets.forEach((entry) => {
      entry.playtimeMinutes = 0
      entry.lastPlayedAt = null
    })
    review.workspace.ownerships.forEach((entry) => {
      entry.acquiredAt = null
    })
    review.expansions = [
      {
        base: { workId: 1, title: 'Bastion' },
        members: [{ work: { workId: 2, title: 'Bastion pack' }, kind: 'expansion_of', fromMetadata: true }],
      },
    ]
    const card = buildMergeCards(review)[0]!,
      [base, pack] = card.rows
    expect([mergePlaytime(base!), mergeIdle(base!, now)]).toEqual(['0h', 'never'])
    expect([mergePlaytime(pack!), mergeIdle(pack!, now)]).toEqual(['—', '—'])
    expect(mergeRowDetail(pack!)).toContain('no separate playtime recorded')
    expect(mergeRollup(card)).toBe('0h rolled up · 2 entries')
  })
  it('retains the earliest ownership year and patched row count while excluding only played totals', () => {
    const review = factsFixture()
    review.workspace.ownerships[0]!.acquiredAt = '2021-03-04T00:00:00Z'
    review.workspace.ownerships[1]!.acquiredAt = '2019-11-30T00:00:00Z'
    review.workspace.buckets[0]!.bucket = 'stale_but_patched'
    const card = buildMergeCards(review)[0]!
    expect(card.rows.map((row) => row.unread)).toEqual([true, false])
    expect(mergeRowDetail(card.rows[0]!)).toMatch(/Patched since you played$/)
    expect(mergeRollup(card)).toContain('owned since 2019 · 1 entry patched since you played')
    expect(mergeRollup(card)).not.toContain('2021')
    expect(mergeRollup(includeMergeRow(card, 2, false))).toBe(
      '1h rolled up · 2 entries · 1 left out · owned since 2019 · 1 entry patched since you played',
    )
  })
  it('shows all owned stores and suppresses installation claims for mixed or absent evidence', () => {
    const review = factsFixture()
    review.workspace.ownerships.push({ id: 5, releaseId: 101, store: 'epic', installed: true })
    let row = buildMergeCards(review)[0]!.rows[0]!
    expect(row.stores).toEqual(['steam', 'epic'])
    expect(row.installed).toBeNull()
    expect(mergeRowDetail(row)).not.toMatch(/installed/i)
    review.workspace.ownerships[0]!.installed = true
    row = buildMergeCards(review)[0]!.rows[0]!
    expect(mergeRowDetail(row)).toContain('Installed')
    review.workspace.ownerships = []
    expect(buildMergeCards(review)[0]!.rows[0]!.installed).toBeNull()
  })
  it.each([
    [45, '45m'],
    [59, '59m'],
    [60, '1h'],
    [119, '1h'],
    [120, '2h'],
  ] as const)('formats %i minutes as %s', (minutes, expected) => {
    expect(mergePlaytime({ minutes, pack: false })).toBe(expected)
  })
  it.each([
    [-1, '1d'],
    [0, '1d'],
    [29, '29d'],
    [100, '3mo'],
    [365.25, '1y'],
    [822, '2y 3mo'],
  ] as const)('formats %i idle days as %s', (days, expected) => {
    expect(
      mergeIdle(
        { minutes: 120, pack: false, lastPlayedAt: new Date(now - days * 86400000).toISOString() },
        now,
      ),
    ).toBe(expected)
  })
})
