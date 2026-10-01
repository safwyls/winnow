import { expect, it, vi } from 'vitest'
import { ArtworkCache, artworkWidth, type OwnedArtwork } from '../src/renderer/components/artwork-cache'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
function pixels(source = 'fixture'): OwnedArtwork {
  return { source, width: 160, height: 240, dispose: vi.fn() }
}

it('one hundred direct callers share one factory and decoded object', async () => {
  const cache = new ArtworkCache()
  const pending = deferred<OwnedArtwork>()
  const load = vi.fn(() => pending.promise)
  const calls = Array.from({ length: 100 }, () => cache.get('shared', load))
  expect(load).toHaveBeenCalledTimes(1)
  expect(cache.pendingCount).toBe(1)
  const art = pixels()
  pending.resolve(art)
  for (const result of await Promise.all(calls)) expect(result).toBe(art)
  expect(cache.decodedCount).toBe(1)
  expect(cache.liveSlots).toBe(0)
  await cache.close()
  expect(art.dispose).toHaveBeenCalledTimes(1)
})

it('one hundred leases retain the shared pixels across cache shutdown and release them once', async () => {
  const cache = new ArtworkCache()
  const pending = deferred<OwnedArtwork>()
  const load = vi.fn(() => pending.promise)
  const leases = Array.from({ length: 100 }, () => cache.acquire('shared', load))
  expect(load).toHaveBeenCalledTimes(1)
  const art = pixels()
  pending.resolve(art)
  for (const result of await Promise.all(leases.map((lease) => lease.ready))) expect(result).toBe(art)
  await cache.close()
  expect(art.dispose).not.toHaveBeenCalled()
  for (const lease of leases) expect(lease.current).toBe(art)
  for (const lease of leases) {
    lease.release()
    lease.release()
  }
  expect(art.dispose).toHaveBeenCalledTimes(1)
  expect(cache.liveSlots).toBe(0)
})

it('eighty scroll requests admit only four slots and two factories, and cancellation drains the queue', async () => {
  const cache = new ArtworkCache({ maxPending: 4, concurrent: 2 })
  const load = vi.fn(
    (signal: AbortSignal) =>
      new Promise<OwnedArtwork | null>((resolve) => {
        signal.addEventListener('abort', () => resolve(null), { once: true })
      }),
  )
  const leases = Array.from({ length: 80 }, (_, index) => cache.acquire(String(index), load))
  expect(cache.pendingCount).toBe(4)
  expect(load).toHaveBeenCalledTimes(2)
  expect(await Promise.all(leases.slice(4).map((lease) => lease.ready))).toEqual(Array(76).fill(null))
  for (const lease of leases) lease.release()
  await Promise.all(leases.map((lease) => lease.ready))
  expect(cache.pendingCount).toBe(0)
  expect(cache.decodedCount).toBe(0)
  expect(cache.liveSlots).toBe(0)
  const retry = pixels('retry')
  expect(await cache.get('0', async () => retry)).toBe(retry)
  await cache.close()
})

it('releasing one lease keeps a shared fetch alive until the final consumer leaves', async () => {
  const cache = new ArtworkCache()
  let signal!: AbortSignal
  const pending = deferred<OwnedArtwork | null>()
  const load = vi.fn((value: AbortSignal) => {
    signal = value
    return pending.promise
  })
  const first = cache.acquire('shared', load),
    second = cache.acquire('shared', load)
  first.release()
  expect(signal.aborted).toBe(false)
  second.release()
  expect(signal.aborted).toBe(true)
  pending.resolve(null)
  expect(await first.ready).toBeNull()
  expect(cache.pendingCount).toBe(0)
  expect(cache.decodedCount).toBe(0)
  await cache.close()
})

it('shutdown drains an ignored cancellation and disposes unpublished pixels', async () => {
  const cache = new ArtworkCache()
  const pending = deferred<OwnedArtwork>()
  const load = vi.fn(() => pending.promise)
  const result = cache.get('late', load)
  let closed = false
  const shutdown = cache.close().then(() => {
    closed = true
  })
  await Promise.resolve()
  expect(closed).toBe(false)
  expect(await cache.get('refused', load)).toBeNull()
  expect(load).toHaveBeenCalledTimes(1)
  const late = pixels('late')
  pending.resolve(late)
  await shutdown
  expect(await result).toBeNull()
  expect(late.dispose).toHaveBeenCalledTimes(1)
  expect(cache.pendingCount).toBe(0)
  expect(cache.decodedCount).toBe(0)
  expect(cache.liveSlots).toBe(0)
})

it('a replacement lease waits for canceled work to retire before starting another factory', async () => {
  const cache = new ArtworkCache()
  const retiring = deferred<OwnedArtwork>()
  const recovered = pixels('recovered')
  const load = vi.fn().mockReturnValueOnce(retiring.promise).mockResolvedValue(recovered)
  const first = cache.acquire('same', load)
  first.release()
  const second = cache.acquire('same', load)
  expect(load).toHaveBeenCalledTimes(1)
  const late = pixels('late')
  retiring.resolve(late)
  expect(await second.ready).toBe(recovered)
  expect(await first.ready).toBeNull()
  expect(load).toHaveBeenCalledTimes(2)
  expect(late.dispose).toHaveBeenCalledTimes(1)
  second.release()
  await cache.close()
  expect(recovered.dispose).toHaveBeenCalledTimes(1)
})

it('a replacement lease exposes the shared failure after canceled work retires', async () => {
  const cache = new ArtworkCache()
  const retiring = deferred<OwnedArtwork | null>()
  const first = cache.acquire('same', () => retiring.promise)
  first.release()
  const replacement = cache.acquire('same', async () => {
    throw new Error('Transport failed')
  })
  expect(replacement.failed).toBe(false)
  retiring.resolve(null)
  expect(await replacement.ready).toBeNull()
  expect(replacement.failed).toBe(true)
  expect(first.failed).toBe(false)
  replacement.release()
  await cache.close()
})

it.each(['null', 'rejection'])('a %s result permits retry at the same width and key', async (failure) => {
  const cache = new ArtworkCache()
  const recovered = pixels()
  const load = vi.fn().mockResolvedValue(recovered)
  if (failure === 'null') load.mockResolvedValueOnce(null)
  else load.mockRejectedValueOnce(new Error('Temporary outage'))
  expect(await cache.get('same:160', load)).toBeNull()
  expect(cache.liveSlots).toBe(0)
  expect(await cache.get('same:160', load)).toBe(recovered)
  expect(load).toHaveBeenCalledTimes(2)
  await cache.close()
})

it('LRU eviction respects the pixel budget without disposing or decoding visible art again', async () => {
  const cache = new ArtworkCache({ maxBytes: 160 * 240 * 4 })
  const first = pixels('first'),
    second = pixels('second')
  const firstLoad = vi.fn(async () => first)
  const visible = cache.acquire('first', firstLoad)
  await visible.ready
  await cache.get('second', async () => second)
  expect(cache.decodedCount).toBe(1)
  expect(cache.decodedBytes).toBe(160 * 240 * 4)
  expect(first.dispose).not.toHaveBeenCalled()
  const another = cache.acquire('first', firstLoad)
  expect(await another.ready).toBe(first)
  expect(firstLoad).toHaveBeenCalledTimes(1)
  visible.release()
  another.release()
  expect(first.dispose).toHaveBeenCalledTimes(1)
  await cache.close()
  expect(second.dispose).toHaveBeenCalledTimes(1)
})

it('refresh and expiry replace cached pixels while the old visible lease remains valid', async () => {
  let now = 0
  const cache = new ArtworkCache({ now: () => now })
  const first = pixels('first'),
    second = pixels('second'),
    third = pixels('third')
  const load = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce(second).mockResolvedValueOnce(third)
  const visible = cache.acquire('same', load)
  await visible.ready
  cache.invalidate('same')
  expect(visible.current).toBe(first)
  expect(await cache.get('same', load)).toBe(second)
  now = 120_001
  expect(await cache.get('same', load)).toBe(third)
  expect(second.dispose).toHaveBeenCalledTimes(1)
  expect(first.dispose).not.toHaveBeenCalled()
  visible.release()
  expect(first.dispose).toHaveBeenCalledTimes(1)
  await cache.close()
  expect(third.dispose).toHaveBeenCalledTimes(1)
})

it.each([
  [0, 160],
  [160, 160],
  [161, 240],
  [400, 480],
  [600, 640],
  [1921, 2560],
  [8000, 3840],
])('snaps %i display pixels to the source width bucket %i', (pixels, width) => {
  expect(artworkWidth(pixels)).toBe(width)
})
