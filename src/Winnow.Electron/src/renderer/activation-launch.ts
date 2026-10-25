import { ApiError, request } from './api/client'

/** Extended shell IDs bypass lossy JSON snapshot identities; the backend selects the current action. */
export class ExtendedActivationLauncher {
  private attempts = new Map<string, string>()
  async launch(ownershipId: string): Promise<void> {
    const operationId = this.attempts.get(ownershipId) ?? crypto.randomUUID()
    this.attempts.set(ownershipId, operationId)
    try {
      const result = await request<number>(
        'actions.execute',
        { ownershipId },
        { operationId, action: 'Primary' },
      )
      this.attempts.delete(ownershipId)
      if (result === 2) throw Error('The launcher could not accept this action.')
    } catch (error) {
      if (!(error instanceof ApiError) || !error.uncertain) this.attempts.delete(ownershipId)
      throw error
    }
  }
}
