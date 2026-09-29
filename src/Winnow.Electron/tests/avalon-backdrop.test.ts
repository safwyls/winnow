import { beforeEach, expect, it, vi } from 'vitest'
import {
  AvalonBackdropController,
  backdropGeometry,
  backdropWidth,
  type BackdropCandidate,
  type BackdropFrame,
  type BackdropPixels,
  type BackdropSelection,
  type BackdropSize,
} from '../src/renderer/themes/avalon-backdrop-model'

const landscape = (id: string, ratio = 16 / 9): BackdropCandidate => ({
  key: { provider: 'igdb-backdrop', id },
  aspectRatio: ratio,
  fitWholeHero: false,
})
const hero: BackdropCandidate = {
  key: { provider: 'steam-hero', id: '42' },
  aspectRatio: 3840 / 1240,
  fitWholeHero: true,
}
const size: BackdropSize = { width: 1920, height: 1080, scale: 1, fullscreen: true }
const pixels = (id: string): BackdropPixels => ({ source: id, width: 1920, height: 1080, dispose: vi.fn() })
const pending = <T>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
let frames: Map<number, FrameRequestCallback>, id: number, current: BackdropFrame
let reads: { workId: number; signal: AbortSignal; finish: (value: BackdropSelection) => void }[]
let images: {
  key: string
  width: number
  signal: AbortSignal
  finish: (value: BackdropPixels | null) => void
}[]
let controller: AvalonBackdropController
const flush = async () => {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}
async function select(
  workId: number,
  candidates = [landscape(String(workId))],
  coverKey: BackdropSelection['coverKey'] = null,
) {
  controller.select(workId, size)
  reads.at(-1)!.finish({ candidates, coverKey })
  await flush()
}
async function complete(value: BackdropPixels | null) {
  images.at(-1)!.finish(value)
  await flush()
}
function frame(time: number) {
  const callbacks = [...frames.values()]
  frames.clear()
  callbacks.forEach((callback) => callback(time))
}
beforeEach(() => {
  frames = new Map()
  reads = []
  images = []
  id = 0
  controller = new AvalonBackdropController({
    resolve: (workId, _ratio, signal) => {
      const value = pending<BackdropSelection>()
      reads.push({ workId, signal, finish: value.resolve })
      return value.promise
    },
    image: (key, width, signal) => {
      const value = pending<BackdropPixels | null>()
      images.push({ key: `${key.provider}:${key.id}`, width, signal, finish: value.resolve })
      return value.promise
    },
    publish: (value) => {
      current = value
    },
    requestFrame: (callback) => {
      frames.set(++id, callback)
      return id
    },
    cancelFrame: (key) => {
      frames.delete(key)
    },
  })
})
it('cancels superseded metadata and cannot present its late selection', async () => {
  controller.select(1, size)
  const obsolete = reads[0]
  await select(2)
  expect(obsolete.signal.aborted).toBe(true)
  obsolete.finish({ candidates: [landscape('old')], coverKey: null })
  await flush()
  expect(images.map((image) => image.key)).toEqual(['igdb-backdrop:2'])
  await complete(pixels('new'))
  expect(current.current?.pixels.source).toBe('new')
})
it.each([false, true])(
  'retains ready landscape while loading and releases superseded pixels with reduced motion %s',
  async (reduced) => {
    controller.setReducedMotion(reduced)
    const first = pixels('first'),
      stale = pixels('stale'),
      chosen = pixels('chosen')
    await select(1)
    await complete(first)
    await select(2)
    const obsolete = images.at(-1)!
    expect(current.current?.pixels).toBe(first)
    await select(3)
    expect(obsolete.signal.aborted).toBe(true)
    obsolete.finish(stale)
    await flush()
    expect(stale.dispose).toHaveBeenCalledOnce()
    await complete(chosen)
    expect(current.current?.pixels).toBe(chosen)
    expect(first.dispose).toHaveBeenCalledTimes(reduced ? 1 : 0)
    controller.dispose()
    expect(first.dispose).toHaveBeenCalledOnce()
    expect(chosen.dispose).toHaveBeenCalledOnce()
    expect(current.current).toBeNull()
  },
)
it.each([
  [false, false],
  [false, true],
  [true, false],
  [true, true],
])(
  'a ready selection preserves the visible blend with reduced motion %s and missing replacement %s',
  async (reduced, missing) => {
    const first = pixels('first'),
      second = pixels('second'),
      third = pixels('third'),
      fourth = pixels('fourth')
    await select(1)
    await complete(first)
    await select(2)
    await complete(second)
    frame(1000)
    frame(1090)
    expect(current.progress).toBe(0.5)
    await select(3)
    await complete(third)
    expect(current.current?.pixels).toBe(second)
    expect(current.outgoing?.pixels).toBe(first)
    expect(current.progress).toBe(0.5)
    await select(4)
    await complete(missing ? null : fourth)
    expect(third.dispose).toHaveBeenCalledOnce()
    expect(current.current?.pixels).toBe(second)
    if (reduced) controller.setReducedMotion(true)
    else frame(1180)
    expect(first.dispose).toHaveBeenCalledOnce()
    if (missing) {
      expect(current.current).toBeNull()
      expect(second.dispose).toHaveBeenCalledOnce()
    } else {
      expect(current.current?.pixels).toBe(fourth)
      expect(current.outgoing?.pixels ?? null).toBe(reduced ? null : second)
      expect(current.progress).toBe(reduced ? 1 : 0)
      if (!reduced) {
        frame(1200)
        frame(1380)
      }
      expect(second.dispose).toHaveBeenCalledOnce()
    }
    controller.dispose()
  },
)
it.each([
  [false, 6, 3840],
  [true, 6, 3840],
  [false, 16 / 9, 1920],
  [true, 16 / 9, 1920],
] as const)(
  'sizes each source candidate independently with fullscreen %s and ratio %s',
  async (fullscreen, ratio, expected) => {
    controller.setReducedMotion(!fullscreen)
    controller.select(1, { ...size, fullscreen })
    reads.at(-1)!.finish({ candidates: [landscape('art', ratio), landscape('shot')], coverKey: null })
    await flush()
    expect(images.at(-1)).toMatchObject({ key: 'igdb-backdrop:art', width: expected })
    await complete(null)
    expect(images.at(-1)).toMatchObject({ key: 'igdb-backdrop:shot', width: 1920 })
    controller.dispose()
    expect(images.every((image) => image.signal.aborted)).toBe(true)
  },
)
it('desktop exhausts high-resolution hero artwork screenshot standard hero and portrait in source order', async () => {
  controller.setReducedMotion(true)
  controller.select(1, { ...size, fullscreen: false })
  const standard = { ...hero, key: { provider: 'steam-hero-standard', id: '42' } }
  reads
    .at(-1)!
    .finish({
      candidates: [hero, landscape('art'), landscape('shot'), standard],
      coverKey: { provider: 'steam', id: '42' },
    })
  await flush()
  for (const [key, width] of [
    ['steam-hero:42', 3840],
    ['igdb-backdrop:art', 1920],
    ['igdb-backdrop:shot', 1920],
    ['steam-hero-standard:42', 3840],
    ['steam:42', 1920],
  ] as const) {
    expect(images.at(-1)).toMatchObject({ key, width })
    await complete(null)
  }
  expect(current).toMatchObject({ current: null, outgoing: null, loading: false })
  controller.dispose()
  expect(images.every((image) => image.signal.aborted)).toBe(true)
})
it('tries ranked artwork then screenshots before a separately sized cover fallback', async () => {
  await select(1, [landscape('art', 6), landscape('shot')], { provider: 'igdb', id: 'portrait' })
  expect(images[0]).toMatchObject({ key: 'igdb-backdrop:art', width: 3840 })
  await complete(null)
  expect(images[1]).toMatchObject({ key: 'igdb-backdrop:shot', width: 1920 })
  expect(current.current).toBeNull()
  await complete(null)
  expect(images[2]).toMatchObject({ key: 'igdb:portrait', width: 1920 })
  const cover = pixels('cover')
  await complete(cover)
  expect(current.current?.fallback).toBe(true)
  controller.dispose()
  expect(cover.dispose).toHaveBeenCalledOnce()
})
it('failed resolution upgrades keep the usable image and smaller sizes do not request it again', async () => {
  const art = pixels('art')
  await select(1)
  await complete(art)
  controller.select(1, { ...size, width: 3840, height: 2160 })
  reads.at(-1)!.finish({ candidates: [landscape('1')], coverKey: null })
  await flush()
  expect(images.at(-1)!.width).toBe(3840)
  await complete(null)
  expect(current.current?.pixels).toBe(art)
  expect(art.dispose).not.toHaveBeenCalled()
  controller.select(1, { ...size, width: 1280, height: 720 })
  reads.at(-1)!.finish({ candidates: [landscape('1')], coverKey: null })
  await flush()
  expect(images).toHaveLength(2)
  controller.dispose()
})
it('an explicit artwork invalidation reloads changed bytes even when the selected key and bucket are unchanged', async () => {
  const before = pixels('before'),
    after = pixels('after')
  await select(1)
  await complete(before)
  controller.select(1, size, true)
  reads.at(-1)!.finish({ candidates: [landscape('1')], coverKey: null })
  await flush()
  expect(images).toHaveLength(2)
  expect(current.current?.pixels).toBe(before)
  await complete(after)
  expect(current.current?.pixels).toBe(after)
  expect(before.dispose).toHaveBeenCalledOnce()
  controller.dispose()
})
it('detach cancels pending art and releases current outgoing queued and late resources exactly once', async () => {
  const first = pixels('first'),
    second = pixels('second'),
    ready = pixels('ready'),
    late = pixels('late')
  await select(1)
  await complete(first)
  await select(2)
  await complete(second)
  await select(3)
  await complete(ready)
  controller.dispose()
  for (const art of [first, second, ready]) expect(art.dispose).toHaveBeenCalledOnce()
  expect(frames.size).toBe(0)
  expect(reads.at(-1)!.signal.aborted).toBe(true)
  controller.dispose()
  expect(first.dispose).toHaveBeenCalledOnce()
  // A separate pending instance also ignores a downloader that does not honour cancellation.
  const pendingController = new AvalonBackdropController({
    resolve: async () => ({ candidates: [landscape('pending')], coverKey: null }),
    image: () => Promise.resolve(late),
    publish: vi.fn(),
    requestFrame: vi.fn(),
    cancelFrame: vi.fn(),
  })
  pendingController.select(1, size)
  await Promise.resolve()
  pendingController.dispose()
  await flush()
  expect(late.dispose).toHaveBeenCalledOnce()
})
it('changing to reduced motion finishes the blend and presents its queued replacement without another transition', async () => {
  const art = [pixels('one'), pixels('two'), pixels('three')]
  for (let index = 0; index < art.length; index++) {
    await select(index + 1)
    await complete(art[index])
  }
  controller.setReducedMotion(true)
  expect(current.current?.pixels).toBe(art[2])
  expect(current.outgoing).toBeNull()
  expect(current.progress).toBe(1)
  expect(art[0].dispose).toHaveBeenCalledOnce()
  expect(art[1].dispose).toHaveBeenCalledOnce()
  controller.dispose()
})
it.each([false, true])(
  'Steam hero geometry and decode width stay independent of landscape layers with fullscreen %s',
  (fullscreen) => {
    const narrow = { ...size, fullscreen },
      wide = { ...narrow, width: 2520 },
      extreme = { ...narrow, width: 3840 }
    expect(backdropWidth(hero, narrow)).toBe(3840)
    expect(backdropGeometry(hero, narrow)).toMatchObject({ width: 1920, height: 1080, fitted: false })
    expect(backdropWidth(hero, wide)).toBe(fullscreen ? 2560 : 3840)
    expect(backdropGeometry(hero, wide)).toMatchObject({
      width: 2520,
      height: fullscreen ? 813.75 : 1080,
      fitted: fullscreen,
    })
    expect(backdropGeometry(landscape('shot'), wide)).toEqual({
      width: 2520,
      height: 1080,
      left: 0,
      fitted: false,
    })
    expect(backdropGeometry(hero, extreme).width).toBeCloseTo(fullscreen ? 3344.516129 : 3840, 5)
    expect(backdropGeometry(hero, extreme).left).toBeCloseTo(fullscreen ? 247.741935 : 0, 5)
    expect(backdropWidth(hero, extreme)).toBe(3840)
  },
)
