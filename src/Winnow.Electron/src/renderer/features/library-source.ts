import { storeLabel } from '../api/client'
import type { GameEntry, Workspace } from '../api/types'

export function librarySourceSummary(entries: readonly GameEntry[], workspace?: Workspace): string {
  return [
    ...new Set(
      entries.flatMap((entry) => {
        const label = workspace?.pluginActions[String(entry.ownershipId)]?.sourceLabel
        return label?.trim() ? [`${storeLabel(entry.store)}: ${label}`] : []
      }),
    ),
  ].join(' · ')
}
