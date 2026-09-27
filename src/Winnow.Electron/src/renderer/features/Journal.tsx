import { useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { BookOpen, X } from 'lucide-react'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError, dateLabel, hours, request } from '../api/client'
import { useActivity, useApiQuery, useLibrary, useStatistics } from '../api/hooks'
import type { JournalResponse, Mode, Session } from '../api/types'
import { Empty, Notice } from './shared'
import { clearViewState, useViewState } from '../viewState'
interface JournalDraftState {
  note: string
  rating: number
  revision: string
  current: JournalResponse | null
  uncertain: boolean
  sending: boolean
}

export function JournalEditor({ sessionId, onClose }: { sessionId: number; onClose: () => void }) {
  const query = useApiQuery<JournalResponse>('journal.get', { sessionId })
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
        <Dialog.Content className="dialog-content journal-dialog">
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
            <JournalDraft initial={query.data} onClose={onClose} />
          ) : null}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

export function JournalDraft({ initial, onClose }: { initial: JournalResponse; onClose: () => void }) {
  const client = useQueryClient()
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
  const { note, rating, revision, current, uncertain, sending: pending } = draft
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
  const [error, setError] = useState<unknown>(null)
  async function save(remove = false) {
    if (pending) return
    setPending(true)
    setError(null)
    try {
      await request(
        remove ? 'journal.delete' : 'journal.put',
        { sessionId: initial.sessionId },
        { note: note.trim() || null, rating: rating || null, expectedRevision: revision },
      )
      await client.invalidateQueries({ queryKey: ['api'] })
      close()
    } catch (failure) {
      setError(failure)
      if (failure instanceof ApiError && (failure.conflict || failure.uncertain)) {
        setUncertain(failure.uncertain)
        try {
          setCurrent(await request<JournalResponse>('journal.get', { sessionId: initial.sessionId }))
        } catch {
          /* Keep the original failure and the local draft. */
        }
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
      className="editor-form"
    >
      <label className="field">
        Your note
        <textarea
          disabled={pending}
          rows={7}
          maxLength={10000}
          value={note}
          onChange={(event) => setNote(event.target.value)}
          placeholder="Where did you leave off?"
        />
      </label>
      <label className="field">
        Your rating
        <select disabled={pending} value={rating} onChange={(event) => setRating(Number(event.target.value))}>
          <option value={0}>No rating</option>
          {[1, 2, 3, 4, 5].map((value) => (
            <option key={value} value={value}>
              {value} / 5
            </option>
          ))}
        </select>
      </label>
      <Notice error={error} />
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
        <button className="primary-button" disabled={pending || Boolean(current)} type="submit">
          {pending ? 'Saving…' : 'Save note'}
        </button>
        <button type="button" disabled={pending || Boolean(current)} onClick={() => void save(true)}>
          Delete note
        </button>
        <button type="button" disabled={pending} onClick={close}>
          Cancel
        </button>
      </div>
    </form>
  )
}

export function SessionRows({ sessions, onEdit }: { sessions: Session[]; onEdit: (id: number) => void }) {
  if (!sessions.length)
    return <Empty>No recorded sessions yet. Sessions appear here after Winnow observes you playing.</Empty>
  return (
    <div className="timeline">
      {sessions.map((session) => (
        <article className="timeline-entry" key={session.id}>
          <time dateTime={session.startedAt}>{dateLabel(session.startedAt)}</time>
          <div>
            <strong>
              {session.durationSeconds != null
                ? hours(session.durationSeconds / 60)
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
}

export function Journal({
  mode = 'desktop',
  onOpenGame,
}: {
  mode?: Mode
  onOpenGame?: (workId: number) => void
}) {
  const [section, setSection] = useViewState(`${mode}:journal:section`, 0)
  const [days, setDays] = useViewState(`${mode}:journal:days`, 30)
  const [bounds, setBounds] = useViewState(`${mode}:journal:bounds`, period(30))
  const [editing, setEditing] = useViewState<number | null>(`${mode}:journal:editing`, null)
  const library = useLibrary()
  const activity = useActivity(bounds.fromUtc, bounds.untilUtc, section)
  const statistics = useStatistics(bounds.fromUtc, bounds.untilUtc)
  const rows = activity.data?.pages.flatMap((page) => page.rows) ?? []
  return (
    <section className={`feature-page journal-page mode-${mode}`}>
      <header className="feature-heading">
        <div>
          <p className="eyebrow">YOUR PLAYING LIFE</p>
          <h1>A little history.</h1>
          <p>Sessions, discoveries, and the notes you leave behind.</p>
        </div>
        <label className="field">
          Time period
          <select
            value={days}
            onChange={(event) => {
              const value = Number(event.target.value)
              setDays(value)
              setBounds(period(value))
            }}
          >
            <option value={7}>Last 7 days</option>
            <option value={30}>Last 30 days</option>
            <option value={90}>Last 90 days</option>
            <option value={365}>Last year</option>
          </select>
        </label>
      </header>
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
      <nav className="tabs" aria-label="Activity type">
        {['Sessions', 'Updates', 'Journal'].map((name, index) => (
          <button key={name} aria-pressed={section === index} onClick={() => setSection(index)}>
            {name}
          </button>
        ))}
      </nav>
      <Notice error={activity.error} />
      {activity.isPending ? (
        <p role="status">Loading activity…</p>
      ) : !rows.length ? (
        <Empty>
          {section === 2
            ? 'Your notes will live here. Open a recorded session to add your first one.'
            : 'No activity in this period.'}
        </Empty>
      ) : (
        <div className="timeline">
          {rows.map((row, index) => {
            const game = library.data?.games.find((item) =>
              item.entries.some((entry) => entry.ownershipId === row.ownershipId),
            )
            return (
              <article
                className="timeline-entry"
                key={`${row.session?.id ?? row.update?.id ?? row.note?.sessionId}-${index}`}
              >
                <time dateTime={row.atUtc}>{dateLabel(row.atUtc)}</time>
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
                        ? 'Session in progress'
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
                      void window.winnow.openExternal(row.update!.url!)
                    }}
                  >
                    Read update
                  </button>
                )}
              </article>
            )
          })}
        </div>
      )}
      {activity.hasNextPage && (
        <button disabled={activity.isFetchingNextPage} onClick={() => void activity.fetchNextPage()}>
          {activity.isFetchingNextPage ? 'Loading…' : 'Earlier activity'}
        </button>
      )}
      {editing != null && <JournalEditor sessionId={editing} onClose={() => setEditing(null)} />}
    </section>
  )
}

function period(days: number) {
  const until = new Date()
  const from = new Date(until)
  from.setDate(from.getDate() - days)
  return { fromUtc: from.toISOString(), untilUtc: until.toISOString() }
}
