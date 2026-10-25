import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import type { ApiRequest } from '../src/shared/bridge'
import { PluginInstallation } from '../src/renderer/features/plugin-installation'
import { resolveRoute } from '../src/main/routes'

const psn = { pluginId: 'psn', releaseTag: 'v0.2.0' }
const xbox = { pluginId: 'xbox', releaseTag: 'v0.2.0' }
const plugin = (id: string) => ({ id, name: id, settings: [] })
const complete = (pluginId: string, outcome = 0) => ({
  state: 'completed',
  message: 'Complete.',
  pluginResult: {
    pluginId,
    outcome,
    message: outcome === 2 ? 'The download failed. Try again.' : 'Plugin settings are ready.',
  },
})
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
function fixture(respond: (input: ApiRequest) => unknown | Promise<unknown>) {
  const transport = vi.fn(async (input: ApiRequest) => {
    resolveRoute(input)
    return { ok: true, status: 200, data: await respond(input) }
  })
  vi.stubGlobal('window', { winnow: { request: transport } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  return { transport, client, model: new PluginInstallation(client) }
}
beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('source plugin installation lifetime', () => {
  it('Browser_request_waits_for_manual_retry_instead_of_being_dropped', async () => {
    const pending = deferred<ReturnType<typeof complete>>()
    const requests: string[] = [],
      opened: string[] = []
    const { model } = fixture((input) => {
      if (input.route === 'operations.plugin') {
        const id = (input.body as { request: typeof psn }).request.pluginId
        requests.push(id)
        return requests.length === 1
          ? complete(id, 2)
          : requests.length === 2
            ? pending.promise
            : complete(id)
      }
      return input.route === 'plugins.get' ? [plugin('psn'), plugin('xbox')] : null
    })
    await model.install(psn, {
      ready: (plugin) => {
        opened.push(plugin.id)
      },
    })
    expect(model.snapshot().canRetry).toBe(true)
    const retry = model.retry()
    expect(model.snapshot().busy).toBe(true)
    const browser = model.install(xbox, {
      ready: (plugin) => {
        opened.push(plugin.id)
      },
    })
    await vi.advanceTimersByTimeAsync(0)
    expect(requests).toEqual(['psn', 'psn'])
    pending.resolve(complete('psn'))
    await Promise.all([retry, browser])
    expect(requests).toEqual(['psn', 'psn', 'xbox'])
    expect(opened).toEqual(['psn', 'xbox'])
    expect(model.snapshot()).toMatchObject({ busy: false, canRetry: false, request: xbox })
  })

  it('Cancellation_clears_busy_state_and_offers_retry', async () => {
    let canceled = false
    const { model, transport } = fixture((input) => {
      if (input.route === 'operations.cancel') {
        canceled = true
        return null
      }
      return input.route === 'operations.detail' && canceled
        ? { state: 'cancelled' }
        : { state: 'running', message: 'Downloading the plugin…' }
    })
    const running = model.install(psn)
    await vi.advanceTimersByTimeAsync(0)
    expect(model.snapshot().status).toBe('Downloading the plugin…')
    await model.cancel()
    await vi.advanceTimersByTimeAsync(3000)
    await running
    expect(model.snapshot()).toMatchObject({ busy: false, canRetry: true })
    expect(model.snapshot().status).toContain('cancelled')
    expect(transport.mock.calls.filter(([input]) => input.route === 'operations.cancel')).toHaveLength(1)
  })

  it.each([0, 1])(
    'publishes settings and refreshes only a newly installed package, outcome %s',
    async (outcome) => {
      const { model, transport, client } = fixture((input) =>
        input.route === 'operations.plugin'
          ? complete('psn', outcome)
          : input.route === 'plugins.get'
            ? [plugin('psn')]
            : null,
      )
      const started = vi.fn(),
        ready = vi.fn()
      await model.install(psn, { started, ready })
      expect(started).toHaveBeenCalledOnce()
      expect(ready).toHaveBeenCalledWith(plugin('psn'))
      expect(transport.mock.calls.filter(([input]) => input.route === 'plugins.refresh')).toHaveLength(
        outcome === 0 ? 1 : 0,
      )
      expect(client.getQueryData(['api', 'plugins.get', undefined])).toEqual([plugin('psn')])
      expect(model.snapshot()).toMatchObject({
        busy: false,
        canRetry: false,
        status: 'Plugin settings are ready.',
      })
    },
  )

  it('retains one identity after an uncertain start, hides private exceptions and refuses duplicate retry input', async () => {
    const ids: string[] = []
    const second = deferred<ReturnType<typeof complete>>()
    const { model } = fixture((input) => {
      if (input.route === 'operations.plugin') {
        ids.push((input.body as { operationId: string }).operationId)
        if (ids.length === 1) throw new Error('private /secret/key token')
        return second.promise
      }
      return input.route === 'plugins.get' ? [plugin('psn')] : null
    })
    await model.install(psn)
    expect(model.snapshot().status).toBe('Could not finish the plugin installation. Try again.')
    const retry = model.retry(),
      duplicate = model.retry()
    await vi.advanceTimersByTimeAsync(0)
    expect(ids).toHaveLength(2)
    expect(ids[0]).toBe(ids[1])
    second.resolve(complete('psn'))
    await Promise.all([retry, duplicate])
    expect(model.snapshot().canRetry).toBe(false)
  })

  it('uses a fresh operation after terminal failure and after cancellation', async () => {
    for (const failed of [complete('psn', 2), { state: 'cancelled' }]) {
      const ids: string[] = []
      const { model } = fixture((input) => {
        if (input.route === 'operations.plugin') {
          ids.push((input.body as { operationId: string }).operationId)
          return ids.length === 1 ? failed : complete('psn')
        }
        return input.route === 'plugins.get' ? [plugin('psn')] : null
      })
      await model.install(psn)
      await model.retry()
      expect(ids).toHaveLength(2)
      expect(ids[0]).not.toBe(ids[1])
    }
  })

  it('cancels a start that accepts after cancellation without losing its backend identity', async () => {
    const start = deferred<{ state: string; message: string }>()
    const { model, transport } = fixture((input) =>
      input.route === 'operations.plugin' ? start.promise : { state: 'cancelled' },
    )
    const pending = model.install(psn)
    await vi.advanceTimersByTimeAsync(0)
    await model.cancel()
    start.resolve({ state: 'running', message: 'Downloading the plugin…' })
    await vi.advanceTimersByTimeAsync(1500)
    await pending
    expect(transport.mock.calls.filter(([input]) => input.route === 'operations.cancel')).toHaveLength(1)
    expect(model.snapshot()).toMatchObject({ busy: false, canRetry: true })
  })

  it('retries a settings publication failure without reinstalling or refreshing twice', async () => {
    let reads = 0
    const { model, transport } = fixture((input) => {
      if (input.route === 'operations.plugin') return complete('psn')
      if (input.route === 'plugins.get') return ++reads === 1 ? [] : [plugin('psn')]
      return null
    })
    const ready = vi.fn()
    await model.install(psn, { ready })
    expect(model.snapshot().canRetry).toBe(true)
    expect(ready).not.toHaveBeenCalled()
    await model.retry()
    const starts = transport.mock.calls
      .map(([input]) => input)
      .filter((input) => input.route === 'operations.plugin')
    expect(starts[0].body).toEqual(starts[1].body)
    expect(transport.mock.calls.filter(([input]) => input.route === 'plugins.refresh')).toHaveLength(1)
    expect(ready).toHaveBeenCalledOnce()
  })
})
