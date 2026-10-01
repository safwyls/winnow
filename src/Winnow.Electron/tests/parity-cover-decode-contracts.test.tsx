// @vitest-environment jsdom
import { waitFor } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ArtworkDecoder } from '../src/renderer/components/artwork-decode'
import {
  artworkImages,
  closeArtworkImages,
  loadArtworkImage,
  type ArtworkKey,
} from '../src/renderer/components/artwork-images'

const png =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAEklEQVR4nGP4z8DwHx9mGBkKAMLXf4EvceABAAAAAElFTkSuQmCC'
const clients: QueryClient[] = []
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((complete) => {
    resolve = complete
  })
  return { promise, resolve }
}
const key = (id: string): ArtworkKey => ['artwork-image', 'steam', id, 160, 'source']
function setup(artwork: ReturnType<typeof vi.fn>, cancelRequest = vi.fn(async () => true)) {
  Object.defineProperty(window, 'winnow', { configurable: true, value: { artwork, cancelRequest } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  const cache = artworkImages(client)
  const acquire = (id: string) =>
    cache.acquire(JSON.stringify(key(id)), (signal) => loadArtworkImage(client, key(id), signal))
  return { client, cache, acquire, cancelRequest }
}
const decode = vi.fn<() => Promise<void>>()
beforeEach(() => {
  decode.mockReset().mockResolvedValue(undefined)
  let sequence = 0
  URL.createObjectURL = vi.fn(() => `blob:decoded-${++sequence}`)
  URL.revokeObjectURL = vi.fn()
  vi.stubGlobal(
    'Image',
    class {
      src = ''
      naturalWidth = 160
      naturalHeight = 240
      decode = decode
    },
  )
})
afterEach(async () => {
  for (const client of clients.splice(0)) {
    await closeArtworkImages(client)
    client.clear()
  }
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it('six blocked image fetches do not stop the seventh already-available backend image from decoding', async () => {
  const network = deferred<string>()
  const artwork = vi.fn((_provider: string, id: string) =>
    id === '43' ? Promise.resolve(png) : network.promise,
  )
  const f = setup(artwork)
  const held = ['42', '44', '45', '46', '47', '48'].map(f.acquire)
  try {
    await waitFor(() => expect(artwork).toHaveBeenCalledTimes(6))
    const cached = f.acquire('43')
    const result = await cached.ready
    expect(result).not.toBeNull()
    expect(artwork).toHaveBeenCalledTimes(7)
    expect(f.cache.pendingCount).toBe(6)
    expect(decode).toHaveBeenCalledTimes(1)
    cached.release()
  } finally {
    held.forEach((lease) => lease.release())
    network.resolve(png)
    await Promise.all(held.map((lease) => lease.ready))
  }
})

it('the real six-permit browser limit includes transient conversion before a seventh image allocates its URL', async () => {
  const converting = deferred<void>()
  decode.mockImplementation(() => converting.promise)
  const artwork = vi.fn().mockResolvedValue(png)
  const f = setup(artwork)
  const leases = ['42', '43', '44', '45', '46', '47', '48'].map(f.acquire)
  try {
    await waitFor(() => expect(artwork).toHaveBeenCalledTimes(7))
    await waitFor(() => expect(decode).toHaveBeenCalledTimes(6))
    expect(URL.createObjectURL).toHaveBeenCalledTimes(6)
    expect(f.cache.pendingCount).toBe(7)
    expect(f.client.getQueryData(key('48'))).toBeUndefined()
    converting.resolve()
    expect((await Promise.all(leases.map((lease) => lease.ready))).every(Boolean)).toBe(true)
    expect(decode).toHaveBeenCalledTimes(7)
    expect(f.cache.pendingCount).toBe(0)
  } finally {
    converting.resolve()
    leases.forEach((lease) => lease.release())
  }
})

it('one conversion permit remains held through the source 300ms boundary and failure releases its URL before the next conversion', async () => {
  const converting = deferred<void>()
  decode
    .mockReturnValueOnce(converting.promise)
    .mockRejectedValueOnce(new Error('Injected browser conversion failure.'))
  const queue = new ArtworkDecoder({ concurrent: 1 })
  const signal = new AbortController().signal
  const first = queue.decode(
    png,
    signal,
    async () => png,
    () => {},
  )
  const second = queue.decode(
    png,
    signal,
    async () => png,
    () => {},
  )
  try {
    await new Promise((resolve) => setTimeout(resolve, 300))
    expect(decode).toHaveBeenCalledTimes(1)
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1)
    expect(queue.activeCount).toBe(1)
    expect(queue.queuedCount).toBe(1)
    converting.resolve()
    const pixels = await first
    expect(pixels).not.toBeNull()
    expect(await second).toBeNull()
    expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:decoded-2')
    expect(queue.activeCount).toBe(0)
    expect(queue.queuedCount).toBe(0)
    pixels?.dispose()
  } finally {
    converting.resolve()
    queue.close()
  }
})

it('encoded queue pressure discards excess strings then completes every still-owned image after a permit opens', async () => {
  const converting = deferred<void>()
  decode.mockReturnValueOnce(converting.promise)
  const queue = new ArtworkDecoder({ concurrent: 1, maxQueuedBytes: png.length * 2 })
  const signal = new AbortController().signal
  const reload = vi.fn(async () => png),
    publish = vi.fn()
  const reads = Array.from({ length: 4 }, () => queue.decode(png, signal, reload, publish))
  try {
    expect(queue.queuedCount).toBe(3)
    expect(queue.queuedBytes).toBe(png.length * 2)
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1)
    expect(reload).not.toHaveBeenCalled()
    converting.resolve()
    const pixels = await Promise.all(reads)
    expect(pixels.every(Boolean)).toBe(true)
    expect(reload).toHaveBeenCalledTimes(2)
    expect(publish).toHaveBeenCalledTimes(4)
    expect(queue.queuedBytes).toBe(0)
    expect(queue.activeCount).toBe(0)
    pixels.forEach((value) => value?.dispose())
  } finally {
    converting.resolve()
    queue.close()
  }
})

it.each(['abort', 'shutdown'] as const)(
  'queued conversion %s settles immediately without allocating a URL or rereading bytes',
  async (action) => {
    const converting = deferred<void>()
    decode.mockReturnValueOnce(converting.promise)
    const queue = new ArtworkDecoder({ concurrent: 1, maxQueuedBytes: 0 })
    const firstController = new AbortController(),
      queuedController = new AbortController()
    const reload = vi.fn(async () => png),
      publish = vi.fn()
    const first = queue.decode(png, firstController.signal, reload, publish)
    const queued = queue.decode(png, queuedController.signal, reload, publish)
    try {
      expect(queue.queuedBytes).toBe(0)
      if (action === 'abort') queuedController.abort()
      else queue.close()
      expect(await queued).toBeNull()
      expect(queue.queuedCount).toBe(0)
      expect(URL.createObjectURL).toHaveBeenCalledTimes(1)
      expect(reload).not.toHaveBeenCalled()
      converting.resolve()
      const image = await first
      if (action === 'shutdown') {
        expect(image).toBeNull()
        expect(publish).not.toHaveBeenCalled()
      } else image?.dispose()
    } finally {
      converting.resolve()
      firstController.abort()
      queue.close()
    }
  },
)

it.each(['abort', 'shutdown'] as const)(
  'an active overflow reread rejection remains null after %s',
  async (boundary) => {
    const converting = deferred<void>()
    decode.mockReturnValueOnce(converting.promise)
    const queue = new ArtworkDecoder({ concurrent: 1, maxQueuedBytes: 0 })
    const controller = new AbortController()
    let rejectReload!: (reason: Error) => void
    const reloading = new Promise<string | undefined>((_resolve, reject) => {
      rejectReload = reject
    })
    const reload = vi.fn(() => reloading)
    const publish = vi.fn()
    const first = queue.decode(png, new AbortController().signal, async () => png, publish)
    const second = queue.decode(png, controller.signal, reload, publish)
    const result = expect(second).resolves.toBeNull()
    try {
      converting.resolve()
      const pixels = await first
      await waitFor(() => expect(reload).toHaveBeenCalledOnce())
      expect(queue.activeCount).toBe(1)
      if (boundary === 'abort') controller.abort()
      else queue.close()
      rejectReload(new Error('Transport ended after cancellation'))
      await result
      expect(queue.activeCount).toBe(0)
      expect(queue.queuedCount).toBe(0)
      expect(publish).toHaveBeenCalledTimes(1)
      expect(URL.createObjectURL).toHaveBeenCalledTimes(1)
      pixels?.dispose()
    } finally {
      converting.resolve()
      rejectReload(new Error('Cleanup'))
      queue.close()
    }
  },
)

it.each(['last consumer', 'shutdown'] as const)(
  'a rejected cancellation IPC cannot replace %s retirement or skip decoded cleanup',
  async (boundary) => {
    const held = deferred<string>()
    const artwork = vi.fn((_provider: string, id: string, _width: number, _requestId: string) =>
      id === '42' ? Promise.resolve(png) : held.promise,
    )
    const cancel = vi.fn(async () => {
      throw new Error('Injected cancellation IPC failure.')
    })
    const f = setup(artwork, cancel)
    const first = f.acquire('42')
    await first.ready
    first.release()
    const second = f.acquire('43')
    try {
      await waitFor(() => expect(artwork).toHaveBeenCalledTimes(2))
      let shutdown: Promise<void> | undefined
      if (boundary === 'last consumer') second.release()
      else shutdown = closeArtworkImages(f.client)
      expect(cancel).toHaveBeenCalledWith(artwork.mock.calls[1][3])
      held.resolve(png)
      expect(await second.ready).toBeNull()
      second.release()
      await (shutdown ?? closeArtworkImages(f.client))
      expect(f.cache.pendingCount).toBe(0)
      expect(f.cache.decodedCount).toBe(0)
      expect(f.cache.liveSlots).toBe(0)
      expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:decoded-1')
      expect(f.client.getQueryData(key('43'))).toBeUndefined()
      await closeArtworkImages(f.client)
    } finally {
      held.resolve(png)
      second.release()
    }
  },
)

it('shutdown finishes within the source three-second bound when source cancellation settles the read but its acknowledgement rejects', async () => {
  let rejectRead!: (reason: DOMException) => void
  const pending = new Promise<string>((_resolve, reject) => {
    rejectRead = reject
  })
  const artwork = vi.fn((_provider: string, id: string, _width: number, _requestId: string) =>
    id === '42' ? Promise.resolve(png) : pending,
  )
  const cancel = vi.fn(async () => {
    rejectRead(new DOMException('The image read was canceled.', 'AbortError'))
    throw new Error('Injected cancellation acknowledgement failure.')
  })
  const f = setup(artwork, cancel)
  const ready = f.acquire('42')
  await ready.ready
  ready.release()
  const held = f.acquire('43')
  let timer: ReturnType<typeof setTimeout> | undefined
  try {
    await waitFor(() => expect(artwork).toHaveBeenCalledTimes(2))
    const deadline = new Promise<never>((_resolve, reject) => {
      timer = setTimeout(() => reject(new Error('Shutdown exceeded the source three-second bound.')), 3000)
    })
    await Promise.race([closeArtworkImages(f.client), deadline])
    expect(cancel).toHaveBeenCalledExactlyOnceWith(artwork.mock.calls[1][3])
    expect(await held.ready).toBeNull()
    held.release()
    expect(f.cache.pendingCount).toBe(0)
    expect(f.cache.decodedCount).toBe(0)
    expect(f.cache.liveSlots).toBe(0)
    expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:decoded-1')
    expect(f.client.getQueryData(key('43'))).toBeUndefined()
    await closeArtworkImages(f.client)
    expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:decoded-1')
  } finally {
    if (timer !== undefined) clearTimeout(timer)
    rejectRead(new DOMException('Cleanup', 'AbortError'))
    held.release()
  }
})
