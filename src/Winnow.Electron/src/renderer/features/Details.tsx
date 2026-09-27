import { useState } from 'react'
import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Play, Download, RefreshCw } from 'lucide-react'
import { ApiError, dateLabel, hours, launchMessage, primaryAction, request, storeLabel } from '../api/client'
import { useApiQuery, useCommand, useDetails, useLibrary, useWorkspace } from '../api/hooks'
import type { ArtworkPage, ArtworkState, GameEntry, Metadata, Mode, Workspace } from '../api/types'
import { JournalEditor, SessionRows } from './Journal'
import { Empty, Notice } from './shared'
import { Artwork } from '../components/Artwork'
import { useViewState } from '../viewState'

export function EntryActions({ entry, workspace }: { entry: GameEntry; workspace?: Workspace }) {
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [attempt, setAttempt] = useState<{ operationId: string; action: string } | null>(null)
  const action = primaryAction(entry, workspace)
  async function dispatch(kind: string, reuse = false) {
    const operation = reuse && attempt ? attempt : { operationId: crypto.randomUUID(), action: kind }
    setAttempt(operation)
    setPending(true)
    setError(null)
    setMessage('')
    try {
      setMessage(
        launchMessage(
          await request<number>('actions.execute', { ownershipId: entry.ownershipId }, operation),
        ),
      )
      setAttempt(null)
    } catch (failure) {
      setError(failure)
      if (!(failure instanceof ApiError) || !failure.uncertain) setAttempt(null)
    } finally {
      setPending(false)
    }
  }
  return (
    <div className="entry-actions">
      <div>
        <strong>{storeLabel(entry.store)}</strong>
        <span>
          {entry.installed ? 'Installed' : 'Not installed'} · {hours(entry.playtimeMinutes)}
        </span>
      </div>
      {action ? (
        <button
          className="primary-button"
          disabled={pending || Boolean(attempt)}
          onClick={() => void dispatch(action)}
        >
          {action === 'Play' ? <Play size={16} /> : <Download size={16} />}
          {pending ? 'Sending…' : action}
        </button>
      ) : (
        <p className="muted">
          {entry.store === 'manual'
            ? 'Manual entries can be tracked here. Launch this game from its shortcut.'
            : 'No supported launch action is available for this entry.'}
        </p>
      )}
      {entry.store === 'steam' && entry.installed && (
        <button disabled={pending || Boolean(attempt)} onClick={() => void dispatch('Uninstall')}>
          Uninstall in Steam
        </button>
      )}
      {(entry.store === 'gog' || entry.store === 'epic') && (
        <button disabled={pending || Boolean(attempt)} onClick={() => void dispatch('Manage')}>
          Manage in launcher
        </button>
      )}
      {entry.store.startsWith('plugin:') &&
        workspace?.pluginActions[String(entry.ownershipId)]?.canOpenStore && (
          <button disabled={pending || Boolean(attempt)} onClick={() => void dispatch('OpenStore')}>
            Open store
          </button>
        )}
      <Notice error={error} message={message} />
      {attempt && !pending && (
        <div className="conflict-panel">
          <p>The response was interrupted. The launcher may already have received the action.</p>
          <button onClick={() => void dispatch(attempt.action, true)}>Check the same action again</button>
          <button
            onClick={() => {
              setAttempt(null)
              setError(null)
            }}
          >
            Dismiss
          </button>
        </div>
      )}
    </div>
  )
}

export function Details({
  workId,
  mode = 'desktop',
  onClose,
}: {
  workId: number
  mode?: Mode
  onClose?: () => void
}) {
  const library = useLibrary()
  const workspace = useWorkspace()
  const details = useDetails(workId)
  const game = library.data?.games.find((item) => item.workId === workId)
  const [tab, setTab] = useViewState(`${mode}:details:${workId}:tab`, 'Overview')
  const [editing, setEditing] = useViewState<number | null>(`${mode}:details:${workId}:editing`, null)
  const command = useCommand()
  const sessions = Object.values(details.data?.sessions ?? {})
    .flat()
    .sort((a, b) => b.startedAt.localeCompare(a.startedAt))
  return (
    <section className={`feature-page details-page mode-${mode}`}>
      {onClose && (
        <button className="back-button" onClick={onClose}>
          <ArrowLeft size={16} /> Back to your library
        </button>
      )}
      <Artwork workId={workId} hero eager className="detail-hero" />
      <header className="feature-heading">
        <div>
          <p className="eyebrow">{game?.bucket.replaceAll('_', ' ') ?? 'YOUR LIBRARY'}</p>
          <h1>{game?.title ?? 'Game details'}</h1>
          <p>{[game?.firstReleaseYear, game?.publisher].filter(Boolean).join(' · ')}</p>
        </div>
        <button
          disabled={command.isPending}
          onClick={() => command.mutate({ route: 'game.refetch', params: { workId } })}
        >
          <RefreshCw size={16} /> Refresh metadata
        </button>
      </header>
      <Notice error={details.error || library.error || command.error} />
      <nav className="tabs" aria-label="Game information">
        {['Overview', 'History', 'Journal', 'Metadata', 'Artwork'].map((name) => (
          <button key={name} aria-pressed={name === tab} onClick={() => setTab(name)}>
            {name}
          </button>
        ))}
      </nav>
      <div className="detail-body">
        <div className="detail-main">
          {tab === 'Overview' && (
            <>
              <section className="feature-panel">
                <h2>About the game</h2>
                <p className="game-summary">
                  {game?.summary ?? 'There is no description for this game yet.'}
                </p>
                <div className="stat-strip">
                  <div className="stat">
                    <strong>{game ? hours(game.playtimeMinutes) : '—'}</strong>
                    <span>Store playtime</span>
                  </div>
                  <div className="stat">
                    <strong>{game?.lastPlayedAt ? dateLabel(game.lastPlayedAt) : 'Not recorded'}</strong>
                    <span>Last played</span>
                  </div>
                </div>
              </section>
              {!!details.data?.ratings.filter((rating) => rating.hasFigure).length && (
                <section className="feature-panel">
                  <h2>What players say</h2>
                  {details.data.ratings
                    .filter((rating) => rating.hasFigure)
                    .map((rating) => (
                      <p key={rating.source}>
                        <strong>{rating.label ?? `${rating.score?.toFixed(0)} / 100`}</strong> ·{' '}
                        {rating.source} · {rating.ratingCount?.toLocaleString()} ratings
                      </p>
                    ))}
                </section>
              )}
              {!!details.data?.achievements.length && (
                <section className="feature-panel">
                  <h2>Achievements by edition</h2>
                  {details.data.achievements.map((item) => (
                    <p key={item.releaseId}>
                      {storeLabel(
                        game?.entries.find((entry) => entry.releaseId === item.releaseId)?.store ?? 'Edition',
                      )}
                      :{' '}
                      {item.hasKnownProgress && item.total > 0
                        ? `${item.unlocked} of ${item.total} unlocked`
                        : 'Progress unavailable'}
                      {item.isStale && ' · Last known reading'}
                    </p>
                  ))}
                </section>
              )}
              <section className="feature-panel">
                <h2>Updates</h2>
                {!details.data?.events.length ? (
                  <Empty>No update signals recorded.</Empty>
                ) : (
                  details.data.events.map((event) => (
                    <article className="update-row" key={event.id}>
                      <div>
                        <time>{dateLabel(event.occurredAt)}</time>
                        <h3>{event.title ?? event.kind.replaceAll('_', ' ')}</h3>
                      </div>
                      {event.url && (
                        <button
                          onClick={() => {
                            void window.winnow.openExternal(event.url!)
                          }}
                        >
                          Read
                        </button>
                      )}
                    </article>
                  ))
                )}
                {!!details.data?.events.length && (
                  <button
                    disabled={command.isPending}
                    onClick={() => {
                      for (const releaseId of new Set(details.data!.events.map((event) => event.releaseId)))
                        command.mutate({
                          route: 'updates.acknowledge',
                          params: { releaseId },
                          body: {
                            observedEventIds: details
                              .data!.events.filter((event) => event.releaseId === releaseId)
                              .map((event) => event.id),
                          },
                        })
                    }}
                  >
                    Mark these updates read
                  </button>
                )}
              </section>
            </>
          )}
          {tab === 'History' && (
            <section className="feature-panel">
              <h2>Recorded sessions</h2>
              <SessionRows sessions={sessions} onEdit={setEditing} />
            </section>
          )}
          {tab === 'Journal' && (
            <section className="feature-panel">
              <h2>Your notes</h2>
              {!details.data?.journalEntries.length ? (
                <Empty>No notes yet. Add one to a recorded session from History.</Empty>
              ) : (
                details.data.journalEntries.map((note) => (
                  <article className="timeline-entry" key={note.sessionId}>
                    <time>{dateLabel(note.sessionAt)}</time>
                    <div>
                      <blockquote>{note.note || 'A session to remember.'}</blockquote>
                      {note.rating && <p>{note.rating} / 5</p>}
                    </div>
                    <button onClick={() => setEditing(note.sessionId)}>Edit note</button>
                  </article>
                ))
              )}
            </section>
          )}
          {tab === 'Metadata' && <MetadataEditor key={workId} workId={workId} />}
          {tab === 'Artwork' && <ArtworkEditor key={workId} workId={workId} />}
        </div>
        <aside className="detail-sidebar">
          <section className="feature-panel">
            <p className="eyebrow">YOUR EDITIONS</p>
            {game?.entries.map((entry) => (
              <EntryActions key={entry.ownershipId} entry={entry} workspace={workspace.data} />
            ))}
          </section>
          <ListMembership workId={workId} />
          <HideGame key={workId} workId={workId} onHidden={onClose} />
        </aside>
      </div>
      {editing != null && <JournalEditor sessionId={editing} onClose={() => setEditing(null)} />}
    </section>
  )
}

export function HideGame({ workId, onHidden }: { workId: number; onHidden?: () => void }) {
  const [confirming, setConfirming] = useState(false)
  const command = useCommand()
  async function hide() {
    try {
      await command.mutateAsync({ route: 'hidden.put', body: { workIds: [workId], hidden: true } })
      setConfirming(false)
      onHidden?.()
    } catch {
      /* Keep the confirmation and render the returned error. */
    }
  }
  return (
    <section className="feature-panel">
      <h2>Keep your library yours</h2>
      {confirming ? (
        <div className="conflict-panel">
          <p>
            Hide this game from your library? Its history stays saved. You can restore it from Library tools.
          </p>
          <div className="form-actions">
            <button disabled={command.isPending} onClick={() => void hide()}>
              Hide game
            </button>
            <button disabled={command.isPending} onClick={() => setConfirming(false)}>
              Keep visible
            </button>
          </div>
        </div>
      ) : (
        <button onClick={() => setConfirming(true)}>Hide game…</button>
      )}
      <Notice error={command.error} />
    </section>
  )
}

function ListMembership({ workId }: { workId: number }) {
  const library = useLibrary()
  const command = useCommand()
  const game = library.data?.games.find((item) => item.workId === workId)
  return (
    <section className="feature-panel">
      <h2>In your lists</h2>
      {!library.data?.lists.length ? (
        <p className="muted">Create a list in Library tools.</p>
      ) : (
        library.data.lists.map((list) => {
          const member = game?.entries.some((entry) => list.releaseIds.includes(entry.releaseId)) ?? false
          return (
            <label className="check-field" key={list.id}>
              <input
                type="checkbox"
                checked={member}
                disabled={list.isLive || command.isPending || !game}
                onChange={() =>
                  command.mutate({
                    route: member ? 'list.member.remove' : 'list.member.add',
                    params: { listId: list.id },
                    body: {
                      releaseIds: game!.entries.map((entry) => entry.releaseId),
                      expectedRevision: list.revision,
                    },
                  })
                }
              />
              {list.name}
              {list.isLive ? ' · Live list' : ''}
            </label>
          )
        })
      )}
      <Notice error={command.error} />
      {command.error instanceof ApiError && command.error.conflict && (
        <p>The list changed elsewhere. Refresh it before changing membership.</p>
      )}
    </section>
  )
}

function MetadataEditor({ workId }: { workId: number }) {
  const metadata = useApiQuery<Metadata>('metadata.get', { workId })
  const [draft, setDraft] = useViewState<{
    field: string
    value: string
    revision: string
    sending: boolean
  } | null>(`draft:metadata:${workId}`, null)
  const command = useCommand<{ outcome: string }>()
  async function save(reset = false) {
    if (!draft || draft.sending) return
    setDraft({ ...draft, sending: true })
    try {
      const result = await command.mutateAsync({
        route: reset ? 'metadata.reset' : 'metadata.put',
        params: { workId },
        body: { field: draft.field, value: draft.value || null, expectedRevision: draft.revision },
      })
      if (
        result.outcome === 'Saved' ||
        result.outcome === 'Changed' ||
        result.outcome === 'Applied' ||
        result.outcome === 'NoChange'
      )
        setDraft(null)
      else setDraft((previous) => (previous ? { ...previous, sending: false } : null))
    } catch {
      setDraft((previous) => (previous ? { ...previous, sending: false } : null))
    }
  }
  return (
    <section className="feature-panel">
      <h2>Metadata & sources</h2>
      <p className="muted">Your changes override provider metadata.</p>
      <Notice
        error={metadata.error || command.error}
        message={command.data ? `Metadata: ${command.data.outcome}` : undefined}
      />
      {metadata.data?.fields.map((field) => (
        <div className="metadata-row" key={field.field}>
          <div>
            <strong>{field.field.replaceAll('_', ' ')}</strong>
            <p>{field.value || 'Not recorded'}</p>
            <small>{field.source ?? 'No source'}</small>
          </div>
          <button
            disabled={draft?.sending}
            onClick={() => {
              command.reset()
              setDraft({
                field: field.field,
                value: field.value ?? '',
                revision: metadata.data!.revision,
                sending: false,
              })
            }}
          >
            Edit
          </button>
        </div>
      ))}
      {draft && (
        <form
          onSubmit={(event) => {
            event.preventDefault()
            void save()
          }}
          className="editor-form"
        >
          <label className="field">
            {draft.field}
            <textarea
              disabled={draft.sending}
              rows={4}
              value={draft.value}
              onChange={(event) => setDraft({ ...draft, value: event.target.value })}
            />
          </label>
          {command.error instanceof ApiError && command.error.conflict && (
            <div className="conflict-panel">
              <p>
                This game changed elsewhere. Your draft is preserved. Refresh the saved metadata before
                deciding which version to keep.
              </p>
              <button disabled={draft.sending} type="button" onClick={() => void metadata.refetch()}>
                Refresh saved metadata
              </button>
              {metadata.data?.revision !== draft.revision && (
                <button
                  disabled={draft.sending}
                  type="button"
                  onClick={() => {
                    setDraft({ ...draft, revision: metadata.data!.revision })
                    command.reset()
                  }}
                >
                  Keep my draft for the next save
                </button>
              )}
            </div>
          )}
          <div className="form-actions">
            <button
              className="primary-button"
              disabled={
                draft.sending ||
                command.isPending ||
                (command.error instanceof ApiError && command.error.conflict)
              }
            >
              {draft.sending ? 'Saving…' : 'Save field'}
            </button>
            <button
              type="button"
              disabled={draft.sending || command.isPending}
              onClick={() => void save(true)}
            >
              Use provider value
            </button>
            <button type="button" disabled={draft.sending} onClick={() => setDraft(null)}>
              Cancel
            </button>
          </div>
        </form>
      )}
    </section>
  )
}

function ArtworkEditor({ workId }: { workId: number }) {
  const [slot, setSlot] = useViewState(`artwork:${workId}:slot`, 'Hero')
  const [source, setSource] = useViewState(`artwork:${workId}:source`, '')
  const [url, setUrl] = useViewState(`draft:artwork:${workId}:url`, '')
  const state = useApiQuery<ArtworkState>('artwork.get', { workId, slot })
  const sources = useApiQuery<{ id: string; name: string; slots: number[] }[]>('artwork.sources')
  const page = useInfiniteQuery({
    queryKey: ['api', 'artwork.browse', workId, slot, source],
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      request<ArtworkPage>('artwork.browse', {
        workId,
        slot,
        source,
        ...(pageParam ? { cursor: pageParam } : {}),
      }),
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    enabled: Boolean(source),
    retry: false,
    staleTime: 30_000,
  })
  const candidates = page.data?.pages.flatMap((item) => item.items) ?? []
  const browseMessage = page.data?.pages.at(-1)?.message
  const command = useCommand<{ success: boolean; message: string }>()
  const client = useQueryClient()
  async function change(route: string, body: unknown) {
    try {
      await command.mutateAsync({ route, params: { workId, slot }, body })
      await client.invalidateQueries({ queryKey: ['artwork'] })
    } catch {
      /* Render the backend error without repeating the command. */
    }
  }
  return (
    <section className="feature-panel">
      <h2>Make it yours</h2>
      <p className="muted">Choose artwork from your connected sources, or use an image URL.</p>
      <div className="form-row">
        <label className="field">
          Artwork
          <select
            value={slot}
            onChange={(event) => {
              setSlot(event.target.value)
              setSource('')
              command.reset()
            }}
          >
            <option>Hero</option>
            <option>Cover</option>
            <option>Icon</option>
          </select>
        </label>
        <label className="field">
          Source
          <select value={source} onChange={(event) => setSource(event.target.value)}>
            <option value="">Choose a source</option>
            {sources.data
              ?.filter((item) => item.slots.includes(['Hero', 'Cover', 'Icon'].indexOf(slot)))
              .map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
          </select>
        </label>
      </div>
      <Notice
        error={state.error || sources.error || page.error || command.error}
        message={command.data?.message ?? browseMessage}
      />
      <div className="artwork-options">
        {candidates.map((candidate) => (
          <button
            className="artwork-option"
            key={`${candidate.sourceId}:${candidate.assetId}`}
            disabled={command.isPending || !candidate.offerId || !state.data}
            onClick={() =>
              void change('artwork.put', { offerId: candidate.offerId, revision: state.data!.revision })
            }
          >
            <CandidateImage provider={candidate.previewKey.provider} id={candidate.previewKey.id} />
            <span>
              {candidate.creator ?? candidate.sourceName}
              {candidate.isCurrent ? ' · Current' : ''}
            </span>
          </button>
        ))}
      </div>
      {page.hasNextPage && (
        <button disabled={page.isFetchingNextPage} onClick={() => void page.fetchNextPage()}>
          {page.isFetchingNextPage ? 'Loading artwork…' : 'More artwork'}
        </button>
      )}
      {source && !page.isPending && !candidates.length && <Empty>No artwork from this source.</Empty>}
      <form
        className="editor-form"
        onSubmit={(event) => {
          event.preventDefault()
          if (state.data) void change('artwork.url', { url, revision: state.data.revision })
        }}
      >
        <label className="field">
          Image URL
          <input
            type="url"
            required
            value={url}
            onChange={(event) => setUrl(event.target.value)}
            placeholder="https://…"
          />
        </label>
        <div className="form-actions">
          <button disabled={command.isPending || !state.data}>Use image URL</button>
          <button
            type="button"
            disabled={command.isPending || !state.data}
            onClick={() => void change('artwork.reset', { revision: state.data!.revision })}
          >
            Restore automatic artwork
          </button>
        </div>
      </form>
      {command.error instanceof ApiError && command.error.conflict && (
        <button
          onClick={() => {
            void state.refetch()
            command.reset()
          }}
        >
          Refresh the current artwork before choosing again
        </button>
      )}
    </section>
  )
}

function CandidateImage({ provider, id }: { provider: string; id: string }) {
  const image = useApiArtwork(provider, id)
  return image ? (
    <img src={image} alt="Artwork preview" loading="lazy" />
  ) : (
    <span className="art-placeholder">Preview unavailable</span>
  )
}
import { useQuery } from '@tanstack/react-query'
function useApiArtwork(provider: string, id: string) {
  return useQuery({
    queryKey: ['artwork', provider, id, 400],
    queryFn: () => window.winnow.artwork(provider, id, 400),
    retry: false,
    staleTime: Infinity,
  }).data
}
