// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Artwork } from '../src/renderer/components/Artwork'
import type { ApiRequest } from '../src/shared/bridge'

afterEach(() => cleanup())

const sourceA = 'data:image/png;base64,YXJ0d29yay1h'
const sourceB = 'data:image/png;base64,YXJ0d29yay1i'
const currentArt = {
  ok: true,
  status: 200,
  data: { current: { previewKey: { provider: 'steam', id: '123' } } },
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
    await act(async () => second.resolve(currentArt))
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
