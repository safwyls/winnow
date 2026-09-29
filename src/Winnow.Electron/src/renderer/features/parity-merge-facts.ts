import { dateLabel, storeLabel } from '../api/client'
import { mergeMinutes, type MergeCard, type MergeRow } from './parity-merge-model'

export function mergePlaytime(row: Pick<MergeRow, 'minutes' | 'pack'>): string {
  return row.minutes <= 0
    ? row.pack
      ? '—'
      : '0h'
    : row.minutes < 60
      ? `${row.minutes}m`
      : `${Math.floor(row.minutes / 60)}h`
}

export function mergeIdle(
  row: Pick<MergeRow, 'lastPlayedAt' | 'minutes' | 'pack'>,
  now = Date.now(),
): string {
  if (!row.lastPlayedAt) return row.pack && row.minutes <= 0 ? '—' : 'never'
  const played = Date.parse(row.lastPlayedAt)
  if (!Number.isFinite(played)) return row.pack && row.minutes <= 0 ? '—' : 'never'
  const days = Math.max(0, (now - played) / 86400000)
  if (days < 30) return `${Math.max(1, Math.floor(days))}d`
  const months = Math.floor(days / 30.4375)
  if (months < 12) return `${months}mo`
  const rest = months % 12
  return `${Math.floor(months / 12)}y${rest ? ` ${rest}mo` : ''}`
}

export function mergeRowDetail(row: MergeRow): string {
  return [
    row.stores.map(storeLabel).join(' / '),
    row.pack && !row.minutes
      ? 'no separate playtime recorded'
      : !row.minutes
        ? 'never opened'
        : `${mergePlaytime(row)}${row.acquiredAt ? ` since ${row.acquiredAt.slice(0, 4)}` : ''}`,
    row.lastPlayedAt
      ? `Last played ${dateLabel(row.lastPlayedAt)}`
      : row.acquiredAt
        ? `Added ${dateLabel(row.acquiredAt)}`
        : '',
    row.installed === true ? 'Installed' : row.installed === false ? 'Not installed' : '',
    row.unread ? 'Patched since you played' : '',
  ]
    .filter(Boolean)
    .join(' · ')
}

export const mergeRowMark = (parent: number, included: number[], row: MergeRow) =>
  row.workId === parent ? 'Header' : included.includes(row.workId) ? 'Nests under' : 'Left out'

export function mergeRollup(card: MergeCard): string {
  const earliest = card.rows
    .map((row) => row.acquiredAt)
    .filter((date): date is string => Boolean(date))
    .sort()[0]
  const unread = card.rows.filter((row) => row.unread).length
  const leftOut = card.rows.length - card.included.length
  return [
    `${mergePlaytime({ minutes: mergeMinutes(card), pack: false })} rolled up`,
    `${card.rows.length} entries`,
    leftOut ? `${leftOut} left out` : '',
    earliest ? `owned since ${earliest.slice(0, 4)}` : '',
    unread ? `${unread} ${unread === 1 ? 'entry' : 'entries'} patched since you played` : '',
  ]
    .filter(Boolean)
    .join(' · ')
}
