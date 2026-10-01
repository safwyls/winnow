import type { InfiniteData, QueryClient } from '@tanstack/react-query'
import type { ActivityPage, ActivityRow, GameDetails, JournalResponse, LibraryResponse } from '../api/types'

export function activityKey(row: ActivityRow): string {
  return row.session || row.note
    ? `session:${row.session?.id ?? row.note!.sessionId}`
    : `update:${row.ownershipId}:${row.update?.id}`
}

export function uniqueActivity(rows: ActivityRow[]): ActivityRow[] {
  return [...new Map(rows.map((row) => [activityKey(row), row])).values()]
}

export function localDate(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

export function mondayWeek(offset = 0, now = new Date()) {
  const from = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  from.setDate(from.getDate() - ((from.getDay() + 6) % 7) + Math.min(0, offset) * 7)
  const until = new Date(from)
  until.setDate(until.getDate() + 7)
  return { fromUtc: from.toISOString(), untilUtc: until.toISOString() }
}

export function gameplayRange(period: string, customFrom: string, customUntil: string, now = new Date()) {
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const parse = (value: string) => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null
    const result = new Date(`${value}T00:00:00`)
    return Number.isFinite(result.getTime()) && localDate(result) === value ? result : null
  }
  const from = period === 'custom' ? parse(customFrom) : new Date(today)
  const end = period === 'custom' ? parse(customUntil) : new Date(today)
  if (!from || !end) throw new Error('Enter both dates as YYYY-MM-DD.')
  if (period !== 'custom') from.setDate(from.getDate() - (period === '90' ? 89 : 29))
  if (from > end) throw new Error('The start date must be on or before the end date.')
  if (end > today) throw new Error('Choose an end date no later than today.')
  const days =
    Math.round(
      (Date.UTC(end.getFullYear(), end.getMonth(), end.getDate()) -
        Date.UTC(from.getFullYear(), from.getMonth(), from.getDate())) /
        86400000,
    ) + 1
  if (days - 1 > 3660 || from.getFullYear() < 1900)
    throw new Error('Choose a period of up to ten years, starting in 1900 or later.')
  const until = new Date(end)
  until.setDate(until.getDate() + 1)
  const step = days <= 90 ? 7 : Math.max(7, Math.ceil(days / 26))
  const timeBins: { fromUtc: string; untilUtc: string }[] = []
  for (let cursor = new Date(from); cursor < until;) {
    const next = new Date(cursor)
    next.setDate(next.getDate() + step)
    if (next > until) next.setTime(until.getTime())
    timeBins.push({ fromUtc: cursor.toISOString(), untilUtc: next.toISOString() })
    cursor = next
  }
  const asOf = new Date(now)
  asOf.setMilliseconds(0)
  return { fromUtc: from.toISOString(), untilUtc: until.toISOString(), asOfUtc: asOf.toISOString(), timeBins }
}

/** A note mutation updates loaded pages in place; an older selection must not send the reader back to page one. */
export function patchJournalCaches(client: QueryClient, sessionId: number, saved: JournalResponse | null) {
  const note =
    saved && (saved.note || saved.rating) ? { sessionId, note: saved.note, rating: saved.rating } : null
  const activities = client.getQueriesData<InfiniteData<ActivityPage>>({
    queryKey: ['api', 'activity.query'],
  })
  let source = activities
    .flatMap(([, data]) => data?.pages.flatMap((page) => page.rows) ?? [])
    .find((row) => row.session?.id === sessionId)
  const library = client.getQueryData<LibraryResponse>(['api', 'library.get'])
  if (!source) {
    const session = client
      .getQueriesData<GameDetails>({ queryKey: ['api', 'game.details'] })
      .flatMap(([, details]) => Object.values(details?.sessions ?? {}).flat())
      .find((session) => session.id === sessionId)
    const entry =
      session &&
      library?.games
        .flatMap((game) => game.entries)
        .find((entry) => entry.ownershipId === session.ownershipId)
    if (session && entry)
      source = { session, ownershipId: entry.ownershipId, store: entry.store, atUtc: session.startedAt }
  }
  for (const [key, data] of activities) {
    if (!data) continue
    const journalOnly = key[4] === 2
    let found = false
    const pages = data.pages.map((page) => ({
      ...page,
      rows: page.rows.flatMap((row) => {
        if ((row.session?.id ?? row.note?.sessionId) !== sessionId) return [row]
        found = true
        return journalOnly && !note ? [] : [{ ...row, note }]
      }),
    }))
    if (
      journalOnly &&
      note &&
      source &&
      !found &&
      pages.length &&
      Date.parse(source.atUtc) >= Date.parse(String(key[2])) &&
      Date.parse(source.atUtc) < Date.parse(String(key[3]))
    ) {
      const workId = key[5]
      if (
        workId == null ||
        library?.games.some(
          (game) =>
            game.workId === workId && game.entries.some((entry) => entry.ownershipId === source.ownershipId),
        )
      ) {
        pages[0] = {
          ...pages[0]!,
          rows: [...pages[0]!.rows, { ...source, note }].sort((a, b) => b.atUtc.localeCompare(a.atUtc)),
        }
      }
    }
    client.setQueryData(key, { ...data, pages })
  }
  if (saved) client.setQueryData(['api', 'journal.get', { sessionId }], saved)
  else void client.invalidateQueries({ queryKey: ['api', 'journal.get', { sessionId }], refetchType: 'none' })
  client.setQueriesData<GameDetails>({ queryKey: ['api', 'game.details'] }, (details) => {
    if (!details) return details
    const session = Object.values(details.sessions)
      .flat()
      .find((value) => value.id === sessionId)
    const previous = details.journalEntries.find((value) => value.sessionId === sessionId)
    if (!session && !previous) return details
    const entries = details.journalEntries.filter((value) => value.sessionId !== sessionId)
    if (note)
      entries.push({
        ...note,
        ownershipId: session?.ownershipId ?? previous!.ownershipId,
        sessionAt: session?.startedAt ?? previous!.sessionAt,
      })
    return { ...details, journalEntries: entries.sort((a, b) => b.sessionAt.localeCompare(a.sessionAt)) }
  })
}
