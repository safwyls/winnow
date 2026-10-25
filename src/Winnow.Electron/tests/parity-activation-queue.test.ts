import { describe, expect, it } from 'vitest'
import { ActivationQueue } from '../src/main/activation-queue'
import type { ApplicationActivation } from '../src/shared/bridge'

describe('native pending activation queue', () => {
  it('keeps mixed plugin fullscreen game and show launches in FIFO order', () => {
    const queue = new ActivationQueue(),
      values: ApplicationActivation[] = [
        { kind: 'plugin', pluginId: 'psn', releaseTag: 'v0.2.0-beta.1' },
        { kind: 'fullscreen' },
        { kind: 'game', ownershipId: 23 },
        { kind: 'show' },
      ]
    values.forEach((value) => expect(queue.enqueue(value)).toBe(true))
    expect(queue.drain()).toEqual(values)
    expect(queue.count).toBe(0)
    expect(queue.drain()).toEqual([])
  })
  it('admits sixty-four distinct requests and recovers after the renderer drains them', () => {
    const queue = new ActivationQueue()
    for (let id = 1; id <= 64; id++) expect(queue.enqueue({ kind: 'game', ownershipId: id })).toBe(true)
    expect(queue.enqueue({ kind: 'game', ownershipId: 64 })).toBe(true)
    expect(queue.enqueue({ kind: 'fullscreen' })).toBe(false)
    expect(queue.count).toBe(64)
    expect(queue.drain().map((value) => (value.kind === 'game' ? value.ownershipId : null))).toEqual(
      Array.from({ length: 64 }, (_, i) => i + 1),
    )
    expect(queue.enqueue({ kind: 'fullscreen' })).toBe(true)
    expect(queue.drain()).toEqual([{ kind: 'fullscreen' }])
  })
  it.each<ApplicationActivation>([
    { kind: 'show' },
    { kind: 'fullscreen' },
    { kind: 'game', ownershipId: 42 },
    { kind: 'plugin', pluginId: 'xbox', releaseTag: 'v1.2.3' },
  ])('coalesces only adjacent identical $kind requests before readiness', (value) => {
    const queue = new ActivationQueue()
    queue.enqueue(value)
    queue.enqueue({ ...value })
    expect(queue.count).toBe(1)
    queue.enqueue({ kind: 'game', ownershipId: 99 })
    queue.enqueue(value)
    expect(queue.drain()).toEqual([value, { kind: 'game', ownershipId: 99 }, value])
    queue.enqueue(value)
    expect(queue.drain()).toEqual([value])
  })
  it('captures immutable launch values even if the caller reuses its object', () => {
    const queue = new ActivationQueue(),
      value = { kind: 'game' as const, ownershipId: 42 }
    queue.enqueue(value)
    value.ownershipId = 99
    queue.enqueue(value)
    expect(queue.drain()).toEqual([
      { kind: 'game', ownershipId: 42 },
      { kind: 'game', ownershipId: 99 },
    ])
  })
})
