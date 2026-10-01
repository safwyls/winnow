import { Fragment, memo, useEffect, useId, useRef, useState } from 'react'
import { SectionLabel, SectionNavigation } from '../components/SectionNavigation'
import { FullscreenNoteReading } from './FullscreenNoteReading'
import * as Dialog from '@radix-ui/react-dialog'
import { BookOpen, X } from 'lucide-react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError, dateLabel, hours, request, storeLabel, openExternal } from '../api/client'
import { useActivity, useApiQuery, useLibrary, useStatistics } from '../api/hooks'
import type { JournalResponse, Mode, Session } from '../api/types'
import { Empty, Notice } from './shared'
import { clearViewState, useViewState } from '../viewState'
import { journalPeriod } from '../api/journalPeriod'
import { activityKey, localDate, mondayWeek, patchJournalCaches, uniqueActivity } from './activity-model'
import { SteamReportedActivity } from './activity-steam'
import { GameplayDashboard } from './activity-gameplay'
import { Artwork } from '../components/Artwork'
import './activity.css'
import { JournalRating } from './journal-prompt-controls'
import { activityDateLabel, activityHours } from './activity-format'

const activeEditors = new Map<number, { close: () => void }>()
interface JournalDraftState {
  note: string
  rating: number
  revision: string
  current: JournalResponse | null
  uncertain: boolean
  sending: boolean
  error?: unknown
  needsRead?: boolean
}

export function JournalEditor({
  sessionId,
  onClose,
  mode = 'desktop',
  editText,
}: {
  sessionId: number
  onClose(): void
  mode?: Mode
  editText?(input: HTMLInputElement | HTMLTextAreaElement): void
}) {
  const query = useApiQuery<JournalResponse>('journal.get', { sessionId })
  const origin = useRef(document.activeElement instanceof HTMLElement ? document.activeElement : null)
  const [draft] = useViewState<JournalDraftState | null>(`draft:journal:${sessionId}`, null)
  const cancel = () => {
    if (draft?.sending) return
    clearViewState(`draft:journal:${sessionId}`)
    onClose()
  }
  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) cancel()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content
          className={`dialog-content journal-dialog mode-${mode}`}
          onEscapeKeyDown={(event) => {
            if (draft?.sending) event.preventDefault()
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault()
            if (origin.current?.isConnected) origin.current.focus({ preventScroll: true })
          }}
        >
          <div className="feature-heading">
            <div>
              <Dialog.Title>Remember this session</Dialog.Title>
              <Dialog.Description>A note for the next time you return.</Dialog.Description>
            </div>
            <Dialog.Close disabled={draft?.sending} aria-label="Close journal editor" className="icon-button">
              <X size={20} />
            </Dialog.Close>
          </div>
          {query.isPending ? (
            <p role="status">Loading your note…</p>
          ) : query.error ? (
            <Notice error={query.error} />
          ) : query.data ? (
            <JournalDraft
              initial={query.data}
              onClose={onClose}
              showKeyboardAction={mode === 'fullscreen'}
              editText={editText}
            />
          ) : null}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

export function JournalDraft({
  initial,
  onClose,
  promptMode,
  editText,
  showKeyboardAction = promptMode === 'fullscreen',
}: {
  initial: JournalResponse
  onClose: () => void
  promptMode?: Mode
  editText?(input: HTMLInputElement | HTMLTextAreaElement): void
  showKeyboardAction?: boolean
}) {
  const client = useQueryClient()
  const noteInput = useRef<HTMLTextAreaElement>(null)
  const noteLabel = useId()
  useEffect(() => {
    const editor = { close: onClose }
    activeEditors.set(initial.sessionId, editor)
    return () => {
      if (activeEditors.get(initial.sessionId) === editor) activeEditors.delete(initial.sessionId)
    }
  }, [initial.sessionId, onClose])
  // The revision belongs to the draft, never to a later background refresh.
  const key = `draft:journal:${initial.sessionId}`
  const [draft, setDraft] = useViewState<JournalDraftState>(key, {
    note: initial.note ?? '',
    rating: initial.rating ?? 0,
    revision: initial.revision,
    current: null,
    uncertain: false,
    sending: false,
  })
  const { note, rating, revision, current, uncertain, sending: pending, error, needsRead } = draft
  const setNote = (value: string) => setDraft((previous) => ({ ...previous, note: value }))
  const setRating = (value: number) => setDraft((previous) => ({ ...previous, rating: value }))
  const setRevision = (value: string) => setDraft((previous) => ({ ...previous, revision: value }))
  const setCurrent = (value: JournalResponse | null) =>
    setDraft((previous) => ({ ...previous, current: value }))
  const setUncertain = (value: boolean) => setDraft((previous) => ({ ...previous, uncertain: value }))
  const setPending = (value: boolean) => setDraft((previous) => ({ ...previous, sending: value }))
  const close = () => {
    clearViewState(key)
    onClose()
  }
  const [confirmDelete, setConfirmDelete] = useState(false)
  const setError = (error: unknown) => setDraft((previous) => ({ ...previous, error }))
  async function readCurrent() {
    try {
      const current = await request<JournalResponse>('journal.get', { sessionId: initial.sessionId })
      setDraft((previous) => ({
        ...previous,
        current: current.revision === previous.revision ? null : current,
        uncertain: current.revision === previous.revision ? false : previous.uncertain,
        needsRead: false,
      }))
    } catch {
      setDraft((previous) => ({ ...previous, needsRead: true }))
    }
  }
  async function save(remove = false) {
    if (pending || current || needsRead) return
    if (!remove && !note.trim() && !(rating >= 1 && rating <= 5)) {
      setError(new Error('Add a note or rating, or delete this entry.'))
      return
    }
    setPending(true)
    setError(null)
    try {
      const saved = await request<JournalResponse>(
        remove ? 'journal.delete' : 'journal.put',
        { sessionId: initial.sessionId },
        { note: note.trim() || null, rating: rating || null, expectedRevision: revision },
      )
      patchJournalCaches(
        client,
        initial.sessionId,
        remove
          ? null
          : { note: note.trim() || null, rating: rating || null, ...saved, sessionId: initial.sessionId },
      )
      clearViewState(key)
      activeEditors.get(initial.sessionId)?.close()
    } catch (failure) {
      setError(failure)
      if (failure instanceof ApiError && (failure.conflict || failure.uncertain)) {
        setUncertain(failure.uncertain)
        await readCurrent()
      }
      setPending(false)
    }
  }
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        void save()
      }}
      className={`editor-form${promptMode ? ` journal-prompt-form prompt-${promptMode}` : ''}`}
    >
      <label className="field journal-note-field">
        <span id={noteLabel} className="journal-note-label">
          Your note
        </span>
        {promptMode === 'desktop' ? (
          <input
            name="note"
            aria-labelledby={noteLabel}
            type="text"
            disabled={pending}
            maxLength={10000}
            value={note}
            onChange={(event) => setNote(event.target.value)}
            placeholder="How was it?"
          />
        ) : (
          <textarea
            ref={noteInput}
            name="note"
            aria-labelledby={noteLabel}
            disabled={pending}
            rows={7}
            maxLength={10000}
            value={note}
            onChange={(event) => setNote(event.target.value)}
            placeholder="Where did you leave off?"
          />
        )}
      </label>
      {showKeyboardAction && (
        <button
          className="journal-edit-note"
          type="button"
          data-controller-context
          data-controller-initial
          disabled={pending}
          onClick={() => {
            if (noteInput.current) editText?.(noteInput.current)
          }}
        >
          Edit note
        </button>
      )}
      {promptMode ? (
        <JournalRating
          value={rating}
          disabled={pending}
          fullscreen={promptMode === 'fullscreen'}
          change={setRating}
        />
      ) : (
        <label className="field">
          Your rating
          <select
            name="rating"
            disabled={pending}
            value={rating}
            onChange={(event) => setRating(Number(event.target.value))}
          >
            <option value={0}>No rating</option>
            {[1, 2, 3, 4, 5].map((value) => (
              <option key={value} value={value}>
                {value} / 5
              </option>
            ))}
          </select>
        </label>
      )}
      <Notice error={error} />
      {needsRead && (
        <div className="conflict-panel">
          <p>
            The saved note could not be checked. Your draft is safe. Read the saved version before trying
            again.
          </p>
          <button type="button" disabled={pending} onClick={() => void readCurrent()}>
            Read saved note
          </button>
        </div>
      )}
      {current && (
        <section className="conflict-panel" aria-label="Resolve journal changes">
          <h3>
            {uncertain ? 'Check whether your last save arrived' : 'This note changed in another window'}
          </h3>
          <p>Your draft is still above. The current saved note is:</p>
          <blockquote>{current.note || 'No saved note'}</blockquote>
          <p>Rating: {current.rating ? `${current.rating} / 5` : 'None'}</p>
          <div className="form-actions">
            <button
              disabled={pending}
              type="button"
              onClick={() => {
                setNote(current.note ?? '')
                setRating(current.rating ?? 0)
                setRevision(current.revision)
                setCurrent(null)
                setError(null)
              }}
            >
              Use saved version
            </button>
            <button
              disabled={pending}
              type="button"
              onClick={() => {
                setRevision(current.revision)
                setCurrent(null)
                setError(null)
              }}
            >
              Keep my draft for the next save
            </button>
          </div>
        </section>
      )}
      <div className="form-actions">
        <button
          className="primary-button"
          disabled={pending || Boolean(current) || needsRead || confirmDelete}
          type="submit"
        >
          {pending ? 'Saving…' : promptMode ? 'Save' : 'Save note'}
        </button>
        {(!promptMode || initial.note || initial.rating) && (
          <button
            type="button"
            disabled={pending || Boolean(current) || needsRead}
            onClick={() => setConfirmDelete(true)}
          >
            Delete note
          </button>
        )}
        {promptMode !== 'desktop' && (
          <button type="button" disabled={pending} onClick={close}>
            {promptMode ? 'Dismiss' : 'Cancel'}
          </button>
        )}
      </div>
      {confirmDelete && (
        <section className="conflict-panel" aria-label="Delete this note?">
          <h3>Delete this note?</h3>
          <p>Your recorded session stays in your history.</p>
          <div className="form-actions">
            <button
              className="danger-button"
              disabled={pending || Boolean(current) || needsRead}
              type="button"
              onClick={() => void save(true)}
            >
              Yes, delete note
            </button>
            <button type="button" disabled={pending} onClick={() => setConfirmDelete(false)}>
              Keep note
            </button>
          </div>
        </section>
      )}
    </form>
  )
}

export const SessionRows = memo(function SessionRows({
  sessions,
  onEdit,
}: {
  sessions: Session[]
  onEdit: (id: number) => void
}) {
  if (!sessions.length)
    return <Empty>No recorded sessions yet. Sessions appear here after Winnow observes you playing.</Empty>
  return (
    <div className="timeline">
      {sessions.map((session) => (
        <article className="timeline-entry" key={session.id}>
          <time dateTime={session.startedAt}>{activityDateLabel(session.startedAt)}</time>
          <div>
            <strong>
              {session.durationSeconds != null
                ? activityHours(session.durationSeconds / 60)
                : session.endedAt
                  ? 'Duration unavailable'
                  : 'Session in progress'}
            </strong>
            <p>{session.detectionMethod.replaceAll('_', ' ')}</p>
          </div>
          <button onClick={() => onEdit(session.id)}>
            <BookOpen size={16} /> Journal
          </button>
        </article>
      ))}
    </div>
  )
})

export function Journal({
  mode = 'desktop',
  onOpenGame,
  editText,
}: {
  mode?: Mode
  onOpenGame?: (workId: number) => void
  editText?(input: HTMLInputElement | HTMLTextAreaElement): void
}) {
  const [section, setSection] = useViewState(`${mode}:journal:section`, 0)
  const [days, setDays] = useViewState(`${mode}:journal:days`, mode === 'fullscreen' ? 0 : 30)
  const [bounds, setBounds] = useViewState(
    `${mode}:journal:bounds`,
    mode === 'fullscreen' ? { ...mondayWeek(), untilUtc: journalPeriod(1).untilUtc } : journalPeriod(30),
  )
  const [editing, setEditing] = useViewState<number | null>(`${mode}:journal:editing`, null)
  const [panel, setPanel] = useViewState(`${mode}:journal:panel`, 'history')
  const [week, setWeek] = useViewState(`${mode}:journal:week`, 0)
  const [workId, setWorkId] = useViewState<number | undefined>(`${mode}:journal:work`, undefined)
  const [selected, setSelected] = useViewState<string | null>(`${mode}:journal:selected`, null)
  const [openError, setOpenError] = useState<unknown>(null)
  const [reading, setReading] = useState(false)
  const readingOrigin = useRef<HTMLElement | null>(null)
  const openReading = () => {
    readingOrigin.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    setReading(true)
  }
  const restoreReadingFocus = () => readingOrigin.current?.focus({ preventScroll: true })
  const library = useLibrary()
  const preferences = useApiQuery<{ promptAfterPlay: boolean }>('journal.preferences.get')
  const activity = useActivity(bounds.fromUtc, bounds.untilUtc, section, workId)
  const statistics = useStatistics(bounds.fromUtc, bounds.untilUtc)
  const rows = uniqueActivity(activity.data?.pages.flatMap((page) => page.rows) ?? []).filter(
    (row) =>
      !library.data ||
      library.data.games.some((game) => game.entries.some((entry) => entry.ownershipId === row.ownershipId)),
  )
  const chosen = rows.find((row) => activityKey(row) === selected) ?? rows[0]
  const chosenGame = library.data?.games.find((game) =>
    game.entries.some((entry) => entry.ownershipId === chosen?.ownershipId),
  )
  const changeWeek = (offset: number) => {
    const next = Math.min(0, offset)
    setWeek(next)
    setDays(0)
    setBounds(next === 0 ? { ...mondayWeek(), untilUtc: journalPeriod(1).untilUtc } : mondayWeek(next))
  }
  const readUpdate = async (url: string) => {
    try {
      await openExternal(url, { failure: 'inline' })
      setOpenError(null)
    } catch (error) {
      setOpenError(error)
    }
  }
  return (
    <section
      className={`feature-page journal-page mode-${mode}`}
      tabIndex={mode === 'fullscreen' ? 0 : undefined}
      onKeyDown={(event) => {
        if (
          panel !== 'history' ||
          event.defaultPrevented ||
          (event.target as HTMLElement).closest('nav, input, select, textarea, [role="dialog"]')
        )
          return
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
          event.preventDefault()
          if (event.key === 'ArrowLeft') changeWeek(days === 0 ? week - 1 : -1)
          else if (days === 0 && week < 0) changeWeek(week + 1)
        }
        if (event.key.toLowerCase() === 'x' && (chosen?.session || chosen?.note)) {
          event.preventDefault()
          setEditing(chosen.session?.id ?? chosen.note!.sessionId)
        }
        if (event.key.toLowerCase() === 'y' && chosen?.note?.note) {
          event.preventDefault()
          openReading()
        }
      }}
    >
      <header className="feature-heading">
        <div>
          <p className="eyebrow">YOUR PLAYING LIFE</p>
          <h1>A little history.</h1>
          <p>Sessions, discoveries, and the notes you leave behind.</p>
        </div>
        {panel === 'history' && (
          <label className="field">
            Time period
            <select
              value={days}
              onChange={(event) => {
                const value = Number(event.target.value)
                setDays(value)
                if (value === 0) changeWeek(0)
                else setBounds(journalPeriod(value))
              }}
            >
              <option value={7}>Last 7 days</option>
              <option value={30}>Last 30 days</option>
              <option value={90}>Last 90 days</option>
              <option value={365}>Last year</option>
              <option value={0}>Monday week</option>
            </select>
          </label>
        )}
      </header>
      <SectionNavigation fullscreen={mode === 'fullscreen'}>
        <nav className="tabs" aria-label="Activity pages">
          <button data-controller-tab aria-pressed={panel === 'history'} onClick={() => setPanel('history')}>
            {mode === 'fullscreen' ? <SectionLabel>History</SectionLabel> : 'History'}
          </button>
          <button data-controller-tab aria-pressed={panel === 'summary'} onClick={() => setPanel('summary')}>
            {mode === 'fullscreen' ? <SectionLabel>Library summary</SectionLabel> : 'Library summary'}
          </button>
          {library.data?.games.some((game) => game.entries.some((entry) => entry.store === 'steam')) && (
            <button data-controller-tab aria-pressed={panel === 'steam'} onClick={() => setPanel('steam')}>
              {mode === 'fullscreen' ? (
                <SectionLabel>Steam-reported activity</SectionLabel>
              ) : (
                'Steam-reported activity'
              )}
            </button>
          )}
        </nav>
      </SectionNavigation>
      {panel === 'summary' ? (
        <GameplayDashboard mode={mode} onOpenGame={onOpenGame} />
      ) : panel === 'steam' ? (
        <SteamReportedActivity games={library.data?.games ?? []} mode={mode} onOpenGame={onOpenGame} />
      ) : (
        <>
          {statistics.data && (
            <div className="stat-strip">
              <div className="stat">
                <strong>{hours(statistics.data.recordedSeconds / 60)}</strong>
                <span>Recorded play</span>
              </div>
              <div className="stat">
                <strong>{statistics.data.gamesPlayedCount}</strong>
                <span>Games played</span>
              </div>
              <div className="stat">
                <strong>{statistics.data.startedSessionCount}</strong>
                <span>Sessions started</span>
              </div>
              <div className="stat">
                <strong>
                  {statistics.data.medianSessionSeconds == null
                    ? '—'
                    : hours(statistics.data.medianSessionSeconds / 60)}
                </strong>
                <span>Typical session</span>
              </div>
            </div>
          )}
          <Notice error={statistics.error} />
          <p className="muted">
            Recorded sessions only. Store playtime counters are separate. Concurrent games contribute
            independently.
          </p>
          <div className="activity-week-navigation">
            <button onClick={() => changeWeek(days === 0 ? week - 1 : -1)}>Previous week</button>
            {days === 0 && (
              <strong>
                {dateLabel(bounds.fromUtc)} –{' '}
                {dateLabel(new Date(Date.parse(bounds.untilUtc) - 1).toISOString())}
              </strong>
            )}
            <button disabled={days !== 0 || week >= 0} onClick={() => changeWeek(week + 1)}>
              Next week
            </button>
            <button onClick={() => changeWeek(0)}>This week</button>
            <label className="field">
              Game
              <select
                value={workId ?? ''}
                onChange={(event) => setWorkId(event.target.value ? Number(event.target.value) : undefined)}
              >
                <option value="">All games</option>
                {library.data?.games.map((game) => (
                  <option key={game.workId} value={game.workId}>
                    {game.title}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <nav className="tabs" aria-label="Activity type">
            {['Sessions', 'Updates', 'Journal'].map((name, index) => (
              <button
                data-controller-tab
                key={name}
                aria-pressed={section === index}
                onClick={() => setSection(index)}
              >
                {name}
              </button>
            ))}
          </nav>
          <Notice error={openError} />
          {activity.error && (
            <div role="alert">
              <p>
                {activity.error instanceof ApiError && [404, 501, 503].includes(activity.error.status)
                  ? 'Your activity history is unavailable.'
                  : "Couldn't read your activity. Try again."}
              </p>
              <button
                disabled={activity.isFetching}
                onClick={() => {
                  if (activity.isFetchNextPageError) void activity.fetchNextPage()
                  else void activity.refetch()
                }}
              >
                Try again
              </button>
            </div>
          )}
          {activity.isPending ? (
            <p role="status">Loading activity…</p>
          ) : !rows.length && !activity.error ? (
            <Empty>
              {section === 2
                ? preferences.data?.promptAfterPlay === false
                  ? 'Journal prompts are off. Turn them on in Display preferences after a game.'
                  : 'Your notes will live here. Open a recorded session to add your first one.'
                : section === 1
                  ? mode === 'fullscreen' && days === 0
                    ? 'No updates this week'
                    : 'No updates in this period. Updates from your games will appear here.'
                  : 'No recorded sessions in this period. Sessions appear after Winnow observes you playing.'}
            </Empty>
          ) : (
            !!rows.length && (
              <div className="activity-layout">
                <div
                  className="activity-history"
                  role="region"
                  aria-label="Activity events"
                  onKeyDown={(event) => {
                    if ((event.target as HTMLElement).matches('input, select, textarea')) return
                    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
                      event.preventDefault()
                      if (event.key === 'ArrowLeft') changeWeek(days === 0 ? week - 1 : -1)
                      else if (days === 0 && week < 0) changeWeek(week + 1)
                    }
                    if (event.key.toLowerCase() === 'x' && (chosen?.session || chosen?.note)) {
                      event.preventDefault()
                      setEditing(chosen.session?.id ?? chosen.note!.sessionId)
                    }
                  }}
                >
                  {rows.map((row, index) => {
                    const game = library.data?.games.find((item) =>
                      item.entries.some((entry) => entry.ownershipId === row.ownershipId),
                    )
                    return (
                      <Fragment key={activityKey(row)}>
                        {(index === 0 ||
                          localDate(new Date(rows[index - 1]!.atUtc)) !== localDate(new Date(row.atUtc))) && (
                          <h3>{dateLabel(row.atUtc)}</h3>
                        )}
                        <article
                          tabIndex={0}
                          aria-label={`${game?.title ?? 'Game'} activity at ${dateLabel(row.atUtc)}`}
                          aria-current={chosen === row ? 'true' : undefined}
                          className="timeline-entry"
                          onClick={() => setSelected(activityKey(row))}
                          onFocus={() => setSelected(activityKey(row))}
                        >
                          <time dateTime={row.atUtc}>
                            {new Date(row.atUtc).toLocaleTimeString([], {
                              hour: 'numeric',
                              minute: '2-digit',
                            })}
                          </time>
                          <div>
                            <button
                              className="text-button"
                              disabled={!game}
                              onClick={() => game && onOpenGame?.(game.workId)}
                            >
                              {game?.title ?? 'Game no longer in your library'}
                            </button>
                            <p>
                              {row.session
                                ? row.session.durationSeconds == null
                                  ? row.session.endedAt
                                    ? 'Duration unavailable'
                                    : 'Session in progress'
                                  : hours(row.session.durationSeconds / 60)
                                : (row.update?.title ?? row.update?.kind ?? 'Journal entry')}
                            </p>
                            {row.note?.note && <blockquote>{row.note.note}</blockquote>}
                            {row.note?.rating && <p>{row.note.rating} / 5</p>}
                          </div>
                          {(row.session || row.note) && (
                            <button onClick={() => setEditing(row.session?.id ?? row.note!.sessionId)}>
                              <BookOpen size={16} /> {row.note ? 'Edit note' : 'Add note'}
                            </button>
                          )}
                          {row.update?.url && (
                            <button
                              onClick={() => {
                                void readUpdate(row.update!.url!)
                              }}
                            >
                              Read update
                            </button>
                          )}
                        </article>
                      </Fragment>
                    )
                  })}
                </div>
                <aside className="feature-panel activity-preview" aria-label="Selected activity">
                  {chosenGame && <Artwork workId={chosenGame.workId} hero />}
                  <p className="eyebrow">{chosen && storeLabel(chosen.store)}</p>
                  <h2>{chosenGame?.title ?? 'Your activity'}</h2>
                  {chosen && <p>{dateLabel(chosen.atUtc)}</p>}
                  {chosen?.session && (
                    <p>
                      {chosen.session.durationSeconds == null
                        ? chosen.session.endedAt
                          ? 'Duration unavailable'
                          : 'Session in progress'
                        : hours(chosen.session.durationSeconds / 60)}{' '}
                      · {chosen.session.detectionMethod.replaceAll('_', ' ')}
                    </p>
                  )}
                  {chosen?.update && <h3>{chosen.update.title ?? chosen.update.kind}</h3>}
                  {chosen?.note?.note && (
                    <>
                      {mode === 'fullscreen' && <h3 className="activity-note-label">Your note</h3>}
                      <blockquote>{chosen.note.note}</blockquote>
                    </>
                  )}
                  {!!chosen?.note?.rating && <p>{chosen.note.rating} / 5</p>}
                  <div className="form-actions">
                    {chosenGame && onOpenGame && (
                      <button onClick={() => onOpenGame(chosenGame.workId)}>Open game</button>
                    )}
                    {(chosen?.session || chosen?.note) && (
                      <button
                        data-controller-play
                        onClick={() => setEditing(chosen.session?.id ?? chosen.note!.sessionId)}
                      >
                        {chosen.note ? 'Edit selected note' : 'Add selected note'}
                      </button>
                    )}
                    {chosen?.note?.note && (
                      <button data-controller-context onClick={openReading}>
                        Read note
                      </button>
                    )}
                    {chosen?.update?.url && (
                      <button onClick={() => void readUpdate(chosen.update!.url!)}>
                        Read selected update
                      </button>
                    )}
                  </div>
                </aside>
              </div>
            )
          )}
          {activity.hasNextPage && (
            <button disabled={activity.isFetchingNextPage} onClick={() => void activity.fetchNextPage()}>
              {activity.isFetchingNextPage ? 'Loading…' : 'Earlier activity'}
            </button>
          )}
        </>
      )}
      {editing != null && (
        <JournalEditor sessionId={editing} onClose={() => setEditing(null)} mode={mode} editText={editText} />
      )}
      {mode === 'fullscreen' ? (
        <FullscreenNoteReading
          open={reading}
          onOpenChange={setReading}
          title={chosenGame?.title ?? 'Your session note'}
          note={chosen?.note?.note ?? ''}
          restoreFocus={restoreReadingFocus}
        />
      ) : (
        <Dialog.Root open={reading} onOpenChange={setReading}>
          <Dialog.Portal>
            <Dialog.Overlay className="dialog-overlay" />
            <Dialog.Content
              className="dialog-content journal-dialog"
              onCloseAutoFocus={(event) => {
                event.preventDefault()
                restoreReadingFocus()
              }}
            >
              <Dialog.Title>{chosenGame?.title ?? 'Your session note'}</Dialog.Title>
              <Dialog.Description>{chosen && dateLabel(chosen.atUtc)}</Dialog.Description>
              <blockquote style={{ whiteSpace: 'pre-wrap' }}>{chosen?.note?.note}</blockquote>
              {!!chosen?.note?.rating && <p>{chosen.note.rating} / 5</p>}
              <Dialog.Close>Close note</Dialog.Close>
            </Dialog.Content>
          </Dialog.Portal>
        </Dialog.Root>
      )}
    </section>
  )
}
