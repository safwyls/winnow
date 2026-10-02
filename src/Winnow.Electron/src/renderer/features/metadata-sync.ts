import { useSyncExternalStore } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { createClientId, request } from '../api/client'
import type { BackendOperation } from '../api/types'

export interface MetadataSyncState {
  busy: boolean
  status: string
  operationId: string | null
}
export const metadataSyncExplanation =
  'Match and update details for games already in your library. Uses cached metadata when available and keeps your manual choices.'
const failure = 'Metadata sync could not finish. Check your connection and IGDB credentials, then try again.'
const completed = [
  'Metadata sync finished. Games without a match may still need a manual match.',
  'Add credentials in IGDB metadata, then try again.',
  'Some metadata steps could not finish. Available updates were kept. Try again.',
  'Metadata sync finished, but the library could not refresh. Reopen the library to see updates.',
] as const

/** One operation survives navigation and shares confirmed progress between both presentations. */
export class MetadataSync {
  private state: MetadataSyncState = { busy: false, status: '', operationId: null }
  private listeners = new Set<() => void>()
  private pending: Promise<void> | undefined
  private terminal = false
  constructor(private readonly client: QueryClient) {}
  snapshot = () => this.state
  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }
  private publish(patch: Partial<MetadataSyncState>) {
    this.state = { ...this.state, ...patch }
    this.listeners.forEach((listener) => listener())
  }
  sync(): Promise<void> {
    if (this.state.busy) return this.pending ?? Promise.resolve()
    // Retrying an uncertain response reuses the backend's idempotent operation identity.
    const id = !this.terminal && this.state.operationId ? this.state.operationId : createClientId()
    this.terminal = false
    this.publish({ busy: true, status: 'Starting metadata sync…', operationId: id })
    const pending = this.run(id).finally(() => {
      if (this.pending === pending) this.pending = undefined
    })
    this.pending = pending
    return pending
  }
  private async run(id: string) {
    try {
      let operation = await request<BackendOperation>('operations.metadata', undefined, { operationId: id })
      for (;;) {
        if (!operation || operation.id !== id || operation.kind !== 'metadata-sync')
          throw new Error('Unexpected metadata operation')
        const state = operation.state.toLowerCase()
        if (!['running', 'queued', 'pending'].includes(state)) break
        this.publish({ status: operation.message || 'Starting metadata sync…' })
        await new Promise((done) => setTimeout(done, 1500))
        operation = await request<BackendOperation>('operations.detail', { id })
      }
      const state = operation.state.toLowerCase()
      if (state === 'cancelled') {
        this.terminal = true
        this.publish({ status: 'Metadata sync stopped. Updates already saved were kept.' })
      } else if (state === 'failed') {
        this.terminal = true
        this.publish({ status: failure })
      } else if (
        state === 'completed' &&
        Number.isInteger(operation.metadataResult) &&
        operation.metadataResult! >= 0 &&
        operation.metadataResult! < completed.length
      ) {
        this.terminal = true
        this.publish({ status: completed[operation.metadataResult!] })
        void this.client.invalidateQueries({ queryKey: ['api'] })
      } else throw new Error('Missing metadata result')
    } catch {
      this.publish({ status: failure })
    } finally {
      this.publish({ busy: false })
      void this.client.invalidateQueries({ queryKey: ['api', 'operations.get'] })
    }
  }
}

const operations = new WeakMap<QueryClient, MetadataSync>()
export function metadataSync(client: QueryClient) {
  let operation = operations.get(client)
  if (!operation) {
    operation = new MetadataSync(client)
    operations.set(client, operation)
  }
  return operation
}
export function useMetadataSync() {
  const operation = metadataSync(useQueryClient())
  return { operation, ...useSyncExternalStore(operation.subscribe, operation.snapshot, operation.snapshot) }
}
