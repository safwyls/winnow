import { steamNavigationAllowed } from './steam-auth-policy'

export type SteamPageKind = 'licenses' | 'history'
export type SteamPageStep = 'continue' | 'exhausted' | 'cap' | 'stalled'
export const steamAccountPages = {
  licenses: 'https://store.steampowered.com/account/licenses/',
  history: 'https://store.steampowered.com/account/history/',
} as const

export function steamCapturePage(value: string): SteamPageKind | null {
  if (!steamNavigationAllowed(value)) return null
  const url = new URL(value)
  if (url.origin !== 'https://store.steampowered.com') return null
  const path = url.pathname.replace(/^\/+|\/+$/g, '').toLowerCase()
  return path === 'account/licenses' ? 'licenses' : path === 'account/history' ? 'history' : null
}

export function steamCaptureLimits(options: { maxLicensesPages?: number; maxLoadMoreClicks?: number } = {}) {
  const limit = (value: number | undefined, fallback: number, maximum: number) =>
    value === undefined || Number.isNaN(value) ? fallback : Math.max(0, Math.min(maximum, Math.trunc(value)))
  return {
    licenses: limit(options.maxLicensesPages, 50, 200),
    history: limit(options.maxLoadMoreClicks, 100, 500),
  }
}

/** Both page mechanisms use the same priority: exhausted, bounded, stalled, then another step. */
export function steamPageStep(
  steps: number,
  rowsBefore: number,
  rowsAfter: number,
  hasMore: boolean,
  cap: number,
): SteamPageStep {
  if (!hasMore) return 'exhausted'
  if (steps >= cap) return 'cap'
  if (steps > 0 && rowsAfter <= rowsBefore) return 'stalled'
  return 'continue'
}
