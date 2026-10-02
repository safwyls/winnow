import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { dateLabel, storeLabel } from '../api/client'
import type { GameDetails, LibraryGame, Mode, Session } from '../api/types'
import { useViewState } from '../viewState'
import {
  buildTimeline,
  timelineDateLabel,
  timelineMarks,
  type PlaytimeSnapshot,
  type TimelineSeries,
  type TimelineUpdate,
} from './activity-timeline-model'
import './activity.css'
import { updateFlagState } from './update-flags'
import { durationText } from './details-facts'

export function TimelinePlot({
  series,
  updates,
  tracked,
  onSelect,
  onTracked,
}: {
  series: TimelineSeries
  updates: TimelineUpdate[]
  tracked: boolean
  onSelect: (text: string, switchToTracked?: boolean) => void
  onTracked: () => void
}) {
  const element = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(600)
  useEffect(() => {
    if (!element.current || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver((entries) => {
      if (entries[0]?.contentRect.width) setWidth(entries[0].contentRect.width)
    })
    observer.observe(element.current)
    return () => observer.disconnect()
  }, [])
  const marks = useMemo(
    () => timelineMarks(series, updates, width, tracked),
    [series, updates, width, tracked],
  )
  const maximum = Math.max(1, ...series.bars.map((bar) => bar.hours))
  const span = Math.max(1, series.end - series.start)
  return (
    <div ref={element} className="activity-timeline-plot" role="group" aria-label={series.periodLabel}>
      {series.end > series.start && (
        <>
          <div className="activity-timeline-grid" aria-hidden="true" />
          {series.lastPlayed != null &&
            series.lastPlayed >= series.start &&
            series.lastPlayed <= series.end && (
              <span
                className="activity-timeline-last-played"
                aria-hidden="true"
                style={{
                  left: `${((12 + ((series.lastPlayed - series.start) / span) * (width - 24)) / width) * 100}%`,
                }}
              />
            )}
          {marks.bars.map((mark) => (
            <button
              key={mark.key}
              className={`activity-timeline-bar ${mark.tracked ? 'tracked' : 'monthly'}`}
              aria-label={mark.label}
              title={mark.label}
              style={{ left: `${(mark.left / width) * 100}%`, width: `${mark.width}px` }}
              onFocus={() => onSelect(mark.label)}
              onClick={() => onSelect(mark.label)}
            >
              <span style={{ height: `${Math.max(2, (mark.hours / maximum) * 100)}%` }} />
              {mark.count > 1 && <small aria-hidden="true">{mark.count}</small>}
            </button>
          ))}
          {marks.updates.map((mark) => (
            <button
              key={mark.key}
              className="activity-timeline-update"
              data-unread={mark.unread > 0}
              aria-label={mark.label}
              title={mark.label}
              style={{ left: `${(mark.left / width) * 100}%` }}
              onFocus={() => onSelect(mark.label)}
              onClick={() => {
                if (mark.count > 1 && !tracked) onTracked()
                onSelect(mark.label, mark.count > 1 && !tracked)
              }}
            >
              <span aria-hidden="true">{mark.count > 1 ? mark.count : '•'}</span>
            </button>
          ))}
          <div className="activity-timeline-dates">
            <span>{timelineDateLabel(series.start)}</span>
            <span>Today</span>
          </div>
          <div className="activity-timeline-coverage" role="img" aria-label="Monthly record coverage">
            {series.coverage.map((range) => (
              <span
                key={range.start}
                style={{
                  left: `${Math.max(0, ((range.start - series.start) / span) * 100)}%`,
                  width: `${Math.min(100, ((range.end - range.start) / span) * 100)}%`,
                }}
              />
            ))}
          </div>
        </>
      )}
    </div>
  )
}

export function ActivityTracker({
  snapshots,
  sessions,
  acquiredAt,
  lastPlayedAt,
  totalMinutes,
  now,
  scopeLabel = '',
  scopeKey,
  rangeKey,
  updates,
  unreadCount,
  children,
  mode = 'desktop',
}: {
  snapshots: PlaytimeSnapshot[]
  sessions: Session[]
  acquiredAt?: string | null
  lastPlayedAt?: string | null
  totalMinutes: number
  now: number
  scopeLabel?: string
  scopeKey: string
  rangeKey: string
  updates: TimelineUpdate[]
  unreadCount?: number
  children?: ReactNode
  mode?: Mode
}) {
  const [tracked, setTracked] = useViewState(rangeKey, false)
  const [selected, setSelected] = useState<{ scope: string; text: string } | null>(null)
  const [recordsOpen, setRecordsOpen] = useState(false)
  const lifetime = useMemo(
    () => buildTimeline(snapshots, sessions, acquiredAt, lastPlayedAt, now),
    [snapshots, sessions, acquiredAt, lastPlayedAt, now],
  )
  const trackedSeries = useMemo(
    () => buildTimeline(snapshots, sessions, acquiredAt, lastPlayedAt, now, true),
    [snapshots, sessions, acquiredAt, lastPlayedAt, now],
  )
  const series = tracked ? trackedSeries : lifetime
  const hasTracked = trackedSeries.bars.length > 0
  const unread = unreadCount ?? updates.filter((update) => update.unread).length
  const scope = `${scopeKey}:${tracked}`
  return (
    <section className={`feature-panel activity-tracker mode-${mode}`} aria-label="Your play history">
      <header className="feature-heading">
        <div>
          <h2>Your play</h2>
          <p>
            <strong className="activity-total">{durationText(totalMinutes)}</strong>{' '}
            {scopeLabel ? `played · ${scopeLabel}` : 'total played'}
          </p>
          <p>
            {lastPlayedAt
              ? `Last played ${dateLabel(lastPlayedAt)}`
              : totalMinutes > 0
                ? 'Last-played date unavailable'
                : 'No playtime recorded'}
          </p>
        </div>
        <nav className="tabs" aria-label="Play history range">
          <button aria-pressed={!tracked} onClick={() => setTracked(false)}>
            Lifetime
          </button>
          <button aria-pressed={tracked} onClick={() => setTracked(true)}>
            Tracked sessions
          </button>
        </nav>
      </header>
      {children}
      {series.end > series.start && (
        <>
          <p>
            {series.periodLabel} ·{' '}
            {series.bars.length
              ? `${Math.max(...series.bars.map((bar) => bar.hours)).toLocaleString(undefined, { maximumFractionDigits: 2 })}h tallest bar`
              : 'No recorded hours in this view'}
          </p>
          <TimelinePlot
            series={series}
            updates={updates}
            tracked={tracked}
            onSelect={(text, switchToTracked) =>
              setSelected({
                scope: `${scopeKey}:${tracked || (switchToTracked && hasTracked) || false}`,
                text,
              })
            }
            onTracked={() => {
              if (hasTracked) setTracked(true)
            }}
          />
          <p className="muted">
            Store monthly history · Winnow {tracked ? 'sessions' : 'monthly totals'} · Unread updates · ╱╱ No
            monthly record
          </p>
        </>
      )}
      <p className="activity-timeline-detail" role="status">
        {selected?.scope === scope ? selected.text : series.summary}
      </p>
      <p className="muted">{series.coverageNote}</p>
      <p className="activity-update-summary">
        {unread > 0
          ? `${unread} unread update${unread === 1 ? '' : 's'}`
          : updates.length
            ? 'No unread updates'
            : 'No updates recorded'}
      </p>
      {!!series.bars.length && (
        <details open={recordsOpen} onToggle={(event) => setRecordsOpen(event.currentTarget.open)}>
          <summary
            onClick={(event) => {
              event.preventDefault()
              setRecordsOpen((open) => !open)
            }}
          >
            Recorded hours
          </summary>
          {recordsOpen && (
            <ul className="activity-records">
              {series.bars.map((bar) => (
                <li key={`${bar.start}:${bar.end}`}>
                  <button onClick={() => setSelected({ scope, text: bar.label })}>{bar.label}</button>
                </li>
              ))}
            </ul>
          )}
        </details>
      )}
    </section>
  )
}

const noSnapshots: PlaytimeSnapshot[] = []
const noSessions: Session[] = []

export function ActivityTimeline({
  game,
  details,
  mode = 'desktop',
}: {
  game: LibraryGame
  details: GameDetails
  mode?: Mode
}) {
  const [ownership, setOwnership] = useViewState(
    `${mode}:timeline:${game.workId}:ownership`,
    game.entries[0]?.ownershipId,
  )
  const entry = game.entries.find((value) => value.ownershipId === ownership) ?? game.entries[0]
  const flags = useMemo(
    () =>
      entry
        ? updateFlagState(
            details.events.filter((event) => event.releaseId === entry.releaseId),
            (details.acknowledgements as Record<string, string> | undefined) ?? {},
            entry.lastPlayedAt,
            entry.playtimeMinutes,
          )
        : null,
    [details.events, details.acknowledgements, entry],
  )
  if (!entry || !flags) return null
  const history = details.history as Record<string, PlaytimeSnapshot[]> | undefined
  const ownerships = details.ownerships as { id: number; acquiredAt?: string | null }[] | undefined
  return (
    <ActivityTracker
      snapshots={history?.[entry.ownershipId] ?? noSnapshots}
      sessions={details.sessions[entry.ownershipId] ?? noSessions}
      acquiredAt={ownerships?.find((value) => value.id === entry.ownershipId)?.acquiredAt}
      lastPlayedAt={entry.lastPlayedAt}
      totalMinutes={entry.playtimeMinutes}
      now={Date.parse(details.readAtUtc)}
      scopeLabel={`${storeLabel(entry.store)} copy`}
      scopeKey={String(entry.ownershipId)}
      rangeKey={`${mode}:timeline:${game.workId}:tracked`}
      updates={flags.rows}
      unreadCount={flags.unread}
      mode={mode}
    >
      {game.entries.length > 1 && (
        <label className="field">
          Edition history
          <select value={entry.ownershipId} onChange={(event) => setOwnership(Number(event.target.value))}>
            {game.entries.map((value) => (
              <option key={value.ownershipId} value={value.ownershipId}>
                {storeLabel(value.store)} · {value.title}
              </option>
            ))}
          </select>
        </label>
      )}
    </ActivityTracker>
  )
}
