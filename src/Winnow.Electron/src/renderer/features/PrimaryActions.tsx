import { createContext, useContext, useRef, useState } from 'react'
import { ApiError } from '../api/client'

type Operation = { operationId: string; action: string }
type Target = { action: string; title: string; store: string }
type Send = (ownershipId: number, target: Target, operation: Operation) => Promise<number>

export class PrimaryActions {
  private attempts = new Map<number, { target: Target; operation: Operation }>()
  private pending = new Map<number, Promise<number>>()

  constructor(
    private resolve: (ownershipId: number) => Target | null,
    private send: Send,
  ) {}

  launch = (ownershipId: number): Promise<number> => {
    const pending = this.pending.get(ownershipId)
    if (pending) return pending
    let attempt = this.attempts.get(ownershipId)
    if (!attempt) {
      const target = this.resolve(ownershipId)
      if (!target) return Promise.reject(Error('No supported launch action is available for this copy.'))
      attempt = { target, operation: { operationId: crypto.randomUUID(), action: target.action } }
      this.attempts.set(ownershipId, attempt)
    }
    // A tile, controller shortcut and Details can join the same pending action. An interrupted
    // response keeps its operation so changing views cannot dispatch that action a second time.
    const request = Promise.resolve()
      .then(() => this.send(ownershipId, attempt.target, attempt.operation))
      .then((result) => {
        this.attempts.delete(ownershipId)
        return result
      })
      .catch((error: unknown) => {
        if (!(error instanceof ApiError) || !error.uncertain) this.attempts.delete(ownershipId)
        throw error
      })
      .finally(() => this.pending.delete(ownershipId))
    this.pending.set(ownershipId, request)
    return request
  }

  dismiss = (ownershipId: number) => {
    if (!this.pending.has(ownershipId)) this.attempts.delete(ownershipId)
  }
}

export const PrimaryActionsContext = createContext<Pick<PrimaryActions, 'launch' | 'dismiss'> | null>(null)
export const usePrimaryActions = () => useContext(PrimaryActionsContext)

export function usePrimaryActionsHost(resolve: (ownershipId: number) => Target | null, send: Send) {
  const current = useRef({ resolve, send })
  current.current = { resolve, send }
  const [actions] = useState(
    () =>
      new PrimaryActions(
        (id) => current.current.resolve(id),
        (id, target, operation) => current.current.send(id, target, operation),
      ),
  )
  return actions
}
