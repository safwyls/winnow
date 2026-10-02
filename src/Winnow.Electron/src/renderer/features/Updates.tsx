import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ApplicationUpdateAction, ApplicationUpdateSnapshot } from '../../shared/bridge'
import { Notice } from './shared'
import './updates.css'
import { FullscreenSwitch } from './FullscreenSettingRows'

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
    if (
      ownedFocus.current &&
      (document.activeElement === document.body ||
        (root.current?.contains(document.activeElement) && document.activeElement?.matches(':disabled')))
    )
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
      {!compact && value.releaseUrl && (
        <button onClick={() => void update.action('release-notes')}>Release notes</button>
      )}
      {(!compact || (!value.canDownload && !value.canRestart)) && value.downloadUrl && !value.busy && (
        <button disabled={busy} onClick={() => void update.action('manual-download')}>
          Download in browser
        </button>
      )}
    </div>
  )
}
export function ApplicationUpdates({ mode = 'desktop' }: { mode?: 'desktop' | 'fullscreen' } = {}) {
  const update = useUpdates(),
    value = update.snapshot
  if (!window.winnow.updateSnapshot) return null
  return (
    <section
      className={`${mode === 'fullscreen' ? 'fullscreen-settings-content' : 'feature-panel'} application-updates`}
      onKeyDown={(event) => {
        if (!event.currentTarget.closest('.mode-fullscreen') || !['ArrowDown', 'ArrowUp'].includes(event.key))
          return
        const controls = [
          ...event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled)'),
        ]
        const index = controls.indexOf(event.target as HTMLElement)
        const next = controls[index + (event.key === 'ArrowDown' ? 1 : -1)]
        if (index < 0 || !next) return
        event.preventDefault()
        next.focus({ preventScroll: true })
        next.scrollIntoView({ block: 'nearest' })
      }}
    >
      <h2 className={mode === 'fullscreen' ? 'fullscreen-settings-group' : undefined}>Updates</h2>
      <p className="reading-prose">
        Winnow can download updates in the background. Restart to install when you are ready.
      </p>
      {mode === 'fullscreen' ? (
        <FullscreenSwitch
          label="Automatic background updates"
          description="Download updates in the background. Restart when you are ready."
          value={value?.automatic ?? true}
          disabled={!value || update.pending || (!!value.busy && !value.canCancel)}
          change={(next) => {
            void update.action('automatic', next)
          }}
        />
      ) : (
        <label className="check-field">
          <input
            type="checkbox"
            checked={value?.automatic ?? true}
            aria-label="Download updates automatically"
            aria-description="Download updates in the background. Restart when you are ready."
            disabled={!value || update.pending || (!!value.busy && !value.canCancel)}
            onChange={(event) => void update.action('automatic', event.target.checked)}
          />
          <span>
            Download updates automatically
            <small className="reading-prose">
              Download updates in the background. Restart when you are ready.
            </small>
          </span>
        </label>
      )}
      {mode === 'fullscreen' ? (
        <FullscreenSwitch
          label="Include beta releases"
          description="Receive preview releases as well as stable updates."
          value={value?.includeBeta ?? false}
          disabled={!value || update.pending || (!!value.busy && !value.canCancel)}
          change={(next) => {
            void update.action('beta', next)
          }}
        />
      ) : (
        <label className="check-field">
          <input
            type="checkbox"
            checked={value?.includeBeta ?? false}
            aria-label="Include beta releases"
            aria-description="Receive preview releases as well as stable updates."
            disabled={!value || update.pending || (!!value.busy && !value.canCancel)}
            onChange={(event) => void update.action('beta', event.target.checked)}
          />
          <span>
            Include beta releases
            <small className="reading-prose">Receive preview releases as well as stable updates.</small>
          </span>
        </label>
      )}
      {value && (
        <p role="status">
          {value.status}
          {value.availableVersion ? ` Version ${value.availableVersion}.` : ''}
        </p>
      )}
      {value?.recoveryStatus && (
        <p role="status" aria-label="Update recovery">
          {value.recoveryStatus}
        </p>
      )}
      <div className="form-actions">
        <UpdateActions update={update} />
      </div>
      <Notice error={update.error} />
    </section>
  )
}
export function UpdateCaption({ mode }: { mode: 'desktop' | 'fullscreen' }) {
  return window.winnow?.updateSnapshot ? <NativeUpdateCaption mode={mode} /> : null
}
function NativeUpdateCaption({ mode }: { mode: 'desktop' | 'fullscreen' }) {
  const update = useUpdates(),
    value = update.snapshot
  if (!value || !(value.canRestart || value.canDownload || value.canCancel)) return null
  return (
    <div
      className={`update-caption mode-${mode}`}
      role="group"
      aria-label="Application update"
      title={value.status}
    >
      {value.canCancel ? (
        <div className="update-caption-progress">
          <span>Updating</span>
          <progress aria-label="Update download progress" max={100} value={value.progress} />
        </div>
      ) : mode === 'fullscreen' ? (
        <span role="status">Update available · Menu</span>
      ) : (
        <button
          disabled={value.busy || update.pending}
          onClick={() => void update.action('update-and-restart')}
        >
          Update and restart
        </button>
      )}
      <Notice error={update.error} />
    </div>
  )
}
export function QuickUpdate({ close }: { close?: () => void }) {
  return window.winnow?.updateSnapshot ? <NativeQuickUpdate close={close} /> : null
}
function NativeQuickUpdate({ close }: { close?: () => void }) {
  const update = useUpdates(),
    value = update.snapshot
  if (!value || !(value.canRestart || value.canDownload || value.canCancel)) return null
  return (
    <>
      <button
        disabled={value.busy || update.pending}
        onClick={() => {
          close?.()
          void update.action('update-and-restart')
        }}
      >
        Update and restart
      </button>
      <Notice error={update.error} />
    </>
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
      {value.recoveryStatus && (
        <span role="status" aria-label="Update recovery">
          {value.recoveryStatus}
        </span>
      )}
      <UpdateActions update={update} compact />
      <Notice error={update.error} />
    </aside>
  )
}
