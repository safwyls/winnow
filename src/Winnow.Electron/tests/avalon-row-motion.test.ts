import { describe, expect, it } from 'vitest'
import { AvalonRowMotion, desktopShelfWidth, homeRowHeight } from '../src/renderer/themes/avalon-row-motion'

function viewport(count = 1000, visible = 2) {
  let callbacks: Array<(time: number) => void> = []
  const model = new AvalonRowMotion((callback) => callbacks.push(callback))
  model.configure(count, visible)
  model.attach()
  model.resize(800, 600)
  return {
    model,
    frame(time: number) {
      const pending = callbacks
      callbacks = []
      pending.forEach((callback) => callback(time))
    },
  }
}

describe('Avalon retained row motion', () => {
  it('keeps outgoing and incoming rows through a 220ms opaque translation', () => {
    const { model, frame } = viewport()
    model.show(1)
    expect(model.snapshot()).toMatchObject({ first: 1, offset: 0, animating: true, realized: [0, 1, 2, 3] })
    frame(1000)
    frame(1110)
    expect(model.snapshot().offset).toBeGreaterThan(0)
    expect(model.snapshot().offset).toBeLessThan(1)
    frame(1220)
    expect(model.snapshot()).toMatchObject({ first: 1, offset: 1, animating: false, realized: [0, 1, 2, 3] })
  })
  it('reverses from the current position and ignores superseded frame callbacks', () => {
    const { model, frame } = viewport()
    model.show(1)
    frame(1000)
    frame(1090)
    const position = model.snapshot().offset
    model.show(0)
    expect(model.snapshot().offset).toBe(position)
    frame(2000)
    expect(model.snapshot().offset).toBe(position)
    frame(2110)
    expect(model.snapshot().offset).toBeGreaterThan(0)
    expect(model.snapshot().offset).toBeLessThan(position)
    frame(2220)
    expect(model.snapshot()).toMatchObject({ first: 0, offset: 0, animating: false })
  })
  it('bounds realization during rapid navigation and clamps long jumps', () => {
    const { model, frame } = viewport()
    model.show(900)
    expect(model.snapshot()).toMatchObject({
      first: 900,
      offset: 900,
      animating: false,
      realized: [899, 900, 901, 902],
    })
    for (let index = 901; index < 970; index++) {
      model.show(index)
      expect(model.snapshot().realized.length).toBeLessThanOrEqual(7)
    }
    frame(1000)
    frame(1220)
    expect(model.snapshot().realized).toHaveLength(4)
    model.show(Number.MAX_SAFE_INTEGER)
    expect(model.snapshot().first).toBe(998)
    model.show(-100)
    expect(model.snapshot().first).toBe(0)
  })
  it('snaps on reduced motion and viewport resize, then allows motion again', () => {
    const { model, frame } = viewport()
    model.show(1)
    frame(1000)
    frame(1080)
    model.reduce(true)
    expect(model.snapshot()).toMatchObject({ first: 1, offset: 1, animating: false })
    model.show(2)
    expect(model.snapshot()).toMatchObject({ first: 2, offset: 2, animating: false })
    model.reduce(false)
    model.show(3)
    expect(model.snapshot().animating).toBe(true)
    model.resize(800, 500)
    expect(model.snapshot()).toMatchObject({ first: 3, offset: 3, animating: false })
    frame(5000)
    expect(model.snapshot().offset).toBe(3)
  })
  it('releases rows on detach and ignores scheduled frames before reattachment', () => {
    const { model, frame } = viewport()
    model.show(1)
    frame(1000)
    frame(1100)
    model.detach()
    frame(1220)
    expect(model.snapshot()).toMatchObject({ first: 1, offset: 1, animating: false, realized: [] })
    model.attach()
    expect(model.snapshot()).toMatchObject({ first: 1, offset: 1, animating: false, realized: [0, 1, 2, 3] })
  })
  it('replaces data without retaining outgoing rows and handles an empty result', () => {
    const { model } = viewport()
    model.show(1)
    model.configure(1, 2)
    expect(model.snapshot()).toEqual({ first: 0, offset: 0, animating: false, realized: [0] })
    model.configure(0, 2)
    model.show(20)
    expect(model.snapshot()).toEqual({ first: 0, offset: 0, animating: false, realized: [] })
  })
  it('appends deferred shelves without moving the selected row', () => {
    const { model } = viewport(1, 1)
    model.append(6)
    expect(model.snapshot()).toEqual({ first: 0, offset: 0, animating: false, realized: [0, 1] })
    model.show(2, false)
    expect(model.snapshot()).toEqual({ first: 2, offset: 2, animating: false, realized: [1, 2, 3] })
  })
})

describe('original desktop five-slot shelf geometry', () => {
  it.each([
    [500, 180],
    [888, 180],
    [972, 180],
    [1072, 200],
    [1272, 240],
    [3112, 240],
  ])('bounds readable covers at viewport %s', (width, expected) => {
    expect(desktopShelfWidth(width)).toBe(expected)
  })
  it.each([972, 1100, 1272])('fills viewport %s within rounding until the cap', (width) => {
    const unused = width - (5 * desktopShelfWidth(width) + 4 * 18)
    expect(unused).toBeGreaterThanOrEqual(0)
    expect(unused).toBeLessThan(5)
  })
  it('overflows a narrow viewport instead of shrinking its five covers', () => {
    expect(5 * desktopShelfWidth(888) + 4 * 18).toBeGreaterThan(888)
    expect(desktopShelfWidth(888)).toBe(180)
  })
  it.each([Number.POSITIVE_INFINITY, Number.NaN, 0])(
    'uses finite readable covers before measurement (%s)',
    (width) => {
      expect(desktopShelfWidth(width)).toBe(180)
    },
  )
})

it('sizes Home from an unscaled reference instead of consuming extra zoomed-out height', () => {
  for (const margin of [0.05, 0.1]) {
    const canvas = 1080 * (1 - 2 * margin),
      width = 1920 * (1 - 2 * margin),
      chrome = 370
    const initial = homeRowHeight(width, canvas - chrome, canvas, 1)
    const smaller = homeRowHeight(width / 0.8, canvas / 0.8 - chrome, canvas, 0.8)
    expect(smaller).toBeCloseTo(initial, 6)
    expect((smaller * 0.8) / initial).toBeCloseTo(0.8, 6)
    expect(homeRowHeight(width / 1.2, canvas / 1.2 - chrome, canvas, 1.2)).toBeLessThanOrEqual(
      canvas / 1.2 - chrome,
    )
  }
})
