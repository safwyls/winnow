import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import type { ApiRequest } from '../src/shared/bridge'
import { metadataSync } from '../src/renderer/features/metadata-sync'
import { resolveRoute } from '../src/main/routes'

function fixture(respond: (input: ApiRequest, id: string) => unknown | Promise<unknown>) {
  let id = ''
  const transport = vi.fn(async (input: ApiRequest) => {
    resolveRoute(input)
    if (input.route === 'operations.metadata') id = (input.body as { operationId: string }).operationId
    return { ok: true, status: 200, data: structuredClone(await respond(input, id)) }
  })
  vi.stubGlobal('window', { winnow: { request: transport } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  return { transport, client, model: metadataSync(client) }
}
const operation = (
  id: string,
  state = 'completed',
  metadataResult: number | null = 0,
  message = 'Complete.',
) => ({ id, kind: 'metadata-sync', state, message, updatedAt: '2026-09-30T00:00:00Z', metadataResult })
beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('shared manual metadata operation', () => {
  it('shares one scope, refuses duplicate starts, publishes progress and ignores late progress after completion', async () => {
    let current = { state: 'running', message: 'Matching games with IGDB…' }
    const { model, client, transport } = fixture((_input, id) =>
      operation(id, current.state, 0, current.message),
    )
    expect(metadataSync(client)).toBe(model)
    expect(metadataSync(new QueryClient())).not.toBe(model)
    const listener = vi.fn()
    const unsubscribe = model.subscribe(listener)
    const running = model.sync()
    expect(model.snapshot()).toMatchObject({ busy: true, status: 'Starting metadata sync…' })
    expect(model.sync()).toBe(running)
    await vi.advanceTimersByTimeAsync(0)
    expect(model.snapshot().status).toBe('Matching games with IGDB…')
    current = { state: 'running', message: 'Updating game details…' }
    await vi.advanceTimersByTimeAsync(1500)
    expect(model.snapshot().status).toBe('Updating game details…')
    current = { state: 'completed', message: 'Complete.' }
    await vi.advanceTimersByTimeAsync(1500)
    await running
    expect(model.snapshot()).toMatchObject({
      busy: false,
      status: 'Metadata sync finished. Games without a match may still need a manual match.',
    })
    const calls = transport.mock.calls.length
    current.message = 'Late progress from the completed operation'
    await vi.advanceTimersByTimeAsync(6000)
    expect(transport).toHaveBeenCalledTimes(calls)
    expect(model.snapshot().status).toContain('finished')
    expect(transport.mock.calls.filter(([request]) => request.route === 'operations.metadata')).toHaveLength(
      1,
    )
    expect(listener).toHaveBeenCalled()
    unsubscribe()
  })

  it.each([
    [0, 'finished'],
    [1, 'credentials in IGDB metadata'],
    [2, 'Try again'],
    [3, 'Reopen the library'],
  ])('maps backend result %s to shared guidance and allows a new explicit run', async (result, guidance) => {
    let outcome = Number(result)
    const { model, transport, client } = fixture((_input, id) => operation(id, 'completed', outcome))
    const invalidation = vi.spyOn(client, 'invalidateQueries')
    await model.sync()
    expect(model.snapshot().busy).toBe(false)
    expect(model.snapshot().status).toContain(String(guidance))
    const previousId = model.snapshot().operationId
    outcome = 0
    await model.sync()
    expect(model.snapshot().status).toContain('finished')
    expect(model.snapshot().operationId).not.toBe(previousId)
    expect(transport).toHaveBeenCalledTimes(2)
    expect(invalidation).toHaveBeenCalledWith({ queryKey: ['api'] })
  })

  it('retains the accepted identity after an interrupted start and hides private exception details', async () => {
    const accepted = new Set<string>()
    let broken = true
    const { model, transport } = fixture((_input, id) => {
      accepted.add(id)
      if (broken) throw Error('private diagnostic detail /secret/token')
      return operation(id)
    })
    await model.sync()
    expect(model.snapshot()).toMatchObject({
      busy: false,
      status: 'Metadata sync could not finish. Check your connection and IGDB credentials, then try again.',
    })
    broken = false
    await model.sync()
    expect(transport).toHaveBeenCalledTimes(2)
    expect(accepted.size).toBe(1)
    expect(model.snapshot().status).toContain('finished')
  })

  it('recovers the same operation after a progress read fails instead of starting duplicate work', async () => {
    let completed = false
    const ids: string[] = []
    const { model } = fixture((input, id) => {
      if (input.route === 'operations.metadata') {
        ids.push(id)
        return operation(id, completed ? 'completed' : 'running')
      }
      throw Error('Disconnected while reading private details')
    })
    const first = model.sync()
    await vi.advanceTimersByTimeAsync(1500)
    await first
    expect(model.snapshot().busy).toBe(false)
    expect(model.snapshot().status).toContain('Check your connection')
    completed = true
    await model.sync()
    expect(ids).toHaveLength(2)
    expect(new Set(ids).size).toBe(1)
    expect(model.snapshot().status).toContain('finished')
  })

  it.each(['failed', 'cancelled'])(
    'clears busy state after %s and retries with a fresh identity',
    async (state) => {
      let current = state
      const { model } = fixture((_input, id) =>
        operation(id, current, current === 'completed' ? 0 : null, 'private diagnostic detail'),
      )
      await model.sync()
      const id = model.snapshot().operationId
      expect(model.snapshot().busy).toBe(false)
      expect(model.snapshot().status).not.toContain('private')
      expect(model.snapshot().status).toContain(
        state === 'failed' ? 'Check your connection' : 'Updates already saved were kept',
      )
      current = 'completed'
      await model.sync()
      expect(model.snapshot().operationId).not.toBe(id)
      expect(model.snapshot().busy).toBe(false)
      expect(model.snapshot().status).toContain('finished')
    },
  )

  it.each(['identity', 'kind', 'missing-result', 'unknown-result', 'unknown-state'])(
    'rejects %s and retains the uncertain operation for retry',
    async (malformation) => {
      const { model } = fixture((_input, id) => {
        const value = operation(id)
        if (malformation === 'identity') value.id = 'another-operation'
        if (malformation === 'kind') value.kind = 'plugin-install'
        if (malformation === 'missing-result') value.metadataResult = null
        if (malformation === 'unknown-result') value.metadataResult = 99
        if (malformation === 'unknown-state') value.state = 'private unknown-state'
        return value
      })
      await model.sync()
      const id = model.snapshot().operationId
      expect(model.snapshot().status).toContain('Check your connection')
      await model.sync()
      expect(model.snapshot().operationId).toBe(id)
      expect(model.snapshot().busy).toBe(false)
    },
  )

  it('keeps running without subscribers and gives a reopened screen the current progress and result', async () => {
    let current = operation('', 'running', null, 'Starting…')
    const { model } = fixture((_input, id) => ({ ...current, id }))
    const original = vi.fn(),
      unsubscribe = model.subscribe(original)
    const running = model.sync()
    await vi.advanceTimersByTimeAsync(0)
    unsubscribe()
    original.mockClear()
    current.message = 'Updating game details…'
    await vi.advanceTimersByTimeAsync(1500)
    expect(original).not.toHaveBeenCalled()
    expect(model.snapshot()).toMatchObject({ busy: true, status: current.message })
    const reopened = vi.fn(),
      leave = model.subscribe(reopened)
    current = operation('', 'completed', 0)
    await vi.advanceTimersByTimeAsync(1500)
    await running
    expect(reopened).toHaveBeenCalled()
    expect(model.snapshot().status).toContain('finished')
    leave()
  })
})
