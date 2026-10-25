import { afterEach, describe, expect, it, vi } from 'vitest'
import { RequestLifetimes } from '../src/main/request-lifetimes'
import { BackendTransport } from '../src/main/transport'

const id = 'a'.repeat(32)
const otherId = 'b'.repeat(32)
const deferred = <T>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
const ok = { ok: true, status: 204 }

describe('renderer-owned request cancellation', () => {
  it('registers before dispatch and only the requesting renderer can cancel', async () => {
    const owners = new RequestLifetimes(),
      first = {},
      second = {}
    const pending = deferred<typeof ok>()
    let signal!: AbortSignal
    const request = owners.run(first, { route: 'identity.refresh', requestId: id }, async (value) => {
      signal = value!
      expect(owners.cancel(second, id)).toBe(false)
      expect(signal.aborted).toBe(false)
      expect(owners.cancel(first, id)).toBe(true)
      expect(signal.aborted).toBe(true)
      return pending.promise
    })
    expect(owners.cancel(first, id)).toBe(false)
    pending.resolve(ok)
    await request
    expect(owners.cancel(first, id)).toBe(false)
  })
  it('keeps identical request IDs isolated between renderers and closes only one owner', async () => {
    const owners = new RequestLifetimes(),
      first = {},
      second = {}
    const pending = deferred<typeof ok>(),
      signals: AbortSignal[] = []
    const send = async (signal?: AbortSignal) => {
      signals.push(signal!)
      return pending.promise
    }
    const one = owners.run(first, { route: 'identity.refresh', requestId: id }, send)
    const two = owners.run(second, { route: 'identity.refresh', requestId: id }, send)
    owners.close(first)
    expect(signals.map((signal) => signal.aborted)).toEqual([true, false])
    expect(owners.cancel(first, id)).toBe(false)
    expect(owners.cancel(second, id)).toBe(true)
    pending.resolve(ok)
    await Promise.all([one, two])
  })
  it('rejects malformed and duplicate IDs without starting another operation', async () => {
    const owners = new RequestLifetimes(),
      owner = {},
      send = vi.fn(async () => ok)
    for (const requestId of ['../secret', '', 'A'.repeat(32), 'x'.repeat(1000), 7]) {
      expect(
        await owners.run(owner, { route: 'identity.refresh', requestId: requestId as string }, send),
      ).toMatchObject({ status: 400 })
      expect(owners.cancel(owner, requestId)).toBe(false)
    }
    expect(send).not.toHaveBeenCalled()
    const pending = deferred<typeof ok>()
    const running = owners.run(
      owner,
      { route: 'identity.refresh', requestId: id },
      async () => pending.promise,
    )
    expect(await owners.run(owner, { route: 'identity.refresh', requestId: id }, send)).toMatchObject({
      status: 409,
    })
    expect(send).not.toHaveBeenCalled()
    pending.resolve(ok)
    await running
  })
  it('forgets settled requests so late cancellation cannot abort the next request', async () => {
    const owners = new RequestLifetimes(),
      owner = {}
    await owners.run(owner, { route: 'identity.refresh', requestId: id }, async () => ok)
    const pending = deferred<typeof ok>()
    let nextSignal!: AbortSignal
    const next = owners.run(owner, { route: 'identity.refresh', requestId: otherId }, async (signal) => {
      nextSignal = signal!
      return pending.promise
    })
    expect(owners.cancel(owner, id)).toBe(false)
    expect(nextSignal.aborted).toBe(false)
    pending.resolve(ok)
    await next
    expect(owners.cancel(owner, otherId)).toBe(false)
  })
  it('cleans up a rejected dispatch and closes every in-flight request for a destroyed owner', async () => {
    const owners = new RequestLifetimes(),
      owner = {}
    await expect(
      owners.run(owner, { route: 'identity.refresh', requestId: id }, async () => {
        throw new Error('closed')
      }),
    ).rejects.toThrow('closed')
    expect(owners.cancel(owner, id)).toBe(false)
    const pending = deferred<typeof ok>(),
      signals: AbortSignal[] = []
    const calls = [id, otherId].map((requestId) =>
      owners.run(owner, { route: 'identity.refresh', requestId }, async (signal) => {
        signals.push(signal!)
        return pending.promise
      }),
    )
    owners.close(owner)
    expect(signals.every((signal) => signal.aborted)).toBe(true)
    pending.resolve(ok)
    await Promise.all(calls)
  })
})

const transports: BackendTransport[] = []
afterEach(() => {
  transports.splice(0).forEach((transport) => transport.stop())
})
describe('cancellable backend transport', () => {
  const discovery = {
    address: 'http://127.0.0.1:32123',
    token: 'private-token',
    epoch: 'a',
    apiVersion: '1',
    processId: 1,
  }
  it('does no discovery or HTTP work for a request cancelled before dispatch', async () => {
    const discover = vi.fn(async () => discovery),
      fetcher = vi.fn<typeof fetch>()
    const transport = new BackendTransport({ discover, fetch: fetcher })
    transports.push(transport)
    const controller = new AbortController()
    controller.abort()
    expect(await transport.request({ route: 'identity.refresh', body: {} }, controller.signal)).toMatchObject(
      { status: 499 },
    )
    expect(discover).not.toHaveBeenCalled()
    expect(fetcher).not.toHaveBeenCalled()
  })
  it('cancels discovery wait immediately and never sends the pending mutation', async () => {
    const discover = deferred<typeof discovery>(),
      fetcher = vi.fn<typeof fetch>()
    const transport = new BackendTransport({ discover: () => discover.promise, fetch: fetcher })
    transports.push(transport)
    const controller = new AbortController()
    const pending = transport.request({ route: 'identity.refresh', body: {} }, controller.signal)
    controller.abort()
    expect(await pending).toMatchObject({ status: 499 })
    expect(fetcher).not.toHaveBeenCalled()
  })
  it('aborts only the chosen HTTP call and can send another request on the same event connection', async () => {
    let calls = 0
    let commandSignal!: AbortSignal
    const fetcher = vi.fn<typeof fetch>(async (input, init) => {
      if (String(input).endsWith('/events'))
        return new Response(
          new ReadableStream({
            start(stream) {
              stream.enqueue(
                new TextEncoder().encode(
                  'id: a:0\nevent: change\ndata: {"epoch":"a","sequence":0,"kind":"resync-required"}\n\n',
                ),
              )
            },
          }),
          { headers: { 'Content-Type': 'text/event-stream' } },
        )
      calls++
      if (calls === 1)
        return new Promise((_resolve, reject) => {
          commandSignal = init!.signal!
          commandSignal.addEventListener('abort', () => reject(commandSignal.reason), { once: true })
        })
      return Response.json({ revision: 'saved' })
    })
    const transport = new BackendTransport({ discover: async () => discovery, fetch: fetcher })
    transports.push(transport)
    const controller = new AbortController()
    const pending = transport.request({ route: 'identity.refresh', body: {} }, controller.signal)
    await vi.waitFor(() => expect(calls).toBe(1))
    controller.abort()
    expect(await pending).toMatchObject({ status: 499 })
    expect(commandSignal.aborted).toBe(true)
    expect(await transport.request({ route: 'identity.get' })).toMatchObject({
      ok: true,
      data: { revision: 'saved' },
    })
    expect(calls).toBe(2)
  })
})
