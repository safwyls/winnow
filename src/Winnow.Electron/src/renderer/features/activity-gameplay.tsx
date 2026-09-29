import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { dateLabel, hours, request, storeLabel } from '../api/client'
import { useLibrary } from '../api/hooks'
import type { GameplayStats, Mode } from '../api/types'
import { useViewState } from '../viewState'
import { gameplayRange, localDate } from './activity-model'
import { AccountStatistics } from './Accounts'
import { Empty } from './shared'
import './activity.css'

interface CompleteGameplayStats extends GameplayStats {
  sessionLengths?: { minimumSeconds: number; maximumSeconds?: number | null; count: number }[]
}
interface Bar {
  key: string
  label: string
  value: number
  text: string
  open?: () => void
}

function Chart({ title, bars, empty }: { title: string; bars: Bar[]; empty: string }) {
  const [selected, setSelected] = useState<string | null>(null)
  const maximum = Math.max(1, ...bars.map((bar) => bar.value))
  const chosen = bars.find((bar) => bar.key === selected)
  return (
    <section className="feature-panel activity-chart" aria-label={title}>
      <h3>{title}</h3>
      {!bars.length ? (
        <Empty>{empty}</Empty>
      ) : (
        <ol>
          {bars.map((bar) => (
            <li key={bar.key}>
              <button aria-pressed={selected === bar.key} onClick={() => setSelected(bar.key)}>
                <span className="activity-bar-label">{bar.label}</span>
                <strong>{bar.text}</strong>
                <span aria-hidden="true" className="activity-bar-track">
                  <span style={{ width: `${(bar.value / maximum) * 100}%` }} />
                </span>
              </button>
            </li>
          ))}
        </ol>
      )}
      {chosen && (
        <div role="status" className="activity-chart-selection">
          <p>
            {chosen.label}: {chosen.text}
          </p>
          {chosen.open && <button onClick={chosen.open}>Open game</button>}
        </div>
      )}
    </section>
  )
}

export function GameplayDashboard({
  mode = 'desktop',
  onOpenGame,
}: {
  mode?: Mode
  onOpenGame?: (id: number) => void
}) {
  const [section, setSection] = useViewState(`${mode}:stats:section`, 'gameplay')
  return (
    <section className={`gameplay-dashboard mode-${mode}`} aria-label="Library summary">
      <nav className="tabs" aria-label="Library summary section">
        {['gameplay', 'spending'].map((value) => (
          <button
            data-controller-tab
            key={value}
            aria-pressed={section === value}
            onClick={() => setSection(value)}
          >
            {value === 'gameplay' ? 'Gameplay' : 'Spending'}
          </button>
        ))}
      </nav>
      {section === 'spending' ? <AccountStatistics /> : <Gameplay mode={mode} onOpenGame={onOpenGame} />}
    </section>
  )
}

function Gameplay({ mode, onOpenGame }: { mode: Mode; onOpenGame?: (id: number) => void }) {
  const library = useLibrary()
  const [period, setPeriod] = useViewState(`${mode}:stats:period`, '30')
  const [store, setStore] = useViewState(`${mode}:stats:store`, '')
  const [from, setFrom] = useViewState(
    `${mode}:stats:from`,
    localDate(new Date(new Date().setDate(new Date().getDate() - 29))),
  )
  const [until, setUntil] = useViewState(`${mode}:stats:until`, localDate(new Date()))
  const [applied, setApplied] = useViewState(`${mode}:stats:applied`, { from, until })
  const [validation, setValidation] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [stopped, setStopped] = useState<string | null>(null)
  const stores = [
    ...new Set(library.data?.games.flatMap((game) => game.entries.map((entry) => entry.store)) ?? []),
  ].sort()
  const effectiveStore = stores.includes(store) ? store : ''
  let bounds: ReturnType<typeof gameplayRange> | null = null
  let problem = validation
  try {
    bounds = gameplayRange(period, applied.from, applied.until)
  } catch (error) {
    problem = (error as Error).message
  }
  const body = bounds && { ...bounds, store: effectiveStore || null }
  // As-of belongs to the read, while date/store selection identifies its scope.
  const scope = JSON.stringify([period, applied, effectiveStore, bounds?.fromUtc, bounds?.untilUtc])
  const canceled = stopped === scope
  const query = useQuery({
    queryKey: ['api', 'statistics.gameplay.dashboard', scope, attempt],
    queryFn: () => request<CompleteGameplayStats>('statistics.gameplay', undefined, body),
    enabled: !!body && !problem && !canceled && !!library.data,
    retry: false,
    staleTime: 30_000,
  })
  const stats = !canceled && !problem ? query.data : undefined
  const games = (library.data?.games ?? []).filter(
    (game) => !effectiveStore || game.entries.some((entry) => entry.store === effectiveStore),
  )
  const counts = [...new Set(games.map((game) => game.bucket))].sort().map((bucket) => ({
    key: bucket,
    label: bucket.replaceAll('_', ' '),
    value: games.filter((game) => game.bucket === bucket).length,
    text: `${games.filter((game) => game.bucket === bucket).length} games`,
  }))
  const applyDates = () => {
    try {
      gameplayRange('custom', from, until)
      setValidation(null)
      setApplied({ from, until })
      setAttempt((value) => value + 1)
    } catch (error) {
      setValidation((error as Error).message)
    }
  }
  return (
    <>
      <header className="feature-heading">
        <div>
          <h2>Your time with your library</h2>
          <p>Completed recorded sessions. Concurrent games contribute independently.</p>
        </div>
      </header>
      <div className="activity-filters">
        <label className="field">
          Gameplay period
          <select
            value={period}
            onChange={(event) => {
              setPeriod(event.target.value)
              setValidation(null)
            }}
          >
            <option value="30">30 days</option>
            <option value="90">90 days</option>
            <option value="custom">Custom</option>
          </select>
        </label>
        <label className="field">
          Store
          <select value={effectiveStore} onChange={(event) => setStore(event.target.value)}>
            <option value="">All stores</option>
            {stores.map((value) => (
              <option key={value} value={value}>
                {storeLabel(value)}
              </option>
            ))}
          </select>
        </label>
        {period === 'custom' && (
          <>
            <label className="field">
              From
              <input
                value={from}
                placeholder="YYYY-MM-DD"
                onChange={(event) => setFrom(event.target.value)}
              />
            </label>
            <label className="field">
              Through
              <input
                value={until}
                placeholder="YYYY-MM-DD"
                onChange={(event) => setUntil(event.target.value)}
              />
            </label>
            <button onClick={applyDates}>Apply dates</button>
          </>
        )}
      </div>
      {problem ? (
        <p role="alert">{problem}</p>
      ) : library.error || query.error || canceled ? (
        <div role="alert">
          <p>
            {canceled
              ? 'Reading stopped. Choose Try again to resume.'
              : "Couldn't read gameplay statistics. Try again."}
          </p>
          <button
            onClick={() => {
              setStopped(null)
              setAttempt((value) => value + 1)
              if (library.error) void library.refetch()
            }}
          >
            Try again
          </button>
        </div>
      ) : query.isPending ? (
        <div role="status">
          <p>Reading gameplay statistics…</p>
          <button onClick={() => setStopped(scope)}>Cancel</button>
        </div>
      ) : null}
      {stats && (
        <>
          <p>
            {dateLabel(body!.fromUtc)} –{' '}
            {dateLabel(new Date(new Date(body!.untilUtc).getTime() - 1).toISOString())}
          </p>
          <div className="stat-strip">
            <div className="stat">
              <strong>{hours(stats.recordedSeconds / 60)}</strong>
              <span>Recorded play</span>
            </div>
            <div className="stat">
              <strong>{stats.gamesPlayedCount}</strong>
              <span>Games played</span>
            </div>
            <div className="stat">
              <strong>{stats.startedSessionCount}</strong>
              <span>Sessions started</span>
            </div>
            <div className="stat">
              <strong>
                {stats.medianSessionSeconds == null ? '—' : hours(stats.medianSessionSeconds / 60)}
              </strong>
              <span>Median session</span>
            </div>
          </div>
          {!!stats.excludedSessionCount && (
            <p>{stats.excludedSessionCount} incomplete or unusable sessions excluded.</p>
          )}
          <div className="activity-charts">
            <Chart
              title="Recorded hours over time"
              empty="No recorded play in this period."
              bars={(stats.periods ?? []).map((row) => ({
                key: row.fromUtc,
                label: dateLabel(row.fromUtc),
                value: row.recordedSeconds,
                text: hours(row.recordedSeconds / 60),
              }))}
            />
            <Chart
              title="Games you spent time with"
              empty="No games with completed sessions in this period."
              bars={(stats.topGames ?? [])
                .slice(0, 10)
                .map((row) => ({
                  key: String(row.resolvedWorkId),
                  label:
                    library.data?.games.find((game) => game.workId === row.resolvedWorkId)?.title ??
                    'Game no longer in your library',
                  value: row.recordedSeconds,
                  text: hours(row.recordedSeconds / 60),
                  open: onOpenGame && (() => onOpenGame(row.resolvedWorkId)),
                }))}
            />
            <Chart
              title="Session lengths"
              empty="No completed session lengths in this period."
              bars={(stats.sessionLengths ?? []).map((row) => ({
                key: String(row.minimumSeconds),
                label:
                  row.maximumSeconds == null
                    ? `${hours(row.minimumSeconds / 60)} or longer`
                    : row.minimumSeconds === 0
                      ? `Under ${hours(row.maximumSeconds / 60)}`
                      : `${hours(row.minimumSeconds / 60)} – ${hours(row.maximumSeconds / 60)}`,
                value: row.count,
                text: `${row.count} sessions`,
              }))}
            />
            <Chart title="Your library today" empty="No visible games in this store." bars={counts} />
          </div>
          <p className="muted">
            Your library today is independent of the selected period. All stores counts each game once; a game
            may appear in more than one store. Never played means no play evidence. Retired does not mean
            completed.
          </p>
        </>
      )}
    </>
  )
}
