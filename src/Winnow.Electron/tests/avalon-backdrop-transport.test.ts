import { afterEach, expect, it, vi } from 'vitest'
import { BackendTransport } from '../src/main/transport'
import { RequestLifetimes } from '../src/main/request-lifetimes'
import { resolveRoute } from '../src/main/routes'

const discovery = {
  address: 'http://127.0.0.1:32123',
  token: 'private-token',
  epoch: 'epochA',
  apiVersion: '1',
  processId: 123,
}
let transport: BackendTransport | undefined
afterEach(() => transport?.stop())
function create(image: (init: RequestInit, url: URL) => Promise<Response>) {
  transport = new BackendTransport({
    discover: async () => discovery,
    fetch: vi.fn<typeof fetch>(async (input, init) => {
      const url = new URL(String(input))
      if (url.pathname.endsWith('/events'))
        return new Response(
          new ReadableStream({
            start(controller) {
              controller.enqueue(
                new TextEncoder().encode(
                  'id: epochA:0\nevent: change\ndata: {"epoch":"epochA","sequence":0,"kind":"resync-required"}\n\n',
                ),
              )
            },
          }),
          { headers: { 'Content-Type': 'text/event-stream' } },
        )
      expect((init!.headers as Record<string, string>).Authorization).toBe('Bearer private-token')
      expect(init!.redirect).toBe('error')
      return image(init!, url)
    }),
  })
  return transport
}
it('exposes only the named local backdrop route and bounds the original largest image bucket', async () => {
  expect(resolveRoute({ route: 'artwork.backdrop', params: { workId: 42, aspectRatio: 2.4 } }).path).toBe(
    '/api/v1/works/42/backdrop?aspectRatio=2.4',
  )
  expect(() =>
    resolveRoute({ route: 'artwork.backdrop', params: { workId: 42, url: 'https://example.com' } }),
  ).toThrow()
  const image = vi.fn(async (_init: RequestInit, url: URL) => {
    expect(url.searchParams.get('width')).toBe('3840')
    return new Response(new Uint8Array([1, 2, 3]), { headers: { 'Content-Type': 'image/png' } })
  })
  const value = create(image)
  expect(await value.artwork('fixture', 'wide', 3840)).toBe('data:image/png;base64,AQID')
  for (const width of [3841, 63, -1, NaN, 1920.5])
    expect(await value.artwork('fixture', 'wide', width)).toBeNull()
  expect(image).toHaveBeenCalledOnce()
})
it.each(['cancel', 'close'] as const)(
  'image cancellation is scoped to its renderer and %s aborts the fetch',
  async (action) => {
    const owner = {},
      other = {},
      lifetimes = new RequestLifetimes()
    let signal: AbortSignal | undefined
    const value = create(async (init) => {
      signal = init.signal as AbortSignal
      return new Promise<Response>((_, reject) =>
        signal!.addEventListener('abort', () => reject(signal!.reason)),
      )
    })
    const id = 'a'.repeat(32)
    const result = lifetimes.run(owner, { route: 'artwork.image', requestId: id }, async (signal) => ({
      ok: true,
      status: 200,
      data: await value.artwork('fixture', 'wide', 1920, signal),
    }))
    await vi.waitFor(() => expect(signal).toBeDefined())
    expect(lifetimes.cancel(other, id)).toBe(false)
    expect(signal!.aborted).toBe(false)
    if (action === 'cancel') expect(lifetimes.cancel(owner, id)).toBe(true)
    else lifetimes.close(owner)
    expect(signal!.aborted).toBe(true)
    expect((await result).data).toBeNull()
    expect(lifetimes.cancel(owner, id)).toBe(false)
  },
)
it('does not expose bytes from a fetcher that completes after cancellation', async () => {
  let finish!: (response: Response) => void
  const value = create(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }),
  )
  const controller = new AbortController()
  const result = value.artwork('fixture', 'wide', 1920, controller.signal)
  await vi.waitFor(() => expect(finish).toBeDefined())
  controller.abort()
  finish(new Response(new Uint8Array([1]), { headers: { 'Content-Type': 'image/png' } }))
  expect(await result).toBeNull()
})
