// @vitest-environment jsdom
import { act, cleanup, render, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonBackdrop } from '../src/renderer/themes/avalon-backdrop'
import { artworkImages, closeArtworkImages } from '../src/renderer/components/artwork-images'
import type { BackendEvent, WinnowBridge } from '../src/shared/bridge'

let notify: (event: BackendEvent) => void
let finish: (value: Uint8Array) => void
const bytes = new Uint8Array([1, 2, 3])
beforeEach(() => {
  vi.stubGlobal('devicePixelRatio', 1)
  vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener() {}, removeEventListener() {} }))
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(1280)
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(720)
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
    width: 1280,
    height: 720,
  } as DOMRect)
  vi.stubGlobal(
    'Image',
    class {
      src = ''
      naturalWidth = 1280
      naturalHeight = 720
      async decode() {}
    },
  )
  let image = 0
  URL.createObjectURL = vi.fn(() => `blob:backdrop-${++image}`)
  URL.revokeObjectURL = vi.fn()
  window.winnow = {
    request: vi.fn(async () => ({
      ok: true,
      status: 200,
      data: {
        candidates: [
          { key: { provider: 'igdb-backdrop', id: 'shot' }, aspectRatio: 16 / 9, fitWholeHero: false },
        ],
        coverKey: null,
      },
    })),
    artworkBytes: vi.fn(async () => bytes),
    cancelRequest: vi.fn(async () => true),
    onEvent: (callback: (event: BackendEvent) => void) => {
      notify = callback
      return () => {}
    },
  } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

for (const fullscreen of [false, true])
  it.each(['pending', 'ready'] as const)(
    `revalidates a %s shared backdrop after delayed cache invalidation, fullscreen ${fullscreen}`,
    async (state) => {
      const client = new QueryClient()
      if (state === 'pending')
        vi.mocked(window.winnow.artworkBytes!).mockImplementationOnce(
          () =>
            new Promise((resolve) => {
              finish = resolve
            }),
        )
      const view = render(
        <QueryClientProvider client={client}>
          <AvalonBackdrop workId={1} fullscreen={fullscreen} reducedMotion />
        </QueryClientProvider>,
      )
      try {
        await waitFor(() => expect(window.winnow.artworkBytes).toHaveBeenCalledOnce())
        if (state === 'ready') {
          await waitFor(() =>
            expect(view.container.querySelector('img')?.getAttribute('src')).toBe('blob:backdrop-1'),
          )
          vi.mocked(window.winnow.artworkBytes!).mockImplementationOnce(
            () =>
              new Promise((resolve) => {
                finish = resolve
              }),
          )
        }
        await act(async () => {
          await client.invalidateQueries({ queryKey: ['artwork-image'], refetchType: 'none' })
        })
        if (state === 'ready') {
          await waitFor(() => expect(window.winnow.artworkBytes).toHaveBeenCalledTimes(2))
          expect(view.container.querySelector('img')?.getAttribute('src')).toBe('blob:backdrop-1')
          expect(URL.revokeObjectURL).not.toHaveBeenCalled()
        }
        await act(async () => finish(bytes))
        await waitFor(() =>
          expect(view.container.querySelector('img')?.getAttribute('src')).toBe(
            `blob:backdrop-${state === 'ready' ? 2 : 1}`,
          ),
        )
        expect(window.winnow.artworkBytes).toHaveBeenCalledTimes(2)
        expect(view.container.querySelector('.avalon-backdrop')?.getAttribute('data-state')).toBe('ready')
        view.unmount()
        expect(artworkImages(client).liveSlots).toBe(0)
        await client.invalidateQueries({ queryKey: ['artwork-image'], refetchType: 'none' })
        expect(window.winnow.artworkBytes).toHaveBeenCalledTimes(2)
      } finally {
        view.unmount()
        await closeArtworkImages(client)
        client.clear()
      }
    },
  )

it('coalesces an artwork event and its query invalidations, then cancels a queued refresh on detach', async () => {
  const client = new QueryClient()
  const view = render(
    <QueryClientProvider client={client}>
      <AvalonBackdrop workId={1} fullscreen={false} />
    </QueryClientProvider>,
  )
  try {
    await waitFor(() => expect(view.container.querySelector('img')).not.toBeNull())
    for (let index = 0; index < 20; index++)
      client.setQueryData(['artwork-image', 'igdb-backdrop', `other-${index}`, 1280, 'asset'], {
        source: bytes,
      })
    await act(async () =>
      notify({ kind: 'library.changed', resource: 'works/1/artwork/Hero', epoch: 'a', sequence: 1 }),
    )
    await waitFor(() =>
      expect(view.container.querySelector('img')?.getAttribute('src')).toBe('blob:backdrop-2'),
    )
    expect(window.winnow.request).toHaveBeenCalledTimes(2)
    expect(window.winnow.artworkBytes).toHaveBeenCalledTimes(2)
    await act(async () => {
      void client.invalidateQueries({ queryKey: ['artwork-image'], refetchType: 'none' })
      view.unmount()
    })
    expect(window.winnow.request).toHaveBeenCalledTimes(2)
  } finally {
    view.unmount()
    await closeArtworkImages(client)
    client.clear()
  }
})
