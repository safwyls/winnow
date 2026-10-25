// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { RequestLifetimes } from '../src/main/request-lifetimes'
import { Artwork } from '../src/renderer/components/Artwork'
import {
  artworkImages,
  artworkLifetime,
  closeArtworkImages,
  loadArtworkImage,
  type ArtworkKey,
} from '../src/renderer/components/artwork-images'

const png = 'data:image/png;base64,YXJ0d29yaw=='
const clients: QueryClient[] = []
function client() {
  const value = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  })
  clients.push(value)
  return value
}
function bridge(artwork = vi.fn().mockResolvedValue(png)) {
  const request = vi.fn(async () => ({
    ok: true,
    status: 200,
    data: { current: { previewKey: { provider: 'steam', id: '42' } }, revision: 'same-selection' },
  }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, artwork, cancelRequest: vi.fn(async () => true) },
  })
  return { request, artwork }
}
beforeEach(() => {
  let sequence = 0
  URL.createObjectURL = vi.fn(() => `blob:runtime-${++sequence}`)
  URL.revokeObjectURL = vi.fn()
  vi.stubGlobal(
    'Image',
    class {
      src = ''
      naturalWidth = 160
      naturalHeight = 240
      async decode() {}
    },
  )
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
    x: 0,
    y: 0,
    left: 0,
    top: 0,
    width: 160,
    height: 240,
    right: 160,
    bottom: 240,
    toJSON() {},
  })
})
afterEach(async () => {
  cleanup()
  for (const value of clients.splice(0)) {
    await closeArtworkImages(value)
    value.clear()
  }
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it('a full artwork queue leaves main request capacity for new cover selections', async () => {
  const value = client(),
    cache = artworkImages(value),
    owner = {},
    requests = new RequestLifetimes()
  let finish!: (source: string) => void
  const held = new Promise<string>((resolve) => {
    finish = resolve
  })
  const artwork = vi.fn(async (_provider: string, _id: string, _width: number, requestId: string) => {
    const result = await requests.run(owner, { route: 'artwork.image', requestId }, async () => ({
      ok: true,
      status: 200,
      data: await held,
    }))
    return result.ok ? (result.data ?? null) : null
  })
  bridge(artwork)
  const leases = Array.from({ length: 128 }, (_, index) => {
    const key: ArtworkKey = ['artwork-image', 'steam', String(index + 1), 160, 'same-selection']
    return cache.acquire(JSON.stringify(key), (signal) => loadArtworkImage(value, key, signal))
  })
  try {
    expect(cache.pendingCount).toBe(128)
    const selection = await requests.run(
      owner,
      { route: 'artworkState', requestId: 'e'.repeat(32) },
      async () => ({
        ok: true,
        status: 200,
        data: { current: { previewKey: { provider: 'steam', id: 'new' } } },
      }),
    )
    expect(selection.ok).toBe(true)
    expect(artwork.mock.calls.length).toBeLessThan(128)
    finish(png)
    expect((await Promise.all(leases.map((lease) => lease.ready))).every(Boolean)).toBe(true)
    expect(artwork).toHaveBeenCalledTimes(128)
  } finally {
    leases.forEach((lease) => lease.release())
    finish(png)
    await Promise.all(leases.map((lease) => lease.ready))
  }
})

it.each(['desktop', 'fullscreen'] as const)(
  '%s selection refresh does not cancel an unchanged cover download',
  async (mode) => {
    let finish!: (value: string) => void
    const api = bridge(
        vi.fn(
          () =>
            new Promise<string>((resolve) => {
              finish = resolve
            }),
        ),
      ),
      value = client()
    const view = render(
      <QueryClientProvider client={value}>
        <div className={`avalon-shell ${mode}`}>
          <Artwork workId={42} />
        </div>
      </QueryClientProvider>,
    )
    await waitFor(() => expect(api.artwork).toHaveBeenCalledOnce())
    for (let index = 0; index < 3; index++)
      await act(async () => {
        await value.invalidateQueries({ queryKey: ['artwork'] })
      })
    expect(api.request).toHaveBeenCalledTimes(4)
    expect(window.winnow.cancelRequest).not.toHaveBeenCalled()
    expect(api.artwork).toHaveBeenCalledOnce()
    await act(async () => finish(png))
    await waitFor(() => expect(view.container.querySelector('img')).not.toBeNull())
    expect(view.container.querySelector('img')?.getAttribute('loading')).toBe('eager')
    fireEvent.load(view.container.querySelector('img')!)
    expect(view.container.querySelector('.artwork')?.getAttribute('data-state')).toBe('ready')
  },
)

it.each(['desktop', 'fullscreen'] as const)(
  '%s retries transient selection failures without scrolling or opening details',
  async (mode) => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] })
    const api = bridge(),
      value = client()
    api.request.mockResolvedValueOnce({ ok: false, status: 429 } as never)
    const view = render(
      <QueryClientProvider client={value}>
        <div className={`avalon-shell ${mode}`}>
          <Artwork workId={42} />
        </div>
      </QueryClientProvider>,
    )
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1)
    })
    expect(view.container.querySelector('.artwork')?.getAttribute('data-state')).toBe('error')
    expect(api.artwork).not.toHaveBeenCalled()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2_000)
    })
    expect(api.request).toHaveBeenCalledTimes(2)
    expect(view.container.querySelector('img')).not.toBeNull()
    fireEvent.load(view.container.querySelector('img')!)
    expect(view.container.querySelector('.artwork')?.getAttribute('data-state')).toBe('ready')
  },
)

it('selection retries are bounded, do not retry invalid requests, and stop on detach', async () => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] })
  const api = bridge(),
    value = client()
  api.request.mockResolvedValue({ ok: false, status: 503 } as never)
  const host = () => (
    <QueryClientProvider client={value}>
      <Artwork workId={42} />
    </QueryClientProvider>
  )
  const view = render(host())
  await act(async () => {
    await vi.advanceTimersByTimeAsync(60_000)
  })
  expect(api.request).toHaveBeenCalledTimes(4)
  await act(async () => {
    await vi.advanceTimersByTimeAsync(60_000)
  })
  expect(api.request).toHaveBeenCalledTimes(4)
  view.unmount()
  const next = render(host())
  await act(async () => {
    await vi.advanceTimersByTimeAsync(1)
  })
  const attempts = api.request.mock.calls.length
  next.unmount()
  await act(async () => {
    await vi.advanceTimersByTimeAsync(60_000)
  })
  expect(api.request).toHaveBeenCalledTimes(attempts)
  value.clear()
  api.request.mockResolvedValue({ ok: false, status: 400 } as never)
  render(host())
  await act(async () => {
    await vi.advanceTimersByTimeAsync(60_000)
  })
  expect(api.request).toHaveBeenCalledTimes(attempts + 1)
})

it.each(['desktop', 'fullscreen'] as const)(
  '%s covers reload after five idle minutes and recover a temporary image failure without navigation or resize',
  async (mode) => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] })
    const api = bridge(),
      value = client()
    const host = (attached: boolean) => (
      <QueryClientProvider client={value}>
        <div className={`avalon-shell ${mode}`}>{attached && <Artwork workId={42} />}</div>
      </QueryClientProvider>
    )
    const view = render(host(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1)
    })
    expect(view.container.querySelector('img')).not.toBeNull()
    fireEvent.load(view.container.querySelector('img')!)
    const original = view.container.querySelector('img')!.getAttribute('src')
    view.rerender(host(false))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(artworkLifetime + 1)
    })
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(original)
    api.artwork.mockResolvedValueOnce(null)
    view.rerender(host(true))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1)
    })
    expect(view.container.textContent).toContain('Artwork unavailable')
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2_000)
    })
    expect(view.container.querySelector('img')).not.toBeNull()
    fireEvent.load(view.container.querySelector('img')!)
    expect(view.container.querySelector('.artwork')?.getAttribute('data-state')).toBe('ready')
    expect(api.artwork).toHaveBeenCalledTimes(3)
    view.unmount()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(60_000)
    })
    expect(api.artwork).toHaveBeenCalledTimes(3)
  },
)

it('failed covers retry only three times and detach cancels a scheduled retry', async () => {
  const api = bridge(vi.fn().mockResolvedValue(null)),
    value = client()
  const host = () => (
    <QueryClientProvider client={value}>
      <Artwork workId={42} />
    </QueryClientProvider>
  )
  const first = render(host())
  await waitFor(() => expect(first.container.textContent).toContain('Artwork unavailable'))
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] })
  first.unmount()
  const second = render(host())
  await act(async () => {
    await vi.advanceTimersByTimeAsync(1)
  })
  await act(async () => {
    await vi.advanceTimersByTimeAsync(60_000)
  })
  expect(api.artwork).toHaveBeenCalledTimes(5)
  await act(async () => {
    await vi.advanceTimersByTimeAsync(60_000)
  })
  expect(api.artwork).toHaveBeenCalledTimes(5)
  second.unmount()
  const third = render(host())
  await act(async () => {
    await vi.advanceTimersByTimeAsync(1)
  })
  const readsBeforeDetach = api.artwork.mock.calls.length
  expect(readsBeforeDetach).toBeGreaterThan(5)
  third.unmount()
  await act(async () => {
    await vi.advanceTimersByTimeAsync(60_000)
  })
  expect(api.artwork).toHaveBeenCalledTimes(readsBeforeDetach)
})

it.each(['desktop', 'fullscreen'] as const)(
  '%s keeps its visible pixels across idle cache collection and a failed refresh until recovery completes',
  async (mode) => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] })
    const api = bridge(),
      value = client()
    const view = render(
      <QueryClientProvider client={value}>
        <div className={`avalon-shell ${mode}`}>
          <Artwork workId={42} />
        </div>
      </QueryClientProvider>,
    )
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1)
    })
    const image = view.container.querySelector('img')!
    fireEvent.load(image)
    const original = image.getAttribute('src')
    await act(async () => {
      await vi.advanceTimersByTimeAsync(artworkLifetime + 1)
    })
    expect(image.getAttribute('src')).toBe(original)
    expect(URL.revokeObjectURL).not.toHaveBeenCalledWith(original)
    api.artwork.mockResolvedValueOnce(null)
    await act(async () => {
      await value.invalidateQueries({ queryKey: ['artwork'] })
      await vi.advanceTimersByTimeAsync(1)
    })
    expect(image.getAttribute('src')).toBe(original)
    expect(view.container.querySelector('.artwork')?.getAttribute('data-state')).toBe('ready')
    expect(URL.revokeObjectURL).not.toHaveBeenCalledWith(original)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2_000)
    })
    expect(image.getAttribute('src')).not.toBe(original)
    expect(api.artwork).toHaveBeenCalledTimes(3)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(original)
  },
)
