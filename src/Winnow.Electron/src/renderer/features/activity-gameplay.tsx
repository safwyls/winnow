import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
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

const recordedHours = (seconds: number) =>
  seconds > 0 && seconds < 360
    ? '<0.1 h'
    : `${(seconds / 3600).toLocaleString(undefined, { maximumFractionDigits: 1 })} h`
const periodDate = (value: string) =>
  new Date(value).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

function Chart({
  title,
  bars,
  empty,
  children,
}: {
  title: string
  bars: Bar[]
  empty: string
  children?: ReactNode
}) {
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
      {children}
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
  const root = useRef<HTMLElement>(null)
  const restoreSection = useRef(false)
  useLayoutEffect(() => {
    if (!restoreSection.current) return
    restoreSection.current = false
    root.current
      ?.querySelector<HTMLButtonElement>('.gameplay-toolbar [aria-pressed="true"]')
      ?.focus({ preventScroll: true })
  }, [section])
  const toolbar = (actions?: ReactNode) => (
    <header className="gameplay-toolbar">
      <h2>{mode === 'desktop' ? 'Stats' : 'Library summary'}</h2>
      <nav
        className="tabs"
        aria-label="Library summary section"
        data-controller-page={mode === 'fullscreen' ? '' : undefined}
        onKeyDown={(event) => {
          if (event.key !== 'PageUp' && event.key !== 'PageDown') return
          event.preventDefault()
          event.stopPropagation()
          const next = event.key === 'PageUp' ? 'gameplay' : 'spending'
          restoreSection.current = section !== next
          setSection(next)
          event.currentTarget
            .querySelectorAll<HTMLButtonElement>('button')
            [next === 'gameplay' ? 0 : 1]?.focus({ preventScroll: true })
        }}
      >
        {['gameplay', 'spending'].map((value) => (
          <button
            data-controller-tab
            key={value}
            aria-pressed={section === value}
            onClick={(event) => {
              restoreSection.current = section !== value && document.activeElement === event.currentTarget
              setSection(value)
            }}
          >
            {value === 'gameplay' ? 'Gameplay' : 'Spending'}
          </button>
        ))}
      </nav>
      {actions}
    </header>
  )
  return (
    <section ref={root} className={`gameplay-dashboard mode-${mode}`} aria-label="Library summary">
      {section === 'spending' ? (
        <AccountStatistics mode={mode} toolbar={toolbar} />
      ) : (
        <>
          {toolbar()}
          <Gameplay mode={mode} onOpenGame={onOpenGame} />
        </>
      )}
    </section>
  )
}

function Gameplay({ mode, onOpenGame }: { mode: Mode; onOpenGame?: (id: number) => void }) {
  const client = useQueryClient()
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
  useEffect(() => {
    if (library.data && store !== effectiveStore) setStore(effectiveStore)
  }, [library.data, store, effectiveStore, setStore])
  let bounds: ReturnType<typeof gameplayRange> | null = null
  let problem = validation
  try {
    bounds = gameplayRange(period, applied.from, applied.until)
  } catch (error) {
    problem = (error as Error).message
  }
  const body = bounds && { ...bounds, store: effectiveStore || null }
  // As-of belongs to the read, while date/store selection identifies its scope.
  // The API derives ownerships itself. This key cancels a changed visible scope
  // without letting the presentation search filter statistics.
  const ownerships = library.data?.games
    .flatMap((game) => game.entries.map((entry) => [entry.ownershipId, game.workId, entry.store, game.title]))
    .sort((a, b) => Number(a[0]) - Number(b[0]))
  const scope = JSON.stringify([
    period,
    applied,
    effectiveStore,
    bounds?.fromUtc,
    bounds?.untilUtc,
    ownerships,
  ])
  const canceled = stopped === scope
  const queryKey = ['api', 'statistics.gameplay.dashboard', scope, attempt] as const
  const query = useQuery({
    queryKey,
    queryFn: ({ signal }) => request<CompleteGameplayStats>('statistics.gameplay', undefined, body, signal),
    enabled: !!body && !problem && !canceled && !!library.data,
    retry: false,
    staleTime: 30_000,
    refetchOnMount: 'always',
  })
  const stats = !canceled && !problem && !query.isFetching && !query.isError ? query.data : undefined
  const games = (library.data?.games ?? []).filter(
    (game) => !effectiveStore || game.entries.some((entry) => entry.store === effectiveStore),
  )
  const counts = [...new Set(games.map((game) => game.bucket))]
    .sort()
    .map((bucket) => ({
      key: bucket,
      label:
        (
          {
            never_played: 'Never played',
            active: 'Active',
            bounced: 'Started',
            retired: 'Retired',
            stale_but_patched: 'Patched',
            derelict: 'Derelict',
          } as Record<string, string>
        )[bucket] ?? bucket,
      value: games.filter((game) => game.bucket === bucket).length,
      text: `${games.filter((game) => game.bucket === bucket).length} games`,
    }))
    .sort((a, b) => b.value - a.value)
  const storeEntries = [
    ...new Map(
      games
        .flatMap((game) => game.entries)
        .filter((entry) => !effectiveStore || entry.store === effectiveStore)
        .map((entry) => [entry.ownershipId, entry]),
    ).values(),
  ]
  const storeCounts = [...new Set(storeEntries.map((entry) => entry.store))].map((value) => ({
    key: value,
    label: storeLabel(value),
    value: storeEntries.filter((entry) => entry.store === value).length,
  }))
  const refresh = () => {
    void client.cancelQueries({ queryKey, exact: true })
    setStopped(null)
    if (library.error) void library.refetch()
    if (period === 'custom') {
      applyDates()
      return
    }
    setValidation(null)
    setAttempt((value) => value + 1)
  }
  const applyDates = () => {
    void client.cancelQueries({ queryKey, exact: true })
    setStopped(null)
    setApplied({ from, until })
    try {
      gameplayRange('custom', from, until)
      setValidation(null)
      setAttempt((value) => value + 1)
    } catch (error) {
      setValidation((error as Error).message)
    }
  }
  return (
    <>
      <div className="activity-filters">
        {mode === 'fullscreen' ? (
          <>
            <div role="group" aria-label="Store" className="gameplay-choices">
              <h3>Store</h3>
              <div>
                {['', ...stores].map((value) => (
                  <button key={value} aria-pressed={effectiveStore === value} onClick={() => setStore(value)}>
                    {value ? storeLabel(value) : 'All stores'}
                  </button>
                ))}
              </div>
            </div>
            <div role="group" aria-label="Gameplay period" className="gameplay-choices">
              <h3>Period</h3>
              <div>
                {[
                  ['30', '30 days'],
                  ['90', '90 days'],
                  ['custom', 'Custom'],
                ].map(([value, label]) => (
                  <button
                    key={value}
                    aria-pressed={period === value}
                    onClick={() => {
                      setPeriod(value!)
                      setValidation(null)
                    }}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>
          </>
        ) : (
          <>
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
          </>
        )}
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
      {problem || library.error || query.error || canceled ? (
        <div role="alert">
          <p>
            {problem ??
              (canceled
                ? 'Reading stopped. Choose Try again to resume.'
                : "Couldn't read gameplay statistics. Try again.")}
          </p>
          <button onClick={refresh}>Try again</button>
        </div>
      ) : (
        <button onClick={refresh}>{mode === 'fullscreen' ? 'Refresh gameplay' : 'Refresh'}</button>
      )}
      {!problem && !canceled && query.isFetching ? (
        <div role="status">
          <p>Reading gameplay statistics…</p>
          <button
            onClick={() => {
              void client.cancelQueries({ queryKey, exact: true })
              setStopped(scope)
            }}
          >
            Cancel
          </button>
        </div>
      ) : null}
      {stats && (
        <>
          <p className="gameplay-period-label">
            {effectiveStore ? storeLabel(effectiveStore) : 'All stores'} · {periodDate(body!.fromUtc)} –{' '}
            {periodDate(new Date(new Date(body!.untilUtc).getTime() - 1).toISOString())} · local dates
          </p>
          <div className="stat-strip">
            <div className="stat">
              <strong>{recordedHours(stats.recordedSeconds)}</strong>
              <span>Recorded hours</span>
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
                {stats.medianSessionSeconds == null
                  ? 'No completed sessions'
                  : stats.medianSessionSeconds < 3600
                    ? `${(stats.medianSessionSeconds / 60).toLocaleString(undefined, { maximumFractionDigits: 1 })} min`
                    : recordedHours(stats.medianSessionSeconds)}
              </strong>
              <span>Median session</span>
            </div>
          </div>
          <p className="gameplay-coverage">
            Completed Winnow sessions on this library, not lifetime playtime or active attention. Overlapping
            games count independently. Account filters select games, not who played.
          </p>
          <div className="activity-charts">
            <Chart
              title="Recorded hours over time"
              empty="No recorded play in this period."
              bars={(stats.periods ?? []).map((row) => ({
                key: row.fromUtc,
                label: `${dateLabel(row.fromUtc)} – ${dateLabel(new Date(Date.parse(row.untilUtc) - 1).toISOString())}`,
                value: row.recordedSeconds,
                text: recordedHours(row.recordedSeconds),
              }))}
            />
            <Chart
              title="Games you spent time with"
              empty="No games with completed sessions in this period."
              bars={(stats.topGames ?? []).slice(0, 10).map((row) => ({
                key: String(row.resolvedWorkId),
                label:
                  library.data?.games.find((game) => game.workId === row.resolvedWorkId)?.title ??
                  'Game no longer in your library',
                value: row.recordedSeconds,
                text: recordedHours(row.recordedSeconds),
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
            >
              <p>
                {stats.startedSessionCount} completed sessions started in this period; median and bands use
                their full lengths. Hours include the portions of {stats.overlappingSessionCount} sessions
                within these dates. {stats.excludedSessionCount} unfinished or invalid records excluded.
              </p>
            </Chart>
            <Chart title="Your library today" empty="No visible games in this store." bars={counts}>
              <div role="group" aria-label="Store entries" className="gameplay-store-counts">
                <p>Store entries · games owned in more than one store appear under each store.</p>
                <ul>
                  {storeCounts.map((row) => (
                    <li key={row.key}>
                      <span>{row.label}</span>
                      <strong>{row.value}</strong>
                      <span aria-hidden="true" className="activity-bar-track">
                        <span
                          style={{
                            width: `${(100 * row.value) / Math.max(1, ...storeCounts.map((item) => item.value))}%`,
                          }}
                        />
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            </Chart>
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
