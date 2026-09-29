import { useState } from 'react'
import { ApiError, dateLabel, storeLabel } from '../api/client'
import { useApiQuery, useCommand, useLibrary, useWorkspace } from '../api/hooks'
import type { GameList, IdentityReview, LibraryFilter, Mode, Workspace } from '../api/types'
import { useViewState } from '../viewState'
import { Empty, Notice } from './shared'
import './parity-library.css'
import { useInlineEditorFocus } from './parity-library-focus'
import { MergeQueue } from './parity-merge'
import { MergeRefresh } from './parity-merge-refresh'
import { useIdentityReview } from './parity-merge-query'

type Facet = { id: number; kind: string; slug: string; name: string }
type FilterWorkspace = Workspace & { facets?: Facet[]; ownerships?: { store: string }[] }
const bucketNames: Record<string, string> = {
  never_played: 'Never played',
  bounced: 'Bounced',
  stale_but_patched: 'Patched since playing',
  active: 'Active',
  retired: 'Retired',
  derelict: 'Derelict',
}

export function LiveFilterFields({
  filter,
  onChange,
  disabled = false,
  legend = 'Live list filters',
}: {
  filter: LibraryFilter
  onChange: (filter: LibraryFilter) => void
  disabled?: boolean
  legend?: string
}) {
  const workspace = useWorkspace()
  const facts = workspace.data as FilterWorkspace | undefined
  const patch = (value: LibraryFilter) => onChange({ ...filter, ...value })
  const stores = [
    ...new Set([
      'steam',
      'epic',
      'gog',
      'manual',
      ...(filter.stores ?? []),
      ...(facts?.ownerships?.map((entry) => entry.store) ?? []),
    ]),
  ]
  const groups = [
    ['genreIds', 'genre', 'Genres'],
    ['themeIds', 'theme', 'Themes'],
    ['tagIds', 'tag', 'Tags'],
    ['featureIds', 'feature', 'Features'],
    ['controllerIds', 'controller', 'Controller support'],
    ['gameModes', 'game_mode', 'Player modes'],
  ] as const
  return (
    <fieldset disabled={disabled} className="editor-form">
      <legend>{legend}</legend>
      <label className="field">
        Title contains
        <input value={filter.search ?? ''} onChange={(e) => patch({ search: e.target.value || null })} />
      </label>
      <div className="form-row">
        <label className="field">
          Installation
          <select
            value={filter.installed == null ? '' : String(filter.installed)}
            onChange={(e) => patch({ installed: e.target.value === '' ? null : e.target.value === 'true' })}
          >
            <option value="">Any installation</option>
            <option value="true">Installed</option>
            <option value="false">Not installed</option>
          </select>
        </label>
        <label className="field">
          Update status
          <select
            value={filter.hasUnread == null ? '' : String(filter.hasUnread)}
            onChange={(e) => patch({ hasUnread: e.target.value === '' ? null : e.target.value === 'true' })}
          >
            <option value="">Any update status</option>
            <option value="true">Unread updates</option>
            <option value="false">No unread updates</option>
          </select>
        </label>
      </div>
      <div className="form-row">
        {(['yearFrom', 'yearTo'] as const).map((key) => (
          <label className="field" key={key}>
            {key === 'yearFrom' ? 'Released from' : 'Released through'}
            <input
              type="number"
              min={1000}
              max={9999}
              value={typeof filter[key] === 'number' ? filter[key] : ''}
              onChange={(e) => patch({ [key]: e.target.value ? Number(e.target.value) : null })}
            />
          </label>
        ))}
      </div>
      <details>
        <summary>Stores and library status</summary>
        {stores.map((store) => (
          <label key={store} className="check-field">
            <input
              type="checkbox"
              checked={filter.stores?.includes(store) ?? false}
              onChange={(e) =>
                patch({
                  stores: e.target.checked
                    ? [...(filter.stores ?? []), store]
                    : filter.stores?.filter((value) => value !== store),
                })
              }
            />
            {storeLabel(store)}
          </label>
        ))}
        {Object.entries(bucketNames).map(([key, label]) => (
          <label key={key} className="check-field">
            <input
              type="checkbox"
              checked={filter.buckets?.includes(key) ?? false}
              onChange={(e) =>
                patch({
                  buckets: e.target.checked
                    ? [...(filter.buckets ?? []), key]
                    : filter.buckets?.filter((value) => value !== key),
                })
              }
            />
            {label}
          </label>
        ))}
      </details>
      {groups.map(([key, kind, label]) => {
        const selected = (filter[key] ?? []) as (number | string)[]
        const facets = facts?.facets?.filter((facet) => facet.kind === kind) ?? []
        const missing = selected.filter(
          (value) => !facets.some((facet) => (key === 'gameModes' ? facet.slug : facet.id) === value),
        )
        if (!facets.length && !missing.length) return null
        return (
          <details key={key}>
            <summary>
              {label}
              {selected.length ? ` · ${selected.length}` : ''}
            </summary>
            {facets.map((facet) => {
              const value = key === 'gameModes' ? facet.slug : facet.id
              return (
                <label className="check-field" key={facet.id}>
                  <input
                    type="checkbox"
                    checked={selected.includes(value)}
                    onChange={(e) =>
                      patch({
                        [key]: e.target.checked
                          ? [...selected, value]
                          : selected.filter((item) => item !== value),
                      })
                    }
                  />
                  {facet.name}
                </label>
              )
            })}
            {missing.map((value) => (
              <label className="check-field" key={`missing:${value}`}>
                <input
                  type="checkbox"
                  checked
                  onChange={() => patch({ [key]: selected.filter((item) => item !== value) })}
                />
                Unavailable saved filter ({value})
              </label>
            ))}
          </details>
        )
      })}
      <Notice error={workspace.error} />
    </fieldset>
  )
}

export function LiveListFilterEditor({ list }: { list: GameList }) {
  const [draft, setDraft] = useViewState<{
    filter: LibraryFilter
    revision: string
    sending: boolean
  } | null>(`draft:list-filter:${list.id}`, null)
  const focus = useInlineEditorFocus(Boolean(draft))
  const command = useCommand<GameList>()
  const changed = Boolean(draft && draft.revision !== list.revision)
  async function save() {
    if (!draft || draft.sending || changed) return
    setDraft({ ...draft, sending: true })
    try {
      await command.mutateAsync({
        route: 'list.filter',
        params: { listId: list.id },
        body: { filter: draft.filter, expectedRevision: draft.revision },
      })
      setDraft(null)
    } catch {
      setDraft((value) => (value ? { ...value, sending: false } : null))
    }
  }
  return (
    <div>
      {!draft ? (
        <button
          ref={focus.trigger}
          onClick={() => {
            command.reset()
            setDraft({ filter: list.filter ?? {}, revision: list.revision, sending: false })
          }}
        >
          Edit live filters
        </button>
      ) : (
        <form
          ref={focus.editor}
          onSubmit={(e) => {
            e.preventDefault()
            void save()
          }}
        >
          <LiveFilterFields
            filter={draft.filter}
            disabled={draft.sending}
            onChange={(filter) => setDraft({ ...draft, filter })}
          />
          {(changed || (command.error instanceof ApiError && command.error.conflict)) && (
            <div className="conflict-panel">
              <p>This list changed elsewhere. Your filter choices are preserved.</p>
              <button
                type="button"
                disabled={draft.sending || !changed}
                onClick={() => {
                  setDraft({ ...draft, revision: list.revision })
                  command.reset()
                }}
              >
                Keep my filters for the next save
              </button>
            </div>
          )}
          <div className="form-actions">
            <button
              disabled={
                draft.sending || changed || (command.error instanceof ApiError && command.error.conflict)
              }
            >
              {draft.sending ? 'Saving filters…' : 'Save live filters'}
            </button>
            <button type="button" disabled={draft.sending} onClick={() => setDraft(null)}>
              Cancel filter changes
            </button>
          </div>
        </form>
      )}
      <Notice error={command.error} />
    </div>
  )
}

export function ListMembers({ list }: { list: GameList }) {
  const library = useLibrary()
  const command = useCommand()
  const entries =
    library.data?.games.flatMap((game) => game.entries.map((entry) => ({ ...entry, title: game.title }))) ??
    []
  const [sending, setSending] = useViewState(`list-members:${list.id}:sending`, false)
  async function change(releaseId: number, direction: -1 | 1 | 'remove') {
    if (sending) return
    const ids = [...list.releaseIds]
    const index = ids.indexOf(releaseId)
    if (index < 0) return
    if (direction !== 'remove') {
      const next = index + direction
      if (next < 0 || next >= ids.length) return
      ;[ids[index], ids[next]] = [ids[next], ids[index]]
    }
    setSending(true)
    try {
      await command.mutateAsync({
        route: direction === 'remove' ? 'list.member.remove' : 'list.order',
        params: { listId: list.id },
        body: { expectedRevision: list.revision, releaseIds: direction === 'remove' ? [releaseId] : ids },
      })
    } catch {
      /* The normal list refresh supplies the revision needed for an explicit retry. */
    } finally {
      setSending(false)
    }
  }
  return (
    <details>
      <summary>Games in this list · {list.releaseIds.length}</summary>
      {!list.releaseIds.length && <p className="muted">Open a game to add it to this list.</p>}
      {list.releaseIds.map((releaseId, index) => {
        const entry = entries.find((entry) => entry.releaseId === releaseId)
        const title = entry?.title ?? `Edition ${releaseId}`
        return (
          <article className="metadata-row" key={releaseId}>
            <div>
              <strong>{title}</strong>
              {entry && <p>{storeLabel(entry.store)}</p>}
            </div>
            {!list.isLive && (
              <div className="form-actions">
                <button
                  aria-label={`Move ${title} earlier`}
                  disabled={sending || index === 0}
                  onClick={() => void change(releaseId, -1)}
                >
                  Move up
                </button>
                <button
                  aria-label={`Move ${title} later`}
                  disabled={sending || index === list.releaseIds.length - 1}
                  onClick={() => void change(releaseId, 1)}
                >
                  Move down
                </button>
                <button
                  aria-label={`Remove ${title} from ${list.name}`}
                  disabled={sending}
                  onClick={() => void change(releaseId, 'remove')}
                >
                  Remove
                </button>
              </div>
            )}
          </article>
        )
      })}
      <Notice error={command.error} />
    </details>
  )
}

interface IdentityLink {
  id: number
  actId: number
  parentWorkId: number
  childWorkId: number
  kind: string
  relationLabel?: string | null
  retractedAt?: string | null
  appliedAt: string
}
interface Review extends IdentityReview {
  history: IdentityLink[]
  expansions: {
    base: { workId: number; title: string }
    members: {
      work: { workId: number; title: string }
      kind: string
      relationLabel?: string | null
      fromMetadata: boolean
    }[]
  }[]
  workspace: IdentityReview['workspace'] & {
    preferredHeaderStores?: Record<string, string | null>
    ownerships?: { releaseId: number; store: string }[]
  }
}
type Undo = {
  actIds: number[]
  candidateIds: number[]
  refusedPairs: { baseWorkId: number; childWorkId: number }[]
  revision: string
}
type LinkDraft = { parent: number; children: number[]; kind: string; label: string; revision: string }

export function IdentityTools({ onOpenGame, mode = 'desktop' }: { onOpenGame?: (workId: number) => void; mode?: Mode } = {}) {
  const review = useIdentityReview<Review>()
  const command = useCommand<{ revision: string; actId?: number; truncated?: boolean }>()
  const [draft, setDraft] = useViewState<LinkDraft | null>('draft:identity-link', null)
  const [undo, setUndo] = useViewState<Undo | null>('identity:last-undo', null)
  const [search, setSearch] = useState('')
  const [message, setMessage] = useState('')
  const [confirmSeparate, setConfirmSeparate] = useState<number | null>(null)
  const [historyOpen, setHistoryOpen] = useState(false)
  const [refreshBusy, setRefreshBusy] = useState(false)
  const [queueWriting] = useViewState('identity:queue-busy', false)
  const queueBusy = queueWriting || refreshBusy
  const facts = review.data
  const works = facts?.workspace.works ?? []
  const title = (id: number) => works.find((work) => work.id === id)?.name ?? `Game ${id}`
  const changed = Boolean(draft && facts?.revision !== draft.revision)
  async function mutate(route: string, body: Record<string, unknown>, undoable?: Omit<Undo, 'revision'>) {
    if (command.isPending || queueBusy || !facts) return
    setMessage('')
    try {
      const result = await command.mutateAsync({ route, body: { expectedRevision: facts.revision, ...body } })
      if (undoable || result?.actId)
        setUndo({
          ...(undoable ?? { actIds: [result.actId!], candidateIds: [], refusedPairs: [] }),
          revision: result.revision,
        })
      setMessage(
        route === 'identity.undo'
          ? 'Decision undone.'
          : route === 'identity.refresh'
            ? result?.truncated
              ? 'Suggestions refreshed. Check again to look for more matches.'
              : 'Suggestions refreshed. Your previous answers are kept.'
            : 'Library relationship saved.',
      )
      if (route === 'identity.link') setDraft(null)
      if (route === 'identity.undo') setUndo(null)
    } catch {
      /* Keep the decision visible until its conflict is resolved. */
    }
  }
  function prepare(parent: number, children: number[], kind = 'same_game', label = '') {
    if (!facts) return
    command.reset()
    setDraft({ parent, children, kind, label, revision: facts.revision })
  }
  return (
    <>
      <section className="feature-panel">
        <div className="feature-heading">
          <div>
            <h2>Are these the same game?</h2>
            <p>Similar names are suggestions. You decide whether editions belong together.</p>
          </div>
          <MergeRefresh disabled={command.isPending || queueBusy} onBusy={setRefreshBusy} />
        </div>
        <Notice error={review.error || command.error} message={message} />
        {command.isPending && (
          <p role="status">
            {command.variables?.route === 'identity.refresh'
              ? 'Checking your library for matches…'
              : 'Saving your decision…'}
          </p>
        )}
        {!facts?.hasCompletedSweep && (
          <p className="muted">The first suggestion scan has not finished yet.</p>
        )}
        {undo && (
          <button
            disabled={command.isPending || queueBusy}
            onClick={() => void mutate('identity.undo', { ...undo, expectedRevision: undo.revision })}
          >
            Undo last decision
          </button>
        )}
        {command.error instanceof ApiError && command.error.conflict && (
          <div className="conflict-panel">
            <p>The saved relationships changed. Refresh and review your choices before saving again.</p>
            <button
              onClick={async () => {
                const refreshed = await review.refetch()
                if (!refreshed.error && refreshed.data) {
                  setUndo((current) => current && { ...current, revision: refreshed.data.revision })
                  command.reset()
                }
              }}
            >
              Refresh relationships
            </button>
          </div>
        )}
        {facts && (
          <MergeQueue
            mode={mode}
            review={facts}
            onReview={prepare}
            onOpenGame={onOpenGame}
            disabled={command.isPending || queueBusy || refreshBusy}
          />
        )}
      </section>
      <section className="feature-panel">
        <h2>Group editions and related games</h2>
        <p>Choose a main game and the editions, expansions or demos that belong with it.</p>
        {!draft ? (
          <button disabled={!facts || command.isPending || queueBusy} onClick={() => prepare(0, [])}>
            Create a relationship
          </button>
        ) : (
          <form
            onSubmit={(e) => {
              e.preventDefault()
              if (!changed)
                void mutate('identity.link', {
                  expectedRevision: draft.revision,
                  parentWorkId: draft.parent,
                  childWorkIds: draft.children,
                  kind: draft.kind,
                  relationLabel: draft.label || null,
                  rejectedCandidateIds: [],
                  refusedPairs: [],
                })
            }}
          >
            <fieldset disabled={command.isPending || queueBusy} className="editor-form">
              <legend>Review this relationship</legend>
              <label className="field">
                Main game
                <select
                  required
                  value={draft.parent || ''}
                  onChange={(e) =>
                    setDraft({
                      ...draft,
                      parent: Number(e.target.value),
                      children: draft.children.filter((id) => id !== Number(e.target.value)),
                    })
                  }
                >
                  <option value="">Choose a game</option>
                  {works.map((work) => (
                    <option key={work.id} value={work.id}>
                      {work.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                Relationship
                <select
                  value={draft.kind}
                  onChange={(e) => setDraft({ ...draft, kind: e.target.value, label: '' })}
                >
                  <option value="same_game">Same game sold in different editions</option>
                  <option value="expansion_of">Expansion or add-on</option>
                  <option value="variant_of">Demo, beta or playtest</option>
                </select>
              </label>
              {draft.kind !== 'same_game' && (
                <label className="field">
                  Relationship label
                  <input
                    value={draft.label}
                    maxLength={100}
                    onChange={(e) => setDraft({ ...draft, label: e.target.value })}
                    placeholder={draft.kind === 'variant_of' ? 'demo' : 'expansion'}
                  />
                </label>
              )}
              <label className="field">
                Find games to include
                <input value={search} onChange={(e) => setSearch(e.target.value)} />
              </label>
              <div role="group" aria-label="Games to include">
                {works
                  .filter(
                    (work) =>
                      work.id !== draft.parent &&
                      (draft.children.includes(work.id) ||
                        work.name.toLocaleLowerCase().includes(search.toLocaleLowerCase())),
                  )
                  .map((work) => (
                    <label className="check-field" key={work.id}>
                      <input
                        type="checkbox"
                        checked={draft.children.includes(work.id)}
                        onChange={(e) =>
                          setDraft({
                            ...draft,
                            children: e.target.checked
                              ? [...draft.children, work.id]
                              : draft.children.filter((id) => id !== work.id),
                          })
                        }
                      />
                      {work.name}
                    </label>
                  ))}
              </div>
              <p>
                {draft.kind === 'same_game'
                  ? 'These editions will share one library identity.'
                  : 'Related games keep their own playtime and recommendations.'}
              </p>
              {changed && (
                <div className="conflict-panel">
                  <p>Relationships changed while you were choosing. Your choices are preserved.</p>
                  <button
                    type="button"
                    onClick={() => {
                      setDraft({ ...draft, revision: facts!.revision })
                      command.reset()
                    }}
                  >
                    Use these choices with the refreshed library
                  </button>
                </div>
              )}
              <div className="form-actions">
                <button disabled={changed || !draft.parent || !draft.children.length}>
                  Confirm relationship
                </button>
                <button type="button" onClick={() => setDraft(null)}>
                  Cancel relationship
                </button>
              </div>
            </fieldset>
          </form>
        )}
      </section>
      <section className="feature-panel">
        <h2>Saved relationships</h2>
        {!facts?.history?.some((link) => !link.retractedAt) && <Empty>No saved relationships.</Empty>}
        {facts?.history
          ?.filter((link) => !link.retractedAt)
          .map((link) => (
            <article className="identity-row" key={link.id}>
              <div>
                <h3>{title(link.parentWorkId)}</h3>
                <p>
                  {title(link.childWorkId)} · {link.relationLabel ?? link.kind.replaceAll('_', ' ')}
                </p>
              </div>
              <button disabled={command.isPending || queueBusy} onClick={() => setConfirmSeparate(link.id)}>
                Separate…
              </button>
              {confirmSeparate === link.id && (
                <div className="conflict-panel">
                  <p>
                    Separate {title(link.childWorkId)} from {title(link.parentWorkId)}? Both games keep their
                    library data.
                  </p>
                  <button
                    disabled={command.isPending || queueBusy}
                    onClick={async () => {
                      try {
                        await command.mutateAsync({
                          route: 'identity.separate',
                          params: { childWorkId: link.childWorkId },
                          body: { expectedLinkId: link.id },
                        })
                        setConfirmSeparate(null)
                      } catch {
                        /* Keep confirmation visible. */
                      }
                    }}
                  >
                    Separate games
                  </button>
                  <button onClick={() => setConfirmSeparate(null)}>Keep relationship</button>
                </div>
              )}
            </article>
          ))}
        {[
          ...new Set(
            facts?.history
              ?.filter((link) => !link.retractedAt && link.kind === 'same_game')
              .map((link) => link.parentWorkId),
          ),
        ].map((workId) => {
          const groupIds = new Set([
            workId,
            ...(facts?.history
              ?.filter((link) => !link.retractedAt && link.parentWorkId === workId)
              .map((link) => link.childWorkId) ?? []),
          ])
          const releases = new Set(
            facts?.workspace.releases
              .filter((release) => groupIds.has(release.workId))
              .map((release) => release.id),
          )
          const stores = [
            ...new Set(
              facts?.workspace.ownerships
                ?.filter((entry) => releases.has(entry.releaseId))
                .map((entry) => entry.store),
            ),
          ]
          return (
            <label className="field" key={workId}>
              Header edition for {title(workId)}
              <select
                disabled={command.isPending || queueBusy}
                value={facts?.workspace.preferredHeaderStores?.[String(workId)] ?? ''}
                onChange={(e) => void mutate('identity.header', { workId, store: e.target.value || null })}
              >
                <option value="">Automatic</option>
                {stores.map((store) => (
                  <option key={store} value={store}>
                    {storeLabel(store)}
                  </option>
                ))}
              </select>
            </label>
          )
        })}
        <button className="text-button" onClick={() => setHistoryOpen(!historyOpen)}>
          {historyOpen ? 'Hide relationship history' : 'Show relationship history'}
        </button>
        {historyOpen &&
          facts?.history?.map((link) => (
            <article className="metadata-row" key={link.id}>
              <p>
                {title(link.childWorkId)} → {title(link.parentWorkId)} · {dateLabel(link.appliedAt)} ·{' '}
                {link.retractedAt ? 'Retracted' : 'Current'}
              </p>
              {!link.retractedAt && (
                <button
                  disabled={command.isPending || queueBusy}
                  onClick={() =>
                    void mutate('identity.undo', { actIds: [link.actId], candidateIds: [], refusedPairs: [] })
                  }
                >
                  Undo relationship group
                </button>
              )}
            </article>
          ))}
      </section>
    </>
  )
}
