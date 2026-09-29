import { useEffect, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { ApiError, dateLabel, request } from '../api/client'
import { useApiQuery, useCommand, useLibrary } from '../api/hooks'
import type { GameList, HiddenGame, LibraryFilter, LibraryResponse, ManualGame, Mode } from '../api/types'
import { Empty, Notice } from './shared'
import { clearViewState, useViewState } from '../viewState'
import { IdentityTools, ListMembers, LiveFilterFields, LiveListFilterEditor } from './parity-library'
import { useInlineEditorFocus } from './parity-library-focus'
import type { ExecutableFacts } from '../../shared/executable-facts'
import { ConfirmationDialog } from './ConfirmationDialog'

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
      {tab === 'Identity review' && <IdentityTools onOpenGame={onOpenGame} />}
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

export function CreateListButton({ mode }: { mode: Mode }) {
  const [open, setOpen] = useState(false),
    [busy, setBusy] = useState(false)
  return (
    <Dialog.Root
      open={open}
      onOpenChange={(value) => {
        if (!busy) setOpen(value)
      }}
    >
      <Dialog.Trigger asChild>
        <button>New list…</button>
      </Dialog.Trigger>
      {open && (
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content
            className={`dialog-content feature-panel mode-${mode}`}
            onEscapeKeyDown={(event) => {
              if (busy) event.preventDefault()
            }}
            onPointerDownOutside={(event) => event.preventDefault()}
          >
            <Dialog.Title>Make room for a list</Dialog.Title>
            <Dialog.Description>
              Choose games yourself, or let a live list follow your filters.
            </Dialog.Description>
            <CreateList onCreated={() => setOpen(false)} onPendingChange={setBusy} />
            <Dialog.Close asChild>
              <button disabled={busy}>Close</button>
            </Dialog.Close>
          </Dialog.Content>
        </Dialog.Portal>
      )}
    </Dialog.Root>
  )
}

export function CreateList({
  draftKey = 'draft:list:new',
  initialFilter,
  onCreated,
  onPendingChange,
}: {
  draftKey?: string
  initialFilter?: LibraryFilter
  onCreated?: (list: GameList) => void
  onPendingChange?: (busy: boolean) => void
} = {}) {
  const empty = {
    name: '',
    live: Boolean(initialFilter),
    filter: initialFilter ?? ({} as LibraryFilter),
    sending: false,
    uncertain: false,
    checked: false,
    matches: [] as GameList[],
  }
  const [draft, setDraft] = useViewState(draftKey, empty)
  const active = useRef(true),
    writing = useRef(false)
  useEffect(() => {
    active.current = true
    return () => {
      active.current = false
    }
  }, [])
  useEffect(() => {
    onPendingChange?.(draft.sending)
  }, [draft.sending, onPendingChange])
  const { name, live } = draft
  const update = (value: Partial<typeof empty>) => setDraft((previous) => ({ ...previous, ...value }))
  const command = useCommand()
  const [checking, setChecking] = useState(false)
  const [checkError, setCheckError] = useState<unknown>(null)
  const discard = () => {
    setDraft(empty)
    clearViewState(draftKey)
    command.reset()
  }
  async function save() {
    if (draft.uncertain || draft.sending || writing.current) return
    writing.current = true
    update({ sending: true })
    try {
      const saved = await command.mutateAsync({
        route: live ? 'list.live' : 'list.create',
        body: live
          ? {
              name,
              filter: draft.filter,
            }
          : { name, releaseIds: [] },
      })
      setDraft(empty)
      clearViewState(draftKey)
      if (active.current) onCreated?.(saved as GameList)
    } catch (error) {
      update({
        sending: false,
        ...(!(error instanceof ApiError) || error.uncertain
          ? { uncertain: true, checked: false, matches: [] }
          : {}),
      })
    } finally {
      writing.current = false
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
          <LiveFilterFields
            filter={draft.filter}
            disabled={draft.sending || draft.uncertain}
            onChange={(filter) => update({ filter })}
          />
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
                    <button
                      type="button"
                      onClick={() => {
                        discard()
                        onCreated?.(list)
                      }}
                    >
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
  const focus = useInlineEditorFocus(Boolean(draft))
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
          ref={focus.trigger}
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
      {list.isLive && <LiveListFilterEditor list={list} />}
      <ListMembers list={list} />
      {draft && (
        <form
          ref={focus.editor}
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
      <ConfirmationDialog
        open={confirm} onOpenChange={setConfirm}
        trigger={<button disabled={draft?.sending} className="text-button">Delete list…</button>}
        title={`Delete ${list.name}?`} description="Its games will stay in your library."
        confirmLabel="Delete list" cancelLabel="Keep list"
        pending={Boolean(draft?.sending) || command.isPending} error={command.error}
        onConfirm={() =>
              command.mutate({
                route: 'list.delete',
                params: { listId: list.id },
                body: { expectedRevision: list.revision },
              }, { onSuccess: () => setConfirm(false) })
            }
      />
      {!confirm && <Notice error={command.error} />}
    </section>
  )
}

function ManualGames({ mode, onOpenGame }: { mode: Mode; onOpenGame?: (workId: number) => void }) {
  const games = useApiQuery<ManualGame[]>('manual.get')
  const [editing, setEditing] = useViewState<ManualGame | 'new' | null>(`${mode}:manual:editing`, null)
  const [confirm, setConfirm] = useState<number | null>(null)
  const [opening, setOpening] = useState(false)
  const [browseOnOpen, setBrowseOnOpen] = useState(false)
  const [openError, setOpenError] = useState<unknown>(null)
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
        {(window.winnow.chooseManualExecutableFacts || window.winnow.chooseManualExecutable) && (
          <button
            disabled={Boolean(editing)}
            onClick={() => {
              setBrowseOnOpen(true)
              setEditing('new')
            }}
          >
            Add from executable…
          </button>
        )}
      </div>
      <Notice error={games.error || command.error || openError} />
      {editing && (
        <ManualEditor
          key={editing === 'new' ? 'new' : editing.ownershipId}
          initial={editing === 'new' ? null : editing}
          browseOnOpen={browseOnOpen}
          onBrowseStarted={() => setBrowseOnOpen(false)}
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
              <button
                disabled={opening}
                onClick={async () => {
                  setOpening(true)
                  setOpenError(null)
                  try {
                    setEditing(await request<ManualGame>('manual.detail', { ownershipId: game.ownershipId }))
                  } catch (error) {
                    setOpenError(error)
                  } finally {
                    setOpening(false)
                  }
                }}
              >
                {opening ? 'Loading game…' : 'Edit game'}
              </button>
              <button onClick={() => setConfirm(game.ownershipId)}>Remove…</button>
            </div>
            {confirm === game.ownershipId && (
              <div className="conflict-panel">
                <p>Remove this manual entry? Other store editions and their library data will stay.</p>
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

export function ManualEditor({
  initial,
  onClose,
  browseOnOpen = false,
  onBrowseStarted,
}: {
  initial: ManualGame | null
  onClose: () => void
  browseOnOpen?: boolean
  onBrowseStarted?: () => void
}) {
  const key = `draft:manual:${initial?.ownershipId ?? 'new'}`
  const [draft, setDraft] = useViewState(key, {
    title: initial?.title ?? '',
    year: initial?.firstReleaseYear?.toString() ?? '',
    platform: initial?.platformLabel ?? '',
    executable: initial?.executablePath ?? '',
    install: initial?.installPath ?? '',
    igdbId: initial?.igdbId?.toString() ?? '',
    steamAppId: initial?.steamAppId ?? '',
    base: initial,
    current: null as ManualGame | null,
    sending: false,
    uncertain: false,
    checked: false,
    matches: [] as ManualGame[],
    proposedTitle: null as string | null,
    executableNote: null as string | null,
    matchNote: null as string | null,
  })
  const latest = useRef(draft),
    active = useRef(true),
    choosing = useRef(false),
    writing = useRef(false)
  latest.current = draft
  const [picking, setPicking] = useState(false)
  useEffect(() => {
    active.current = true
    return () => {
      active.current = false
    }
  }, [])
  useEffect(() => {
    if (browseOnOpen && !choosing.current) {
      onBrowseStarted?.()
      void browse()
    }
  }, [browseOnOpen])
  const { title, year, platform, base, current } = draft
  const update = (value: Partial<typeof draft>) => setDraft((previous) => ({ ...previous, ...value }))
  const command = useCommand()
  const [checking, setChecking] = useState(false)
  const [checkError, setCheckError] = useState<unknown>(null)
  const close = () => {
    clearViewState(key)
    if (active.current) onClose()
  }
  const [candidates, setCandidates] = useState<
    { igdbId: number; name: string; firstReleaseYear?: number | null; platforms: string[] }[] | null
  >(null)
  const [searching, setSearching] = useState(false)
  async function browse() {
    if (choosing.current || latest.current.sending || latest.current.uncertain) return
    choosing.current = true
    setPicking(true)
    setCheckError(null)
    try {
      let facts: ExecutableFacts | null = null
      if (window.winnow.chooseManualExecutableFacts) facts = await window.winnow.chooseManualExecutableFacts()
      else {
        const path = await window.winnow.chooseManualExecutable?.()
        if (path)
          facts = {
            executablePath: path,
            installPath: path.replace(/[\\/][^\\/]+$/, '') || null,
            title:
              path
                .split(/[\\/]/)
                .at(-1)
                ?.replace(/\.[^.]+$/, '') || null,
            titleSource: 'file-name',
            publisher: null,
          }
      }
      if (!facts) return
      const current = latest.current
      const proposed =
        facts.title && (!current.title.trim() || current.title === current.proposedTitle)
          ? facts.title
          : current.title
      const publisher = facts.publisher ? ` Published by ${facts.publisher}.` : ''
      const note =
        facts.titleSource === 'file-description' || facts.titleSource === 'product-name'
          ? `The file identifies itself as ${facts.title}.${publisher}`
          : facts.title
            ? `Guessed ${facts.title} from the path.${publisher}`
            : 'No title found in the file. Type one above.'
      update({
        executable: facts.executablePath,
        install: facts.installPath ?? '',
        title: proposed,
        proposedTitle: proposed === facts.title ? proposed : current.proposedTitle,
        executableNote: note,
        matchNote: null,
      })
      setCandidates(null)
      if (proposed.trim()) await searchMatches(proposed)
    } catch {
      setCheckError(new Error('The executable could not be selected. Enter its path instead.'))
    } finally {
      choosing.current = false
      setPicking(false)
    }
  }
  async function searchMatches(query = title) {
    if (!query.trim() || searching) return
    setSearching(true)
    setCandidates(null)
    setCheckError(null)
    try {
      setCandidates(await request('metadata.search', { title: query }))
    } catch (error) {
      setCheckError(error)
    } finally {
      setSearching(false)
    }
  }
  async function save() {
    if (draft.uncertain || draft.sending || writing.current) return
    writing.current = true
    update({ sending: true })
    try {
      await command.mutateAsync({
        route: initial ? 'manual.update' : 'manual.create',
        params: initial ? { ownershipId: initial.ownershipId } : undefined,
        body: {
          title,
          firstReleaseYear: year ? Number(year) : null,
          platformLabel: platform || null,
          executablePath: draft.executable || null,
          installPath: draft.install || null,
          igdbId: draft.igdbId ? Number(draft.igdbId) : null,
          steamAppId: draft.steamAppId || null,
          expectedRevision: base?.revision,
          expectedIgdbMappingRevision: base?.igdbMappingRevision,
        },
      })
      close()
    } catch (error) {
      update({ sending: false })
      if (initial && error instanceof ApiError && error.conflict) {
        try {
          const all = await request<ManualGame[]>('manual.get')
          update({ current: all.find((game) => game.ownershipId === initial.ownershipId) ?? null })
        } catch (failure) {
          setCheckError(failure)
        }
      } else if (!initial && (!(error instanceof ApiError) || error.uncertain))
        update({ uncertain: true, checked: false, matches: [] })
    } finally {
      writing.current = false
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
              min={1900}
              max={2200}
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
        <label className="field">
          Executable path
          <input
            disabled={draft.sending || draft.uncertain}
            value={draft.executable}
            onChange={(event) => update({ executable: event.target.value })}
            placeholder="Path to the game executable"
          />
        </label>
        {(window.winnow.chooseManualExecutableFacts || window.winnow.chooseManualExecutable) && (
          <button
            type="button"
            disabled={draft.sending || draft.uncertain || searching || picking}
            onClick={() => void browse()}
          >
            {picking ? 'Reading executable…' : 'Choose executable'}
          </button>
        )}
        <p className="muted">Winnow uses the executable to recognize recorded play sessions.</p>
        {draft.executableNote && <p className="muted">{draft.executableNote}</p>}
        {draft.matchNote && <p className="muted">{draft.matchNote}</p>}
        <label className="field">
          Installation folder
          <input
            disabled={draft.sending || draft.uncertain}
            value={draft.install}
            onChange={(event) => update({ install: event.target.value })}
          />
        </label>
        <div className="form-row">
          <label className="field">
            IGDB ID
            <input
              type="number"
              min={1}
              step={1}
              disabled={draft.sending || draft.uncertain}
              value={draft.igdbId}
              onChange={(event) => update({ igdbId: event.target.value })}
            />
          </label>
          <label className="field">
            Steam app ID
            <input
              inputMode="numeric"
              pattern="[0-9]+"
              disabled={draft.sending || draft.uncertain}
              value={draft.steamAppId}
              onChange={(event) => update({ steamAppId: event.target.value })}
            />
          </label>
        </div>
        <button
          type="button"
          disabled={draft.sending || draft.uncertain || searching || !title.trim()}
          onClick={() => void searchMatches()}
        >
          {searching ? 'Searching IGDB…' : 'Find IGDB matches'}
        </button>
        {candidates?.length === 0 && (
          <p className="muted">No matching games. You can still fill the form by hand.</p>
        )}
        {candidates && candidates.length > 0 && (
          <div className="igdb-candidates">
            {candidates.map((candidate) => (
              <article className="metadata-row" key={candidate.igdbId}>
                <div>
                  <strong>{candidate.name}</strong>
                  <p>
                    {[candidate.firstReleaseYear, candidate.platforms.join(', ')].filter(Boolean).join(' · ')}
                  </p>
                </div>
                <button
                  type="button"
                  disabled={draft.sending || draft.uncertain}
                  onClick={() => {
                    update({
                      title: candidate.name,
                      year: candidate.firstReleaseYear?.toString() ?? '',
                      igdbId: String(candidate.igdbId),
                      proposedTitle: candidate.name,
                      matchNote: `Using details from ${candidate.name}. Nothing is saved until you choose Save game.`,
                    })
                    setCandidates(null)
                  }}
                >
                  Use these details
                </button>
              </article>
            ))}
            <button type="button" onClick={() => setCandidates(null)}>
              Keep my own details
            </button>
          </div>
        )}
        <Notice error={command.error} />
        <Notice error={checkError} />
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
            disabled={
              command.isPending ||
              draft.sending ||
              draft.uncertain ||
              Boolean(current) ||
              (command.error instanceof ApiError && command.error.conflict)
            }
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
