// @vitest-environment jsdom
import { act, cleanup, render, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonBackdrop } from '../src/renderer/themes/avalon-backdrop'
import { loadBackdropImage } from '../src/renderer/themes/avalon-backdrop-image'
import type { ApiRequest, BackendEvent, WinnowBridge } from '../src/shared/bridge'
import type { BackdropPixels } from '../src/renderer/themes/avalon-backdrop-model'

vi.mock('../src/renderer/themes/avalon-backdrop-image', () => ({ loadBackdropImage: vi.fn() }))
let width: number, height: number, resize: () => void, notify: (event: BackendEvent) => void
const frames = new Map<number, FrameRequestCallback>()
const released = vi.fn()
beforeEach(() => {
  width = 2520
  height = 1080
  frames.clear()
  released.mockClear()
  let id = 0
  vi.stubGlobal('requestAnimationFrame', function (this: unknown, callback: FrameRequestCallback) {
    if (this !== undefined && this !== window) throw new TypeError('Illegal invocation')
    frames.set(++id, callback)
    return id
  })
  vi.stubGlobal('cancelAnimationFrame', function (this: unknown, id: number) {
    if (this !== undefined && this !== window) throw new TypeError('Illegal invocation')
    frames.delete(id)
  })
  vi.stubGlobal('devicePixelRatio', 1)
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener() {}, removeEventListener() {} }))
  vi.stubGlobal(
    'ResizeObserver',
    class {
      constructor(callback: () => void) {
        resize = callback
      }
      observe() {}
      disconnect() {}
    },
  )
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(() => width)
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockImplementation(() => height)
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(() => ({
    x: 0,
    y: 0,
    left: 0,
    top: 0,
    right: width,
    bottom: height,
    width,
    height,
    toJSON() {},
  }))
  window.winnow = {
    request: vi.fn(async (value: ApiRequest) => ({
      ok: true,
      status: 200,
      data: {
        candidates: [
          {
            key: {
              provider: value.params?.workId === 1 ? 'steam-hero' : 'igdb-backdrop',
              id: String(value.params?.workId),
            },
            aspectRatio: value.params?.workId === 1 ? 3840 / 1240 : 16 / 9,
            fitWholeHero: value.params?.workId === 1,
          },
        ],
        coverKey: null,
      },
    })),
    onEvent: vi.fn((callback) => {
      notify = callback
      return released
    }),
    cancelRequest: vi.fn(async () => true),
  } as unknown as WinnowBridge
  vi.mocked(loadBackdropImage).mockImplementation(async (key) => ({
    source: `blob:${key.id}`,
    width: key.id === '1' ? 3840 : 1920,
    height: key.id === '1' ? 1240 : 1080,
    dispose: vi.fn(),
  }))
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  document.documentElement.classList.remove('reduced-motion')
})
function frame(time: number) {
  act(() => {
    const pending = [...frames.values()]
    frames.clear()
    pending.forEach((callback) => callback(time))
  })
}
it.each([false, true])(
  'renders source geometry in independent transition layers with cinematic %s',
  async (cinematic) => {
    const view = render(<AvalonBackdrop workId={1} cinematic={cinematic} />)
    await waitFor(() => expect(view.container.querySelector('img')?.getAttribute('src')).toBe('blob:1'))
    expect(loadBackdropImage).toHaveBeenLastCalledWith(
      { provider: 'steam-hero', id: '1' },
      2560,
      expect.any(AbortSignal),
    )
    const hero = view.container.querySelector<HTMLElement>('.avalon-backdrop-art')!
    expect(hero.style.height).toBe('813.75px')
    expect(hero.dataset.fitted).toBe('true')
    view.rerender(<AvalonBackdrop workId={2} cinematic={cinematic} />)
    await waitFor(() => expect(view.container.querySelectorAll('img')).toHaveLength(2))
    frame(1000)
    frame(1090)
    const outgoing = view.container.querySelector<HTMLElement>('[data-outgoing]')!
    expect(outgoing.querySelector<HTMLElement>('.avalon-backdrop-art')!.style.height).toBe('813.75px')
    const incoming = view.container.querySelector<HTMLElement>('.avalon-backdrop-layer:not([data-outgoing])')!
    expect(incoming.style.opacity).toBe('0.5')
    expect(incoming.querySelector<HTMLElement>('.avalon-backdrop-art')!.style.height).toBe('1080px')
    expect(view.container.querySelector('.avalon-backdrop')?.getAttribute('aria-hidden')).toBe('true')
    frame(1180)
    expect(view.container.querySelectorAll('img')).toHaveLength(1)
  },
)
it('preserves current art during metadata and image loads and releases both on detach', async () => {
  const view = render(<AvalonBackdrop workId={1} />)
  await waitFor(() => expect(view.container.querySelector('img')).not.toBeNull())
  const first = await vi.mocked(loadBackdropImage).mock.results[0].value
  let finish!: (pixels: BackdropPixels | null) => void
  vi.mocked(loadBackdropImage).mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }),
  )
  view.rerender(<AvalonBackdrop workId={2} />)
  await waitFor(() => expect(finish).toBeDefined())
  const signal = vi.mocked(loadBackdropImage).mock.calls.at(-1)![2]
  expect(view.container.querySelector('img')?.getAttribute('src')).toBe('blob:1')
  view.unmount()
  expect(signal.aborted).toBe(true)
  expect(first?.dispose).toHaveBeenCalledOnce()
  const late = { source: 'late', width: 10, height: 10, dispose: vi.fn() }
  await act(async () => finish(late))
  expect(late.dispose).toHaveBeenCalledOnce()
  expect(released).toHaveBeenCalledOnce()
})
it.each([false, true])(
  'uses source crop in desktop and respects the saved motion preference in fullscreen %s',
  async (fullscreen) => {
    document.documentElement.classList.add('reduced-motion')
    const view = render(<AvalonBackdrop workId={1} fullscreen={fullscreen} />)
    await waitFor(() => expect(view.container.querySelector('img')).not.toBeNull())
    expect(view.container.querySelector<HTMLElement>('.avalon-backdrop-art')!.style.height).toBe(
      fullscreen ? '813.75px' : '1080px',
    )
    view.rerender(<AvalonBackdrop workId={2} fullscreen={fullscreen} />)
    await waitFor(() => expect(view.container.querySelector('img')?.getAttribute('src')).toBe('blob:2'))
    expect(view.container.querySelector('[data-outgoing]')).toBeNull()
    expect(frames.size).toBe(0)
  },
)
it('reacts to artwork/source changes and resizes while preserving bounded decode buckets', async () => {
  const view = render(<AvalonBackdrop workId={1} />)
  await waitFor(() => expect(view.container.querySelector('img')).not.toBeNull())
  const calls = vi.mocked(window.winnow.request).mock.calls.length
  act(() => notify({ kind: 'preferences.changed', resource: 'ArtworkSourceOrder', epoch: 'a', sequence: 1 }))
  await waitFor(() => expect(vi.mocked(window.winnow.request).mock.calls.length).toBe(calls + 1))
  act(() => {
    width = 3840
    resize()
  })
  await waitFor(() =>
    expect(loadBackdropImage).toHaveBeenLastCalledWith(
      { provider: 'steam-hero', id: '1' },
      3840,
      expect.any(AbortSignal),
    ),
  )
  const art = view.container.querySelector<HTMLElement>('.avalon-backdrop-art')!
  expect(parseFloat(art.style.width)).toBeCloseTo(3344.516129)
  expect(parseFloat(art.style.left)).toBeCloseTo(247.741935)
})
it('measures on attachment and window resize when ResizeObserver is unavailable', async () => {
  vi.stubGlobal('ResizeObserver', undefined)
  const view = render(<AvalonBackdrop workId={1} />)
  await waitFor(() => expect(view.container.querySelector('img')).not.toBeNull())
  act(() => {
    width = 1920
    window.dispatchEvent(new Event('resize'))
  })
  await waitFor(() =>
    expect(view.container.querySelector<HTMLElement>('.avalon-backdrop-art')!.style.height).toBe('1080px'),
  )
  expect(view.container.querySelector('[data-fitted]')).toBeNull()
})

it.each([false, true])(
  'keeps canonical hero metadata while a saved header changes the cover fallback, fullscreen %s',
  async (fullscreen) => {
    vi.mocked(window.winnow.request).mockImplementation(
      async (input: ApiRequest) =>
        ({
          ok: true,
          status: 200,
          data:
            input.route === 'artwork.get'
              ? { current: { previewKey: { provider: 'igdb', id: `cover-${input.params?.workId}` } } }
              : {
                  candidates: [
                    {
                      key: { provider: 'igdb-backdrop', id: 'canonical-hero' },
                      aspectRatio: 16 / 9,
                      fitWholeHero: false,
                    },
                  ],
                  coverKey: { provider: 'steam', id: 'canonical-cover' },
                },
        }) as never,
    )
    let heroAvailable = true
    vi.mocked(loadBackdropImage).mockImplementation(async (key) =>
      key.id === 'canonical-hero' && !heroAvailable
        ? null
        : {
            source: `blob:${key.id}`,
            width: 1920,
            height: 1080,
            dispose: vi.fn(),
          },
    )
    const view = render(<AvalonBackdrop workId={1} coverWorkId={2} fullscreen={fullscreen} />)
    await waitFor(() =>
      expect(view.container.querySelector('img')?.getAttribute('src')).toBe('blob:canonical-hero'),
    )
    expect(
      vi
        .mocked(window.winnow.request)
        .mock.calls.filter(([input]) => input.route === 'artwork.backdrop')
        .every(([input]) => input.params?.workId === 1),
    ).toBe(true)

    heroAvailable = false
    // Reopening with an unavailable hero uses the preferred cover.
    view.unmount()
    const fallback = render(<AvalonBackdrop workId={1} coverWorkId={3} fullscreen={fullscreen} />)
    await waitFor(() =>
      expect(fallback.container.querySelector('img')?.getAttribute('src')).toBe('blob:cover-3'),
    )
    expect(fallback.container.querySelector('[data-fallback]')).not.toBeNull()
    fallback.rerender(<AvalonBackdrop workId={1} coverWorkId={2} fullscreen={fullscreen} />)
    await waitFor(() =>
      expect(fallback.container.querySelector('img')?.getAttribute('src')).toBe('blob:cover-2'),
    )
    expect(vi.mocked(loadBackdropImage).mock.calls.some(([key]) => key.id === 'canonical-cover')).toBe(false)
  },
)
