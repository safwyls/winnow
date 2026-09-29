import { useEffect, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { ApiError, dateLabel, request } from '../api/client'
import { useApiQuery, useCommand, useLibrary } from '../api/hooks'
import type { GameList, HiddenGame, LibraryFilter, LibraryResponse, Mode } from '../api/types'
import { Empty, Notice } from './shared'
import { clearViewState, useViewState } from '../viewState'
import { IdentityTools, ListMembers, LiveFilterFields, LiveListFilterEditor } from './parity-library'
import { useInlineEditorFocus } from './parity-library-focus'
import { ManualGames } from './ManualGames'
export { ManualEditor } from './ManualEditor'
import { ConfirmationDialog } from './ConfirmationDialog'
import { orderedLists } from './parity-list-prompt'

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
          {orderedLists(library.data?.lists ?? []).map((list) => (
            <ListEditor key={list.id} list={list} />
          ))}
        </div>
      )}
      {tab === 'Manual games' && <ManualGames mode={mode} onOpenGame={onOpenGame} />}
      {tab === 'Hidden games' && <HiddenGames />}
      {tab === 'Identity review' && <IdentityTools onOpenGame={onOpenGame} mode={mode} />}
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
  initialName = '',
  onCreated,
  onPendingChange,
}: {
  draftKey?: string
  initialFilter?: LibraryFilter
  initialName?: string
  onCreated?: (list: GameList) => void | Promise<void>
  onPendingChange?: (busy: boolean) => void
} = {}) {
  const empty = {
    name: initialName,
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
      if (active.current) await onCreated?.(saved as GameList)
      setDraft(empty)
      clearViewState(draftKey)
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
        open={confirm}
        onOpenChange={setConfirm}
        trigger={
          <button disabled={draft?.sending} className="text-button">
            Delete list…
          </button>
        }
        title={`Delete ${list.name}?`}
        description="Its games will stay in your library."
        confirmLabel="Delete list"
        cancelLabel="Keep list"
        pending={Boolean(draft?.sending) || command.isPending}
        error={command.error}
        onConfirm={() =>
          command.mutate(
            {
              route: 'list.delete',
              params: { listId: list.id },
              body: { expectedRevision: list.revision },
            },
            { onSuccess: () => setConfirm(false) },
          )
        }
      />
      {!confirm && <Notice error={command.error} />}
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
