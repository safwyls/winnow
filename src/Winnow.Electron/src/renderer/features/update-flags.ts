import type { UpdateEvent } from '../api/types'
import { timelineUpdates, type TimelineUpdate } from './activity-timeline-model'

const patchCount = (updates: TimelineUpdate[]) => {
  const counts = new Map<number, number>()
  for (const event of updates)
    if (event.kind === 'build_push' && event.unread)
      counts.set(event.releaseId, (counts.get(event.releaseId) ?? 0) + 1)
  // Linked editions can report the same patches; their counts cannot be added.
  return Math.max(0, ...counts.values())
}

export function updateFlagState(
  events: UpdateEvent[],
  acknowledgements: Record<string, string>,
  lastPlayedAt: string | null | undefined,
  playtimeMinutes: number,
) {
  const rows = timelineUpdates(events, acknowledgements, lastPlayedAt, playtimeMinutes)
  const unread = patchCount(rows)
  const total = patchCount(timelineUpdates(events, {}, lastPlayedAt, playtimeMinutes))
  const count = unread || total
  const caption = count
    ? `${count} ${count === 1 ? 'update landed' : 'updates landed'} while you were away.${
        unread ? '' : count === 1 ? " You've marked it read." : " You've marked them read."
      }`
    : 'No updates recorded in that stretch.'
  return {
    rows,
    unread,
    caption,
    unreadReleases: [
      ...new Set(rows.filter((row) => row.kind === 'build_push' && row.unread).map((row) => row.releaseId)),
    ],
  }
}
