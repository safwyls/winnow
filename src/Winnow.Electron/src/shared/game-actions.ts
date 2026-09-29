import type { GameEntry, Workspace } from '../renderer/api/types'

/** Availability mirrors the public action facts. The backend rechecks these before dispatch. */
export function primaryAction(entry: GameEntry, workspace?: Workspace): 'Play' | 'Install' | null {
  if (!workspace) return null
  if (entry.store.startsWith('plugin:'))
    return workspace.pluginActions[String(entry.ownershipId)]?.canPlay ? 'Play' : null
  const ids = workspace.externalIds.filter((id) => id.releaseId === entry.releaseId)
  const id = ids.find((item) => item.provider === entry.store)?.providerId
  if (entry.store === 'steam' && id && /^\d{1,10}$/.test(id)) return entry.installed ? 'Play' : 'Install'
  if (entry.store === 'gog' && id && /^\d{1,12}$/.test(id)) return entry.installed ? 'Play' : 'Install'
  if (entry.store === 'epic' && id && workspace.epicLaunchKeys[id])
    return entry.installed ? 'Play' : 'Install'
  return null
}
