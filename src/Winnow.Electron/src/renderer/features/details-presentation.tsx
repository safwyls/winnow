import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { RefreshCw } from 'lucide-react'
import type { GameDetails, Workspace } from '../api/types'
import { request } from '../api/client'
import { receptionFigures, refetchStatus, type RefetchResult } from './details-facts'
import { useViewState } from '../viewState'
import { lifecycleForWork, lifecycleText } from '../../shared/lifecycle'

export function LifecycleEvidence({ workId, workspace }: { workId: number; workspace?: Workspace }) {
  const text = lifecycleText(lifecycleForWork(workId, workspace))
  return text ? <p className="lifecycle-evidence">{text}</p> : null
}

export function ReceptionLine({
  ratings,
  compact = false,
}: {
  ratings?: GameDetails['ratings']
  compact?: boolean
}) {
  const figures = receptionFigures(ratings)
  if (!figures.length) return null
  return (
    <div className={`reception-line${compact ? ' compact' : ''}`} aria-label="Reception">
      {figures.map((figure, index) => (
        <span key={figure.source} title={figure.tooltip} aria-label={figure.automationName}>
          {index > 0 && <span aria-hidden="true"> · </span>}
          {compact ? (
            `${figure.compactSource}: ${figure.compactValue}`
          ) : (
            <>
              {figure.source} <strong>{figure.value}</strong> / {figure.count}
            </>
          )}
        </span>
      ))}
    </div>
  )
}

/** A completed write refreshes details in place, keeping the chosen section and its confirmation. */
export function useMetadataRefresh(workId: number) {
  const client = useQueryClient()
  const [status, setStatus] = useViewState<ReturnType<typeof refetchStatus> | null>(
    `details:${workId}:refetch`,
    null,
  )
  const [pending, setPending] = useState(false)
  const controller = useRef<AbortController | null>(null)
  useEffect(() => () => controller.current?.abort(), [])
  async function refresh() {
    if (controller.current) return
    const active = new AbortController()
    controller.current = active
    setPending(true)
    setStatus(null)
    try {
      const result = await request<RefetchResult>('game.refetch', { workId }, undefined, active.signal)
      const next = refetchStatus(result)
      setStatus(next)
      if (next.changed) {
        await client.invalidateQueries({
          predicate: (query) => ['api', 'artwork', 'artwork-image'].includes(String(query.queryKey[0])),
        })
      }
    } catch {
      if (!active.signal.aborted) setStatus(refetchStatus({ outcome: 'Unreachable' }))
    } finally {
      if (controller.current === active) controller.current = null
      if (!active.signal.aborted) setPending(false)
    }
  }
  return { pending, status, refresh }
}

type MetadataRefreshState = ReturnType<typeof useMetadataRefresh>

export function MetadataRefreshButton({
  state,
  onInvoked,
}: {
  state: MetadataRefreshState
  onInvoked?(): void
}) {
  return (
    <button
      disabled={state.pending}
      title="Re-ask IGDB and the Steam store about this game"
      onClick={() => {
        void state.refresh()
        onInvoked?.()
      }}
    >
      <RefreshCw size={16} aria-hidden="true" /> Refetch metadata
    </button>
  )
}

export function MetadataRefreshStatus({
  state,
  className,
  polite = false,
}: {
  state: MetadataRefreshState
  className?: string
  polite?: boolean
}) {
  if (!state.pending && !state.status) return null
  return (
    <p
      className={className}
      role={polite || !state.status?.problem ? 'status' : 'alert'}
      aria-live={polite ? 'polite' : undefined}
      data-problem={(!state.pending && state.status?.problem) || undefined}
    >
      {state.pending ? 'Refetching…' : state.status!.message}
    </p>
  )
}

export function MetadataRefresh({ workId }: { workId: number }) {
  const state = useMetadataRefresh(workId)
  return (
    <div className="metadata-refetch">
      <MetadataRefreshButton state={state} />
      <MetadataRefreshStatus state={state} />
    </div>
  )
}
