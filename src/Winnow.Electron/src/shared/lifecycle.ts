import type { Workspace } from '../renderer/api/types'

export interface LifecycleEvidenceRecord {
  status: number | string
  confidence: number
  reason: string
  isExemptFromDerelict?: boolean
}
interface LifecycleBucket {
  ownershipId: number
  resolvedWorkId: number
  lifecycle?: LifecycleEvidenceRecord | null
  game?: { lifecycle?: LifecycleEvidenceRecord | null }
}
const statuses = ['Unknown', 'Active', 'Inactive', 'Dead', 'Abandoned', 'Offline', 'Delisted', 'Cancelled']

export function lifecycleStatus(evidence?: LifecycleEvidenceRecord | null): string | null {
  if (!evidence) return null
  const status = typeof evidence.status === 'number' ? statuses[evidence.status] : evidence.status
  return status && statuses.includes(status) && status !== 'Unknown' ? status : null
}

export function isDerelict(evidence?: LifecycleEvidenceRecord | null): boolean {
  return (
    !evidence?.isExemptFromDerelict &&
    ['Dead', 'Abandoned', 'Offline', 'Delisted', 'Cancelled'].includes(lifecycleStatus(evidence) ?? '')
  )
}

export function lifecycleForOwnership(ownershipId: number, workspace?: Workspace) {
  return (workspace?.buckets as LifecycleBucket[] | undefined)?.find((row) => row.ownershipId === ownershipId)
    ?.lifecycle
}

export function lifecycleForWork(workId: number, workspace?: Workspace) {
  return (workspace?.buckets as LifecycleBucket[] | undefined)?.find((row) => row.resolvedWorkId === workId)
    ?.game?.lifecycle
}

export function lifecycleText(evidence?: LifecycleEvidenceRecord | null): string | null {
  const status = lifecycleStatus(evidence)
  if (!status || !evidence) return null
  const confidence = evidence.confidence.toLocaleString(undefined, {
    style: 'percent',
    maximumFractionDigits: 0,
  })
  return `${status} · ${confidence} confidence. ${evidence.reason}${evidence.isExemptFromDerelict ? ' Kept out of Derelict by your choice.' : ''}`
}
