import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiError, dateLabel, hours, request } from '../api/client'
import type { LibraryGame, Mode } from '../api/types'
import { Empty } from './shared'

interface SteamActivity {
  id: number
  ownershipId: number
  windowStartedAt: string
  windowEndedAt: string
  steamDeltaMinutes: number
  unexplainedMinutes?: number | null
  comparisonUnavailable: boolean
}
interface SteamResponse {
  accountConfirmationRequired: boolean
  activity: SteamActivity[]
}

export function SteamReportedActivity({
  games,
  mode = 'desktop',
  onOpenGame,
}: {
  games: LibraryGame[]
  mode?: Mode
  onOpenGame?: (id: number) => void
}) {
  const scope = [
    ...new Set(
      games.flatMap((game) =>
        game.entries.filter((entry) => entry.store === 'steam').map((entry) => entry.ownershipId),
      ),
    ),
  ].sort((a, b) => a - b)
  if (!scope.length) return null
  return (
    <SteamProjection key={scope.join(',')} scope={scope} games={games} mode={mode} onOpenGame={onOpenGame} />
  )
}

function SteamProjection({
  scope,
  games,
  mode,
  onOpenGame,
}: {
  scope: number[]
  games: LibraryGame[]
  mode: Mode
  onOpenGame?: (id: number) => void
}) {
  const [page, setPage] = useState(0)
  const [attempt, setAttempt] = useState(0)
  const query = useQuery({
    queryKey: ['api', 'activity.steam', scope, attempt],
    queryFn: () => request<SteamResponse>('activity.steam', undefined, { ownershipIds: scope }),
    staleTime: 30_000,
    retry: false,
  })
  const rows = query.data?.accountConfirmationRequired
    ? []
    : (query.data?.activity ?? []).filter((row) => scope.includes(row.ownershipId))
  const pages = Math.ceil(rows.length / 20)
  const currentPage = Math.min(page, Math.max(0, pages - 1))
  const refresh = () => {
    setPage(0)
    setAttempt((value) => value + 1)
  }
  return (
    <section className={`feature-panel steam-activity mode-${mode}`} aria-label="Steam-reported activity">
      <header className="feature-heading">
        <div>
          <h2>Steam-reported activity</h2>
          <p>
            Approximate increases in Steam's playtime, observed between library checks. These are not exact
            sessions and are not added to recorded-session totals. Steam's lifetime total already includes
            this playtime.
          </p>
        </div>
        <button disabled={query.isFetching} onClick={refresh}>
          Refresh
        </button>
      </header>
      {query.isPending ? (
        <p role="status">Reading Steam-reported activity…</p>
      ) : query.error ? (
        <div role="alert">
          <p>
            {query.error instanceof ApiError && [404, 501, 503].includes(query.error.status)
              ? 'Steam-reported activity is unavailable.'
              : "Couldn't read Steam-reported activity. Try again."}
          </p>
          <button onClick={refresh}>Try again</button>
        </div>
      ) : query.data?.accountConfirmationRequired ? (
        <Empty>Confirm your Steam account in Settings to see its reported activity.</Empty>
      ) : !rows.length ? (
        <Empty>
          No unexplained Steam activity found. New observations need time to settle before they appear.
        </Empty>
      ) : (
        <>
          <div className="timeline">
            {rows.slice(currentPage * 20, (currentPage + 1) * 20).map((row) => {
              const game = games.find((value) =>
                value.entries.some((entry) => entry.ownershipId === row.ownershipId),
              )
              return (
                <article className="timeline-entry" key={row.id}>
                  <div>
                    {onOpenGame && game ? (
                      <button className="text-button" onClick={() => onOpenGame(game.workId)}>
                        {game.title}
                      </button>
                    ) : (
                      <h3>{game?.title ?? 'Steam game'}</h3>
                    )}
                    <strong>
                      {row.comparisonUnavailable
                        ? `Steam increased by ${hours(row.steamDeltaMinutes)}`
                        : `About ${hours(row.unexplainedMinutes ?? 0)} not matched to recorded sessions`}
                    </strong>
                    <p>
                      Observed between{' '}
                      <time dateTime={row.windowStartedAt}>
                        {dateLabel(row.windowStartedAt)}{' '}
                        {new Date(row.windowStartedAt).toLocaleTimeString([], {
                          hour: 'numeric',
                          minute: '2-digit',
                        })}
                      </time>{' '}
                      and{' '}
                      <time dateTime={row.windowEndedAt}>
                        {dateLabel(row.windowEndedAt)}{' '}
                        {new Date(row.windowEndedAt).toLocaleTimeString([], {
                          hour: 'numeric',
                          minute: '2-digit',
                        })}
                      </time>
                      .
                    </p>
                    <p className="muted">
                      {row.comparisonUnavailable
                        ? 'Comparison is unavailable; this increase may overlap recorded sessions.'
                        : 'This is an estimate between observations, not an exact start time or session length.'}
                    </p>
                  </div>
                </article>
              )
            })}
          </div>
          <nav className="form-actions" aria-label="Steam activity pages">
            <button disabled={currentPage === 0} onClick={() => setPage(currentPage - 1)}>
              Previous
            </button>
            <span>
              Page {currentPage + 1} of {pages}
            </span>
            <button disabled={currentPage + 1 >= pages} onClick={() => setPage(currentPage + 1)}>
              Next
            </button>
          </nav>
        </>
      )}
    </section>
  )
}
