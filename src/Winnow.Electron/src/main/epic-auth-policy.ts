import { createHash, timingSafeEqual } from 'node:crypto'
import type { EpicAuthChallenge, EpicPromptRequest } from '../shared/epic'
import { readableWebUrl } from './security'

export const EpicStrategy = { bridge: 1, redirect: 2, body: 4, harvest: 8 } as const
function url(value: unknown): URL | null {
  if (typeof value !== 'string' || value.length > 16384 || /[\u0000-\u0020\u007f]/.test(value)) return null
  try {
    const result = new URL(value)
    return result.protocol === 'https:' && !result.username && !result.password ? result : null
  } catch {
    return null
  }
}
export function epicOrigin(value: unknown): string | null {
  const parsed = url(value)
  return parsed ? `${parsed.protocol}//${parsed.hostname.toLowerCase()}:${parsed.port || '443'}` : null
}
export function validEpicCode(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.trim().length > 0 &&
    value.length <= 4096 &&
    !/[\u0000-\u001f\u007f]/.test(value)
  )
}
export type EpicBody = { outcome: 'other' | 'no-session' } | { outcome: 'code'; code: string; kind: 0 | 1 }
export function readEpicBody(body: unknown, fields: EpicPromptRequest['jsonCodeFields']): EpicBody {
  if (typeof body !== 'string' || body.length > 1024 * 1024 || !fields.length) return { outcome: 'other' }
  try {
    const value = JSON.parse(body)
    if (!value || typeof value !== 'object' || Array.isArray(value)) return { outcome: 'other' }
    let recognised = false
    for (const field of fields) {
      if (!Object.hasOwn(value, field.fieldName)) continue
      recognised = true
      if (validEpicCode(value[field.fieldName])) {
        const captured: EpicBody = { outcome: 'code', code: value[field.fieldName], kind: field.kind }
        // A code is an account credential, including when a future caller formats a reading for diagnostics.
        return Object.defineProperties(captured, {
          toString: { value: () => `EpicBody(${field.kind}, value redacted)` },
          toJSON: { value: () => ({ outcome: 'code', kind: field.kind, code: '[redacted]' }) },
        })
      }
    }
    return { outcome: recognised ? 'no-session' : 'other' }
  } catch {
    return { outcome: 'other' }
  }
}
export class EpicAuthPolicy {
  readonly trusted: string[]
  readonly navigable: Set<string>
  constructor(readonly request: EpicPromptRequest) {
    this.trusted = [
      ...new Set(
        [request.startUrl, request.harvestUrl, request.redirectUrl]
          .map(epicOrigin)
          .filter((x): x is string => !!x),
      ),
    ]
    this.navigable = new Set([
      ...this.trusted,
      ...request.additionalNavigableOrigins.map(epicOrigin).filter((x): x is string => !!x),
    ])
  }
  has(strategy: number) {
    return (this.request.strategies & strategy) !== 0
  }
  trusts(address: string) {
    const origin = epicOrigin(address)
    return !!origin && this.trusted.includes(origin)
  }
  redirect(address: string) {
    const target = url(this.request.redirectUrl),
      candidate = url(address)
    return (
      !!target &&
      !!candidate &&
      epicOrigin(address) === epicOrigin(this.request.redirectUrl) &&
      candidate.pathname.replace(/\/+$/, '').toLowerCase() ===
        target.pathname.replace(/\/+$/, '').toLowerCase()
    )
  }
  state(address: string): 'matched' | 'missing' | 'mismatch' | 'not-required' {
    const expected = this.request.expectedState
    if (!expected?.trim()) return 'not-required'
    const actual = this.parameter(address, this.request.stateParameter)
    if (!actual) return 'missing'
    const a = createHash('sha256').update(actual).digest(),
      b = createHash('sha256').update(expected).digest()
    return timingSafeEqual(a, b) ? 'matched' : 'mismatch'
  }
  parameter(address: string, key: string): string | null {
    const parsed = url(address)
    if (!parsed) return null
    // OAuth values are opaque; match the original percent decoding without turning '+' into a space.
    for (const pair of parsed.search.slice(1).split('&')) {
      const split = pair.indexOf('=')
      if (split < 0) continue
      try {
        if (decodeURIComponent(pair.slice(0, split)) === key) {
          const value = decodeURIComponent(pair.slice(split + 1))
          return value.trim() ? value : null
        }
      } catch {
        return null
      }
    }
    return null
  }
  navigation(address: string): 'allow' | 'redirect' | 'block' {
    if (this.has(EpicStrategy.redirect) && this.redirect(address)) return 'redirect'
    if (address === 'about:blank') return 'allow'
    const origin = epicOrigin(address)
    // The registered loopback redirect is intercepted, never rendered or sent to a listener.
    return !!origin && this.navigable.has(origin) && !!readableWebUrl(address) ? 'allow' : 'block'
  }
  popup(address: string): 'allow' | 'redirect' | 'external' | 'block' {
    const decision = this.navigation(address)
    if (decision !== 'block') return decision
    return readableWebUrl(address) ? 'external' : 'block'
  }
  frame(address: string, main: boolean) {
    if (main) return this.navigation(address) !== 'block'
    return (
      address === 'about:blank' ||
      address === 'about:srcdoc' ||
      (!!epicOrigin(address) && !!readableWebUrl(address))
    )
  }
  leftJourney(address: string) {
    const current = url(address),
      start = url(this.request.startUrl)
    return (
      !!current &&
      !!start &&
      this.trusts(address) &&
      current.hostname === start.hostname &&
      current.pathname.split('/')[1]?.toLowerCase() !== start.pathname.split('/')[1]?.toLowerCase()
    )
  }
}
export function validateEpicChallenge(value: unknown, now = Date.now()): EpicAuthChallenge {
  const challenge = value as EpicAuthChallenge,
    r = challenge?.request
  if (
    !challenge ||
    !/^[a-f0-9]{32}$/.test(challenge.attemptId) ||
    !Number.isFinite(Date.parse(challenge.expiresAt)) ||
    Date.parse(challenge.expiresAt) <= now ||
    Date.parse(challenge.expiresAt) > now + 15 * 60_000 ||
    !r ||
    !epicOrigin(r.startUrl) ||
    !readableWebUrl(r.startUrl) ||
    (r.harvestUrl && (!epicOrigin(r.harvestUrl) || !readableWebUrl(r.harvestUrl))) ||
    (r.redirectUrl && !epicOrigin(r.redirectUrl)) ||
    typeof r.consentNotice !== 'string' ||
    !r.consentNotice.trim() ||
    r.consentNotice.length > 20000 ||
    !Array.isArray(r.additionalNavigableOrigins) ||
    r.additionalNavigableOrigins.length > 64 ||
    !r.additionalNavigableOrigins.every((x) => typeof x === 'string' && x.length < 16384) ||
    !Array.isArray(r.jsonCodeFields) ||
    r.jsonCodeFields.length > 16 ||
    !r.jsonCodeFields.every(
      (x) => x && /^[a-zA-Z][\w-]{0,100}$/.test(x.fieldName) && (x.kind === 0 || x.kind === 1),
    ) ||
    !Number.isInteger(r.strategies) ||
    r.strategies < 0 ||
    r.strategies > 15 ||
    typeof r.redirectCodeParameter !== 'string' ||
    !/^[\w-]{1,100}$/.test(r.redirectCodeParameter) ||
    typeof r.stateParameter !== 'string' ||
    !/^[\w-]{1,100}$/.test(r.stateParameter) ||
    (r.expectedState != null && (typeof r.expectedState !== 'string' || r.expectedState.length > 4096))
  )
    throw new Error('Epic returned an invalid sign-in request. Try again.')
  return challenge
}
