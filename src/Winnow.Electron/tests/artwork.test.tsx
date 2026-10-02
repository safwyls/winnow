// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Artwork } from '../src/renderer/components/Artwork'
import { refreshSnapshots } from '../src/renderer/refresh'
import type { ApiRequest } from '../src/shared/bridge'
import { artworkImages, closeArtworkImages } from '../src/renderer/components/artwork-images'

const clients: QueryClient[] = []
beforeEach(() => {
  const BlobType = Blob
  const urls = new WeakMap<Blob, string>()
  vi.stubGlobal(
    'Blob',
    class extends BlobType {
      constructor(parts: BlobPart[], options?: BlobPropertyBag) {
        super(parts, options)
        urls.set(this, `blob:${btoa(String.fromCharCode(...(parts[0] as Uint8Array)))}`)
      }
    },
  )
  URL.createObjectURL = vi.fn((blob: Blob) => urls.get(blob)!)
  URL.revokeObjectURL = vi.fn()
  vi.stubGlobal(
    'Image',
    class {
      src = ''
      naturalWidth = 640
      naturalHeight = 960
      decode = vi.fn(async () => undefined)
    },
  )
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    return {
      x: 0,
      y: 0,
      width: this.classList.contains('hero-fixture') ? 1920 : 600,
      height: 900,
      top: 0,
      bottom: 900,
      left: 0,
      right: 600,
      toJSON() {},
    }
  })
})

afterEach(async () => {
  cleanup()
  for (const client of clients.splice(0)) {
    await closeArtworkImages(client)
    client.clear()
  }
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

const sourceA = 'data:image/png;base64,YXJ0d29yay1h'
const sourceB = 'data:image/png;base64,YXJ0d29yay1i'
const currentArt = {
  ok: true,
  status: 200,
  data: { current: { previewKey: { provider: 'steam', id: '123' } }, revision: 'selection-a' },
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((complete) => {
    resolve = complete
  })
  return { promise, resolve }
}
function installBridge(request: ReturnType<typeof vi.fn>, artwork = vi.fn().mockResolvedValue(sourceA)) {
  const cancelRequest = vi.fn(async () => true)
  Object.defineProperty(window, 'winnow', { value: { request, artwork, cancelRequest }, configurable: true })
  return { request, artwork, cancelRequest }
}
function queryClient() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  })
  clients.push(client)
  return client
}
function view(client: QueryClient, workId = 1) {
  return (
    <QueryClientProvider client={client}>
      <Artwork workId={workId} />
    </QueryClientProvider>
  )
}

describe('Artwork loading and fallback states', () => {
  it('shows loading until the API confirms artwork is missing', async () => {
    const state = deferred<unknown>()
    const bridge = installBridge(vi.fn().mockReturnValue(state.promise))
    const { container } = render(view(queryClient()))
    expect(container.querySelector('.artwork')?.getAttribute('data-loading')).toBe('true')
    expect(container.querySelector('.art-loading')).not.toBeNull()
    expect(container.textContent).not.toContain('Artwork unavailable')
    await act(async () => state.resolve({ ok: true, status: 200, data: { current: null } }))
    await waitFor(() => expect(container.textContent).toContain('Artwork unavailable'))
    expect(container.querySelector('.artwork')?.hasAttribute('data-loading')).toBe(false)
    expect(container.querySelector('.art-loading')).toBeNull()
    expect(bridge.artwork).not.toHaveBeenCalled()
  })

  it('keeps loading while fetching bytes and until the image load event', async () => {
    const bytes = deferred<string>()
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi.fn().mockReturnValue(bytes.promise),
    )
    const { container } = render(view(queryClient()))
    await waitFor(() =>
      expect(bridge.artwork).toHaveBeenCalledWith(
        'steam',
        '123',
        640,
        expect.stringMatching(/^[a-f0-9]{32}$/),
      ),
    )
    expect(container.querySelector('.art-loading')).not.toBeNull()
    expect(container.querySelector('img')).toBeNull()
    await act(async () => bytes.resolve(sourceA))
    await waitFor(() =>
      expect(container.querySelector('img')?.getAttribute('src')).toBe(
        sourceA.replace('data:image/png;base64,', 'blob:'),
      ),
    )
    const image = container.querySelector('img')!
    expect(image.classList.contains('art-ready')).toBe(false)
    expect(container.querySelector('.art-loading')).not.toBeNull()
    fireEvent.load(image)
    expect(image.classList.contains('art-ready')).toBe(true)
    expect(container.querySelector('.art-loading')).toBeNull()
    expect(container.textContent).not.toContain('Artwork unavailable')
  })

  it('ends loading with a fallback when the image fails to decode', async () => {
    installBridge(vi.fn().mockResolvedValue(currentArt))
    const { container } = render(view(queryClient()))
    await waitFor(() => expect(container.querySelector('img')).not.toBeNull())
    fireEvent.error(container.querySelector('img')!)
    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('.art-loading')).toBeNull()
    expect(container.textContent).toContain('Artwork unavailable')
    expect(container.querySelector('.artwork')?.hasAttribute('data-loading')).toBe(false)
  })

  it('removes the previous game image immediately when workId changes', async () => {
    const second = deferred<unknown>()
    installBridge(
      vi
        .fn()
        .mockImplementation((input: ApiRequest) =>
          input.params?.workId === 1 ? Promise.resolve(currentArt) : second.promise,
        ),
      vi.fn().mockResolvedValueOnce(sourceA).mockResolvedValueOnce(sourceB),
    )
    const client = queryClient()
    const { container, rerender } = render(view(client, 1))
    await waitFor(() => expect(container.querySelector('img')).not.toBeNull())
    fireEvent.load(container.querySelector('img')!)
    rerender(view(client, 2))
    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('.art-loading')).not.toBeNull()
    await act(async () =>
      second.resolve({
        ...currentArt,
        data: { current: { previewKey: { provider: 'steam', id: '456' } }, revision: 'selection-b' },
      }),
    )
    await waitFor(() =>
      expect(container.querySelector('img')?.getAttribute('src')).toBe(
        sourceB.replace('data:image/png;base64,', 'blob:'),
      ),
    )
    expect(container.querySelector('.art-loading')).not.toBeNull()
    fireEvent.load(container.querySelector('img')!)
    expect(container.querySelector('.art-loading')).toBeNull()
    expect(container.querySelector('img')?.getAttribute('src')).toBe(
      sourceB.replace('data:image/png;base64,', 'blob:'),
    )
  })

  it('reuses valid cached bytes on remount but waits for the new image element to load', async () => {
    const bridge = installBridge(vi.fn().mockResolvedValue(currentArt))
    const client = queryClient()
    const first = render(view(client))
    await waitFor(() => expect(first.container.querySelector('img')).not.toBeNull())
    fireEvent.load(first.container.querySelector('img')!)
    first.unmount()
    const second = render(view(client))
    expect(second.container.querySelector('img')?.getAttribute('src')).toBe(
      sourceA.replace('data:image/png;base64,', 'blob:'),
    )
    expect(second.container.querySelector('.art-loading')).not.toBeNull()
    fireEvent.load(second.container.querySelector('img')!)
    expect(second.container.querySelector('.art-loading')).toBeNull()
    expect(second.container.querySelector('img')?.classList.contains('art-ready')).toBe(true)
    expect(bridge.request).toHaveBeenCalledTimes(1)
    expect(bridge.artwork).toHaveBeenCalledTimes(1)
  })

  it.each(['error response', 'request rejection', 'artwork rejection'] as const)(
    'settles on a fallback after an API %s',
    async (failure) => {
      const request =
        failure === 'request rejection'
          ? vi.fn().mockRejectedValue(new Error('Connection lost'))
          : vi
              .fn()
              .mockResolvedValue(
                failure === 'error response'
                  ? { ok: false, status: 503, message: 'Unavailable' }
                  : currentArt,
              )
      const artwork =
        failure === 'artwork rejection'
          ? vi.fn().mockRejectedValue(new Error('Image request interrupted'))
          : vi.fn()
      installBridge(request, artwork)
      const { container } = render(view(queryClient()))
      await waitFor(() => expect(container.textContent).toContain('Artwork unavailable'))
      expect(container.querySelector('.art-loading')).toBeNull()
      expect(container.querySelector('.artwork')?.hasAttribute('data-loading')).toBe(false)
      expect(container.querySelector('img')).toBeNull()
      expect(request).toHaveBeenCalledTimes(1)
    },
  )
})

describe('Artwork image reuse', () => {
  it('shares one in-flight image read across works with the same selection and size', async () => {
    const bytes = deferred<string>()
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi.fn().mockReturnValue(bytes.promise),
    )
    const client = queryClient()
    const { container } = render(
      <QueryClientProvider client={client}>
        <Artwork workId={1} />
        <Artwork workId={2} />
      </QueryClientProvider>,
    )
    await waitFor(() => expect(bridge.request).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(bridge.artwork).toHaveBeenCalledTimes(1))
    await act(async () => bytes.resolve(sourceA))
    await waitFor(() => expect(container.querySelectorAll('img')).toHaveLength(2))
    expect(bridge.artwork).toHaveBeenCalledTimes(1)
  })

  it('keeps the decoded image element through unrelated snapshot refreshes', async () => {
    const bridge = installBridge(vi.fn().mockResolvedValue(currentArt))
    const client = queryClient()
    const { container } = render(view(client))
    await waitFor(() => expect(container.querySelector('img')).not.toBeNull())
    const image = container.querySelector('img')!
    fireEvent.load(image)
    await act(async () => refreshSnapshots(client))
    expect(bridge.request).toHaveBeenCalledTimes(2)
    expect(bridge.artwork).toHaveBeenCalledTimes(1)
    expect(container.querySelector('img')).toBe(image)
    expect(image.classList.contains('art-ready')).toBe(true)
    expect(container.querySelector('.art-loading')).toBeNull()
  })

  it('loads fresh bytes for a changed selection revision with the same provider and id', async () => {
    const request = vi.fn().mockResolvedValue(currentArt)
    const bridge = installBridge(
      request,
      vi.fn().mockResolvedValueOnce(sourceA).mockResolvedValueOnce(sourceB),
    )
    const client = queryClient()
    const { container } = render(view(client))
    await waitFor(() => expect(container.querySelector('img')).not.toBeNull())
    fireEvent.load(container.querySelector('img')!)
    request.mockResolvedValue({ ...currentArt, data: { ...currentArt.data, revision: 'selection-b' } })
    await act(async () => refreshSnapshots(client))
    await waitFor(() =>
      expect(container.querySelector('img')?.getAttribute('src')).toBe(
        sourceB.replace('data:image/png;base64,', 'blob:'),
      ),
    )
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
    expect(bridge.artwork).toHaveBeenNthCalledWith(
      2,
      'steam',
      '123',
      640,
      expect.stringMatching(/^[a-f0-9]{32}$/),
    )
    expect(container.querySelector('.art-loading')).not.toBeNull()
    fireEvent.load(container.querySelector('img')!)
    expect(container.querySelector('.art-loading')).toBeNull()
  })

  it('revalidates unchanged keys on explicit artwork changes or resync', async () => {
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi.fn().mockResolvedValueOnce(sourceA).mockResolvedValueOnce(sourceB),
    )
    const client = queryClient()
    const { container } = render(view(client))
    await waitFor(() => expect(container.querySelector('img')).not.toBeNull())
    await act(async () => refreshSnapshots(client, { artwork: true }))
    await waitFor(() =>
      expect(container.querySelector('img')?.getAttribute('src')).toBe(
        sourceB.replace('data:image/png;base64,', 'blob:'),
      ),
    )
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
  })

  it('reads again when a forced refresh races an older in-flight image', async () => {
    const oldBytes = deferred<string>()
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi.fn().mockReturnValueOnce(oldBytes.promise).mockResolvedValueOnce(sourceB),
    )
    const client = queryClient()
    const { container } = render(view(client))
    await waitFor(() => expect(bridge.artwork).toHaveBeenCalledTimes(1))
    await act(async () => {
      const refresh = refreshSnapshots(client, { artwork: true })
      oldBytes.resolve(sourceA)
      await refresh
    })
    await waitFor(() =>
      expect(container.querySelector('img')?.getAttribute('src')).toBe(
        sourceB.replace('data:image/png;base64,', 'blob:'),
      ),
    )
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
  })

  it('keeps hero and cover size variants separate even with the same source key', async () => {
    const bridge = installBridge(vi.fn().mockResolvedValue(currentArt))
    const client = queryClient()
    const { container } = render(
      <QueryClientProvider client={client}>
        <Artwork workId={1} />
        <Artwork workId={1} hero className="hero-fixture" />
      </QueryClientProvider>,
    )
    await waitFor(() => expect(container.querySelectorAll('img')).toHaveLength(2))
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
    expect(bridge.artwork).toHaveBeenCalledWith('steam', '123', 640, expect.stringMatching(/^[a-f0-9]{32}$/))
    expect(bridge.artwork).toHaveBeenCalledWith('steam', '123', 1920, expect.stringMatching(/^[a-f0-9]{32}$/))
  })

  it('bounds unchanged byte reuse to two minutes when state refreshes', async () => {
    const now = Date.now()
    const clock = vi.spyOn(Date, 'now').mockReturnValue(now)
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi.fn().mockResolvedValueOnce(sourceA).mockResolvedValueOnce(sourceB),
    )
    const client = queryClient()
    const { container } = render(view(client))
    await waitFor(() => expect(container.querySelector('img')).not.toBeNull())
    clock.mockReturnValue(now + 120_001)
    await act(async () => refreshSnapshots(client))
    await waitFor(() =>
      expect(container.querySelector('img')?.getAttribute('src')).toBe(
        sourceB.replace('data:image/png;base64,', 'blob:'),
      ),
    )
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
  })

  it.each(['null', 'rejection'] as const)(
    'retries %s image results on the next state refresh',
    async (failure) => {
      const artwork = vi.fn().mockResolvedValue(sourceA)
      if (failure === 'null') artwork.mockResolvedValueOnce(null)
      else artwork.mockRejectedValueOnce(new Error('Interrupted'))
      const bridge = installBridge(vi.fn().mockResolvedValue(currentArt), artwork)
      const client = queryClient()
      const { container } = render(view(client))
      await waitFor(() => expect(container.textContent).toContain('Artwork unavailable'))
      await act(async () => refreshSnapshots(client))
      await waitFor(() =>
        expect(container.querySelector('img')?.getAttribute('src')).toBe(
          sourceA.replace('data:image/png;base64,', 'blob:'),
        ),
      )
      expect(bridge.artwork).toHaveBeenCalledTimes(2)
      fireEvent.load(container.querySelector('img')!)
      expect(container.querySelector('.artwork')?.getAttribute('data-state')).toBe('ready')
    },
  )

  it('retries a decode failure even when the source string and selection stay the same', async () => {
    const bridge = installBridge(vi.fn().mockResolvedValue(currentArt))
    const client = queryClient()
    const { container } = render(view(client))
    await waitFor(() => expect(container.querySelector('img')).not.toBeNull())
    fireEvent.error(container.querySelector('img')!)
    expect(container.querySelector('img')).toBeNull()
    await act(async () => refreshSnapshots(client))
    await waitFor(() =>
      expect(container.querySelector('img')?.getAttribute('src')).toBe(
        sourceA.replace('data:image/png;base64,', 'blob:'),
      ),
    )
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
    expect(container.querySelector('.art-loading')).not.toBeNull()
    fireEvent.load(container.querySelector('img')!)
    expect(container.querySelector('.artwork')?.getAttribute('data-state')).toBe('ready')
  })

  it('removes cached artwork when live selection state becomes missing', async () => {
    const request = vi.fn().mockResolvedValue(currentArt)
    const bridge = installBridge(request)
    const client = queryClient()
    const { container } = render(view(client))
    await waitFor(() => expect(container.querySelector('img')).not.toBeNull())
    fireEvent.load(container.querySelector('img')!)
    request.mockResolvedValue({ ok: true, status: 200, data: { current: null, revision: 'missing' } })
    await act(async () => refreshSnapshots(client))
    await waitFor(() => expect(container.querySelector('img')).toBeNull())
    expect(container.querySelector('.artwork')?.getAttribute('data-state')).toBe('missing')
    expect(bridge.artwork).toHaveBeenCalledTimes(1)
  })

  it('releases inactive state and encoded image entries after five minutes', async () => {
    installBridge(vi.fn().mockResolvedValue(currentArt))
    vi.useFakeTimers()
    const client = queryClient()
    const result = render(view(client))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10)
    })
    expect(result.container.querySelector('img')).not.toBeNull()
    const cache = artworkImages(client)
    result.unmount()
    expect(cache.liveSlots).toBe(0)
    expect(URL.revokeObjectURL).not.toHaveBeenCalled()
    expect(client.getQueryCache().getAll()).toHaveLength(2)
    await vi.advanceTimersByTimeAsync(300_001)
    expect(client.getQueryCache().getAll()).toHaveLength(0)
    expect(cache.decodedCount).toBe(0)
    expect(URL.revokeObjectURL).toHaveBeenCalledTimes(1)
    client.clear()
  })
})

describe('Artwork visible ownership', () => {
  it('cancels shared bytes only after the final mounted consumer leaves and refuses a late response', async () => {
    const bytes = deferred<string>()
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi.fn(() => bytes.promise),
    )
    const client = queryClient()
    const covers = (ids: number[]) => (
      <QueryClientProvider client={client}>
        {ids.map((id) => (
          <Artwork key={id} workId={id} />
        ))}
      </QueryClientProvider>
    )
    const result = render(covers([1, 2]))
    try {
      await waitFor(() => expect(bridge.artwork).toHaveBeenCalledTimes(1))
      const requestId = bridge.artwork.mock.calls[0]![3]
      result.rerender(covers([2]))
      expect(bridge.cancelRequest).not.toHaveBeenCalledWith(requestId)
      result.unmount()
      expect(bridge.cancelRequest).toHaveBeenCalledWith(requestId)
      bytes.resolve(sourceA)
      await waitFor(() => expect(artworkImages(client).pendingCount).toBe(0))
      expect(artworkImages(client).liveSlots).toBe(0)
      expect(artworkImages(client).decodedCount).toBe(0)
      expect(URL.createObjectURL).not.toHaveBeenCalled()
      expect(client.getQueryData(['artwork-image', 'steam', '123', 640, 'selection-a'])).toBeUndefined()
    } finally {
      bytes.resolve(sourceA)
    }
  })

  it('reattaching waits for the retiring request before retrying the same image', async () => {
    const bytes = deferred<string>()
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi.fn().mockReturnValueOnce(bytes.promise).mockResolvedValue(sourceB),
    )
    const client = queryClient()
    const first = render(view(client))
    try {
      await waitFor(() => expect(bridge.artwork).toHaveBeenCalledTimes(1))
      first.unmount()
      const second = render(view(client))
      expect(bridge.artwork).toHaveBeenCalledTimes(1)
      await act(async () => bytes.resolve(sourceA))
      await waitFor(() =>
        expect(second.container.querySelector('img')?.getAttribute('src')).toBe(
          sourceB.replace('data:image/png;base64,', 'blob:'),
        ),
      )
      expect(bridge.artwork).toHaveBeenCalledTimes(2)
      expect(URL.createObjectURL).toHaveBeenCalledTimes(1)
      second.unmount()
      expect(artworkImages(client).liveSlots).toBe(0)
    } finally {
      bytes.resolve(sourceA)
    }
  })

  it('clears detached DOM sources, retains warm pixels and reuses the node through dormancy changes', async () => {
    const bridge = installBridge(vi.fn().mockResolvedValue(currentArt))
    const client = queryClient()
    const cover = (dormant: boolean) => (
      <QueryClientProvider client={client}>
        <Artwork workId={1} className={dormant ? 'dormant' : 'vivid'} />
      </QueryClientProvider>
    )
    const first = render(cover(false))
    await waitFor(() => expect(first.container.querySelector('img')).not.toBeNull())
    const image = first.container.querySelector('img')!
    fireEvent.load(image)
    first.rerender(cover(true))
    first.rerender(cover(false))
    expect(first.container.querySelector('img')).toBe(image)
    expect(bridge.artwork).toHaveBeenCalledTimes(1)
    first.unmount()
    expect(image.getAttribute('src')).toBeNull()
    expect(artworkImages(client).liveSlots).toBe(0)
    const second = render(cover(false))
    expect(second.container.querySelector('img')).not.toBeNull()
    expect(bridge.artwork).toHaveBeenCalledTimes(1)
    second.unmount()
    expect(artworkImages(client).liveSlots).toBe(0)
  })

  it('retries a transient decode failure after detach and reattach', async () => {
    const decode = vi
      .fn()
      .mockRejectedValueOnce(new Error('Temporary decode failure'))
      .mockResolvedValue(undefined)
    vi.stubGlobal(
      'Image',
      class {
        src = ''
        naturalWidth = 640
        naturalHeight = 960
        decode = decode
      },
    )
    const bridge = installBridge(vi.fn().mockResolvedValue(currentArt))
    const client = queryClient()
    const first = render(view(client))
    await waitFor(() => expect(first.container.textContent).toContain('Artwork unavailable'))
    first.unmount()
    expect(artworkImages(client).liveSlots).toBe(0)
    const second = render(view(client))
    await waitFor(() => expect(second.container.querySelector('img')).not.toBeNull())
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
    expect(decode).toHaveBeenCalledTimes(2)
    second.unmount()
  })

  it('requests the first real display bucket and retries a null result at the unchanged width', async () => {
    vi.mocked(HTMLElement.prototype.getBoundingClientRect).mockReturnValue({
      x: 0,
      y: 0,
      width: 400,
      height: 600,
      top: 0,
      bottom: 600,
      left: 0,
      right: 400,
      toJSON() {},
    })
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi.fn().mockResolvedValueOnce(null).mockResolvedValue(sourceA),
    )
    const result = render(view(queryClient()))
    await waitFor(() => expect(result.container.textContent).toContain('Artwork unavailable'))
    expect(bridge.artwork).toHaveBeenNthCalledWith(1, 'steam', '123', 480, expect.any(String))
    fireEvent(window, new Event('resize'))
    await waitFor(() => expect(result.container.querySelector('img')).not.toBeNull())
    expect(bridge.artwork).toHaveBeenNthCalledWith(2, 'steam', '123', 480, expect.any(String))
    fireEvent(window, new Event('resize'))
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
  })
})

it('shutdown during browser decoding waits for retirement and releases unpublished pixels once', async () => {
  const decoding = deferred<void>()
  const decode = vi.fn(() => decoding.promise)
  const decoder = { src: '', naturalWidth: 640, naturalHeight: 960, decode }
  vi.stubGlobal(
    'Image',
    class {
      constructor() {
        return decoder
      }
    },
  )
  installBridge(vi.fn().mockResolvedValue(currentArt))
  const client = queryClient(),
    result = render(view(client))
  try {
    await waitFor(() => expect(decode).toHaveBeenCalledTimes(1))
    const cache = artworkImages(client)
    let stopped = false
    const shutdown = closeArtworkImages(client).then(() => {
      stopped = true
    })
    await Promise.resolve()
    expect(stopped).toBe(false)
    expect(decoder.src).toBe('')
    decoding.resolve()
    await act(async () => shutdown)
    expect(result.container.querySelector('img')).toBeNull()
    expect(cache.pendingCount).toBe(0)
    expect(cache.decodedCount).toBe(0)
    expect(URL.revokeObjectURL).toHaveBeenCalledTimes(1)
  } finally {
    decoding.resolve()
  }
})

it('shutdown drains an ignored bridge cancellation without caching late bytes or admitting new reads', async () => {
  const bytes = deferred<string>()
  const bridge = installBridge(
    vi.fn().mockResolvedValue(currentArt),
    vi.fn(() => bytes.promise),
  )
  const client = queryClient(),
    result = render(view(client))
  try {
    await waitFor(() => expect(bridge.artwork).toHaveBeenCalledTimes(1))
    const cache = artworkImages(client)
    let stopped = false
    const shutdown = closeArtworkImages(client).then(() => {
      stopped = true
    })
    await Promise.resolve()
    expect(stopped).toBe(false)
    expect(bridge.cancelRequest).toHaveBeenCalledWith(bridge.artwork.mock.calls[0]![3])
    const refused = vi.fn()
    expect(await cache.get('another', refused)).toBeNull()
    expect(refused).not.toHaveBeenCalled()
    bytes.resolve(sourceA)
    await act(async () => shutdown)
    expect(result.container.querySelector('img')).toBeNull()
    expect(cache.pendingCount).toBe(0)
    expect(cache.decodedCount).toBe(0)
    expect(URL.createObjectURL).not.toHaveBeenCalled()
    expect(client.getQueryData(['artwork-image', 'steam', '123', 640, 'selection-a'])).toBeUndefined()
    expect(bridge.artwork).toHaveBeenCalledTimes(1)
  } finally {
    bytes.resolve(sourceA)
  }
})

describe('Artwork surface isolation and display-size upgrades', () => {
  function measured(width: (node: HTMLElement) => number) {
    vi.mocked(HTMLElement.prototype.getBoundingClientRect).mockImplementation(function (this: HTMLElement) {
      const pixels = width(this)
      return {
        x: 0,
        y: 0,
        width: pixels,
        height: pixels * 1.5,
        top: 0,
        bottom: pixels * 1.5,
        left: 0,
        right: pixels,
        toJSON() {},
      }
    })
  }
  const blob = (source: string) => source.replace('data:image/png;base64,', 'blob:')

  it('recycling a wall leaves the feed image alive and releasing both leaves only the memory cache', async () => {
    measured((node) => (node.classList.contains('wall') ? 148 : 108))
    const bridge = installBridge(
      vi.fn(async (input: ApiRequest) =>
        input.params?.workId === 1
          ? currentArt
          : { ok: true, status: 200, data: { current: null, revision: 'missing' } },
      ),
    )
    const client = queryClient()
    const surfaces = (id: number, feed = true) => (
      <QueryClientProvider client={client}>
        <Artwork workId={id} className="wall" />
        {feed && <Artwork workId={1} className="feed" />}
      </QueryClientProvider>
    )
    const result = render(surfaces(1))
    await waitFor(() => expect(result.container.querySelectorAll('img')).toHaveLength(2))
    for (const image of result.container.querySelectorAll('img')) fireEvent.load(image)
    const feed = result.container.querySelector('.feed img')!
    expect(bridge.artwork).toHaveBeenCalledTimes(1)
    expect(artworkImages(client).liveSlots).toBe(1)
    result.rerender(surfaces(2))
    expect(result.container.querySelector('.wall img')).toBeNull()
    await waitFor(() =>
      expect(result.container.querySelector('.wall')?.getAttribute('data-state')).toBe('missing'),
    )
    expect(result.container.querySelector('.feed img')).toBe(feed)
    expect(feed.getAttribute('src')).toBe(blob(sourceA))
    expect(result.container.querySelector('.feed')?.getAttribute('data-state')).toBe('ready')
    expect(artworkImages(client).liveSlots).toBe(1)
    result.rerender(surfaces(2, false))
    expect(feed.getAttribute('src')).toBeNull()
    expect(artworkImages(client).liveSlots).toBe(0)
    expect(artworkImages(client).decodedCount).toBe(1)
    expect(URL.revokeObjectURL).not.toHaveBeenCalled()
  })

  it('a smaller feed request cannot change the wall image or its larger size bucket', async () => {
    measured((node) => (node.classList.contains('wall') ? 300 : 108))
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi.fn(async (_provider, _id, width) => (width === 320 ? sourceA : sourceB)),
    )
    const client = queryClient()
    const surfaces = (feed: boolean) => (
      <QueryClientProvider client={client}>
        <Artwork workId={1} className="wall" />
        {feed && <Artwork workId={1} className="feed" />}
      </QueryClientProvider>
    )
    const result = render(surfaces(false))
    await waitFor(() => expect(result.container.querySelector('.wall img')).not.toBeNull())
    const wall = result.container.querySelector('.wall img')!
    fireEvent.load(wall)
    result.rerender(surfaces(true))
    await waitFor(() => expect(result.container.querySelector('.feed img')).not.toBeNull())
    expect(bridge.artwork.mock.calls.map((call) => call[2])).toEqual([320, 160])
    expect(result.container.querySelector('.wall img')).toBe(wall)
    expect(wall.getAttribute('src')).toBe(blob(sourceA))
    expect(result.container.querySelector('.feed img')?.getAttribute('src')).toBe(blob(sourceB))
    expect(artworkImages(client).liveSlots).toBe(2)
    result.unmount()
    expect(artworkImages(client).liveSlots).toBe(0)
  })

  it('a late small result and a later density shrink cannot downgrade a realized larger image', async () => {
    let width = 148
    measured(() => width)
    const small = deferred<string>(),
      large = deferred<string>()
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi.fn((_provider, _id, pixels) => (pixels === 160 ? small.promise : large.promise)),
    )
    const client = queryClient(),
      result = render(view(client))
    await waitFor(() => expect(bridge.artwork).toHaveBeenCalledTimes(1))
    width = 300
    fireEvent(window, new Event('resize'))
    await waitFor(() => expect(bridge.artwork).toHaveBeenCalledTimes(2))
    await act(async () => large.resolve(sourceB))
    await waitFor(() =>
      expect(result.container.querySelector('img')?.getAttribute('src')).toBe(blob(sourceB)),
    )
    const image = result.container.querySelector('img')!
    fireEvent.load(image)
    await act(async () => small.resolve(sourceA))
    expect(bridge.cancelRequest).toHaveBeenCalledWith(bridge.artwork.mock.calls[0]![3])
    expect(result.container.querySelector('img')).toBe(image)
    expect(image.getAttribute('src')).toBe(blob(sourceB))
    width = 108
    fireEvent(window, new Event('resize'))
    expect(result.container.querySelector('img')).toBe(image)
    expect(result.container.querySelector('.art-loading')).toBeNull()
    expect(bridge.artwork.mock.calls.map((call) => call[2])).toEqual([160, 320])
    expect(client.getQueryData(['artwork-image', 'steam', '123', 160, 'selection-a'])).toBeUndefined()
  })

  it('keeps ready pixels through a pending or failed upgrade and retries without blanking on the next measurement', async () => {
    let width = 148
    measured(() => width)
    const upgrade = deferred<string | null>()
    const bridge = installBridge(
      vi.fn().mockResolvedValue(currentArt),
      vi
        .fn()
        .mockResolvedValueOnce(sourceA)
        .mockReturnValueOnce(upgrade.promise)
        .mockResolvedValueOnce(sourceB),
    )
    const client = queryClient(),
      result = render(view(client))
    try {
      await waitFor(() => expect(result.container.querySelector('img')).not.toBeNull())
      const image = result.container.querySelector('img')!
      fireEvent.load(image)
      width = 300
      fireEvent(window, new Event('resize'))
      await waitFor(() => expect(bridge.artwork).toHaveBeenCalledTimes(2))
      expect(result.container.querySelector('img')).toBe(image)
      expect(image.getAttribute('src')).toBe(blob(sourceA))
      expect(result.container.querySelector('.art-loading')).toBeNull()
      expect(artworkImages(client).liveSlots).toBe(2)
      await act(async () => upgrade.resolve(null))
      expect(result.container.querySelector('img')).toBe(image)
      expect(result.container.querySelector('.artwork')?.getAttribute('data-state')).toBe('ready')
      expect(artworkImages(client).liveSlots).toBe(1)
      fireEvent(window, new Event('resize'))
      await waitFor(() => expect(image.getAttribute('src')).toBe(blob(sourceB)))
      fireEvent.load(image)
      expect(bridge.artwork.mock.calls.map((call) => call[2])).toEqual([160, 320, 320])
      expect(artworkImages(client).liveSlots).toBe(1)
    } finally {
      upgrade.resolve(null)
    }
  })

  it('an old game response arriving after recycling cannot paint or cache into its replacement', async () => {
    measured(() => 148)
    const old = deferred<string>(),
      next = deferred<string>()
    const bridge = installBridge(
      vi.fn(async (input: ApiRequest) =>
        input.params?.workId === 1
          ? currentArt
          : {
              ...currentArt,
              data: { current: { previewKey: { provider: 'steam', id: '70' } }, revision: 'life' },
            },
      ),
      vi.fn((_provider, id) => (id === '123' ? old.promise : next.promise)),
    )
    const client = queryClient(),
      result = render(view(client, 1))
    await waitFor(() => expect(bridge.artwork).toHaveBeenCalledTimes(1))
    result.rerender(view(client, 2))
    await waitFor(() => expect(bridge.artwork).toHaveBeenCalledTimes(2))
    await act(async () => old.resolve(sourceA))
    expect(result.container.querySelector('img')).toBeNull()
    expect(result.container.querySelector('.artwork')?.getAttribute('data-state')).toBe('loading')
    expect(bridge.cancelRequest).toHaveBeenCalledWith(bridge.artwork.mock.calls[0]![3])
    expect(URL.createObjectURL).not.toHaveBeenCalled()
    await act(async () => next.resolve(sourceB))
    await waitFor(() =>
      expect(result.container.querySelector('img')?.getAttribute('src')).toBe(blob(sourceB)),
    )
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1)
  })
})
