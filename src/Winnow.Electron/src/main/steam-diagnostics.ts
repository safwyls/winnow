import { appendFileSync, existsSync, mkdirSync, renameSync, statSync } from 'node:fs'
import { isAbsolute, join } from 'node:path'
import type { SteamSignInResult } from '../shared/bridge'

const outcomes = [
  'SignedIn',
  'NoToken',
  'NotSignedIn',
  'IdentityMismatch',
  'Cancelled',
  'Unavailable',
  'Failed',
]

/** Construct the complete log line from allowed facts; never stringify the provider result. */
export function steamSignInDiagnostic(result: SteamSignInResult): string {
  const outcome =
    result.signedIn === true
      ? 'SignedIn'
      : typeof result.outcome === 'number' && Number.isInteger(result.outcome) && result.outcome > 0
        ? (outcomes[result.outcome] ?? 'Failed')
        : 'Failed'
  const expiry =
    result.expiresAt &&
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(result.expiresAt) &&
    Number.isFinite(Date.parse(result.expiresAt))
      ? new Date(result.expiresAt).toISOString()
      : 'unknown'
  const bytes = (html?: string | null) => (typeof html === 'string' ? Buffer.byteLength(html, 'utf8') : 0)
  const licenses = [result.pages?.licensesHtml, ...(result.pages?.additionalLicensesHtml ?? [])]
  const pageFacts = result.pages
    ? `, licences=${licenses.reduce<number>((sum, html) => sum + bytes(html), 0)} bytes, history=${bytes(result.pages.historyHtml)} bytes`
    : ''
  return `SteamSignInResult(${outcome}, expires=${expiry}, access token ${result.signedIn === true ? 'held' : 'absent'}, refresh token ${result.refreshTokenCaptured === true ? 'held' : 'absent'}${pageFacts})`
}

/** The caller passes only a formatted line and the backend's active data directory. */
export function writeSteamDiagnostic(directory: string, message: string): void {
  try {
    if (!isAbsolute(directory) || !existsSync(directory)) return
    const logs = join(directory, 'logs')
    mkdirSync(logs, { recursive: true })
    const file = join(logs, 'electron-steam.log')
    if (existsSync(file) && statSync(file).size >= 512 * 1024) renameSync(file, `${file}.previous`)
    appendFileSync(file, `${new Date().toISOString()} ${message}\n`, 'utf8')
  } catch {
    // Diagnostics cannot change whether the account connected.
  }
}
