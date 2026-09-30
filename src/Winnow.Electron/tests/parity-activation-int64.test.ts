import { afterEach, describe, expect, it, vi } from 'vitest'
import { ownershipId } from '../src/shared/ownership-id'
import { readActivation, validatedActivation } from '../src/main/activation'
import { resolveRoute } from '../src/main/routes'
import { ActivationQueue } from '../src/main/activation-queue'
import { ExtendedActivationLauncher } from '../src/renderer/activation-launch'

const maximum = '9223372036854775807'
afterEach(() => vi.unstubAllGlobals())
describe('lossless ownership activation IDs', () => {
  it.each(['9007199254740991', '9007199254740992', '9007199254740993', maximum])(
    'preserves %s through arguments structured messages and the exact HTTP path',
    (text) => {
      const id = BigInt(text) <= BigInt(Number.MAX_SAFE_INTEGER) ? Number(text) : text
      expect(ownershipId(text)).toBe(id)
      expect(readActivation(['--jump-list-game', text])).toEqual({ kind: 'game', ownershipId: id })
      expect(validatedActivation({ kind: 'game', ownershipId: id })).toEqual({
        kind: 'game',
        ownershipId: id,
      })
      expect(
        resolveRoute({ route: 'actions.execute', params: { ownershipId: id }, body: { action: 'Primary' } })
          .path,
      ).toBe(`/api/v1/entries/${text}/actions`)
    },
  )
  it.each([
    '0',
    '-1',
    '01',
    '+1',
    '1.0',
    '1e3',
    ' 42',
    '42 ',
    '9223372036854775808',
    '9'.repeat(65535),
    Number.MAX_SAFE_INTEGER + 1,
    NaN,
    Infinity,
    null,
  ])('rejects malformed or already rounded identity %#', (value) => {
    expect(ownershipId(value)).toBeNull()
    expect(validatedActivation({ kind: 'game', ownershipId: value })).toBeNull()
    expect(() =>
      resolveRoute({ route: 'actions.execute', params: { ownershipId: value as string }, body: {} }),
    ).toThrow()
  })
  it('retains the original cold-start game fullscreen Int64-max FIFO contract', () => {
    const queue = new ActivationQueue()
    for (const args of [
      ['--jump-list-game', '23'],
      ['--jump-list-fullscreen'],
      ['--jump-list-game', maximum],
    ])
      queue.enqueue(readActivation(args))
    expect(queue.drain()).toEqual([
      { kind: 'game', ownershipId: 23 },
      { kind: 'fullscreen' },
      { kind: 'game', ownershipId: maximum },
    ])
    queue.enqueue(readActivation([]))
    expect(queue.drain()).toEqual([{ kind: 'show' }])
  })
  it('reuses an uncertain action identity and clears it after confirmed completion', async () => {
    const call = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, status: 503, message: 'Unavailable' })
      .mockResolvedValue({ ok: true, status: 200, data: 0 })
    vi.stubGlobal('window', { winnow: { request: call } })
    const launcher = new ExtendedActivationLauncher()
    await expect(launcher.launch(maximum)).rejects.toThrow('Unavailable')
    await launcher.launch(maximum)
    await launcher.launch(maximum)
    const values = call.mock.calls.map(([value]) => value)
    expect(values.map((value) => value.params)).toEqual(Array(3).fill({ ownershipId: maximum }))
    expect(values[0].body).toEqual({ operationId: expect.any(String), action: 'Primary' })
    expect(values[1].body.operationId).toBe(values[0].body.operationId)
    expect(values[2].body.operationId).not.toBe(values[1].body.operationId)
  })
  it.each([400, 404, 409])(
    'retires a definitive %s failure before the next explicit activation',
    async (status) => {
      const call = vi
        .fn()
        .mockResolvedValueOnce({ ok: false, status, message: 'Rejected' })
        .mockResolvedValue({ ok: true, status: 200, data: 2 })
      vi.stubGlobal('window', { winnow: { request: call } })
      const launcher = new ExtendedActivationLauncher()
      await expect(launcher.launch(maximum)).rejects.toThrow('Rejected')
      await expect(launcher.launch(maximum)).rejects.toThrow('could not accept')
      expect(call.mock.calls[1][0].body.operationId).not.toBe(call.mock.calls[0][0].body.operationId)
    },
  )
})
