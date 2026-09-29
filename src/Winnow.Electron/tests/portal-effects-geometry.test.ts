import { describe, expect, it } from 'vitest'
import {
  DEFAULT_PORTAL_OPTIONS,
  normalizePortalOptions,
  portalActivityRate,
  portalContour,
  portalCoversSurface,
  portalGeometry,
} from '../src/renderer/components/portal-effects/geometry'

describe('portal geometry', () => {
  it('starts at the source aperture instead of changing the dimensions of the reading plane', () => {
    const source = { x: 570, y: 210, width: 400, height: 540 }
    const start = portalGeometry(1280, 720, 0, DEFAULT_PORTAL_OPTIONS, 0, undefined, source)
    expect(start.center).toEqual({ x: 770, y: 480 })
    expect(start.radius).toEqual({ x: 192, y: 262 })
    const halfway = portalGeometry(1280, 720, 0.5, DEFAULT_PORTAL_OPTIONS, 0, undefined, source)
    expect(halfway.radius.x).toBeGreaterThan(start.radius.x)
    expect(halfway.center.x).toBeLessThan(start.center.x)
  })

  it.each([
    [390, 600],
    [1280, 720],
    [1920, 960],
    [2560, 1440],
  ])(
    'clears all four corners at completion on a %s × %s plane, including the most rounded active edge',
    (width, height) => {
      const shape = portalGeometry(
        width,
        height,
        1,
        { roundness: 100, waviness: 100, activity: 100 },
        45,
        undefined,
        { x: 80, y: 40, width: 160, height: 240 },
      )
      const polygon = portalContour(shape)
      const inside = (x: number, y: number) => {
        let contained = false
        for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
          const a = polygon[i],
            b = polygon[j]
          if (a.y > y !== b.y > y && x < ((b.x - a.x) * (y - a.y)) / (b.y - a.y) + a.x) contained = !contained
        }
        return contained
      }
      expect(
        [
          [0, 0],
          [width, 0],
          [0, height],
          [width, height],
        ].every(([x, y]) => inside(x, y)),
      ).toBe(true)
    },
  )

  it('grows from a cursor beyond the side of the final panel without moving its resting art plane', () => {
    const start = portalGeometry(400, 540, 0, DEFAULT_PORTAL_OPTIONS, 0, { x: -30, y: 200 })
    const end = portalGeometry(400, 540, 1, DEFAULT_PORTAL_OPTIONS, 0, { x: -30, y: 200 })
    expect(start.center).toEqual({ x: -30, y: 200 })
    expect(end.center).toEqual({ x: 200, y: 270 })
  })

  it.each([
    [390, 600],
    [1280, 720],
    [3440, 1440],
  ])(
    'only retires the rim after the complete %s × %s perimeter is safely inside the aperture',
    (width, height) => {
      for (const roundness of [0, 70, 100]) {
        const options = { roundness, waviness: 100, activity: 100 }
        const source = { x: -80, y: height - 100, width: 400, height: 540 }
        expect(
          portalCoversSurface(portalGeometry(width, height, 0, options, 0, undefined, source), width, height),
        ).toBe(false)
        let covered = false
        for (let step = 1; step <= 40; step++) {
          const geometry = portalGeometry(width, height, step / 40, options, step / 10, undefined, source)
          if (!portalCoversSurface(geometry, width, height)) continue
          covered = true
          const polygon = portalContour(geometry)
          for (let sample = 0; sample <= 20; sample++) {
            const fraction = sample / 20
            for (const [x, y] of [
              [fraction * width, 0],
              [fraction * width, height],
              [0, fraction * height],
              [width, fraction * height],
            ]) {
              let inside = false
              for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
                const a = polygon[i],
                  b = polygon[j]
                if (a.y > y !== b.y > y && x < ((b.x - a.x) * (y - a.y)) / (b.y - a.y) + a.x) inside = !inside
              }
              expect(inside).toBe(true)
            }
          }
        }
        // Small panes can reach the duration limit before the complete halo clears.
        if (width >= 1280) expect(covered).toBe(true)
      }
    },
  )

  it('stops at zero, reproduces the original rate at forty and clamps malformed settings', () => {
    expect(portalActivityRate(0)).toBe(0)
    expect(portalActivityRate(40)).toBe(1)
    expect(portalActivityRate(100)).toBeCloseTo(3.9528, 4)
    expect(normalizePortalOptions({ roundness: Infinity, waviness: -30, activity: 1000 })).toEqual({
      roundness: 70,
      waviness: 0,
      activity: 100,
    })
  })
})
