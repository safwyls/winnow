import { useEffect, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { RefreshCw } from 'lucide-react'
import { request } from '../api/client'
import type { MergeReview, MergeUndo } from './parity-merge-model'
import { mergeReviewKey, refreshIdentityReview } from './parity-merge-query'
import { useViewState } from '../viewState'
import type { Mode } from '../api/types'

export function useMergeRefresh({ disabled, onBusy }: { disabled: boolean; onBusy(value: boolean): void }) {
  const client = useQueryClient()
  const current = useRef<AbortController | null>(null)
  const mounted = useRef(true)
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState('')
  const [, setUndo] = useViewState<MergeUndo | null>('identity:review-undo', null)
  const sweep = useMutation({
    mutationFn: async (signal: AbortSignal) => {
      const previous = client.getQueryData<MergeReview>(mergeReviewKey)?.revision
      const result = await request<{ truncated?: boolean }>('identity.refresh', undefined, {}, signal)
      const fresh = await refreshIdentityReview(client, signal)
      // Only an explicitly requested sweep advances Undo. Background invalidation never rebases it.
      setUndo((current) =>
        current && current.revision === previous ? { ...current, revision: fresh.revision } : current,
      )
      return result
    },
  })
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      current.current?.abort()
    }
  }, [])
  async function refresh() {
    if (current.current || disabled) return
    const operation = new AbortController()
    current.current = operation
    setBusy(true)
    onBusy(true)
    setStatus('Checking your library for matches…')
    try {
      const result = await sweep.mutateAsync(operation.signal)
      if (!mounted.current || operation.signal.aborted) return
      setStatus(
        result?.truncated
          ? 'Suggestions refreshed. Choose Refresh suggestions again to check more matches.'
          : 'Suggestions refreshed. Your previous answers are kept.',
      )
    } catch {
      if (mounted.current)
        setStatus(
          operation.signal.aborted
            ? 'Refresh stopped. Choose Refresh suggestions to try again.'
            : "Couldn't refresh suggestions. Choose Refresh suggestions to try again.",
        )
    } finally {
      if (current.current === operation) current.current = null
      if (mounted.current) {
        setBusy(false)
        onBusy(false)
      }
    }
  }
  return { busy, status, disabled, refresh, cancel: () => current.current?.abort() }
}

export function MergeRefreshControl({
  state: { busy, status, disabled, refresh, cancel },
  mode = 'desktop',
}: {
  state: ReturnType<typeof useMergeRefresh>
  mode?: Mode
}) {
  return (
    <div className={`merge-refresh merge-refresh-${mode}`}>
      <button
        aria-label="Refresh suggestions"
        title="Refresh suggestions"
        disabled={disabled || busy}
        onClick={() => void refresh()}
      >
        <RefreshCw aria-hidden="true" size={18} />
        {mode === 'fullscreen' && <span>Refresh suggestions</span>}
      </button>
      {busy && <button onClick={cancel}>Cancel refresh</button>}
      {status && (
        <p className="merge-refresh-status" role="status" aria-live="polite">
          {status}
        </p>
      )}
    </div>
  )
}

export function MergeRefresh({
  mode,
  ...options
}: {
  disabled: boolean
  onBusy(value: boolean): void
  mode?: Mode
}) {
  const state = useMergeRefresh(options)
  return <MergeRefreshControl state={state} mode={mode} />
}
