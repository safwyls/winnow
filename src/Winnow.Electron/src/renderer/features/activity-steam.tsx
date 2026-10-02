import { useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiError, dateLabel, request } from '../api/client'
import type { LibraryGame, Mode } from '../api/types'
import { Empty } from './shared'
import { FullscreenNoteReading } from './FullscreenNoteReading'
import selectGlyph from './assets/xbox_button_a_outline.svg?raw'
import backGlyph from './assets/xbox_button_b_outline.svg?raw'
import readGlyph from './assets/xbox_dpad.svg?raw'
import previousGlyph from './assets/xbox_lt_outline.svg?raw'
import nextGlyph from './assets/xbox_rt_outline.svg?raw'
import './steam-activity.css'

const explanation =
  "Approximate increases in Steam's playtime, observed between library checks. These are not exact sessions and are not added to recorded-session totals. Steam's lifetime total already includes this playtime."
const duration = (row: SteamActivity) =>
  row.comparisonUnavailable
    ? `About ${row.steamDeltaMinutes.toLocaleString()} min reported by Steam`
    : `About ${Math.ceil(row.unexplainedMinutes ?? 0).toLocaleString()} min not matched to recorded sessions`
const uncertainty = (row: SteamActivity) =>
  row.comparisonUnavailable
    ? 'Shared accounts or an unfinished session prevent a reliable comparison. This increase may overlap recorded sessions; exact play times are unknown.'
    : "Exact start and end times are unknown. This is an estimate from Steam's cumulative playtime."
const observationTime = (value: string) =>
  `${dateLabel(value)} ${new Date(value).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}`
const observationBounds = (row: SteamActivity) =>
  `Observed between ${observationTime(row.windowStartedAt)} and ${observationTime(row.windowEndedAt)}.`

function SteamActivityHints({ reading = false }: { reading?: boolean }) {
  const actions = reading
    ? [
        [readGlyph, 'Up/Down', 'Read'],
        [backGlyph, 'B', 'Back'],
      ]
    : [
        [selectGlyph, 'A', 'Read activity'],
        [previousGlyph, 'LT', 'Previous page'],
        [nextGlyph, 'RT', 'Next page'],
        [backGlyph, 'B', 'Back'],
      ]
  return (
    <div
      className="steam-activity-hints"
      role="group"
      aria-label={reading ? 'Steam activity reading controls' : 'Steam activity controls'}
    >
      {actions.map(([art, key, label]) => (
        <span key={key}>
          <span
            aria-hidden="true"
            dangerouslySetInnerHTML={{ __html: art.replace('<svg ', '<svg viewBox="8 8 48 48" ') }}
          />
          <span className="sr-only">{key} </span>
          {label}
        </span>
      ))}
    </div>
  )
}

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
  entryPoint = false,
  onBack,
}: {
  games: LibraryGame[]
  mode?: Mode
  onOpenGame?: (id: number) => void
  entryPoint?: boolean
  onBack?(): void
}) {
  const [open, setOpen] = useState(false)
  const origin = useRef<HTMLButtonElement>(null)
  const scope = [
    ...new Set(
      games.flatMap((game) =>
        game.entries.filter((entry) => entry.store === 'steam').map((entry) => entry.ownershipId),
      ),
    ),
  ].sort((a, b) => a - b)
  if (!scope.length) return null
  if (mode === 'fullscreen' && entryPoint)
    return (
      <>
        <button ref={origin} className="steam-activity-entry" onClick={() => setOpen(true)}>
          Steam-reported activity
        </button>
        <FullscreenNoteReading
          open={open}
          onOpenChange={setOpen}
          title="Steam-reported activity"
          regionLabel="Steam activity projection"
          scrollWithArrows={false}
          restoreFocus={() => origin.current?.focus({ preventScroll: true })}
          hints={<SteamActivityHints />}
        >
          {open && (
            <SteamProjection
              key={scope.join(',')}
              scope={scope}
              games={games}
              mode={mode}
              showHints={false}
            />
          )}
        </FullscreenNoteReading>
      </>
    )
  return (
    <SteamProjection
      key={scope.join(',')}
      scope={scope}
      games={games}
      mode={mode}
      onOpenGame={onOpenGame}
      onBack={onBack}
    />
  )
}

function SteamProjection({
  scope,
  games,
  mode,
  onOpenGame,
  showHints = true,
  onBack,
}: {
  scope: number[]
  games: LibraryGame[]
  mode: Mode
  onOpenGame?: (id: number) => void
  showHints?: boolean
  onBack?(): void
}) {
  const [page, setPage] = useState(0)
  const [attempt, setAttempt] = useState(0)
  const [readingId, setReadingId] = useState<number | null>(null)
  const readingOrigin = useRef<HTMLButtonElement | null>(null)
  const refreshButton = useRef<HTMLButtonElement>(null)
  const query = useQuery({
    queryKey: ['api', 'activity.steam', scope, attempt],
    queryFn: ({ signal }) =>
      request<SteamResponse>('activity.steam', undefined, { ownershipIds: scope }, signal),
    staleTime: 30_000,
    retry: false,
  })
  const rows =
    query.error || query.data?.accountConfirmationRequired
      ? []
      : (query.data?.activity ?? []).filter((row) => scope.includes(row.ownershipId))
  const pages = Math.ceil(rows.length / 20)
  const currentPage = Math.min(page, Math.max(0, pages - 1))
  const reading = rows.find((row) => row.id === readingId)
  const readingGame =
    reading && games.find((game) => game.entries.some((entry) => entry.ownershipId === reading.ownershipId))
  const refresh = () => {
    setPage(0)
    setAttempt((value) => value + 1)
  }
  return (
    <section
      className={`feature-panel steam-activity mode-${mode}`}
      aria-label="Steam-reported activity"
      data-controller-page={mode === 'fullscreen' ? '' : undefined}
      onKeyDown={(event) => {
        if (
          mode !== 'fullscreen' ||
          event.defaultPrevented ||
          !event.currentTarget.contains(event.target as Node)
        )
          return
        if (
          (event.target as HTMLElement).closest('[role="dialog"]') !==
          event.currentTarget.closest('[role="dialog"]')
        )
          return
        if (event.key === 'Escape' && onBack) {
          event.preventDefault()
          event.stopPropagation()
          onBack()
          return
        }
        if (event.key !== 'PageDown' && event.key !== 'PageUp') return
        event.preventDefault()
        event.stopPropagation()
        setPage(
          Math.max(0, Math.min(Math.max(0, pages - 1), currentPage + (event.key === 'PageDown' ? 1 : -1))),
        )
        refreshButton.current?.focus()
      }}
    >
      <header className="feature-heading">
        <div>
          {showHints && <h2>Steam-reported activity</h2>}
          <p>{explanation}</p>
        </div>
        <button ref={refreshButton} disabled={query.isFetching} onClick={refresh}>
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
        <div role="status" aria-live="polite">
          <Empty>Confirm your Steam account in Settings to see its reported activity.</Empty>
        </div>
      ) : !rows.length ? (
        <div role="status" aria-live="polite">
          <Empty>
            No unexplained Steam activity found. New observations need time to settle before they appear.
          </Empty>
        </div>
      ) : (
        <>
          <div className="timeline">
            {rows.slice(currentPage * 20, (currentPage + 1) * 20).map((row) => {
              const game = games.find((value) =>
                value.entries.some((entry) => entry.ownershipId === row.ownershipId),
              )
              const words = (
                <>
                  <h3>{game?.title ?? 'Steam game'}</h3>
                  <strong>{duration(row)}</strong>
                  <p className="steam-observation-bounds">
                    Observed between{' '}
                    <time dateTime={row.windowStartedAt}>{observationTime(row.windowStartedAt)}</time> and{' '}
                    <time dateTime={row.windowEndedAt}>{observationTime(row.windowEndedAt)}</time>.
                  </p>
                  <p className="muted">{uncertainty(row)}</p>
                </>
              )
              return (
                <article className="timeline-entry" key={row.id}>
                  {mode === 'fullscreen' ? (
                    <button
                      className="steam-observation"
                      aria-label={`${game?.title ?? 'Steam game'}. ${duration(row)}. ${observationBounds(row)} ${uncertainty(row)}`}
                      onClick={(event) => {
                        readingOrigin.current = event.currentTarget
                        setReadingId(row.id)
                      }}
                    >
                      {words}
                    </button>
                  ) : (
                    <div>
                      {onOpenGame && game ? (
                        <button className="text-button" onClick={() => onOpenGame(game.workId)}>
                          {game.title}
                        </button>
                      ) : (
                        <h3>{game?.title ?? 'Steam game'}</h3>
                      )}
                      <strong>{duration(row)}</strong>
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
                      <p className="muted">{uncertainty(row)}</p>
                    </div>
                  )}
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
      {mode === 'fullscreen' && (
        <>
          {showHints && <SteamActivityHints />}
          <FullscreenNoteReading
            open={!!reading}
            onOpenChange={(open) => {
              if (!open) setReadingId(null)
            }}
            title={readingGame?.title ?? 'Steam game'}
            regionLabel="Steam activity observation"
            note={
              reading
                ? `${duration(reading)}\n\n${observationBounds(reading)}\n\n${uncertainty(reading)}\n\n${explanation}`
                : ''
            }
            restoreFocus={() => readingOrigin.current?.focus({ preventScroll: true })}
            hints={<SteamActivityHints reading />}
          />
        </>
      )}
    </section>
  )
}
