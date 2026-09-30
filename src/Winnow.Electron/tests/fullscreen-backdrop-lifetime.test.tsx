// @vitest-environment jsdom
import { act, cleanup, render, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { Artwork } from '../src/renderer/components/Artwork'
import { artworkImages, closeArtworkImages } from '../src/renderer/components/artwork-images'
import { AvalonBackdrop } from '../src/renderer/themes/avalon-backdrop'
import { loadBackdropImage } from '../src/renderer/themes/avalon-backdrop-image'
import type { BackdropPixels } from '../src/renderer/themes/avalon-backdrop-model'
import type { WinnowBridge } from '../src/shared/bridge'

vi.mock('../src/renderer/themes/avalon-backdrop-image', () => ({ loadBackdropImage: vi.fn() }))
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it('desktop ranked backdrop and portrait keep independent ownership through failed candidates, resize and disposal', async () => {
  let width = 1920,
    resize = () => {},
    completePortrait!: (source: string) => void
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
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
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(1080)
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    const value = this.classList.contains('artwork') ? 200 : width
    return {
      width: value,
      height: 1080,
      x: 0,
      y: 0,
      top: 0,
      left: 0,
      right: value,
      bottom: 1080,
      toJSON() {},
    }
  })
  const candidate = (id: string) => ({
    key: { provider: id === 'override' ? 'user' : 'igdb-backdrop', id },
    aspectRatio: 16 / 9,
    fitWholeHero: false,
  })
  const cancelRequest = vi.fn(async () => true),
    artwork = vi.fn(
      (_provider: string, _id: string, _width?: number, _requestId?: string) =>
        new Promise<string>((resolve) => {
          completePortrait = resolve
        }),
    )
  window.winnow = {
    request: vi.fn(async (request) => ({
      ok: true,
      status: 200,
      data:
        request.route === 'artworkState'
          ? { revision: 'fixture', current: { previewKey: { provider: 'igdb', id: 'portrait' } } }
          : { candidates: [candidate('override'), candidate('art'), candidate('shot')], coverKey: null },
    })),
    artwork,
    cancelRequest,
    onEvent: () => () => {},
  } as unknown as WinnowBridge
  const pending: { signal: AbortSignal; finish: (value: BackdropPixels | null) => void }[] = []
  vi.mocked(loadBackdropImage).mockImplementation(
    (_key, _width, signal) => new Promise((finish) => pending.push({ signal, finish })),
  )
  const view = render(
    <QueryClientProvider client={client}>
      <Artwork workId={1} />
      <AvalonBackdrop workId={1} fullscreen={false} />
    </QueryClientProvider>,
  )
  await waitFor(() => expect(artwork).toHaveBeenCalledOnce())
  await waitFor(() => expect(pending).toHaveLength(1))
  expect(vi.mocked(loadBackdropImage).mock.calls[0].slice(0, 2)).toEqual([
    { provider: 'user', id: 'override' },
    1920,
  ])
  await act(async () => pending[0].finish(null))
  expect(vi.mocked(loadBackdropImage).mock.calls[1].slice(0, 2)).toEqual([
    { provider: 'igdb-backdrop', id: 'art' },
    1920,
  ])
  await act(async () => pending[1].finish(null))
  expect(vi.mocked(loadBackdropImage).mock.calls[2].slice(0, 2)).toEqual([
    { provider: 'igdb-backdrop', id: 'shot' },
    1920,
  ])
  const shot = { source: 'blob:shot', width: 1920, height: 1080, dispose: vi.fn() }
  await act(async () => pending[2].finish(shot))
  expect(view.container.querySelector('.avalon-backdrop img')?.getAttribute('src')).toBe('blob:shot')
  expect(view.container.querySelector('.artwork img')).toBeNull()
  expect(cancelRequest).not.toHaveBeenCalled()
  act(() => {
    width = 3840
    resize()
  })
  await waitFor(() => expect(pending).toHaveLength(4))
  expect(vi.mocked(loadBackdropImage).mock.calls[3][1]).toBe(3840)
  view.unmount()
  expect(pending[3].signal.aborted).toBe(true)
  expect(shot.dispose).toHaveBeenCalledOnce()
  expect(cancelRequest).toHaveBeenCalledWith(artwork.mock.calls[0][3])
  const late = { source: 'blob:late', width: 3840, height: 2160, dispose: vi.fn() }
  await act(async () => {
    pending[3].finish(late)
    completePortrait('data:image/png;base64,bGF0ZQ==')
  })
  expect(late.dispose).toHaveBeenCalledOnce()
  expect(view.container.querySelector('img')).toBeNull()
  await waitFor(() => expect(artworkImages(client).pendingCount).toBe(0))
  expect(
    client
      .getQueryCache()
      .findAll({ queryKey: ['artwork-image'] })
      .every((query) => !query.state.data),
  ).toBe(true)
  await closeArtworkImages(client)
  client.clear()
})
