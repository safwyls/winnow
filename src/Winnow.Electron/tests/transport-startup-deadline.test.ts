import { afterEach, expect, it, vi } from 'vitest'
import { BackendTransport } from '../src/main/transport'

const transports: BackendTransport[] = []
afterEach(async () => {
  for (const transport of transports.splice(0)) transport.stop()
  await vi.advanceTimersByTimeAsync(0)
  vi.useRealTimers()
})
function fixture(holdDiscovery = true) {
  vi.useFakeTimers()
  let release!: () => void
  const discovered = new Promise<void>((resolve) => {
    release = resolve
  })
  let stream!: ReadableStreamDefaultController<Uint8Array>
  const calls: string[] = []
  const transport = new BackendTransport({
    discover: async () => {
      if (holdDiscovery) await discovered
      return {
        address: 'http://127.0.0.1:32123/',
        token: 'fixture-only',
        epoch: 'cold',
        apiVersion: '1',
        processId: 123,
      }
    },
    fetch: async (input, init) => {
      const path = new URL(String(input)).pathname
      calls.push(path)
      if (path.endsWith('/events'))
        return new Response(
          new ReadableStream<Uint8Array>({
            start(controller) {
              stream = controller
              controller.enqueue(
                new TextEncoder().encode(
                  'id: cold:0\nevent: change\ndata: {"epoch":"cold","sequence":0,"kind":"resync-required"}\n\n',
                ),
              )
              init?.signal?.addEventListener(
                'abort',
                () => {
                  try {
                    controller.error(new DOMException('Stopped', 'AbortError'))
                  } catch {}
                },
                { once: true },
              )
            },
          }),
          { headers: { 'Content-Type': 'text/event-stream' } },
        )
      return Response.json({ games: [] })
    },
  })
  transports.push(transport)
  return { transport, calls, release, disconnect: () => stream.close() }
}

it('keeps initial snapshot reads pending beyond twelve seconds until the first backend handshake', async () => {
  const { transport, calls, release } = fixture()
  let settled = false
  const request = transport.request({ route: 'library.get' }).finally(() => {
    settled = true
  })
  await vi.advanceTimersByTimeAsync(17000)
  expect(settled).toBe(false)
  expect(calls).toEqual([])
  release()
  await vi.advanceTimersByTimeAsync(0)
  expect(await request).toEqual({ ok: true, status: 200, data: { games: [] } })
  expect(calls).toEqual(['/api/v1/events', '/api/v1/library'])
})

it('bounds a first attachment by the same forty-five-second startup policy as the connection state', async () => {
  const { transport, release } = fixture()
  let settled = false
  const request = transport.request({ route: 'library.get' }).finally(() => {
    settled = true
  })
  await vi.advanceTimersByTimeAsync(44999)
  expect(settled).toBe(false)
  await vi.advanceTimersByTimeAsync(1)
  expect(await request).toMatchObject({ ok: false, status: 503 })
  expect(transport.connection().message).toContain('45 seconds')
  release()
})

it('retains the twelve-second request bound after a previously connected backend disconnects', async () => {
  const { transport, calls, disconnect } = fixture(false)
  expect(await transport.request({ route: 'library.get' })).toMatchObject({ ok: true })
  disconnect()
  await vi.advanceTimersByTimeAsync(0)
  expect(transport.connection().connected).toBe(false)
  let settled = false
  const request = transport.request({ route: 'library.get' }).finally(() => {
    settled = true
  })
  await vi.advanceTimersByTimeAsync(11999)
  expect(settled).toBe(false)
  await vi.advanceTimersByTimeAsync(1)
  expect(await request).toMatchObject({ ok: false, status: 503 })
  expect(calls).toEqual(['/api/v1/events', '/api/v1/library'])
})

it('cancels a queued cold-start mutation without sending it when the backend eventually connects', async () => {
  const { transport, calls, release } = fixture()
  const cancellation = new AbortController()
  const request = transport.request(
    {
      route: 'preferences.library.put',
      body: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'teen' },
    },
    cancellation.signal,
  )
  await vi.advanceTimersByTimeAsync(10000)
  cancellation.abort()
  expect(await request).toMatchObject({ ok: false, status: 499 })
  release()
  await vi.advanceTimersByTimeAsync(0)
  expect(calls).toEqual(['/api/v1/events'])
})
