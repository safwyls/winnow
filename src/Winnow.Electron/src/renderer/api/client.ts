import type { ApiRequest, LinkOpenResult } from '../../shared/bridge'

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

export async function request<T>(route: string, params?: ApiRequest['params'], body?: unknown, signal?: AbortSignal): Promise<T> {
  signal?.throwIfAborted()
  const requestId = signal ? crypto.randomUUID().replaceAll('-', '') : undefined
  const cancel = () => {
    if (requestId) void window.winnow.cancelRequest?.(requestId).catch(() => undefined)
  }
  const pending = window.winnow.request<T>({ route, params, body, ...(requestId ? { requestId } : {}) })
  signal?.addEventListener('abort', cancel, { once: true })
  if (signal?.aborted) cancel()
  let result
  try {
    result = await pending
    signal?.throwIfAborted()
  } finally {
    signal?.removeEventListener('abort', cancel)
  }
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

export const linkNoticeEvent = 'winnow-link-notice'
/** Keep fallback feedback visible even when the initiating detail view has closed. */
export async function openExternal(url: string, options?: { failure: 'inline' }): Promise<LinkOpenResult> {
  let result: LinkOpenResult
  try {
    result = await window.winnow.openExternal(url)
    if (!result || typeof result.opened !== 'boolean') throw new Error('Missing link result')
  } catch {
    result = { opened: false, message: 'Could not open this link. Try again.' }
  }
  if (!result.opened && options?.failure === 'inline') {
    window.dispatchEvent(new CustomEvent<LinkOpenResult>(linkNoticeEvent, { detail: { opened: true } }))
    throw new Error(result.message ?? 'Could not open this link. Try again.')
  }
  window.dispatchEvent(new CustomEvent<LinkOpenResult>(linkNoticeEvent, { detail: result }))
  return result
}
/** String client/operation IDs use Guid N format; action DTOs accept a standard UUID. */
export const createClientId = (): string => crypto.randomUUID().replaceAll('-', '')
export const hours = (minutes: number): string =>
  minutes < 60
    ? `${Math.round(minutes)} min`
    : `${(minutes / 60).toLocaleString(undefined, { maximumFractionDigits: 1 })} hr`
export const dateLabel = (value: string): string =>
  new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
export const storeLabel = (value: string): string =>
  ({
    steam: 'Steam',
    epic: 'Epic Games',
    gog: 'GOG',
    manual: 'Manual',
    'plugin:xbox': 'Xbox',
    'plugin:psn': 'PlayStation Network',
  })[value] ?? value.replace(/^plugin:/, '')

export { primaryAction } from '../../shared/game-actions'

export function launchMessage(result: number | string): string {
  // LaunchDispatch is a numeric enum in API v1: HandedOff, AlreadyRunning, Refused.
  if (result === 0 || result === 'HandedOff')
    return 'Sent to the launcher. The launcher will handle the next step.'
  if (result === 1 || result === 'AlreadyRunning') return 'This game is already running.'
  return 'The launcher could not accept this action. Refresh the library and try again.'
}
