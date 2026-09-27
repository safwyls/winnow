import type { ApiRequest } from '../../shared/bridge'
import type { GameEntry, Workspace } from './types'

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public current?: unknown,
  ) {
    super(message)
    this.name = 'ApiError'
  }
  get conflict() {
    return this.status === 409
  }
  get uncertain() {
    return this.status === 0 || this.status >= 500
  }
}

export async function request<T>(route: string, params?: ApiRequest['params'], body?: unknown): Promise<T> {
  const result = await window.winnow.request<T>({ route, params, body })
  if (!result.ok)
    throw new ApiError(
      result.status,
      result.message || `The request could not finish (${result.status}).`,
      result.data,
    )
  return result.data as T
}

export const errorMessage = (error: unknown): string =>
  error instanceof Error ? error.message : 'The request could not finish.'
/** String client/operation IDs use Guid N format; action DTOs accept a standard UUID. */
export const createClientId = (): string => crypto.randomUUID().replaceAll('-', '')
export const hours = (minutes: number): string =>
  minutes < 60
    ? `${Math.round(minutes)} min`
    : `${(minutes / 60).toLocaleString(undefined, { maximumFractionDigits: 1 })} hr`
export const dateLabel = (value: string): string =>
  new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
export const storeLabel = (value: string): string =>
  ({ steam: 'Steam', epic: 'Epic Games', gog: 'GOG', manual: 'Manual' })[value] ??
  value.replace(/^plugin:/, '')

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

export function launchMessage(result: number | string): string {
  // LaunchDispatch is a numeric enum in API v1: HandedOff, AlreadyRunning, Refused.
  if (result === 0 || result === 'HandedOff')
    return 'Sent to the launcher. The launcher will handle the next step.'
  if (result === 1 || result === 'AlreadyRunning') return 'This game is already running.'
  return 'The launcher could not accept this action. Refresh the library and try again.'
}
