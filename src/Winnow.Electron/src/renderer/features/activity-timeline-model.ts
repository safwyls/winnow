import type { Session, UpdateEvent } from '../api/types'

export interface PlaytimeSnapshot {
  ownershipId: number
  observedAt: string
  playtimeMinutes: number
}
export interface TimelineBar {
  start: number
  end: number
  hours: number
  tracked: boolean
  label: string
}
export interface TimelineSeries {
  start: number
  end: number
  bars: TimelineBar[]
  coverage: { start: number; end: number }[]
  lastPlayed: number | null
  summary: string
  coverageNote: string
  periodLabel: string
}
const day = 86400000
const monthStart = (time: number) => {
  const date = new Date(time)
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1)
}
const nextMonth = (time: number) => {
  const date = new Date(time)
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 1)
}
const monthLabel = (time: number) =>
  new Date(time).toLocaleDateString(undefined, { month: 'short', year: 'numeric', timeZone: 'UTC' })
const dateText = (time: number) =>
  new Date(time).toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  })
const hoursText = (hours: number) => `${hours.toLocaleString(undefined, { maximumFractionDigits: 2 })}h`

/** Cumulative counters only describe a month when both adjacent month-end readings are usable. */
export function buildTimeline(
  snapshots: PlaytimeSnapshot[],
  sessions: Session[],
  acquiredAt: string | null | undefined,
  lastPlayedAt: string | null | undefined,
  now: number,
  tracked = false,
): TimelineSeries {
  const validDate = (value: string | null | undefined) => {
    const time = value ? Date.parse(value) : NaN
    return Number.isFinite(time) && time > -62135596800000 && time <= now ? time : null
  }
  const acquired = validDate(acquiredAt)
  const lastPlayed = validDate(lastPlayedAt)
  const valid = [
    ...new Map(
      sessions
        .filter((session) => {
          const start = Date.parse(session.startedAt),
            end = Date.parse(session.endedAt ?? '')
          return (
            start > -62135596800000 &&
            end <= now &&
            end > start &&
            (session.durationSeconds ?? 0) > 0 &&
            session.durationSeconds! <= (end - start) / 1000 + 1
          )
        })
        .map((session) => [
          `${session.ownershipId}:${session.startedAt}:${session.endedAt}:${session.durationSeconds}`,
          session,
        ]),
    ).values(),
  ].sort((a, b) => Date.parse(a.startedAt) - Date.parse(b.startedAt))
  const monthly = new Map<number, TimelineBar>()
  const coverage: TimelineSeries['coverage'] = []
  const readings = snapshots
    .filter((value) => validDate(value.observedAt) != null)
    .sort((a, b) => Date.parse(a.observedAt) - Date.parse(b.observedAt))
  let baseline = ''
  if (new Set(readings.map((value) => value.ownershipId)).size === 1) {
    const endpoints = new Map<number, Set<number>>()
    for (const reading of readings) {
      const at = Date.parse(reading.observedAt)
      if (at !== nextMonth(at) - 1000) continue
      if (!endpoints.has(at)) endpoints.set(at, new Set())
      endpoints.get(at)!.add(reading.playtimeMinutes)
    }
    const points = [...endpoints].map(([at, values]) => ({ at, values: [...values] }))
    if (points.length && points.every((point) => point.values.length === 1 && point.values[0]! >= 0))
      baseline = `${hoursText(points[0]!.values[0]! / 60)} recorded by ${monthLabel(points[0]!.at)}`
    for (let i = 1; i < points.length; i++) {
      const previous = points[i - 1]!,
        current = points[i]!,
        month = monthStart(current.at)
      if (
        nextMonth(previous.at) !== month ||
        previous.values.length !== 1 ||
        current.values.length !== 1 ||
        previous.values[0]! < 0 ||
        current.values[0]! < previous.values[0]!
      )
        continue
      const between = readings.filter(
        (value) => Date.parse(value.observedAt) >= previous.at && Date.parse(value.observedAt) <= current.at,
      )
      if (
        between.some(
          (value, index) =>
            value.playtimeMinutes < previous.values[0]! ||
            value.playtimeMinutes > current.values[0]! ||
            (index > 0 && value.playtimeMinutes < between[index - 1]!.playtimeMinutes),
        )
      )
        continue
      const hours = (current.values[0]! - previous.values[0]!) / 60
      monthly.set(month, {
        start: month,
        end: nextMonth(month),
        hours,
        tracked: false,
        label: `${monthLabel(month)} · ${hoursText(hours)} · monthly history`,
      })
      coverage.push({ start: month, end: nextMonth(month) })
    }
  }
  const trackedMonths = new Map<number, number>()
  for (const session of valid) {
    const start = Date.parse(session.startedAt),
      end = Date.parse(session.endedAt!)
    for (let cursor = start; cursor < end;) {
      const month = monthStart(cursor),
        next = Math.min(nextMonth(cursor), end)
      trackedMonths.set(
        month,
        (trackedMonths.get(month) ?? 0) +
          ((session.durationSeconds! / 3600) * (next - cursor)) / (end - start),
      )
      cursor = next
    }
  }
  for (const [month, hours] of trackedMonths)
    if (!monthly.has(month))
      monthly.set(month, {
        start: month,
        end: Math.min(nextMonth(month), now),
        hours,
        tracked: true,
        label: `${monthLabel(month)} · ${hoursText(hours)} · Winnow sessions`,
      })
  const bars = tracked
    ? valid.map((session) => ({
        start: Date.parse(session.startedAt),
        end: Date.parse(session.endedAt!),
        hours: session.durationSeconds! / 3600,
        tracked: true,
        label: `${dateText(Date.parse(session.startedAt))} ${new Date(session.startedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', timeZone: 'UTC' })} UTC · ${hoursText(session.durationSeconds! / 3600)} · Winnow session`,
      }))
    : [...monthly.values()].sort((a, b) => a.start - b.start)
  const evidence = [
    acquired,
    lastPlayed,
    readings.length ? Date.parse(readings[0]!.observedAt) : null,
    bars[0]?.start ?? null,
  ].filter((value): value is number => value != null)
  return {
    start: tracked
      ? bars.length
        ? Math.min(bars[0]!.start, now - 30 * day)
        : now
      : evidence.length
        ? Math.min(...evidence)
        : now,
    end: now,
    bars,
    coverage: tracked ? [] : coverage,
    lastPlayed,
    summary: tracked
      ? bars.length
        ? `${bars.length} observed ${bars.length === 1 ? 'session' : 'sessions'} · ${dateText(bars[0]!.start)}–${dateText(bars.at(-1)!.end)}`
        : 'No completed sessions recorded yet'
      : [acquired != null ? `Acquired · ${dateText(acquired)}` : 'Acquisition date unavailable', baseline]
          .filter(Boolean)
          .join(' · '),
    coverageNote: tracked
      ? 'Only sessions observed by Winnow are shown.'
      : 'Gaps may have no records. Winnow months include observed sessions only.',
    periodLabel: tracked ? 'Hours per session' : 'Hours per month',
  }
}

export interface TimelineUpdate extends UpdateEvent {
  unread: boolean
}
export function timelineUpdates(
  events: UpdateEvent[],
  acknowledgements: Record<string, string>,
  lastPlayedAt: string | null | undefined,
  playtimeMinutes: number,
): TimelineUpdate[] {
  const sincePlay = (event: UpdateEvent) =>
    lastPlayedAt ? Date.parse(event.occurredAt) > Date.parse(lastPlayedAt) : playtimeMinutes > 0
  const correlates = (a: UpdateEvent, b: UpdateEvent) =>
    a.releaseId === b.releaseId && Math.abs(Date.parse(a.occurredAt) - Date.parse(b.occurredAt)) <= 7 * day
  const pushes = events.filter(
    (event) =>
      event.kind === 'build_push' &&
      events.some((news) => news.kind === 'announcement' && correlates(event, news)),
  )
  return events.map((event) => ({
    ...event,
    unread:
      sincePlay(event) &&
      pushes.some(
        (push) =>
          push.releaseId === event.releaseId &&
          sincePlay(push) &&
          (event.kind === 'announcement'
            ? correlates(push, event)
            : Date.parse(push.occurredAt) === Date.parse(event.occurredAt)) &&
          (!acknowledgements[String(push.releaseId)] ||
            Date.parse(push.occurredAt) > Date.parse(acknowledgements[String(push.releaseId)]!)),
      ),
  }))
}

export function timelineMarks(
  series: TimelineSeries,
  updates: TimelineUpdate[],
  width: number,
  tracked: boolean,
) {
  const usable = Math.max(0, width - 24),
    span = Math.max(1, series.end - series.start)
  const x = (at: number) => 12 + Math.max(0, Math.min(1, (at - series.start) / span)) * usable
  const monthWidth = Math.min(usable, Math.max(1, ((28 * day) / span) * usable - 1))
  const groups: TimelineBar[][] = []
  for (const bar of series.bars.filter(
    (bar) => bar.hours > 0 && bar.end >= series.start && bar.start <= series.end,
  )) {
    const previous = groups.at(-1)
    if (tracked && previous && x(bar.start) - x(previous[0]!.start) < 12) previous.push(bar)
    else groups.push([bar])
  }
  const bars = groups.map((group) => {
    const first = group[0]!,
      longest = Math.max(...group.map((bar) => bar.hours)),
      barWidth = tracked ? Math.min(10, usable) : monthWidth
    return {
      key: `${first.start}:${first.end}`,
      left: Math.max(
        12,
        Math.min(
          width - 12 - barWidth,
          x(tracked ? first.start : (first.start + first.end) / 2) - barWidth / 2,
        ),
      ),
      width: barWidth,
      hours: longest,
      count: group.length,
      tracked: first.tracked,
      label:
        group.length === 1
          ? first.label
          : `${group.length} observed sessions · ${hoursText(group.reduce((sum, bar) => sum + bar.hours, 0))} total · longest ${hoursText(longest)} · ${dateText(first.start)}–${dateText(group.at(-1)!.end)}`,
    }
  })
  const updateGroups: TimelineUpdate[][] = []
  for (const update of updates
    .filter(
      (update) =>
        Date.parse(update.occurredAt) >= series.start && Date.parse(update.occurredAt) <= series.end,
    )
    .sort((a, b) => Date.parse(a.occurredAt) - Date.parse(b.occurredAt))) {
    const previous = updateGroups.at(-1)
    if (previous && x(Date.parse(update.occurredAt)) - x(Date.parse(previous.at(-1)!.occurredAt)) < 26)
      previous.push(update)
    else updateGroups.push([update])
  }
  return {
    bars,
    updates: updateGroups.map((group) => ({
      key: group.map((item) => item.id).join(','),
      count: group.length,
      unread: group.filter((item) => item.unread).length,
      left: Math.max(
        0,
        Math.min(
          width - 24,
          x((Date.parse(group[0]!.occurredAt) + Date.parse(group.at(-1)!.occurredAt)) / 2) - 12,
        ),
      ),
      label: `${group.length} ${group.length === 1 ? 'update' : 'updates'} · ${group.filter((item) => item.unread).length} unread\n${group.map((item) => `${dateText(Date.parse(item.occurredAt))} · ${item.title ?? item.kind}${item.unread ? ' · unread' : ''}`).join('\n')}`,
    })),
  }
}
