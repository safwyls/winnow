import { useEffect, useRef, useState } from 'react'
import { Play, Download } from 'lucide-react'
import type { GameEntry, LibraryGame, Workspace } from '../api/types'
import { ApiError, launchMessage, request, storeLabel } from '../api/client'
import { primaryAction } from '../../shared/game-actions'
import { AvalonAction, AvalonActions } from '../themes/avalon-actions'
import { useLaunchFeedback } from './LaunchFeedback'
import { Notice } from './shared'
import { restoreFocusWhenReady } from './restore-focus'

type Attempt = { entry: GameEntry; action: 'Play' | 'Install'; operationId: string }
export function ChooseLaunchVersion({ game, workspace }: { game: LibraryGame; workspace?: Workspace }) {
  const [open, setOpen] = useState(false),
    [busy, setBusy] = useState(false)
  const [attempt, setAttempt] = useState<Attempt | null>(null)
  const [error, setError] = useState<unknown>(null),
    [message, setMessage] = useState('')
  const origin = useRef<HTMLButtonElement>(null),
    mounted = useRef(true),
    writing = useRef(false)
  const feedback = useLaunchFeedback()
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])
  async function dispatch(entry: GameEntry, retry?: Attempt) {
    if (!mounted.current || writing.current) return
    const action = retry?.action ?? primaryAction(entry, workspace)
    if (!action) return
    const operation = retry ?? { entry, action, operationId: crypto.randomUUID() }
    writing.current = true
    setBusy(true)
    setAttempt(operation)
    setError(null)
    setMessage('')
    try {
      const send = () =>
        request<number>(
          'actions.execute',
          { ownershipId: entry.ownershipId },
          { action: operation.action, operationId: operation.operationId },
        )
      const result = feedback
        ? await feedback.track(entry.ownershipId, game.title, storeLabel(entry.store), action, send)
        : await send()
      if (mounted.current) {
        setAttempt(null)
        setMessage(feedback && action === 'Play' ? '' : launchMessage(result))
      }
    } catch (failure) {
      if (mounted.current) {
        setError(failure)
        if (!(failure instanceof ApiError) || !failure.uncertain) setAttempt(null)
      }
    } finally {
      writing.current = false
      if (mounted.current) setBusy(false)
    }
  }
  return (
    <div className="entry-actions launch-version">
      <button ref={origin} disabled={busy || Boolean(attempt)} onClick={() => setOpen(true)}>
        Choose launch version
      </button>
      <AvalonActions
        open={open}
        title="Choose launch version"
        close={() => setOpen(false)}
        restoreFocus={() => restoreFocusWhenReady(origin.current)}
      >
        {game.entries.map((entry) => {
          const action = primaryAction(entry, workspace)
          const installed =
            typeof entry.installed === 'boolean'
              ? entry.installed
                ? 'Installed'
                : 'Not installed'
              : 'Install state unknown'
          return (
            <AvalonAction
              key={entry.ownershipId}
              icon={action === 'Install' ? Download : Play}
              label={`${storeLabel(entry.store)} · ${installed} · ${action ?? 'Unavailable'}`}
              disabled={!action || busy}
              onChoose={() => void dispatch(entry)}
            />
          )
        })}
        <AvalonAction label="Cancel" onChoose={() => {}} />
      </AvalonActions>
      <Notice error={error} message={message} />
      {attempt && !busy && (
        <div className="conflict-panel">
          <p>The response was interrupted. The launcher may already have received the action.</p>
          <button onClick={() => void dispatch(attempt.entry, attempt)}>Check the same action again</button>
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
