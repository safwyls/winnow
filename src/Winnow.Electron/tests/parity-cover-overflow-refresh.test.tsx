// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ArtworkAsset } from '../src/renderer/components/ArtworkAsset'
import { artworkImages, closeArtworkImages } from '../src/renderer/components/artwork-images'

vi.mock('../src/renderer/components/artwork-decode', async (importOriginal) => {
  const original = await importOriginal<typeof import('../src/renderer/components/artwork-decode')>()
  return {
    ...original,
    ArtworkDecoder: class extends original.ArtworkDecoder {
      constructor() {
        super({ concurrent: 1, maxQueuedBytes: 0 })
      }
    },
  }
})

const png =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAEklEQVR4nGP4z8DwHx9mGBkKAMLXf4EvceABAAAAAElFTkSuQmCC'
const clients: QueryClient[] = []
const decode = vi.fn<() => Promise<void>>()
beforeEach(() => {
  decode.mockReset().mockResolvedValue(undefined)
  let sequence = 0
  URL.createObjectURL = vi.fn(() => `blob:pressure-${++sequence}`)
  URL.revokeObjectURL = vi.fn()
  vi.stubGlobal(
    'Image',
    class {
      src = ''
      naturalWidth = 8
      naturalHeight = 8
      decode = decode
    },
  )
})
afterEach(async () => {
  cleanup()
  for (const client of clients.splice(0)) {
    await closeArtworkImages(client)
    client.clear()
  }
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it.each(['failed reread', 'confirmed absence'] as const)(
  'a warm screenshot survives queue pressure and distinguishes %s after its backend reread',
  async (outcome) => {
    let targetReads = 0
    const artwork = vi.fn(async (_provider: string, id: string) => {
      if (id === 'aa11' && ++targetReads === 3) {
        if (outcome === 'failed reread') throw new Error('Backend reread unavailable')
        return null
      }
      return png
    })
    Object.defineProperty(window, 'winnow', { configurable: true, value: { artwork } })
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    clients.push(client)
    const cache = artworkImages(client)
    const host = (blocker: boolean) => (
      <QueryClientProvider client={client}>
        <ArtworkAsset asset={{ provider: 'igdb-shot', id: 'aa11' }} width={1280} alt="Warm screenshot" />
        {blocker && (
          <ArtworkAsset
            asset={{ provider: 'igdb-shot', id: 'blocking' }}
            width={1280}
            alt="Blocking screenshot"
          />
        )}
      </QueryClientProvider>
    )
    const view = render(host(false))
    const image = await screen.findByRole('img', { name: 'Warm screenshot' })
    const original = image.getAttribute('src')!
    let release!: () => void
    decode.mockReturnValueOnce(
      new Promise<void>((resolve) => {
        release = resolve
      }),
    )
    view.rerender(host(true))
    try {
      await waitFor(() => expect(decode).toHaveBeenCalledTimes(2))
      await act(async () => {
        await client.invalidateQueries({ queryKey: ['artwork-image', 'igdb-shot', 'aa11', 1280, 'asset'] })
      })
      await waitFor(() => expect(targetReads).toBe(2))
      expect(cache.pendingCount).toBe(2)
      expect(URL.createObjectURL).toHaveBeenCalledTimes(2)
      expect(image.getAttribute('src')).toBe(original)
      expect(URL.revokeObjectURL).not.toHaveBeenCalledWith(original)
      await act(async () => release())
      await waitFor(() => expect(targetReads).toBe(3))
      await waitFor(() => expect(cache.pendingCount).toBe(0))
      expect(decode).toHaveBeenCalledTimes(2)
      expect(URL.createObjectURL).toHaveBeenCalledTimes(2)
      if (outcome === 'failed reread') {
        expect(screen.getByRole('img', { name: 'Warm screenshot' })).toBe(image)
        expect(image.getAttribute('src')).toBe(original)
        expect(URL.revokeObjectURL).not.toHaveBeenCalledWith(original)
        expect(cache.liveSlots).toBe(2)
      } else {
        expect(screen.queryByRole('img', { name: 'Warm screenshot' })).toBeNull()
        expect(screen.getByText('Screenshot unavailable')).toBeDefined()
        expect(image.getAttribute('src')).toBeNull()
        expect(URL.revokeObjectURL).toHaveBeenCalledWith(original)
        expect(cache.liveSlots).toBe(1)
      }
      view.unmount()
      expect(image.getAttribute('src')).toBeNull()
      expect(cache.liveSlots).toBe(0)
      await closeArtworkImages(client)
      expect(URL.revokeObjectURL).toHaveBeenCalledTimes(2)
    } finally {
      release()
    }
  },
)
