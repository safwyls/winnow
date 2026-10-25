// @vitest-environment jsdom
import { act, cleanup, fireEvent, render } from '@testing-library/react'
import { useRef } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ArtworkEffects, ArtworkEffectsProvider } from '../src/renderer/components/artwork-effects'
import { DEFAULT_ARTWORK_EFFECTS } from '../src/shared/artworkEffects'
import type { ArtworkRenderer } from '../src/renderer/components/artwork-effects/renderer'

const factory = vi.hoisted(() => vi.fn())
vi.mock('../src/renderer/components/artwork-effects/renderer', () => ({ createArtworkRenderer: factory }))

function runtime() {
  const canvas = document.createElement('canvas')
  canvas.className = 'winnow-artwork-canvas'
  return {
    get attached() {
      return canvas.isConnected
    },
    attach: vi.fn<ArtworkRenderer['attach']>((surface) => surface.append(canvas)),
    draw: vi.fn<ArtworkRenderer['draw']>(),
    detach: vi.fn(() => canvas.remove()),
    destroy: vi.fn(() => canvas.remove()),
  }
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
function Card({ name = 'First', effects }: { name?: string; effects?: false }) {
  const ref = useRef<HTMLButtonElement>(null)
  return (
    <button ref={ref} aria-label={name}>
      <ArtworkEffects interactionRef={ref} effects={effects}>
        <img src={`data:image/png,${name}`} alt="" />
      </ArtworkEffects>
    </button>
  )
}
function pointer(button: HTMLElement, x = 190, y = 50) {
  fireEvent.pointerMove(button, { clientX: x, clientY: y, pointerType: 'mouse' })
}
async function settle() {
  await act(async () => {
    await Promise.resolve()
    await Promise.resolve()
  })
  await act(async () => {
    vi.advanceTimersByTime(1500)
  })
}

let renderer: ReturnType<typeof runtime>
let reduce = false
let mediaListener: (() => void) | undefined
beforeEach(() => {
  vi.useFakeTimers()
  reduce = false
  renderer = runtime()
  factory.mockReset().mockResolvedValue(renderer)
  vi.stubGlobal('PointerEvent', MouseEvent)
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({
      get matches() {
        return reduce
      },
      addEventListener: (_: string, listener: () => void) => {
        mediaListener = listener
      },
      removeEventListener: vi.fn(),
    })),
  )
  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(200)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(300)
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
    x: 0,
    y: 0,
    left: 0,
    top: 0,
    width: 200,
    height: 300,
    right: 200,
    bottom: 300,
    toJSON: () => ({}),
  })
  vi.spyOn(HTMLImageElement.prototype, 'complete', 'get').mockReturnValue(true)
  vi.spyOn(HTMLImageElement.prototype, 'naturalWidth', 'get').mockReturnValue(600)
  vi.spyOn(HTMLImageElement.prototype, 'naturalHeight', 'get').mockReturnValue(900)
})
afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('reusable artwork effects', () => {
  it('loads lazily, reuses decoded DOM images, shares one canvas and stops rendering after settling', async () => {
    const view = render(
      <>
        <Card />
        <Card name="Second" />
      </>,
    )
    expect(factory).not.toHaveBeenCalled()
    const first = view.getByRole('button', { name: 'First' })
    pointer(first)
    await settle()
    expect(factory).toHaveBeenCalledTimes(1)
    expect(renderer.attach.mock.calls[0][1]).toBe(first.querySelector('img'))
    expect(document.querySelectorAll('canvas')).toHaveLength(1)
    const renders = renderer.draw.mock.calls.length
    act(() => vi.advanceTimersByTime(5000))
    expect(renderer.draw).toHaveBeenCalledTimes(renders)
    const second = view.getByRole('button', { name: 'Second' })
    pointer(second)
    await settle()
    expect(factory).toHaveBeenCalledTimes(1)
    expect(document.querySelectorAll('canvas')).toHaveLength(1)
    expect(second.querySelector('canvas')).not.toBeNull()
    expect(first.querySelector('[data-artwork-floating]')).toBeNull()
    view.unmount()
    expect(renderer.destroy).toHaveBeenCalledTimes(1)
    expect(document.querySelectorAll('canvas')).toHaveLength(0)
  })

  it('does not mount a late initialization after a virtualized card unmounts', async () => {
    const pending = deferred<ArtworkRenderer>()
    factory.mockReturnValueOnce(pending.promise)
    const view = render(<Card />)
    pointer(view.getByRole('button'))
    await act(async () => {
      await Promise.resolve()
      await Promise.resolve()
    })
    view.unmount()
    await act(async () => pending.resolve(renderer))
    expect(renderer.attach).not.toHaveBeenCalled()
    expect(renderer.destroy).toHaveBeenCalledTimes(1)
  })

  it('only mounts the latest hovered card when initialization finishes', async () => {
    const pending = deferred<ArtworkRenderer>()
    factory.mockReturnValueOnce(pending.promise)
    const view = render(
      <>
        <Card />
        <Card name="Second" />
      </>,
    )
    pointer(view.getByRole('button', { name: 'First' }))
    pointer(view.getByRole('button', { name: 'Second' }))
    await act(async () => pending.resolve(renderer))
    await settle()
    expect(renderer.attach).toHaveBeenCalledTimes(1)
    expect(view.getByRole('button', { name: 'Second' }).querySelector('canvas')).not.toBeNull()
  })

  it('disposes pending initialization before allocating a replacement theme renderer', async () => {
    const pending = deferred<ArtworkRenderer>()
    const replacement = runtime()
    factory.mockReturnValueOnce(pending.promise).mockImplementationOnce(() => {
      expect(renderer.destroy).toHaveBeenCalledTimes(1)
      return Promise.resolve(replacement)
    })
    const first = render(<Card />)
    pointer(first.getByRole('button'))
    await act(async () => {
      await Promise.resolve()
      await Promise.resolve()
    })
    first.unmount()
    const second = render(<Card name="New theme" />)
    pointer(second.getByRole('button'))
    await act(async () => {
      await Promise.resolve()
      await Promise.resolve()
    })
    expect(factory).toHaveBeenCalledTimes(1)
    await act(async () => pending.resolve(renderer))
    await settle()
    expect(factory).toHaveBeenCalledTimes(2)
    expect(replacement.attach).toHaveBeenCalledTimes(1)
    expect(document.querySelectorAll('canvas')).toHaveLength(1)
  })

  it('activates an already focused card after its lazy image finishes loading', async () => {
    const loaded = vi.spyOn(HTMLImageElement.prototype, 'naturalWidth', 'get').mockReturnValue(0)
    const view = render(<Card />)
    act(() => view.getByRole('button').focus())
    expect(factory).not.toHaveBeenCalled()
    loaded.mockReturnValue(600)
    fireEvent.load(view.container.querySelector('img')!)
    await settle()
    expect(factory).toHaveBeenCalledTimes(1)
    expect(document.querySelector<HTMLElement>('[data-artwork-active]')?.dataset.artworkInput).toBe(
      'keyboard',
    )
  })

  it.each(['keyboard', 'profile', 'system', 'follow off'] as const)(
    'uses a level, stationary raised card for %s',
    async (mode) => {
      reduce = mode === 'system'
      const options = { ...DEFAULT_ARTWORK_EFFECTS, followPointer: mode !== 'follow off' }
      const view = render(
        <ArtworkEffectsProvider options={options} reducedMotion={mode === 'profile'}>
          <Card />
        </ArtworkEffectsProvider>,
      )
      const button = view.getByRole('button')
      if (mode === 'keyboard') act(() => button.focus())
      else pointer(button)
      await settle()
      const surface = button.querySelector<HTMLElement>('.winnow-artwork-effects')!
      expect(surface.dataset.artworkFloating).toBe('true')
      expect(surface.style.getPropertyValue('--artwork-tilt-x')).toBe('0deg')
      expect(surface.style.getPropertyValue('--artwork-tilt-y')).toBe('0deg')
      expect(surface.dataset.artworkMotion).toBe('still')
      expect(renderer.draw).toHaveBeenCalledTimes(1)
      expect(renderer.draw.mock.calls[0][3]).toBe('still')
    },
  )

  it('clears active work when system motion changes, then applies the new preference', async () => {
    const view = render(
      <ArtworkEffectsProvider options={DEFAULT_ARTWORK_EFFECTS} reducedMotion={false}>
        <Card />
      </ArtworkEffectsProvider>,
    )
    pointer(view.getByRole('button'))
    await settle()
    act(() => {
      reduce = true
      mediaListener?.()
    })
    expect(document.querySelector('[data-artwork-active]')).toBeNull()
    const replacement = runtime()
    factory.mockResolvedValue(replacement)
    pointer(view.getByRole('button'))
    await settle()
    expect(document.querySelector<HTMLElement>('[data-artwork-active]')?.dataset.artworkMotion).toBe('still')
  })

  it('keeps depth and the original artwork when WebGL initialization fails', async () => {
    factory.mockRejectedValueOnce(new Error('WebGL unavailable'))
    const view = render(<Card />)
    pointer(view.getByRole('button'))
    await settle()
    const surface = view.container.querySelector<HTMLElement>('.winnow-artwork-effects')!
    expect(surface.dataset.artworkFloating).toBe('true')
    expect(surface.dataset.artworkLighting).toBe('unavailable')
    expect(surface.querySelector('img')).not.toBeNull()
    pointer(view.getByRole('button'), 50, 150)
    await settle()
    expect(factory).toHaveBeenCalledTimes(1)
    expect(document.querySelector('canvas')).toBeNull()
  })

  it('does not initialize effects for disabled, missing or undecoded artwork', async () => {
    const view = render(<Card effects={false} />)
    pointer(view.getByRole('button'))
    await settle()
    expect(factory).not.toHaveBeenCalled()
    view.rerender(<Card />)
    vi.spyOn(HTMLImageElement.prototype, 'naturalWidth', 'get').mockReturnValue(0)
    pointer(view.getByRole('button'))
    await settle()
    expect(factory).not.toHaveBeenCalled()
  })

  it.each(['scroll', 'blur', 'visibilitychange', 'resize'] as const)(
    'clears the active canvas and tilt on %s',
    async (event) => {
      const view = render(<Card />)
      pointer(view.getByRole('button'))
      await settle()
      fireEvent(event === 'blur' || event === 'resize' ? window : document, new Event(event))
      expect(document.querySelector('[data-artwork-active]')).toBeNull()
      expect(document.querySelector('canvas')).toBeNull()
      const renders = renderer.draw.mock.calls.length
      act(() => vi.advanceTimersByTime(2000))
      expect(renderer.draw).toHaveBeenCalledTimes(renders)
    },
  )

  it('cleans up an image replaced in place before the next renderer frame', async () => {
    const view = render(<Card />)
    pointer(view.getByRole('button'))
    await settle()
    view.rerender(<Card name="Replacement" />)
    await act(async () => {
      await Promise.resolve()
    })
    expect(document.querySelector('canvas')).toBeNull()
    expect(document.querySelector('[data-artwork-active]')).toBeNull()
  })

  it('restores a keyboard card after native focus scrolling brings it into view', async () => {
    const view = render(<Card />)
    const button = view.getByRole('button')
    const box = vi.spyOn(button, 'getBoundingClientRect')
    const bounds = {
      x: 0,
      y: 1000,
      left: 0,
      top: 1000,
      right: 200,
      bottom: 1300,
      width: 200,
      height: 300,
      toJSON: () => ({}),
    }
    box.mockReturnValue(bounds)
    act(() => button.focus())
    expect(factory).not.toHaveBeenCalled()
    box.mockReturnValue({ ...bounds, y: 60, top: 60, bottom: 360 })
    fireEvent.scroll(document)
    await settle()
    await settle()
    expect(button.querySelector('canvas')).not.toBeNull()
    expect(button.querySelector<HTMLElement>('[data-artwork-active]')?.dataset.artworkInput).toBe('keyboard')
    expect(renderer.draw.mock.calls.at(-1)?.[3]).toBe('still')
    fireEvent.scroll(document)
    await settle()
    await settle()
    expect(button.querySelector('canvas')).not.toBeNull()
    expect(factory).toHaveBeenCalledTimes(1)
  })

  it('does not retain a focused effect outside its subsection clipping bounds', async () => {
    const view = render(
      <div data-testid="scroll-section" style={{ overflowX: 'auto', overflowY: 'auto' }}>
        <Card />
      </div>,
    )
    const button = view.getByRole('button')
    const section = view.getByTestId('scroll-section')
    const bounds = {
      x: 0,
      y: 0,
      left: 0,
      top: 0,
      right: 200,
      bottom: 300,
      width: 200,
      height: 300,
      toJSON: () => ({}),
    }
    Object.defineProperty(section, 'getBoundingClientRect', {
      value: vi.fn(() => ({ ...bounds, bottom: 180, height: 180 })),
    })
    const box = vi.fn(() => bounds)
    Object.defineProperty(button, 'getBoundingClientRect', { value: box })
    act(() => button.focus())
    await settle()
    expect(button.querySelector('canvas')).not.toBeNull()
    box.mockReturnValue({ ...bounds, y: 250, top: 250, bottom: 550 })
    fireEvent.scroll(section)
    expect(button.querySelector('canvas')).toBeNull()
    await settle()
    expect(document.activeElement).toBe(button)
    expect(button.querySelector('canvas')).toBeNull()
    expect(button.querySelector('[data-artwork-active]')).toBeNull()
  })

  it('navigation keys restore keyboard treatment without requiring another focus event', async () => {
    const view = render(<Card />)
    const button = view.getByRole('button')
    act(() => button.focus())
    await settle()
    pointer(button)
    await settle()
    expect(button.querySelector<HTMLElement>('[data-artwork-active]')?.dataset.artworkInput).toBe('pointer')
    fireEvent.keyDown(button, { key: 'ArrowRight' })
    await settle()
    await settle()
    expect(document.activeElement).toBe(button)
    expect(button.querySelector<HTMLElement>('[data-artwork-active]')?.dataset.artworkInput).toBe('keyboard')
    fireEvent.pointerLeave(button)
    expect(button.querySelector('[data-artwork-active]')).not.toBeNull()
  })
})
