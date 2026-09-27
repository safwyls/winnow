import { useState } from 'react'
import { ApiError, dateLabel, request } from '../api/client'
import { useApiQuery, useCommand, useLibrary } from '../api/hooks'
import type {
  GameList,
  HiddenGame,
  IdentityReview,
  LibraryFilter,
  LibraryResponse,
  ManualGame,
  Mode,
} from '../api/types'
import { Empty, Notice } from './shared'
import { clearViewState, useViewState } from '../viewState'

export function LibraryTools({
  mode = 'desktop',
  selectedWorkId,
  onOpenGame,
}: {
  mode?: Mode
  selectedWorkId?: number
  onOpenGame?: (workId: number) => void
}) {
  const [tab, setTab] = useViewState(`${mode}:library-tools:tab`, 'Lists')
  const library = useLibrary()
  const command = useCommand()
  return (
    <section className={`feature-page library-tools mode-${mode}`}>
      <header className="feature-heading">
        <div>
          <p className="eyebrow">ROOM FOR YOUR COLLECTION</p>
          <h1>Keep it in order.</h1>
          <p>Make a list, add something personal, or tidy the edges.</p>
        </div>
      </header>
      <nav className="tabs" aria-label="Library tools">
        {['Lists', 'Manual games', 'Hidden games', 'Identity review'].map((name) => (
          <button key={name} aria-pressed={tab === name} onClick={() => setTab(name)}>
            {name}
          </button>
        ))}
      </nav>
      {tab === 'Lists' && (
        <div className="feature-grid">
          <CreateList />
          {library.data?.lists.map((list) => (
            <ListEditor key={list.id} list={list} />
          ))}
        </div>
      )}
      {tab === 'Manual games' && <ManualGames mode={mode} onOpenGame={onOpenGame} />}
      {tab === 'Hidden games' && <HiddenGames />}
      {tab === 'Identity review' && <IdentityTools />}
      {selectedWorkId && (
        <section className="feature-panel">
          <h2>Selected game</h2>
          <p>{library.data?.games.find((game) => game.workId === selectedWorkId)?.title}</p>
          <button
            disabled={command.isPending}
            onClick={() =>
              command.mutate({ route: 'hidden.put', body: { workIds: [selectedWorkId], hidden: true } })
            }
          >
            Hide from library
          </button>
          <Notice error={command.error} />
        </section>
      )}
    </section>
  )
}

export function CreateList() {
  const empty = {
    name: '',
    live: false,
    store: '',
    installed: false,
    search: '',
    sending: false,
    uncertain: false,
    checked: false,
    matches: [] as GameList[],
  }
  const [draft, setDraft] = useViewState('draft:list:new', empty)
  const { name, live, store, installed, search } = draft
  const update = (value: Partial<typeof empty>) => setDraft((previous) => ({ ...previous, ...value }))
  const command = useCommand()
  const [checking, setChecking] = useState(false)
  const [checkError, setCheckError] = useState<unknown>(null)
  const discard = () => {
    setDraft(empty)
    clearViewState('draft:list:new')
    command.reset()
  }
  async function save() {
    if (draft.uncertain || draft.sending) return
    update({ sending: true })
    try {
      await command.mutateAsync({
        route: live ? 'list.live' : 'list.create',
        body: live
          ? {
              name,
              filter: {
                stores: store ? [store] : [],
                installed: installed ? true : null,
                search: search || null,
              },
            }
          : { name, releaseIds: [] },
      })
      setDraft(empty)
      clearViewState('draft:list:new')
    } catch (error) {
      update({
        sending: false,
        ...(!(error instanceof ApiError) || error.uncertain
          ? { uncertain: true, checked: false, matches: [] }
          : {}),
      })
    }
  }
  async function reconcile() {
    setChecking(true)
    setCheckError(null)
    try {
      const saved = await request<LibraryResponse>('library.get')
      update({
        checked: true,
        matches: saved.lists.filter(
          (list) => list.name.trim().toLocaleLowerCase() === name.trim().toLocaleLowerCase(),
        ),
      })
    } catch (error) {
      setCheckError(error)
    } finally {
      setChecking(false)
    }
  }
  return (
    <section className="feature-panel">
      <h2>A new list</h2>
      <form
        onSubmit={(event) => {
          event.preventDefault()
          void save()
        }}
      >
        <label className="field">
          List name
          <input
            required
            maxLength={200}
            disabled={draft.sending || draft.uncertain}
            value={name}
            onChange={(event) => update({ name: event.target.value })}
            placeholder="For a rainy afternoon"
          />
        </label>
        <label className="check-field">
          <input
            type="checkbox"
            disabled={draft.sending || draft.uncertain}
            checked={live}
            onChange={(event) => update({ live: event.target.checked })}
          />
          Keep this list up to date with filters
        </label>
        {live && (
          <>
            <label className="field">
              Store
              <select
                disabled={draft.sending || draft.uncertain}
                value={store}
                onChange={(event) => update({ store: event.target.value })}
              >
                <option value="">Every store</option>
                <option value="steam">Steam</option>
                <option value="epic">Epic Games</option>
                <option value="gog">GOG</option>
                <option value="manual">Manual</option>
              </select>
            </label>
            <label className="field">
              Title contains
              <input
                disabled={draft.sending || draft.uncertain}
                value={search}
                onChange={(event) => update({ search: event.target.value })}
              />
            </label>
            <label className="check-field">
              <input
                type="checkbox"
                disabled={draft.sending || draft.uncertain}
                checked={installed}
                onChange={(event) => update({ installed: event.target.checked })}
              />
              Installed games only
            </label>
          </>
        )}
        {draft.uncertain && (
          <section className="conflict-panel">
            <h3>Check whether this list was created</h3>
            <p>
              The response was interrupted. Your draft is kept here; creating it again could make a duplicate.
            </p>
            <button type="button" disabled={checking} onClick={() => void reconcile()}>
              {checking ? 'Checking saved lists…' : 'Check saved lists'}
            </button>
            {draft.checked && (
              <>
                <p>
                  {draft.matches.length
                    ? 'These saved lists have the same name:'
                    : 'No saved list has this name. Check the library before creating another.'}
                </p>
                {draft.matches.map((list) => (
                  <p key={list.id}>
                    {list.name} · {list.releaseIds.length} editions{' '}
                    <button type="button" onClick={discard}>
                      Use saved list
                    </button>
                  </p>
                ))}
                <button
                  type="button"
                  onClick={() => {
                    update({ uncertain: false, checked: false, matches: [] })
                    command.reset()
                  }}
                >
                  Create another anyway
                </button>
              </>
            )}
            <Notice error={checkError} />
          </section>
        )}
        <div className="form-actions">
          <button
            className="primary-button"
            disabled={command.isPending || draft.sending || draft.uncertain || !name.trim()}
          >
            Create {live ? 'live ' : ''}list
          </button>
          <button type="button" disabled={command.isPending || draft.sending} onClick={discard}>
            Discard draft
          </button>
        </div>
        {draft.sending && <p role="status">Creating your list…</p>}
        <Notice
          error={command.error}
          message={command.isSuccess ? 'List created. View a game to add it to a list.' : undefined}
        />
      </form>
    </section>
  )
}

function ListEditor({ list }: { list: GameList }) {
  const [draft, setDraft] = useViewState<{
    name: string
    description: string
    revision: string
    filter: LibraryFilter
    sending: boolean
  } | null>(`draft:list:${list.id}`, null)
  const [confirm, setConfirm] = useState(false)
  const command = useCommand()
  const changed = draft && list.revision !== draft.revision
  async function save() {
    if (!draft || draft.sending) return
    setDraft({ ...draft, sending: true })
    try {
      await command.mutateAsync({
        route: 'list.update',
        params: { listId: list.id },
        body: { name: draft.name, description: draft.description || null, expectedRevision: draft.revision },
      })
      setDraft(null)
    } catch {
      setDraft((previous) => (previous ? { ...previous, sending: false } : null))
    }
  }
  return (
    <section className="feature-panel">
      <div className="feature-heading">
        <div>
          <h2>{list.name}</h2>
          <p>
            {list.releaseIds.length} editions · {list.isLive ? 'Live list' : 'Handpicked'}
          </p>
        </div>
        <button
          disabled={draft?.sending}
          onClick={() => {
            setDraft({
              name: list.name,
              description: list.description ?? '',
              revision: list.revision,
              filter: list.filter ?? {},
              sending: false,
            })
            command.reset()
          }}
        >
          Edit
        </button>
      </div>
      <p>{list.description}</p>
      {list.isLive && (
        <p className="muted">
          {list.filter?.installed ? 'Installed games' : 'All install states'}
          {list.filter?.stores?.length ? ` · ${list.filter.stores.join(', ')}` : ''}
          {list.filter?.search ? ` · “${list.filter.search}”` : ''}
        </p>
      )}
      {draft && (
        <form
          onSubmit={(event) => {
            event.preventDefault()
            void save()
          }}
        >
          <label className="field">
            List name
            <input
              disabled={draft.sending}
              required
              value={draft.name}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })}
            />
          </label>
          <label className="field">
            Description
            <textarea
              disabled={draft.sending}
              value={draft.description}
              onChange={(event) => setDraft({ ...draft, description: event.target.value })}
            />
          </label>
          {(changed || (command.error instanceof ApiError && command.error.conflict)) && (
            <div className="conflict-panel">
              <p>
                This list changed while you were editing. The saved name is “{list.name}”. Your draft is
                preserved.
              </p>
              <button
                disabled={draft.sending}
                type="button"
                onClick={() => {
                  setDraft({ ...draft, revision: list.revision })
                  command.reset()
                }}
              >
                Keep my draft for the next save
              </button>
              <button disabled={draft.sending} type="button" onClick={() => setDraft(null)}>
                Use saved version
              </button>
            </div>
          )}
          <div className="form-actions">
            <button disabled={draft.sending || command.isPending || Boolean(changed)}>
              {draft.sending ? 'Saving…' : 'Save list'}
            </button>
            <button disabled={draft.sending} type="button" onClick={() => setDraft(null)}>
              Cancel
            </button>
          </div>
        </form>
      )}
      {confirm ? (
        <div className="conflict-panel">
          <p>Delete “{list.name}”? Its games will stay in your library.</p>
          <button
            disabled={draft?.sending || command.isPending}
            onClick={() =>
              command.mutate({
                route: 'list.delete',
                params: { listId: list.id },
                body: { expectedRevision: list.revision },
              })
            }
          >
            Delete list
          </button>
          <button onClick={() => setConfirm(false)}>Keep list</button>
        </div>
      ) : (
        <button disabled={draft?.sending} className="text-button" onClick={() => setConfirm(true)}>
          Delete list…
        </button>
      )}
      <Notice error={command.error} />
    </section>
  )
}

function ManualGames({ mode, onOpenGame }: { mode: Mode; onOpenGame?: (workId: number) => void }) {
  const games = useApiQuery<ManualGame[]>('manual.get')
  const [editing, setEditing] = useViewState<ManualGame | 'new' | null>(`${mode}:manual:editing`, null)
  const [confirm, setConfirm] = useState<number | null>(null)
  const command = useCommand()
  return (
    <>
      <div className="feature-heading">
        <p>
          Keep the games that come from somewhere else. Manual entries can be tracked here; use your game
          shortcut to launch them.
        </p>
        <button className="primary-button" onClick={() => setEditing('new')}>
          Add a game
        </button>
      </div>
      <Notice error={games.error || command.error} />
      {editing && (
        <ManualEditor
          key={editing === 'new' ? 'new' : editing.ownershipId}
          initial={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
        />
      )}
      {!games.data?.length && !editing && <Empty>No manual games yet.</Empty>}
      <div className="feature-grid">
        {games.data?.map((game) => (
          <section className="feature-panel" key={game.ownershipId}>
            <h2>
              <button className="text-button" onClick={() => onOpenGame?.(game.workId)}>
                {game.title}
              </button>
            </h2>
            <p>{[game.firstReleaseYear, game.platformLabel].filter(Boolean).join(' · ')}</p>
            <div className="form-actions">
              <button onClick={() => setEditing(game)}>Edit game</button>
              <button onClick={() => setConfirm(game.ownershipId)}>Remove…</button>
            </div>
            {confirm === game.ownershipId && (
              <div className="conflict-panel">
                <p>Remove this manual entry and its recorded library data?</p>
                <button
                  disabled={command.isPending}
                  onClick={() =>
                    command.mutate({ route: 'manual.delete', params: { ownershipId: game.ownershipId } })
                  }
                >
                  Remove entry
                </button>
                <button onClick={() => setConfirm(null)}>Keep game</button>
              </div>
            )}
          </section>
        ))}
      </div>
    </>
  )
}

export function ManualEditor({ initial, onClose }: { initial: ManualGame | null; onClose: () => void }) {
  const key = `draft:manual:${initial?.ownershipId ?? 'new'}`
  const [draft, setDraft] = useViewState(key, {
    title: initial?.title ?? '',
    year: initial?.firstReleaseYear?.toString() ?? '',
    platform: initial?.platformLabel ?? '',
    base: initial,
    current: null as ManualGame | null,
    sending: false,
    uncertain: false,
    checked: false,
    matches: [] as ManualGame[],
  })
  const { title, year, platform, base, current } = draft
  const update = (value: Partial<typeof draft>) => setDraft((previous) => ({ ...previous, ...value }))
  const command = useCommand()
  const [checking, setChecking] = useState(false)
  const [checkError, setCheckError] = useState<unknown>(null)
  const close = () => {
    clearViewState(key)
    onClose()
  }
  async function save() {
    if (draft.uncertain || draft.sending) return
    update({ sending: true })
    try {
      await command.mutateAsync({
        route: initial ? 'manual.update' : 'manual.create',
        params: initial ? { ownershipId: initial.ownershipId } : undefined,
        body: {
          title,
          firstReleaseYear: year ? Number(year) : null,
          platformLabel: platform || null,
          executablePath: base?.executablePath,
          installPath: base?.installPath,
          igdbId: base?.igdbId,
          steamAppId: base?.steamAppId,
          expectedRevision: base?.revision,
          expectedIgdbMappingRevision: base?.igdbMappingRevision,
        },
      })
      close()
    } catch (error) {
      update({ sending: false })
      if (initial && error instanceof ApiError && error.conflict) {
        const all = await request<ManualGame[]>('manual.get')
        update({ current: all.find((game) => game.ownershipId === initial.ownershipId) ?? null })
      } else if (!initial && (!(error instanceof ApiError) || error.uncertain))
        update({ uncertain: true, checked: false, matches: [] })
    }
  }
  async function reconcile() {
    setChecking(true)
    setCheckError(null)
    try {
      const saved = await request<ManualGame[]>('manual.get')
      update({
        checked: true,
        matches: saved.filter(
          (game) => game.title.trim().toLocaleLowerCase() === title.trim().toLocaleLowerCase(),
        ),
      })
    } catch (error) {
      setCheckError(error)
    } finally {
      setChecking(false)
    }
  }
  return (
    <section className="feature-panel">
      <h2>{initial ? 'Edit game' : 'Add a game'}</h2>
      <form
        onSubmit={(event) => {
          event.preventDefault()
          void save().catch(() => {})
        }}
      >
        <label className="field">
          Title
          <input
            required
            maxLength={500}
            disabled={draft.sending || draft.uncertain}
            value={title}
            onChange={(event) => update({ title: event.target.value })}
          />
        </label>
        <div className="form-row">
          <label className="field">
            Release year
            <input
              type="number"
              min={1950}
              max={2100}
              disabled={draft.sending || draft.uncertain}
              value={year}
              onChange={(event) => update({ year: event.target.value })}
            />
          </label>
          <label className="field">
            Platform
            <input
              disabled={draft.sending || draft.uncertain}
              value={platform}
              onChange={(event) => update({ platform: event.target.value })}
              placeholder="Windows, Linux, console…"
            />
          </label>
        </div>
        <Notice error={command.error} />
        {current && (
          <div className="conflict-panel">
            <p>The saved entry changed to “{current.title}”. Your draft is preserved.</p>
            <button
              type="button"
              onClick={() => {
                update({ base: current, current: null })
                command.reset()
              }}
            >
              Keep my draft for the next save
            </button>
            <button type="button" onClick={close}>
              Use saved entry
            </button>
          </div>
        )}
        {draft.uncertain && (
          <section className="conflict-panel">
            <h3>Check whether this game was added</h3>
            <p>
              The response was interrupted. Your draft is kept here; adding it again could create another
              entry.
            </p>
            <button type="button" disabled={checking} onClick={() => void reconcile()}>
              {checking ? 'Checking saved games…' : 'Check saved games'}
            </button>
            {draft.checked && (
              <>
                <p>
                  {draft.matches.length
                    ? 'These saved games have the same title:'
                    : 'No saved manual game has this title. Check the library before adding another.'}
                </p>
                {draft.matches.map((game) => (
                  <p key={game.ownershipId}>
                    {game.title}
                    {game.firstReleaseYear ? ` · ${game.firstReleaseYear}` : ''}{' '}
                    <button type="button" onClick={close}>
                      Use saved game
                    </button>
                  </p>
                ))}
                <button
                  type="button"
                  onClick={() => {
                    update({ uncertain: false, checked: false, matches: [] })
                    command.reset()
                  }}
                >
                  Create another anyway
                </button>
              </>
            )}
            <Notice error={checkError} />
          </section>
        )}
        <div className="form-actions">
          <button
            className="primary-button"
            disabled={command.isPending || draft.sending || draft.uncertain || Boolean(current)}
          >
            Save game
          </button>
          <button type="button" disabled={draft.sending} onClick={close}>
            Cancel
          </button>
        </div>
        {draft.sending && <p role="status">Saving your game…</p>}
      </form>
    </section>
  )
}

function HiddenGames() {
  const games = useApiQuery<HiddenGame[]>('hidden.get')
  const command = useCommand()
  return (
    <section className="feature-panel">
      <h2>Out of sight</h2>
      <p>Hidden games keep their history and can be restored at any time.</p>
      <Notice error={games.error || command.error} />
      {games.data?.length ? (
        games.data.map((game) => (
          <article className="metadata-row" key={game.workId}>
            <div>
              <strong>{game.title}</strong>
              <p>
                Hidden {dateLabel(game.hiddenAt)} · {game.storeEntryCount} editions
              </p>
            </div>
            <button
              disabled={command.isPending}
              onClick={() =>
                command.mutate({ route: 'hidden.put', body: { workIds: [game.workId], hidden: false } })
              }
            >
              Restore to library
            </button>
          </article>
        ))
      ) : (
        <Empty>No hidden games.</Empty>
      )}
    </section>
  )
}

function IdentityTools() {
  const review = useApiQuery<IdentityReview>('identity.get')
  const command = useCommand()
  const [confirm, setConfirm] = useState<number | null>(null)
  const candidates = review.data?.candidates.filter((item) => item.status === 'pending') ?? []
  const title = (releaseId: number) => {
    const workId = review.data?.workspace.releases.find((release) => release.id === releaseId)?.workId
    return review.data?.workspace.works.find((work) => work.id === workId)?.title ?? `Edition ${releaseId}`
  }
  return (
    <section className="feature-panel">
      <h2>Are these the same game?</h2>
      <p>Similar names are suggestions. You decide whether editions belong together.</p>
      <Notice error={review.error || command.error} />
      {!candidates.length && <Empty>No identity suggestions waiting for review.</Empty>}
      {candidates.map((candidate) => (
        <article className="identity-row" key={candidate.id}>
          <div>
            <h3>{title(candidate.leftReleaseId)}</h3>
            <p>{title(candidate.rightReleaseId)}</p>
          </div>
          <div className="form-actions">
            <button disabled={command.isPending} onClick={() => setConfirm(candidate.id)}>
              Same game…
            </button>
            <button
              disabled={command.isPending}
              onClick={() =>
                command.mutate({
                  route: 'identity.dismiss',
                  body: {
                    expectedRevision: review.data!.revision,
                    candidateIds: [candidate.id],
                    refusedPairs: [],
                  },
                })
              }
            >
              Different games
            </button>
          </div>
          {confirm === candidate.id && (
            <div className="conflict-panel">
              <p>Group these editions under {title(candidate.leftReleaseId)}?</p>
              <button
                disabled={command.isPending}
                onClick={() => {
                  const left = review.data!.workspace.releases.find(
                    (item) => item.id === candidate.leftReleaseId,
                  )
                  const right = review.data!.workspace.releases.find(
                    (item) => item.id === candidate.rightReleaseId,
                  )
                  if (left && right)
                    command.mutate({
                      route: 'identity.link',
                      body: {
                        expectedRevision: review.data!.revision,
                        parentWorkId: left.workId,
                        childWorkIds: [right.workId],
                        kind: 'same_game',
                        relationLabel: null,
                        rejectedCandidateIds: [],
                        refusedPairs: [],
                      },
                    })
                }}
              >
                Group editions
              </button>
              <button onClick={() => setConfirm(null)}>Cancel</button>
            </div>
          )}
        </article>
      ))}
      {command.error instanceof ApiError && command.error.conflict && (
        <button
          onClick={() => {
            void review.refetch()
            command.reset()
          }}
        >
          Refresh identity suggestions
        </button>
      )}
    </section>
  )
}
