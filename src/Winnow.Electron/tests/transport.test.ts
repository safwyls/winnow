import { afterEach, describe, expect, it, vi } from 'vitest'
import { BackendTransport, EventCursor, parseEvents, validateDiscovery } from '../src/main/transport'
import { resolveRoute } from '../src/main/routes'

const discovery = {
  address: 'http://127.0.0.1:32123',
  token: 'private-token',
  epoch: 'epochA',
  apiVersion: '1',
  processId: 123,
}
const encoder = new TextEncoder()
const frame = (sequence: number, kind = 'library.changed', epoch = 'epochA') =>
  `id: ${epoch}:${sequence}\nevent: change\ndata: ${JSON.stringify({ epoch, sequence, kind })}\n\n`
const transports: BackendTransport[] = []
afterEach(() => {
  for (const transport of transports) transport.stop()
  transports.length = 0
})

describe('discovery and narrow routes', () => {
  it('accepts only exact IPv4 loopback root authorities and supported versions', () => {
    expect(validateDiscovery(discovery).address).toBe('http://127.0.0.1:32123/')
    for (const address of [
      'http://localhost:123/',
      'http://127.0.0.2:123/',
      'http://2130706433:123/',
      'http://127.1:123/',
      'https://127.0.0.1:123/',
      'http://127.0.0.1:123/api/',
      'http://user:pass@127.0.0.1:123/',
      'http://127.0.0.1:123/?token=x',
      'http://127.0.0.1:123/#x',
    ])
      expect(() => validateDiscovery({ ...discovery, address })).toThrow()
    expect(() => validateDiscovery({ ...discovery, apiVersion: '2' })).toThrow(/version/)
    expect(() => validateDiscovery({ ...discovery, token: 'secret\r\nX-Injected: true' })).toThrow()
  })
  it('does not accept arbitrary URLs, prototype keys, methods or traversal parameters', () => {
    for (const route of [
      'http://example.com',
      '../lifecycle/shutdown',
      'lifecycle.shutdown',
      '__proto__',
      'constructor',
    ])
      expect(() => resolveRoute({ route })).toThrow()
    for (const workId of ['../secrets', '1/../../health', '-1', '0', '1?x=y', '9007199254740999'])
      expect(() => resolveRoute({ route: 'game.details', params: { workId } })).toThrow()
    expect(() =>
      resolveRoute({ route: 'game.details', params: { workId: 1, url: 'http://example.com' } }),
    ).toThrow()
    expect(() => resolveRoute({ route: 'library.get', body: { action: 'delete' } })).toThrow()
    expect(resolveRoute({ route: 'metadata.search', params: { title: 'A&B?game' } }).path).toBe(
      '/api/v1/metadata/igdb/search?title=A%26B%3Fgame',
    )
    expect(resolveRoute({ route: 'artworkState', params: { workId: 4, slot: 'Hero' } }).path).toBe(
      '/api/v1/works/4/artwork/Hero',
    )
  })
})

describe('event reader', () => {
  it('handles split UTF-8, CRLF, comments and multiline events', async () => {
    const bytes = encoder.encode(
      ': heartbeat\r\nid: a:1\r\nevent: change\r\ndata: {"name":\r\ndata: "é"}\r\n\r\n',
    )
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        for (const byte of bytes) controller.enqueue(new Uint8Array([byte]))
        controller.close()
      },
    })
    const frames = []
    for await (const event of parseEvents(stream)) frames.push(event)
    expect(frames).toEqual([{ id: 'a:1', event: 'change', data: '{"name":\n"é"}' }])
  })
  it('discards duplicate replay, resyncs gaps and rejects forged cursors', () => {
    const cursor = new EventCursor()
    const consume = (seq: number, epoch = 'a', kind = 'library.changed') =>
      cursor.consume({ id: `${epoch}:${seq}`, data: JSON.stringify({ epoch, sequence: seq, kind }) })
    expect(consume(2, 'a', 'resync-required')?.kind).toBe('resync-required')
    expect(consume(2)).toBeNull()
    expect(consume(3)?.kind).toBe('library.changed')
    expect(consume(5)?.kind).toBe('resync-required')
    expect(consume(1, 'b')?.kind).toBe('resync-required')
    expect(() =>
      cursor.consume({
        id: 'b:2',
        data: JSON.stringify({ epoch: 'b', sequence: 3, kind: 'library.changed' }),
      }),
    ).toThrow()
  })
})

describe('main-owned transport', () => {
  it('registers events before sending snapshots and never exposes the bearer', async () => {
    let stream!: ReadableStreamDefaultController<Uint8Array>
    const calls: string[] = []
    const fetcher = vi.fn<typeof fetch>(async (input, init) => {
      const url = String(input)
      calls.push(url)
      expect(init?.redirect).toBe('error')
      expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer private-token')
      if (url.endsWith('/events'))
        return new Response(
          new ReadableStream({
            start(controller) {
              stream = controller
            },
          }),
          { headers: { 'Content-Type': 'text/event-stream' } },
        )
      return Response.json({ games: [] })
    })
    const transport = new BackendTransport({ discover: async () => discovery, fetch: fetcher })
    transports.push(transport)
    const request = transport.request({ route: 'library.get' })
    await vi.waitFor(() => expect(calls).toHaveLength(1))
    expect(calls[0]).toContain('/events')
    stream.enqueue(encoder.encode(frame(0, 'resync-required')))
    expect(await request).toEqual({ ok: true, status: 200, data: { games: [] } })
    expect(calls[1]).toContain('/library')
    expect(JSON.stringify(transport.connection())).not.toContain('private-token')
    stream.close()
  })
  it('does not retry uncertain mutations and preserves conflicts', async () => {
    let commands = 0
    const fetcher = vi.fn<typeof fetch>(async (input) => {
      if (String(input).endsWith('/events'))
        return new Response(
          new ReadableStream({
            start(controller) {
              controller.enqueue(encoder.encode(frame(0, 'resync-required')))
            },
          }),
          { headers: { 'Content-Type': 'text/event-stream' } },
        )
      commands++
      if (commands === 1) throw new TypeError('network failure')
      return Response.json({ detail: 'The revision changed' }, { status: 409 })
    })
    const transport = new BackendTransport({ discover: async () => discovery, fetch: fetcher })
    transports.push(transport)
    const request = {
      route: 'journal.put',
      params: { sessionId: 1 },
      body: { note: 'Draft', expectedRevision: 2 },
    }
    expect(await transport.request(request)).toMatchObject({
      ok: false,
      status: 0,
      message: expect.stringContaining('may have been saved'),
    })
    expect(commands).toBe(1)
    expect(await transport.request(request)).toMatchObject({
      ok: false,
      status: 409,
      message: 'The revision changed',
    })
    expect(commands).toBe(2)
  })
  it('rereads discovery and replays its last consumed event after reconnect', async () => {
    const discover = vi.fn(async () => discovery)
    let connects = 0
    const events: string[] = []
    const fetcher = vi.fn<typeof fetch>(async (_input, init) => {
      connects++
      if (connects === 2) expect((init?.headers as Record<string, string>)['Last-Event-ID']).toBe('epochA:4')
      return new Response(
        new ReadableStream({
          start(controller) {
            if (connects === 1) {
              controller.enqueue(encoder.encode(frame(4, 'resync-required')))
              controller.close()
            } else controller.enqueue(encoder.encode(frame(6)))
          },
        }),
        { headers: { 'Content-Type': 'text/event-stream' } },
      )
    })
    const transport = new BackendTransport({
      discover,
      fetch: fetcher,
      reconnectMs: 5,
      onEvent: (event) => events.push(event.kind),
    })
    transports.push(transport)
    transport.start()
    await vi.waitFor(() => expect(events).toEqual(['resync-required', 'resync-required']))
    expect(discover).toHaveBeenCalledTimes(2)
  })
})
