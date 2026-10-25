import type { ApplicationActivation } from '../shared/bridge'

function same(left: ApplicationActivation | undefined, right: ApplicationActivation) {
  if (!left || left.kind !== right.kind) return false
  if (left.kind === 'game' && right.kind === 'game') return left.ownershipId === right.ownershipId
  if (left.kind === 'plugin' && right.kind === 'plugin')
    return left.pluginId === right.pluginId && left.releaseTag === right.releaseTag
  return true
}

/** Consecutive identical launches coalesce only while the renderer is not ready. */
export class ActivationQueue {
  private pending: ApplicationActivation[] = []
  get count() {
    return this.pending.length
  }
  enqueue(value: ApplicationActivation): boolean {
    if (same(this.pending.at(-1), value)) return true
    if (this.pending.length >= 64) return false
    this.pending.push({ ...value })
    return true
  }
  drain(): ApplicationActivation[] {
    const pending = this.pending
    this.pending = []
    return pending
  }
}
