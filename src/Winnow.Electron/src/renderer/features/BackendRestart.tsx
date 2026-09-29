import { useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useQueryClient } from '@tanstack/react-query'
import { Notice } from './shared'

export function BackendRestart() {
  const [confirm, setConfirm] = useState(false),
    [pending, setPending] = useState(false)
  const [error, setError] = useState<unknown>(null),
    [message, setMessage] = useState('')
  const client = useQueryClient()
  async function restart() {
    if (!window.winnow.restartBackend || pending) return
    setPending(true)
    setError(null)
    setMessage('')
    try {
      await window.winnow.restartBackend()
      setConfirm(false)
      setMessage('The library service restarted. Provider changes are now applied.')
      await client.invalidateQueries({ queryKey: ['api'] })
    } catch (failure) {
      setError(failure)
    } finally {
      setPending(false)
    }
  }
  if (!window.winnow.restartBackend) return null
  return (
    <section className="feature-panel">
      <h3>Apply provider changes</h3>
      <p>
        Restart the shared library service after enabling, disabling or installing a provider. Open Winnow
        windows reconnect automatically.
      </p>
      <Dialog.Root
        open={confirm}
        onOpenChange={(open) => {
          if (!pending) setConfirm(open)
        }}
      >
        <Dialog.Trigger asChild>
          <button>Restart library service…</button>
        </Dialog.Trigger>
        <Dialog.Portal>
          <Dialog.Overlay className="dialog-overlay" />
          <Dialog.Content
            className="dialog-content"
            role="alertdialog"
            onEscapeKeyDown={(event) => {
              if (pending) event.preventDefault()
            }}
          >
            <Dialog.Title>Restart the library service?</Dialog.Title>
            <Dialog.Description>
              Background work and session tracking pause while the service restarts. Your library data and
              provider settings are kept.
            </Dialog.Description>
            <div className="form-actions">
              <button disabled={pending} onClick={() => setConfirm(false)}>
                Cancel restart
              </button>
              <button disabled={pending} onClick={() => void restart()}>
                Restart library service
              </button>
            </div>
            {pending && <p role="status">Waiting for the library service to stop and reconnect…</p>}
            <Notice error={error} />
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
      <Notice error={!confirm ? error : null} message={message} />
    </section>
  )
}
