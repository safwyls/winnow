// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { loadBackdropImage } from '../src/renderer/themes/avalon-backdrop-image'
import type { WinnowBridge } from '../src/shared/bridge'

const key = { provider: 'fixture', id: 'wide' }
const encoded = 'data:image/png;base64,AQID'
let decode: Promise<void>
let image: { src: string; decode(): Promise<void>; naturalWidth: number; naturalHeight: number }
beforeEach(() => {
  decode = Promise.resolve()
  image = { src: '', decode: () => decode, naturalWidth: 3840, naturalHeight: 1240 }
  vi.stubGlobal(
    'Image',
    class {
      constructor() {
        return image
      }
    },
  )
  URL.createObjectURL = vi.fn(() => 'blob:fixture')
  URL.revokeObjectURL = vi.fn()
  window.winnow = {
    artwork: vi.fn(async () => encoded),
    cancelRequest: vi.fn(async () => true),
  } as unknown as WinnowBridge
})
afterEach(() => vi.unstubAllGlobals())
it('holds one decoded URL per lease and releases its image and URL exactly once', async () => {
  const asset = await loadBackdropImage(key, 3840, new AbortController().signal)
  expect(asset).toMatchObject({ source: 'blob:fixture', width: 3840, height: 1240 })
  expect(window.winnow.artwork).toHaveBeenCalledWith(
    'fixture',
    'wide',
    3840,
    expect.stringMatching(/^[a-f0-9]{32}$/),
  )
  expect(URL.revokeObjectURL).not.toHaveBeenCalled()
  asset!.dispose()
  asset!.dispose()
  expect(image.src).toBe('')
  expect(URL.revokeObjectURL).toHaveBeenCalledExactlyOnceWith('blob:fixture')
})
it('cancels the named image request and never decodes a late response', async () => {
  let finish!: (value: string) => void
  vi.mocked(window.winnow.artwork).mockReturnValue(
    new Promise((resolve) => {
      finish = resolve
    }),
  )
  const controller = new AbortController()
  const result = loadBackdropImage(key, 1920, controller.signal)
  const rejected = expect(result).rejects.toMatchObject({ name: 'AbortError' })
  controller.abort()
  expect(window.winnow.cancelRequest).toHaveBeenCalledWith(vi.mocked(window.winnow.artwork).mock.calls[0][3])
  finish(encoded)
  await rejected
  expect(URL.createObjectURL).not.toHaveBeenCalled()
})
it('releases a pending decoder on cancellation and cannot return its eventual completion', async () => {
  let finish!: () => void
  decode = new Promise((resolve) => {
    finish = resolve
  })
  const controller = new AbortController()
  const result = loadBackdropImage(key, 1920, controller.signal)
  await Promise.resolve()
  expect(image.src).toBe('blob:fixture')
  controller.abort()
  expect(image.src).toBe('')
  finish()
  expect(await result).toBeNull()
  expect(URL.revokeObjectURL).toHaveBeenCalledOnce()
})
it.each(['decode', 'dimensions', 'missing'])(
  'cleans up unusable images after %s failure',
  async (failure) => {
    if (failure === 'decode')
      image.decode = async () => {
        throw Error('Invalid image')
      }
    if (failure === 'dimensions') image.naturalWidth = 0
    if (failure === 'missing') vi.mocked(window.winnow.artwork).mockResolvedValue(null)
    expect(await loadBackdropImage(key, 1920, new AbortController().signal)).toBeNull()
    expect(URL.revokeObjectURL).toHaveBeenCalledTimes(failure === 'missing' ? 0 : 1)
  },
)
