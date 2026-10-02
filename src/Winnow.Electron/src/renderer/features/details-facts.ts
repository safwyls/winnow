import type { GameDetails, UpdateEvent } from '../api/types'
import { dateLabel } from '../api/client'
import { readableWebUrl } from '../../shared/external-links'

export interface AcquisitionInput {
  acquiredAt?: string | null
  licenseType?: string | null
}
const licenceLabels: Record<string, string> = {
  steam_store: 'Steam Store',
  complimentary: 'Complimentary',
  gift: 'Gift or guest pass',
  retail: 'Retail key',
}

/** Acquisition carries dates and licence vocabulary; money belongs to account statistics. */
export function acquisitionFacts(ownerships?: AcquisitionInput[] | null) {
  const dates = (ownerships ?? [])
    .map((row) => row.acquiredAt)
    .filter((at): at is string => Boolean(at) && Number.isFinite(Date.parse(at!)))
    .sort((a, b) => Date.parse(a) - Date.parse(b))
  const licence = ownerships?.find((row) => row.licenseType?.trim())?.licenseType
  const licenseText = licenceLabels[licence ?? ''] ?? ''
  if (!dates.length && !licenseText) return null
  return { acquiredAt: dates[0] ?? null, dateText: dates[0] ? dateLabel(dates[0]) : '', licenseText }
}

export function receptionFigures(ratings?: GameDetails['ratings'] | null) {
  const sources = [
    ['igdb_users', 'IGDB USERS', 'IGDB', 'rating'],
    ['igdb_critics', 'IGDB CRITICS', 'IGDB critics', 'critic score'],
    ['steam', 'STEAM', 'Steam', 'review'],
  ] as const
  return sources.flatMap(([id, source, compactSource, noun]) => {
    const rating = ratings?.find((row) => row.source === id)
    if (!rating?.hasFigure || !Number.isFinite(rating.score) || !rating.ratingCount || rating.ratingCount < 1)
      return []
    const score = Math.round(rating.score!)
    const count = `${rating.ratingCount.toLocaleString()} ${noun}${rating.ratingCount === 1 ? '' : 's'}`
    const value = `${score.toLocaleString()}${id === 'steam' ? '%' : ''}`
    const label = id === 'steam' ? rating.label?.trim() || 'Unclassified' : undefined
    const tooltip =
      id === 'steam'
        ? `${label} on Steam: ${value} positive, from ${count}.`
        : `${id === 'igdb_critics' ? 'Critic score aggregated by IGDB' : 'IGDB user rating'}: ${score} out of 100, from ${count}.`
    return [
      { source, compactSource, value, compactValue: label ?? value, count, tooltip, automationName: tooltip },
    ]
  })
}

export interface PlaytimeReading {
  playtimeMinutes: number
  observedAt: string
}
export function playtimeRecordLine(readings?: PlaytimeReading[] | null) {
  const ordered = [...(readings ?? [])].sort((a, b) => Date.parse(a.observedAt) - Date.parse(b.observedAt))
  if (!ordered.length) return ''
  const since = dateLabel(ordered[0].observedAt)
  if (ordered.length === 1) return `Checked once, on ${since}.`
  const gained = ordered.at(-1)!.playtimeMinutes - ordered[0].playtimeMinutes
  return `Checked ${ordered.length.toLocaleString()} times since ${since} — ${gained > 0 ? `up ${durationText(gained)}` : 'no change'}.`
}
export function durationText(minutes: number) {
  const whole = Math.max(0, Math.floor(minutes)),
    hours = Math.floor(whole / 60),
    rest = whole % 60
  return hours ? `${hours}h${rest ? ` ${rest}m` : ''}` : `${whole}m`
}

export function detailPlaytime(minutes: number) {
  return minutes <= 0 ? '—' : minutes < 60 ? `${minutes}m` : `${Math.floor(minutes / 60)}h`
}

export function detailIdle(date: string, now = Date.now()) {
  const days = Math.max(0, (now - Date.parse(date)) / 86_400_000)
  if (!Number.isFinite(days)) return '—'
  if (days < 30) return `${Math.max(1, Math.floor(days))}d`
  const months = Math.floor(days / 30.4375)
  if (months < 12) return `${months}mo`
  const years = Math.floor(months / 12),
    rest = months % 12
  return `${years}y${rest ? ` ${rest}mo` : ''}`
}

export function updateHeadline(event: Pick<UpdateEvent, 'title' | 'buildId' | 'kind'>) {
  return event.title?.trim()
    ? event.title
    : event.buildId
      ? `Build ${event.buildId}`
      : event.kind === 'announcement'
        ? 'Announcement'
        : 'Build pushed'
}

export function updatePageUrl(value?: string | null): string | null {
  return readableWebUrl(value)
}

export interface RefetchResult {
  outcome: string | number
  retryAfter?: string | number | null
}
export function refetchStatus(result: RefetchResult) {
  const outcome =
    typeof result.outcome === 'number'
      ? ['Updated', 'NothingNew', 'NoSourceToAsk', 'NotConfigured', 'Unreachable', 'TooSoon', 'WorkNotFound'][
          result.outcome
        ]
      : result.outcome
  const messages: Record<string, string> = {
    Updated: 'Metadata updated.',
    NothingNew: 'Checked. Nothing new from the sources.',
    NotConfigured: 'IGDB credentials are not set up.',
    Unreachable: 'A source could not be reached.',
    NoSourceToAsk: 'This game has no IGDB id or Steam appid to look up.',
    WorkNotFound: 'This game is no longer in your library.',
  }
  let message = messages[outcome] ?? messages.Unreachable
  if (outcome === 'TooSoon') {
    const parts = typeof result.retryAfter === 'string' ? result.retryAfter.split(':').map(Number) : []
    const seconds = Math.max(
      1,
      typeof result.retryAfter === 'number'
        ? result.retryAfter
        : parts.length === 3
          ? parts[0] * 3600 + parts[1] * 60 + parts[2]
          : 1,
    )
    const count = Math.ceil(seconds < 60 ? seconds : seconds / 60),
      unit = seconds < 60 ? 'second' : 'minute'
    message = `Try again in ${count} ${unit}${count === 1 ? '' : 's'}.`
  }
  return { message, problem: !['Updated', 'NothingNew'].includes(outcome), changed: outcome === 'Updated' }
}
