import { useLayoutEffect, useRef, useState } from 'react'
import { request } from '../api/client'
import { useApiQuery, useCommand } from '../api/hooks'
import type { ManualGame, Mode } from '../api/types'
import { Empty, Notice } from './shared'
import { useViewState } from '../viewState'
import { ConfirmationDialog } from './ConfirmationDialog'
import { ManualEditor } from './ManualEditor'
export function ManualGames({ mode, onOpenGame }: { mode: Mode; onOpenGame?: (workId: number) => void }) {
  const games = useApiQuery<ManualGame[]>('manual.get')
  const [editing, setEditing] = useViewState<ManualGame | 'new' | null>(`${mode}:manual:editing`, null)
  const [confirm, setConfirm] = useState<number | null>(null)
  const [opening, setOpening] = useState(false)
  const [browseOnOpen, setBrowseOnOpen] = useState(false)
  const [openError, setOpenError] = useState<unknown>(null)
  const addButton = useRef<HTMLButtonElement>(null)
  const invokingControl = useRef<HTMLElement | null>(null)
  const editor = useRef<HTMLDivElement>(null)
  const wasEditing = useRef(false)
  useLayoutEffect(() => {
    if (editing && !wasEditing.current) editor.current?.querySelector<HTMLInputElement>('input')?.focus()
    else if (!editing && wasEditing.current)
      (invokingControl.current?.isConnected ? invokingControl.current : addButton.current)?.focus()
    wasEditing.current = !!editing
  }, [!!editing])
  const command = useCommand()
  return (
    <>
      <div className="feature-heading">
        <p>
          Keep the games that come from somewhere else. Manual entries can be tracked here; use your game
          shortcut to launch them.
        </p>
        <button
          ref={addButton}
          className="primary-button"
          onClick={(event) => {
            invokingControl.current = event.currentTarget
            setEditing('new')
          }}
        >
          Add a game
        </button>
        {(window.winnow.chooseManualExecutableFacts || window.winnow.chooseManualExecutable) && (
          <button
            disabled={Boolean(editing)}
            onClick={(event) => {
              invokingControl.current = event.currentTarget
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
        <div ref={editor}>
          <ManualEditor
            key={editing === 'new' ? 'new' : editing.ownershipId}
            initial={editing === 'new' ? null : editing}
            browseOnOpen={browseOnOpen}
            onBrowseStarted={() => setBrowseOnOpen(false)}
            onClose={() => setEditing(null)}
          />
        </div>
      )}
      {!games.data?.length && !editing && <Empty className="reading-prose">No manual games yet.</Empty>}
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
                onClick={async (event) => {
                  invokingControl.current = event.currentTarget
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
              <ConfirmationDialog
                open={confirm === game.ownershipId}
                onOpenChange={(open) => {
                  setConfirm(open ? game.ownershipId : null)
                  command.reset()
                }}
                trigger={<button>Remove…</button>}
                title={`Remove “${game.title}”?`}
                description="Only the entry you typed goes. Other store editions and their library data will stay."
                confirmLabel="Remove entry"
                cancelLabel="Keep game"
                pending={command.isPending}
                error={command.error}
                onConfirm={() =>
                  command.mutate(
                    { route: 'manual.delete', params: { ownershipId: game.ownershipId } },
                    {
                      onSuccess: () => {
                        setConfirm(null)
                        requestAnimationFrame(() => addButton.current?.focus())
                      },
                    },
                  )
                }
              />
            </div>
          </section>
        ))}
      </div>
    </>
  )
}
