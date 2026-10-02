// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { ApiResult } from '../src/shared/bridge'
import { Artwork } from '../src/renderer/components/Artwork'
import { artworkImages, closeArtworkImages } from '../src/renderer/components/artwork-images'

const pixels =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAEklEQVR4nGP4z8DwHx9mGBkKAMLXf4EvceABAAAAAElFTkSuQmCC'
const clients: QueryClient[] = []
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((complete) => {
    resolve = complete
  })
  return { promise, resolve }
}
function selection(provider: string, id: string): ApiResult<unknown> {
  return { ok: true, status: 200, data: { current: { previewKey: { provider, id } }, revision: 'source' } }
}
function client() {
  const value = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  })
  clients.push(value)
  return value
}
function view(value: QueryClient) {
  return (
    <QueryClientProvider client={value}>
      <div style={{ width: 440, height: 190 }}>
        <Artwork workId={1} hero />
      </div>
    </QueryClientProvider>
  )
}
function bridge(request: ReturnType<typeof vi.fn>, artwork: ReturnType<typeof vi.fn>) {
  const cancelRequest = vi.fn(async () => true)
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, artwork, cancelRequest } })
  return cancelRequest
}

beforeEach(() => {
  const NativeBlob = Blob
  const bytes = new WeakMap<Blob, Uint8Array>()
  vi.stubGlobal(
    'Blob',
    class extends NativeBlob {
      constructor(parts: BlobPart[], options?: BlobPropertyBag) {
        super(parts, options)
        bytes.set(this, parts[0] as Uint8Array)
      }
    },
  )
  const decoded = new Map<string, Uint8Array>()
  URL.createObjectURL = vi.fn((blob: Blob) => {
    const url = `blob:source-${decoded.size + 1}`
    decoded.set(url, bytes.get(blob)!)
    return url
  })
  URL.revokeObjectURL = vi.fn()
  // jsdom has no image decoder. Retain the actual eight-pixel PNG and read its IHDR at this boundary.
  vi.stubGlobal(
    'Image',
    class {
      src = ''
      naturalWidth = 0
      naturalHeight = 0
      async decode() {
        const data = decoded.get(this.src)!
        const header = new DataView(data.buffer, data.byteOffset, data.byteLength)
        this.naturalWidth = header.getUint32(16)
        this.naturalHeight = header.getUint32(20)
        expect([this.naturalWidth, this.naturalHeight]).toEqual([8, 8])
      }
    },
  )
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
    x: 0,
    y: 0,
    left: 0,
    top: 0,
    right: 440,
    bottom: 190,
    width: 440,
    height: 190,
    toJSON() {},
  })
})
afterEach(async () => {
  cleanup()
  for (const value of clients.splice(0)) {
    await closeArtworkImages(value)
    value.clear()
  }
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it('released 440 by 190 Steam hero123 ignores late eight-pixel artwork and reacquires on attachment', async () => {
  const old = deferred<string>(),
    current = deferred<string>()
  const request = vi.fn().mockResolvedValue(selection('steam-hero', '123'))
  const artwork = vi.fn().mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
  const cancel = bridge(request, artwork)
  const queries = client(),
    cache = artworkImages(queries)
  const mounted = render(view(queries))
  try {
    await waitFor(() => expect(artwork).toHaveBeenCalledTimes(1))
    expect(request.mock.calls[0][0]).toMatchObject({
      route: 'artworkState',
      params: { workId: 1, slot: 'Hero' },
    })
    const oldRequest = artwork.mock.calls[0][3]
    // Electron chooses the next available decode width while retaining the source's 440x190 target.
    expect(artwork.mock.calls[0]).toEqual(['steam-hero', '123', 480, oldRequest])
    expect(cache.liveSlots).toBe(1)
    mounted.unmount()
    expect(cache.liveSlots).toBe(0)
    expect(cancel).toHaveBeenCalledWith(oldRequest)
    await act(async () => old.resolve(pixels))
    expect(queries.getQueryData(['artwork-image', 'steam-hero', '123', 480, 'source'])).toBeUndefined()
    expect(URL.createObjectURL).not.toHaveBeenCalled()
    expect(document.querySelector('.artwork img')).toBeNull()

    const attached = render(view(queries))
    await waitFor(() => expect(artwork).toHaveBeenCalledTimes(2))
    const newRequest = artwork.mock.calls[1][3]
    expect(newRequest).not.toBe(oldRequest)
    expect(artwork.mock.calls[1]).toEqual(['steam-hero', '123', 480, newRequest])
    await act(async () => current.resolve(pixels))
    await waitFor(() => expect(attached.container.querySelector('img')).not.toBeNull())
    const image = attached.container.querySelector('img')!
    fireEvent.load(image)
    expect(attached.container.querySelector('.artwork')?.getAttribute('data-state')).toBe('ready')
    expect(image.getAttribute('src')).toBe('blob:source-1')
    expect(cache.liveSlots).toBe(1)
    expect(cache.decodedBytes).toBe(8 * 8 * 4)
    attached.unmount()
    expect(image.getAttribute('src')).toBeNull()
    expect(cache.liveSlots).toBe(0)
    // Releasing the consumer leaves the shared scrollback cache owning pixels until it closes.
    await closeArtworkImages(queries)
    expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:source-1')
  } finally {
    old.resolve(pixels)
    current.resolve(pixels)
  }
})

it('detached cancellation-ignoring artwork metadata cannot replace reattached currentart candidates', async () => {
  const old = deferred<ApiResult<unknown>>(),
    current = deferred<ApiResult<unknown>>()
  const request = vi.fn().mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
  const artwork = vi.fn().mockResolvedValue(pixels)
  const cancel = bridge(request, artwork)
  const queries = client()
  const mounted = render(view(queries))
  try {
    await waitFor(() => expect(request).toHaveBeenCalledTimes(1))
    mounted.unmount()
    expect(cancel).toHaveBeenCalledWith(request.mock.calls[0][0].requestId)
    const attached = render(view(queries))
    await waitFor(() => expect(request).toHaveBeenCalledTimes(2))
    expect(request.mock.calls[1][0].requestId).not.toBe(request.mock.calls[0][0].requestId)
    await act(async () => old.resolve(selection('igdb-backdrop', 'staleart')))
    expect(artwork).not.toHaveBeenCalled()
    expect(attached.container.querySelector('img')).toBeNull()
    await act(async () => current.resolve(selection('igdb-backdrop', 'currentart')))
    await waitFor(() => expect(artwork).toHaveBeenCalledTimes(1))
    expect(artwork.mock.calls[0]).toEqual(['igdb-backdrop', 'currentart', 480, expect.any(String)])
    await waitFor(() => expect(attached.container.querySelector('img')).not.toBeNull())
    expect(artworkImages(queries).liveSlots).toBe(1)
    attached.unmount()
    expect(artworkImages(queries).liveSlots).toBe(0)
  } finally {
    old.resolve(selection('igdb-backdrop', 'staleart'))
    current.resolve(selection('igdb-backdrop', 'currentart'))
  }
})
