import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ApplicationUpdateAction, ApplicationUpdateSnapshot } from '../../shared/bridge'
import { Notice } from './shared'
import './updates.css'

const updateKey = ['native', 'application-update']
function useUpdates() {
  const client = useQueryClient(),
    newest = useRef<ApplicationUpdateSnapshot | null>(null)
  const query = useQuery({
    queryKey: updateKey,
    enabled: !!window.winnow.updateSnapshot,
    queryFn: async () => {
      const before = newest.current,
        snapshot = await window.winnow.updateSnapshot!()
      return newest.current !== before ? newest.current! : snapshot
    },
  })
  const [error, setError] = useState<unknown>(null),
    [pending, setPending] = useState(false)
  useEffect(
    () =>
      window.winnow.onUpdate?.((snapshot) => {
        newest.current = snapshot
        client.setQueryData(updateKey, snapshot)
      }),
    [client],
  )
  async function action(value: ApplicationUpdateAction, preference?: boolean) {
    setPending(true)
    setError(null)
    const before = newest.current
    try {
      const snapshot = await window.winnow.updateAction!(value, preference)
      if (newest.current === before) {
        newest.current = snapshot
        client.setQueryData(updateKey, snapshot)
      }
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  return { snapshot: query.data, error: error || query.error, pending, action }
}
function UpdateActions({
  update,
  compact = false,
}: {
  update: ReturnType<typeof useUpdates>
  compact?: boolean
}) {
  const value = update.snapshot
  const root = useRef<HTMLDivElement>(null),
    ownedFocus = useRef(false)
  useLayoutEffect(() => {
    if (ownedFocus.current && document.activeElement === document.body)
      root.current
        ?.querySelector<HTMLButtonElement>('button.primary-button:not(:disabled), button:not(:disabled)')
        ?.focus()
  }, [value])
  if (!value) return null
  const busy = value.busy || update.pending
  return (
    <div
      className="form-actions"
      ref={root}
      onFocusCapture={() => {
        ownedFocus.current = true
      }}
      onBlurCapture={(event) => {
        if (event.relatedTarget && !root.current?.contains(event.relatedTarget as Node))
          ownedFocus.current = false
      }}
    >
      {value.busy && value.canCancel && (
        <>
          <progress aria-label="Update download progress" max={100} value={value.progress} />
          <button onClick={() => void update.action('cancel')}>Cancel download</button>
        </>
      )}
      {!compact && (
        <button disabled={busy} onClick={() => void update.action('check')}>
          Check for updates
        </button>
      )}
      {value.canDownload && !value.busy && (
        <button disabled={busy} onClick={() => void update.action('download')}>
          Download update
        </button>
      )}
      {value.canDownload && !value.busy && (
        <button
          className="primary-button"
          disabled={busy}
          onClick={() => void update.action('update-and-restart')}
        >
          Update and restart
        </button>
      )}
      {value.canRestart && (
        <button className="primary-button" disabled={busy} onClick={() => void update.action('restart')}>
          Restart to update
        </button>
      )}
      {!value.canDownload && !value.canRestart && value.downloadUrl && !value.busy && (
        <button disabled={busy} onClick={() => void update.action('manual-download')}>
          Download in browser
        </button>
      )}
    </div>
  )
}
export function ApplicationUpdates() {
  const update = useUpdates(),
    value = update.snapshot
  if (!window.winnow.updateSnapshot) return null
  return (
    <section className="feature-panel">
      <h2>Updates</h2>
      <p>Winnow can download updates in the background. Restart to install when you are ready.</p>
      <label className="check-field">
        <input
          type="checkbox"
          checked={value?.automatic ?? true}
          disabled={!value || update.pending || (!!value.busy && !value.canCancel)}
          onChange={(event) => void update.action('automatic', event.target.checked)}
        />
        Download updates automatically
      </label>
      <label className="check-field">
        <input
          type="checkbox"
          checked={value?.includeBeta ?? false}
          disabled={!value || update.pending || (!!value.busy && !value.canCancel)}
          onChange={(event) => void update.action('beta', event.target.checked)}
        />
        Include beta releases
      </label>
      {value && (
        <p role="status">
          {value.status}
          {value.availableVersion ? ` Version ${value.availableVersion}.` : ''}
        </p>
      )}
      {value?.recoveryStatus && <p role="alert">{value.recoveryStatus}</p>}
      <div className="form-actions">
        <UpdateActions update={update} />
      </div>
      <Notice error={update.error} />
    </section>
  )
}
/** Mount in the shared shell so desktop and fullscreen show the same staged update. */
export function UpdateStatus() {
  const update = useUpdates(),
    value = update.snapshot
  if (!value || !(value.canRestart || value.canDownload || value.canCancel || value.recoveryStatus))
    return null
  return (
    <aside className="update-status" aria-label="Application update">
      <span role="status">{value.status}</span>
      {value.recoveryStatus && <span role="alert">{value.recoveryStatus}</span>}
      <UpdateActions update={update} compact />
      <Notice error={update.error} />
    </aside>
  )
}
