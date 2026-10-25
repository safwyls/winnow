import { useRef } from 'react'
import { useIsMutating, useMutation, useQueryClient } from '@tanstack/react-query'
import { useViewState } from '../viewState'

export function BackendRestart({ disabled = false }: { disabled?: boolean } = {}) {
  const [result, setResult] = useViewState('plugins:service-restart', { message: '', failed: false })
  const pending = useIsMutating({ mutationKey: ['library-restart'] }) > 0
  const lock = useRef(false)
  const client = useQueryClient()
  const restart = useMutation({
    mutationKey: ['library-restart'],
    retry: false,
    mutationFn: async () => {
      if (!window.winnow.restartBackend || lock.current || disabled) return
      lock.current = true
      setResult({ message: '', failed: false })
      try {
        await window.winnow.restartBackend()
        await client.invalidateQueries({ queryKey: ['api'] })
        setResult({
          message: 'The library service restarted. Provider changes are now applied.',
          failed: false,
        })
      } catch {
        setResult({ message: 'Could not restart the library service. Try again.', failed: true })
      } finally {
        lock.current = false
      }
    },
  })
  if (!window.winnow.restartBackend) return null
  return (
    <section className="feature-panel">
      <h3>Apply provider changes</h3>
      <p>
        Restart the shared library service after enabling, disabling or installing a provider. Background work
        and session tracking pause during restart. Open Winnow windows reconnect automatically.
      </p>
      <button disabled={disabled || pending} onClick={() => restart.mutate()}>
        Restart library service
      </button>
      {pending ? (
        <p role="status">Waiting for the library service to stop and reconnect…</p>
      ) : (
        result.message && <p role={result.failed ? 'alert' : 'status'}>{result.message}</p>
      )}
    </section>
  )
}
