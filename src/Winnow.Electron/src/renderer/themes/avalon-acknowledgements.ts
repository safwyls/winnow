import { request } from '../api/client'
import type { GameDetails, LibraryGame } from '../api/types'
import { updateFlagState } from '../features/update-flags'
import type { AvalonFactMap } from './avalon-filters'

export function captureReadSelection(games: LibraryGame[], facts: AvalonFactMap) {
  // Every target uses the displayed selection, even if a refresh arrives while
  // an earlier game's details or acknowledgement is still pending.
  return games
    .filter((game) => facts.get(game.workId)?.unread)
    .map((game) => ({
      workId: game.workId,
      lastPlayedAt: game.lastPlayedAt,
      playtimeMinutes: game.playtimeMinutes,
      watermarks: new Map(facts.get(game.workId)?.watermarks),
    }))
}

/** The selected tiles bound the write; fresh Details supplies current acknowledgements, never newer pushes. */
export async function acknowledgeReadSelection(batch: ReturnType<typeof captureReadSelection>) {
  let failures = 0
  const visited = new Set<number>()
  for (const game of batch) {
    if (!game.watermarks.size) {
      failures++
      continue
    }
    let detail: GameDetails & { acknowledgements?: Record<string, string> }
    try {
      detail = await request<GameDetails>('game.details', { workId: game.workId })
    } catch {
      failures++
      continue
    }
    for (const [releaseId, watermark] of game.watermarks) {
      if (visited.has(releaseId)) continue
      visited.add(releaseId)
      const events = detail.events.filter(
        (event) =>
          event.releaseId === releaseId &&
          (event.kind !== 'build_push' || Date.parse(event.occurredAt) <= Date.parse(watermark)),
      )
      if (
        !updateFlagState(events, detail.acknowledgements ?? {}, game.lastPlayedAt, game.playtimeMinutes)
          .unread
      )
        continue
      try {
        const response = await request<{ result: string }>(
          'updates.acknowledge',
          { releaseId },
          {
            observedEventIds: events.map((event) => event.id),
          },
        )
        if (response.result !== 'Stored') failures++
      } catch {
        failures++
      }
    }
  }
  return failures
}
