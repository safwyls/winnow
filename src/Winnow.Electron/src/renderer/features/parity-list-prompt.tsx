import { useEffect, useRef, useState, type ReactNode } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError, request } from '../api/client'
import { useLibrary } from '../api/hooks'
import type { GameList, LibraryGame, LibraryResponse, Mode } from '../api/types'
import { useViewState } from '../viewState'
import { Notice } from './shared'

export async function cacheSavedList(client: ReturnType<typeof useQueryClient>, list: GameList) {
  if (!list || !Number.isFinite(list.id) || !list.revision || !Array.isArray(list.releaseIds))
    throw new Error('The saved list could not be checked.')
  await client.cancelQueries({ queryKey: ['api', 'library.get'] })
  client.setQueryData<LibraryResponse>(
    ['api', 'library.get'],
    (previous) =>
      previous && {
        ...previous,
        lists: [...previous.lists.filter((item) => item.id !== list.id), list].sort((a, b) =>
          a.name.localeCompare(b.name),
        ),
      },
  )
}

export function AddToListButton({
  games,
  mode = 'desktop',
  origin = 'library',
  label,
  icon,
}: {
  games: LibraryGame[]
  mode?: Mode
  origin?: string
  label?: string
  icon?: ReactNode
}) {
  const [open, setOpen] = useState(false)
  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger asChild>
        <button disabled={!games.length} onClick={(event) => event.stopPropagation()}>
          {icon}
          {label ?? (games.length > 1 ? `Add ${games.length} to list…` : 'Add to list…')}
        </button>
      </Dialog.Trigger>
      {open && <ListPrompt games={games} mode={mode} origin={origin} onClose={() => setOpen(false)} />}
    </Dialog.Root>
  )
}

function ListPrompt({
  games,
  mode,
  origin,
  onClose,
}: {
  games: LibraryGame[]
  mode: Mode
  origin: string
  onClose(): void
}) {
  const library = useLibrary(),
    client = useQueryClient()
  const releaseIds = [
    ...new Set(games.flatMap((game) => (game.entries[0] ? [game.entries[0].releaseId] : []))),
  ]
  const key = `draft:add-list:${origin}:${releaseIds.join(',')}`
  const [draft, setDraft] = useViewState(key, {
    name: '',
    busy: false,
    uncertain: false,
    checked: false,
    matches: [] as GameList[],
    target: null as number | null,
  })
  const [error, setError] = useState<unknown>(null)
  const active = useRef(true),
    sending = useRef(false),
    input = useRef<HTMLInputElement>(null)
  useEffect(() => {
    active.current = true
    return () => {
      active.current = false
    }
  }, [])
  const update = (value: Partial<typeof draft>) => setDraft((previous) => ({ ...previous, ...value }))
  const lists = (library.data?.lists ?? [])
    .filter((list) => !list.isLive)
    .sort((a, b) => a.name.localeCompare(b.name))
  async function save(list?: GameList) {
    if (sending.current || draft.busy || draft.uncertain || (!list && !draft.name.trim())) return
    sending.current = true
    update({ busy: true, target: list?.id ?? null })
    setError(null)
    try {
      const result = await request<GameList>(
        list ? 'list.member.add' : 'list.create',
        list ? { listId: list.id } : undefined,
        list ? { releaseIds, expectedRevision: list.revision } : { name: draft.name.trim(), releaseIds },
      )
      await cacheSavedList(client, result)
      if (!releaseIds.every((id) => result.releaseIds.includes(id)))
        throw new Error('The saved list did not include the selected games.')
      setDraft({ name: '', busy: false, uncertain: false, checked: false, matches: [], target: null })
      if (active.current) onClose()
    } catch (failure) {
      setError(failure)
      const uncertain = !(failure instanceof ApiError) || failure.uncertain
      update({ busy: false, uncertain, checked: false })
      if (failure instanceof ApiError && failure.conflict)
        await client.invalidateQueries({ queryKey: ['api', 'library.get'] })
    } finally {
      sending.current = false
    }
  }
  async function check() {
    if (sending.current || draft.busy) return
    sending.current = true
    update({ busy: true })
    setError(null)
    try {
      const result = await request<LibraryResponse>('library.get')
      client.setQueryData(['api', 'library.get'], result)
      update({
        checked: true,
        matches: result.lists.filter(
          (list) =>
            !list.isLive &&
            (draft.target != null
              ? list.id === draft.target
              : list.name.trim().toLocaleLowerCase() === draft.name.trim().toLocaleLowerCase()) &&
            releaseIds.every((id) => list.releaseIds.includes(id)),
        ),
      })
    } catch (failure) {
      setError(failure)
    } finally {
      sending.current = false
      update({ busy: false })
    }
  }
  return (
    <Dialog.Portal>
      <Dialog.Overlay className="dialog-overlay" />
      <Dialog.Content
        className={`dialog-content feature-panel list-prompt-dialog mode-${mode}`}
        onOpenAutoFocus={(event) => {
          event.preventDefault()
          input.current?.focus()
        }}
        onEscapeKeyDown={(event) => {
          if (draft.busy) event.preventDefault()
        }}
        onPointerDownOutside={(event) => event.preventDefault()}
        onInteractOutside={(event) => {
          if (draft.busy) event.preventDefault()
        }}
      >
        <Dialog.Title>
          Add {games.length === 1 ? games[0].title : `${games.length} games`} to a list
        </Dialog.Title>
        <Dialog.Description>Choose a handpicked list or make a new one.</Dialog.Description>
        <div className="list-prompt-choices" role="group" aria-label="Existing lists">
          {lists.map((list) => (
            <button key={list.id} disabled={draft.busy || draft.uncertain} onClick={() => void save(list)}>
              {list.name}
            </button>
          ))}
        </div>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            void save()
          }}
        >
          <label className="field">
            New list name
            <input
              ref={input}
              value={draft.name}
              maxLength={200}
              disabled={draft.busy || draft.uncertain}
              onChange={(event) => update({ name: event.target.value })}
            />
          </label>
          <div className="form-actions">
            <button disabled={draft.busy || draft.uncertain || !draft.name.trim()}>New list</button>
            <button type="button" disabled={draft.busy} onClick={onClose}>
              Cancel
            </button>
          </div>
        </form>
        {draft.busy && <p role="status">Saving list changes…</p>}
        <Notice error={error} />
        {draft.uncertain && (
          <div className="conflict-panel">
            <p>The last response was interrupted. Check the saved list before trying again.</p>
            <button disabled={draft.busy} onClick={() => void check()}>
              Check saved lists
            </button>
            {draft.checked && (
              <>
                {draft.matches.map((list) => (
                  <button
                    key={list.id}
                    disabled={draft.busy}
                    onClick={() => {
                      update({ name: '', uncertain: false, checked: false, matches: [], target: null })
                      onClose()
                    }}
                  >
                    Use saved list: {list.name}
                  </button>
                ))}
                <button disabled={draft.busy} onClick={() => update({ uncertain: false, checked: false })}>
                  {draft.target == null ? 'Create another anyway' : 'Try adding again'}
                </button>
              </>
            )}
          </div>
        )}
      </Dialog.Content>
    </Dialog.Portal>
  )
}
