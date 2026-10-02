import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  DragonRenderer,
  dragonDash,
  type DragonProgress,
  type DragonFrameScheduler,
} from '../src/renderer/startup/dragon-renderer'

class Frames implements DragonFrameScheduler {
  private id = 0
  callbacks = new Map<number, (time: number) => void>()
  frame = (callback: (time: number) => void) => {
    this.callbacks.set(++this.id, callback)
    return this.id
  }
  cancel = (id: number) => {
    this.callbacks.delete(id)
  }
  at(time: number) {
    const callbacks = [...this.callbacks.values()]
    this.callbacks.clear()
    callbacks.forEach((callback) => callback(time))
  }
}
function fixture() {
  vi.stubGlobal(
    'Path2D',
    class {
      constructor(public source: string) {}
    },
  )
  const context = {
      clearRect: vi.fn(),
      save: vi.fn(),
      scale: vi.fn(),
      translate: vi.fn(),
      fill: vi.fn(),
      setLineDash: vi.fn(),
      stroke: vi.fn(),
      restore: vi.fn(),
    },
    canvas = { width: 0, height: 0, getContext: () => context },
    frames = new Frames(),
    reports: DragonProgress[] = [],
    appearance = { ink: '#faebd7', glow: '#40e0d0', size: 100 },
    renderer = new DragonRenderer(
      canvas as unknown as OffscreenCanvas,
      {
        mark: 'M0 0L10 0L10 10Z',
        contours: [{ path: 'M0 0L10 0L10 10Z', length: 34.14 }],
      },
      appearance,
      (value) => reports.push(value),
      frames,
    )
  return { renderer, frames, reports, appearance, context, canvas }
}
afterEach(() => vi.unstubAllGlobals())
describe('owned dragon drawing', () => {
  it('keeps one frame callback and advances a full circuit from rendered time', () => {
    const { renderer, frames, reports } = fixture()
    renderer.start()
    renderer.start()
    expect(frames.callbacks.size).toBe(1)
    for (const time of [0, 450, 1800, 2250]) frames.at(time)
    expect(reports.map(({ elapsed, phase }) => [elapsed, phase])).toEqual([
      [0, 0],
      [450, 0.25],
      [1800, 0],
      [2250, 0.25],
    ])
    expect(frames.callbacks.size).toBe(1)
    renderer.stop()
  })
  it('color and size changes retain the completed circuit and frame count', () => {
    const { renderer, frames, reports, canvas, context } = fixture()
    renderer.start()
    frames.at(1000)
    frames.at(3250)
    renderer.update({ ink: '#ffffff', glow: '#008080', size: 140 })
    expect(frames.callbacks.size).toBe(1)
    frames.at(3475)
    expect(reports.at(-1)).toEqual({
      ink: '#ffffff',
      glow: '#008080',
      size: 140,
      elapsed: 2475,
      phase: 0.375,
      frames: 3,
    })
    expect(canvas.width).toBe(140)
    expect(canvas.height).toBe(140)
    expect(context.scale).toHaveBeenLastCalledWith(0.25, 0.25)
    renderer.stop()
  })
  it('disable and detach stop queued drawing, and a restarted presentation owns a fresh clock', () => {
    const { renderer, frames, reports, context } = fixture()
    renderer.start()
    frames.at(0)
    frames.at(450)
    renderer.stop()
    expect(frames.callbacks.size).toBe(0)
    frames.at(900)
    expect(reports).toHaveLength(2)
    expect(context.clearRect).toHaveBeenCalledTimes(2)
    renderer.start()
    frames.at(1000)
    frames.at(1450)
    expect(reports.at(-1)).toMatchObject({ elapsed: 450, phase: 0.25, frames: 2 })
    renderer.stop()
    frames.at(1700)
    expect(frames.callbacks.size).toBe(0)
    expect(context.clearRect).toHaveBeenCalledTimes(4)
  })
  it('fails explicitly when an offscreen context cannot be created', () => {
    expect(
      () =>
        new DragonRenderer(
          { getContext: () => null } as unknown as OffscreenCanvas,
          { mark: '', contours: [] },
          { ink: '#fff', glow: '#0ff', size: 100 },
          vi.fn(),
          new Frames(),
        ),
    ).toThrow('could not be drawn')
  })
  it.each([0, 0.01, 0.12, 0.13, 0.5, 0.99, 1])(
    'the dash at phase %s ends at its moving head with a thirteen percent trail',
    (phase) => {
      const length = 937.123,
        { pattern, offset } = dragonDash(length, phase)
      const painted: [number, number][] = []
      // Independently expand Canvas's periodic dash pattern and clip it to one closed contour.
      for (let repeat = -2; repeat <= 2; repeat++) {
        const start = repeat * (pattern[0] + pattern[1]) - offset,
          a = Math.max(0, start),
          b = Math.min(length, start + pattern[0])
        if (b > a) painted.push([a, b])
      }
      expect(painted.reduce((total, [a, b]) => total + b - a, 0)).toBeCloseTo(length * 0.13, 8)
      const head = phase === 0 ? length : phase * length
      expect(Math.min(...painted.map(([, b]) => Math.abs(b - head)))).toBeLessThan(1e-8)
    },
  )
})
