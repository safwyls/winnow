import type { GameDetails, LibraryGame } from '../api/types'
import { storeLabel } from '../api/client'

export function ReleaseAchievements({ game, details }: { game?: LibraryGame; details?: GameDetails }) {
  const rows =
    details?.achievements.flatMap((item) => {
      const entry = game?.entries.find((entry) => entry.releaseId === item.releaseId)
      if (!entry) return []
      const noSchema = item.availability === 2 || item.availability === 'NoSchema'
      const unavailable = item.availability === 1 || item.availability === 'Unavailable'
      const count = noSchema
        ? 'No achievements'
        : item.hasKnownProgress
          ? item.total > 0
            ? `${item.unlocked} of ${item.total} unlocked`
            : 'No achievements'
          : unavailable
            ? 'Unavailable'
            : entry.store === 'steam'
              ? 'Not fetched'
              : 'Not supported'
      const percent =
        !noSchema && item.hasKnownProgress && item.total > 0
          ? `${((item.unlocked / item.total) * 100).toLocaleString(undefined, { maximumFractionDigits: 1 })}%`
          : null
      return [{ item, entry, count, percent }]
    }) ?? []
  if (!rows.length) return null
  return (
    <div className="detail-achievements" role="group" aria-label="Achievements by release">
      <h3>Achievements</h3>
      {rows.map(({ item, entry, count, percent }) => (
        <p key={item.releaseId} data-release-id={item.releaseId}>
          {entry.title} · {storeLabel(entry.store)}: {count}
          {percent && ` · ${percent}`}
          {item.isStale && item.hasKnownProgress ? ' · last known' : ''}
        </p>
      ))}
    </div>
  )
}
