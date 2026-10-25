import type { GameEntry, Workspace } from '../renderer/api/types'
import { isDerelict, lifecycleForOwnership } from './lifecycle'

/** Availability mirrors the public action facts. The backend rechecks these before dispatch. */
type ActionEntry = Omit<GameEntry, 'installed'> & { installed: boolean | null }
function validEpicKey(key: unknown): boolean {
  if (!key || typeof key !== 'object') return false
  const value = key as Record<string, unknown>
  return ['namespace', 'catalogItemId', 'artifactId'].every(
    (part) => typeof value[part] === 'string' && /^[A-Za-z0-9._-]{1,64}$/.test(value[part]),
  )
}

export function primaryAction(entry: ActionEntry, workspace?: Workspace): 'Play' | 'Install' | null {
  if (!workspace) return null
  if (entry.store.startsWith('plugin:'))
    return workspace.pluginActions[String(entry.ownershipId)]?.canPlay ? 'Play' : null
  if (typeof entry.installed !== 'boolean') return null
  const ids = workspace.externalIds.filter((id) => id.releaseId === entry.releaseId)
  const id = ids.find((item) => item.provider === entry.store)?.providerId
  if (entry.store === 'steam' && id && /^\d{1,10}$/.test(id)) return entry.installed ? 'Play' : 'Install'
  if (entry.store === 'gog' && id && /^\d{1,12}$/.test(id)) return entry.installed ? 'Play' : 'Install'
  if (entry.store === 'epic' && id && validEpicKey(workspace.epicLaunchKeys[id]))
    return entry.installed ? 'Play' : 'Install'
  return null
}

/** Prefer an installed copy only when it can actually be reached. */
export function primaryEntry(entries: GameEntry[], workspace?: Workspace): GameEntry | undefined {
  const reachable = entries.filter((entry) => primaryAction(entry, workspace))
  const viable = reachable.filter((entry) => !isDerelict(lifecycleForOwnership(entry.ownershipId, workspace)))
  // Delisting is evidence, not a launch prohibition. All-Derelict groups still offer their copies.
  return (
    viable.find((entry) => entry.installed) ??
    viable[0] ??
    reachable.find((entry) => entry.installed) ??
    reachable[0]
  )
}

export function noActionSentence(entry: ActionEntry, workspace?: Workspace): string | null {
  if (!workspace || primaryAction(entry, workspace)) return null
  if (entry.store.startsWith('plugin:') && workspace.pluginActions[String(entry.ownershipId)]?.canOpenStore)
    return null
  const id = workspace.externalIds.find(
    (row) => row.releaseId === entry.releaseId && row.provider === entry.store,
  )?.providerId
  // A valid Steam or Galaxy identity still has a readable store/launcher page.
  if (
    (entry.store === 'steam' && id && /^\d{1,10}$/.test(id)) ||
    (entry.store === 'gog' && id && /^\d{1,12}$/.test(id))
  )
    return null
  const key = entry.store === 'epic' && id ? workspace.epicLaunchKeys[id] : null
  const storefront = key && workspace.storefronts?.[`epic:${key.namespace}`]?.storeUrl
  if (storefront && !/[\u0000-\u0020\u007f]/.test(storefront)) {
    try {
      const url = new URL(storefront)
      if (['http:', 'https:'].includes(url.protocol) && !url.username && !url.password) return null
    } catch {
      /* A malformed stored URL is not an available destination. */
    }
  }
  if (validEpicKey(key) && entry.installed === null)
    return "Winnow has not read this copy's install state yet."
  return 'Winnow does not yet hold the identifier this store needs to reach this game.'
}
