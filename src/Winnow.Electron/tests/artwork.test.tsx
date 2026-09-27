// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Artwork } from '../src/renderer/components/Artwork'
import { refreshSnapshots } from '../src/renderer/refresh'
import type { ApiRequest } from '../src/shared/bridge'

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
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
  Object.defineProperty(window, 'winnow', { value: { request, artwork }, configurable: true })
  return { request, artwork }
}
function queryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } })
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
    await waitFor(() => expect(bridge.artwork).toHaveBeenCalledWith('steam', '123', 600))
    expect(container.querySelector('.art-loading')).not.toBeNull()
    expect(container.querySelector('img')).toBeNull()
    await act(async () => bytes.resolve(sourceA))
    await waitFor(() => expect(container.querySelector('img')?.getAttribute('src')).toBe(sourceA))
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
    await waitFor(() => expect(container.querySelector('img')?.getAttribute('src')).toBe(sourceB))
    expect(container.querySelector('.art-loading')).not.toBeNull()
    fireEvent.load(container.querySelector('img')!)
    expect(container.querySelector('.art-loading')).toBeNull()
    expect(container.querySelector('img')?.getAttribute('src')).toBe(sourceB)
  })

  it('reuses valid cached bytes on remount but waits for the new image element to load', async () => {
    const bridge = installBridge(vi.fn().mockResolvedValue(currentArt))
    const client = queryClient()
    const first = render(view(client))
    await waitFor(() => expect(first.container.querySelector('img')).not.toBeNull())
    fireEvent.load(first.container.querySelector('img')!)
    first.unmount()
    const second = render(view(client))
    expect(second.container.querySelector('img')?.getAttribute('src')).toBe(sourceA)
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
    expect(bridge.artwork).toHaveBeenCalledTimes(1)
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
    await waitFor(() => expect(container.querySelector('img')?.getAttribute('src')).toBe(sourceB))
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
    expect(bridge.artwork).toHaveBeenNthCalledWith(2, 'steam', '123', 600)
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
    await waitFor(() => expect(container.querySelector('img')?.getAttribute('src')).toBe(sourceB))
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
    await waitFor(() => expect(container.querySelector('img')?.getAttribute('src')).toBe(sourceB))
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
  })

  it('keeps hero and cover size variants separate even with the same source key', async () => {
    const bridge = installBridge(vi.fn().mockResolvedValue(currentArt))
    const client = queryClient()
    const { container } = render(
      <QueryClientProvider client={client}>
        <Artwork workId={1} />
        <Artwork workId={1} hero />
      </QueryClientProvider>,
    )
    await waitFor(() => expect(container.querySelectorAll('img')).toHaveLength(2))
    expect(bridge.artwork).toHaveBeenCalledTimes(2)
    expect(bridge.artwork).toHaveBeenCalledWith('steam', '123', 600)
    expect(bridge.artwork).toHaveBeenCalledWith('steam', '123', 1920)
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
    await waitFor(() => expect(container.querySelector('img')?.getAttribute('src')).toBe(sourceB))
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
      await waitFor(() => expect(container.querySelector('img')?.getAttribute('src')).toBe(sourceA))
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
    await waitFor(() => expect(container.querySelector('img')?.getAttribute('src')).toBe(sourceA))
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
    const client = queryClient()
    const result = render(view(client))
    await waitFor(() => expect(result.container.querySelector('img')).not.toBeNull())
    vi.useFakeTimers()
    // Reschedule both GC timers under the fake clock before unmounting.
    await act(async () => refreshSnapshots(client, { artwork: true }))
    result.unmount()
    expect(client.getQueryCache().getAll()).toHaveLength(2)
    await vi.advanceTimersByTimeAsync(300_001)
    expect(client.getQueryCache().getAll()).toHaveLength(0)
    client.clear()
  })
})
