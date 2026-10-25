// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import type { WinnowBridge } from '../src/shared/bridge'
import {
  preloadBackground,
  detailsBackdropSize,
  type BackgroundPreloadTarget,
} from '../src/renderer/components/background-preload'
import { preloadArtworkSource } from '../src/renderer/components/artwork-source'
import { artworkImages, closeArtworkImages, type ArtworkKey } from '../src/renderer/components/artwork-images'
import { loadCachedBackdropImage } from '../src/renderer/components/backdrop-image'

let client: QueryClient
const bytes = new Uint8Array([137, 80, 78, 71])
const target: BackgroundPreloadTarget = {
  workId: 7,
  compositionId: 'avalon',
  fullscreen: false,
  width: 1440,
  height: 900,
  scale: 1,
}
const key = (id: string, width = 1280): ArtworkKey => ['artwork-image', 'fixture', id, width, 'asset']
const signal = () => new AbortController().signal
function deferred<T>() {
  let resolve!: (value: T) => void
  return {
    promise: new Promise<T>((done) => {
      resolve = done
    }),
    resolve: (value: T) => resolve(value),
  }
}

beforeEach(() => {
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  window.winnow = {
    artworkBytes: vi.fn(async () => bytes),
    cancelRequest: vi.fn(async () => true),
    request: vi.fn(async ({ route }) => ({
      ok: true,
      status: 200,
      data:
        route === 'artwork.backdrop'
          ? {
              candidates: [
                { key: { provider: 'fixture', id: 'landscape' }, aspectRatio: 16 / 9, fitWholeHero: false },
              ],
              coverKey: { provider: 'fixture', id: 'cover' },
            }
          : { current: { previewKey: { provider: 'fixture', id: 'hero' } }, revision: 'r1' },
    })),
  } as unknown as WinnowBridge
  vi.stubGlobal(
    'Image',
    vi.fn(
      class {
        src = ''
        naturalWidth = 1280
        naturalHeight = 720
        decode = async () => {}
      },
    ),
  )
  URL.createObjectURL = vi.fn(() => 'blob:background')
  URL.revokeObjectURL = vi.fn()
})
afterEach(async () => {
  await closeArtworkImages(client)
  client.clear()
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

it.each([[], null, { candidates: null, coverKey: null }])(
  'preloads nothing and settles quietly when the backdrop selection payload is %j',
  async (payload) => {
    vi.mocked(window.winnow.request).mockImplementation(async ({ route }) => ({
      ok: true,
      status: 200,
      data: route === 'artwork.backdrop' ? payload : { current: null, revision: 'r1' },
    }))
    await expect(preloadBackground(client, { ...target, coverWorkId: 9 }, signal())).resolves.toBeUndefined()
    expect(window.winnow.artworkBytes).not.toHaveBeenCalled()
    expect(client.getQueryCache().getAll()).toEqual([])
  },
)
it('warms the desktop Details crop width and the actual backdrop consumer reuses its bytes without speculative decoding', async () => {
  await preloadBackground(client, target, signal())
  expect(window.winnow.request).toHaveBeenCalledWith(
    expect.objectContaining({ route: 'artwork.backdrop', params: { workId: 7, aspectRatio: 858 / 718 } }),
  )
  expect(window.winnow.artworkBytes).toHaveBeenCalledWith('fixture', 'landscape', 1280, expect.any(String))
  expect(Image).not.toHaveBeenCalled()
  const pixels = await loadCachedBackdropImage(
    client,
    { provider: 'fixture', id: 'landscape' },
    1280,
    signal(),
  )
  expect(pixels).not.toBeNull()
  expect(window.winnow.artworkBytes).toHaveBeenCalledOnce()
  pixels!.dispose()
})

it('uses ultrawide whole-hero geometry for fullscreen and never warms unrelated screenshots', async () => {
  vi.mocked(window.winnow.request).mockResolvedValue({
    ok: true,
    status: 200,
    data: {
      candidates: [
        { key: { provider: 'fixture', id: 'wide' }, aspectRatio: 3840 / 1240, fitWholeHero: true },
        { key: { provider: 'fixture', id: 'unneeded' }, aspectRatio: 16 / 9, fitWholeHero: false },
      ],
      coverKey: null,
    },
  })
  await preloadBackground(client, { ...target, fullscreen: true, width: 2560, height: 1080 }, signal())
  expect(window.winnow.artworkBytes).toHaveBeenCalledExactlyOnceWith(
    'fixture',
    'wide',
    2560,
    expect.any(String),
  )
})

it('preserves candidate order and uses the header work cover only after backgrounds fail', async () => {
  vi.mocked(window.winnow.artworkBytes!).mockImplementation(async (_provider, id) =>
    id === 'hero' ? bytes : null,
  )
  await preloadBackground(client, { ...target, coverWorkId: 99 }, signal())
  expect(window.winnow.request).toHaveBeenCalledWith(
    expect.objectContaining({ route: 'artwork.get', params: { workId: 99, slot: 'Cover' } }),
  )
  expect(vi.mocked(window.winnow.artworkBytes!).mock.calls.map((call) => call[1])).toEqual([
    'landscape',
    'hero',
  ])
})

it.each(['afterglow', 'catalogue', 'rift'])(
  'warms %s through the shared backdrop candidates and reuses the same cached bytes',
  async (compositionId) => {
    await preloadBackground(client, { ...target, compositionId }, signal())
    expect(window.winnow.request).toHaveBeenCalledWith(
      expect.objectContaining({
        route: 'artwork.backdrop',
        params: { workId: 7, aspectRatio: expect.any(Number) },
      }),
    )
    expect(client.getQueryData(['artwork-image', 'fixture', 'landscape', 1920, 'asset'])).toEqual({
      source: bytes,
    })
    expect(Image).not.toHaveBeenCalled()
    const pixels = await loadCachedBackdropImage(
      client,
      { provider: 'fixture', id: 'landscape' },
      1920,
      signal(),
    )
    expect(pixels).not.toBeNull()
    expect(window.winnow.artworkBytes).toHaveBeenCalledOnce()
    pixels!.dispose()
  },
)

it('keeps an overlapping foreground read alive when speculation is canceled', async () => {
  const pending = deferred<Uint8Array>()
  vi.mocked(window.winnow.artworkBytes!).mockReturnValue(pending.promise)
  const controller = new AbortController()
  const warm = preloadArtworkSource(client, key('joined'), controller.signal)
  const rejected = expect(warm).rejects.toMatchObject({ name: 'AbortError' })
  const foreground = loadCachedBackdropImage(client, { provider: 'fixture', id: 'joined' }, 1280, signal())
  controller.abort()
  expect(window.winnow.cancelRequest).not.toHaveBeenCalled()
  pending.resolve(bytes)
  await rejected
  const pixels = await foreground
  expect(pixels).not.toBeNull()
  expect(window.winnow.artworkBytes).toHaveBeenCalledOnce()
  pixels!.dispose()
})

it('holds cancellation until acknowledgment and does not publish late bytes or overlap a replacement', async () => {
  const pending = deferred<Uint8Array>()
  vi.mocked(window.winnow.artworkBytes!).mockReturnValueOnce(pending.promise)
  const controller = new AbortController()
  const warm = preloadArtworkSource(client, key('late'), controller.signal)
  const rejected = expect(warm).rejects.toMatchObject({ name: 'AbortError' })
  controller.abort()
  const replacement = preloadArtworkSource(client, key('late'), signal())
  await Promise.resolve()
  expect(window.winnow.artworkBytes).toHaveBeenCalledOnce()
  expect(window.winnow.cancelRequest).toHaveBeenCalledOnce()
  pending.resolve(bytes)
  await rejected
  await replacement
  expect(window.winnow.artworkBytes).toHaveBeenCalledTimes(2)
  expect(client.getQueryData(key('late'))).toEqual({ source: bytes })
})

it('invalidation retires pending warm bytes before they can become current', async () => {
  const pending = deferred<Uint8Array>()
  vi.mocked(window.winnow.artworkBytes!).mockReturnValue(pending.promise)
  const warm = preloadArtworkSource(client, key('invalidated'), signal())
  const rejected = expect(warm).rejects.toMatchObject({ name: 'AbortError' })
  await client.invalidateQueries({ queryKey: ['artwork-image'] })
  pending.resolve(bytes)
  await rejected
  expect(client.getQueryData(key('invalidated'))).toBeUndefined()
})

it('bounds speculative entry retention without evicting bytes or pixels already used by a foreground view', async () => {
  await preloadArtworkSource(client, key('visible'), signal())
  const visible = await loadCachedBackdropImage(
    client,
    { provider: 'fixture', id: 'visible' },
    1280,
    signal(),
  )
  for (let index = 0; index < 131; index++) await preloadArtworkSource(client, key(`spare${index}`), signal())
  expect(client.getQueryData(key('spare0'))).toBeUndefined()
  expect(client.getQueryData(key('visible'))).toEqual({ source: bytes })
  expect(artworkImages(client).decodedCount).toBe(1)
  expect(URL.revokeObjectURL).not.toHaveBeenCalled()
  expect(
    client
      .getQueryCache()
      .getAll()
      .filter((query) => query.state.data),
  ).toHaveLength(129)
  visible!.dispose()
})

it('bounds speculative bytes independently of entry count', async () => {
  vi.mocked(window.winnow.artworkBytes!).mockResolvedValue(new Uint8Array(17 * 1024 * 1024))
  for (const id of ['one', 'two', 'three']) await preloadArtworkSource(client, key(id), signal())
  expect(client.getQueryData(key('one'))).toBeUndefined()
  expect(client.getQueryData(key('two'))).toBeUndefined()
  expect(client.getQueryData(key('three'))).toBeDefined()
  expect(Image).not.toHaveBeenCalled()
})

it('retains all 36 visible Rift backgrounds when their encoded bytes fit the memory budget', async () => {
  vi.mocked(window.winnow.artworkBytes!).mockResolvedValue(new Uint8Array(128 * 1024))
  for (let id = 1; id <= 36; id++) await preloadArtworkSource(client, key(String(id)), signal())
  for (let id = 1; id <= 36; id++) expect(client.getQueryData(key(String(id)))).toBeDefined()
  const pixels = await loadCachedBackdropImage(client, { provider: 'fixture', id: '1' }, 1280, signal())
  expect(window.winnow.artworkBytes).toHaveBeenCalledTimes(36)
  pixels!.dispose()
})

it('promotes still-cached backgrounds when returning to a viewport before warming its remaining titles', async () => {
  for (let id = 355; id <= 486; id++) await preloadArtworkSource(client, key(String(id)), signal())
  for (let id = 885; id <= 999; id++) await preloadArtworkSource(client, key(String(id)), signal())
  for (let id = 481; id <= 499; id++) await preloadArtworkSource(client, key(String(id)), signal())
  const reads = vi.mocked(window.winnow.artworkBytes!).mock.calls.length
  const pixels = await loadCachedBackdropImage(client, { provider: 'fixture', id: '481' }, 1280, signal())
  expect(window.winnow.artworkBytes).toHaveBeenCalledTimes(reads)
  for (let id = 481; id <= 499; id++) expect(client.getQueryData(key(String(id)))).toBeDefined()
  pixels!.dispose()
})

it('keeps invalidated encoded bytes within the speculative budget', async () => {
  vi.mocked(window.winnow.artworkBytes!).mockResolvedValue(new Uint8Array(17 * 1024 * 1024))
  await preloadArtworkSource(client, key('old'), signal())
  await client.invalidateQueries({ queryKey: ['artwork-image'] })
  await preloadArtworkSource(client, key('new'), signal())
  expect(client.getQueryData(key('old'))).toBeUndefined()
  expect(client.getQueryData(key('new'))).toBeDefined()
})

it('does not publish when invalidated after the byte read settles but before its continuation', async () => {
  const pending = deferred<Uint8Array>()
  vi.mocked(window.winnow.artworkBytes!).mockReturnValue(pending.promise)
  const warm = preloadArtworkSource(client, key('settled'), signal())
  void pending.promise.then(() => client.invalidateQueries({ queryKey: ['artwork-image'] }))
  pending.resolve(bytes)
  expect(await warm).toBe(false)
  expect(client.getQueryData(key('settled'))).toBeUndefined()
})

it('revalidates expired bytes and never mistakes a smaller rendition for a larger one', async () => {
  const now = vi.spyOn(Date, 'now').mockReturnValue(1000)
  await preloadArtworkSource(client, key('sized', 640), signal())
  await preloadArtworkSource(client, key('sized', 1280), signal())
  await preloadArtworkSource(client, key('sized', 1280), signal())
  expect(window.winnow.artworkBytes).toHaveBeenCalledTimes(2)
  now.mockReturnValue(121001)
  await preloadArtworkSource(client, key('sized', 1280), signal())
  expect(window.winnow.artworkBytes).toHaveBeenCalledTimes(3)
})

it('accounts for narrow desktop margins and fullscreen interface zoom', () => {
  expect(detailsBackdropSize({ ...target, width: 800, height: 600 })).toMatchObject({
    width: 766,
    height: 566,
  })
  document.body.style.zoom = '.85'
  expect(detailsBackdropSize({ ...target, fullscreen: true, width: 1920, height: 1080 })).toEqual({
    width: 1920 / 0.85,
    height: 1080 / 0.85,
    scale: 0.85,
    fullscreen: true,
  })
  document.body.style.zoom = ''
})
