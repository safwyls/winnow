import { useEffect, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError, request } from '../api/client'
import type { GameList, LibraryResponse, Mode } from '../api/types'
import type { useAvalonLists } from '../themes/avalon-list-state'
import { filterFingerprint } from '../themes/avalon-list-state'
import { cacheSavedList } from './parity-list-prompt'
import { CreateList } from './LibraryTools'
import { Notice } from './shared'

export function LiveListActions({
  state,
  mode,
  compact = false,
  nameSuggestion = '',
}: {
  state: ReturnType<typeof useAvalonLists>
  mode: Mode
  compact?: boolean
  nameSuggestion?: string
}) {
  const [open, setOpen] = useState(false),
    [busy, setBusy] = useState(false),
    [problem, setProblem] = useState<unknown>(null),
    [blocked, setBlocked] = useState(false)
  const writing = useRef(false),
    client = useQueryClient()
  const [creating, setCreating] = useState(false)
  const [created, setCreated] = useState<GameList | null>(null)
  const { list, base, dirty, filter } = state
  useEffect(() => {
    // Navigation uses the published collection, so a queued older library read
    // cannot immediately clear the newly saved list as missing.
    if (!created || !state.lists.some((list) => list.id === created.id)) return
    state.selectList(String(created.id))
    setCreated(null)
    setOpen(false)
  }, [created, state])
  async function save() {
    if (!list?.isLive || !base || writing.current || blocked) return
    writing.current = true
    setBusy(true)
    setProblem(null)
    try {
      const saved = await request<GameList>(
        'list.filter',
        { listId: list.id },
        { filter, expectedRevision: base.revision },
      )
      await cacheSavedList(client, saved)
      state.setBase(saved)
    } catch (error) {
      setProblem(error)
      setBlocked(true)
    } finally {
      writing.current = false
      setBusy(false)
    }
  }
  async function check(keep: boolean) {
    if (writing.current || !list) return
    writing.current = true
    setBusy(true)
    try {
      const response = await request<LibraryResponse>('library.get')
      const saved = response.lists.find((item) => item.id === list.id && item.isLive)
      if (!saved) throw new Error('This live list is no longer available.')
      await cacheSavedList(client, saved)
      state.setBase(saved)
      if (!keep) {
        const { search, ...rules } = saved.filter ?? {}
        state.setQuery(search ?? '')
        state.setBucket('all')
        state.setStore('all')
        state.setRules(rules)
      }
      setBlocked(false)
      setProblem(null)
    } catch (error) {
      setProblem(error)
    } finally {
      writing.current = false
      setBusy(false)
    }
  }
  if (list?.isLive)
    return (
      <div className="avalon-applied-filters" aria-label="Live list rules">
        <span className={compact ? 'sr-only' : undefined}>
          {dirty ? `Unsaved rules for ${list.name}` : `Rules for ${list.name}`}
        </span>
        {dirty && (
          <>
            <button disabled={busy || blocked} onClick={() => void save()}>
              Update {list.name}
            </button>
            <button
              disabled={busy}
              onClick={() => {
                if (blocked) void check(false)
                else state.revert()
              }}
            >
              Revert {list.name}
            </button>
          </>
        )}
        {busy && <span role="status">Saving list rules…</span>}
        <Notice error={problem} />
        {blocked && (
          <button disabled={busy} onClick={() => void check(true)}>
            {problem instanceof ApiError && problem.conflict
              ? 'Keep these rules and use latest revision'
              : 'Check saved rules'}
          </button>
        )}
      </div>
    )
  if (filterFingerprint(filter) === '{}') return null
  return (
    <Dialog.Root
      open={open}
      onOpenChange={(value) => {
        if (!creating) setOpen(value)
      }}
    >
      <Dialog.Trigger asChild>
        <button>Save filters as a live list…</button>
      </Dialog.Trigger>
      {open && (
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content
            className={`dialog-content feature-panel mode-${mode}`}
            onEscapeKeyDown={(event) => {
              if (creating) event.preventDefault()
            }}
            onPointerDownOutside={(event) => event.preventDefault()}
          >
            <Dialog.Title>Name this live list</Dialog.Title>
            <Dialog.Description>A live list keeps finding games that match these rules.</Dialog.Description>
            <CreateList
              draftKey={`draft:list:filters:${mode}`}
              initialFilter={filter}
              initialName={nameSuggestion}
              onCreated={async (saved) => {
                await cacheSavedList(client, saved)
                setCreated(saved)
              }}
              onPendingChange={setCreating}
            />
            <Dialog.Close asChild>
              <button disabled={creating}>Close</button>
            </Dialog.Close>
          </Dialog.Content>
        </Dialog.Portal>
      )}
    </Dialog.Root>
  )
}
