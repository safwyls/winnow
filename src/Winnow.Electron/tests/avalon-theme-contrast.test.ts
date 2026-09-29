import { describe, expect, it } from 'vitest'
import { AVALON_PALETTES, avalonPaletteStyle } from '../src/renderer/themes/avalon-palettes'
import { aaCeiling, avalonSurfaceTokens } from '../src/renderer/themes/avalon-translucency'

// Measure independently of the production color helpers, including byte compositing.
const rgb = (color: string) => [1, 3, 5].map((i) => Number.parseInt(color.slice(i, i + 2), 16))
const opacity = (color: string) => (color.length === 9 ? Number.parseInt(color.slice(7), 16) / 255 : 1)
const byte = (value: number) => (value % 1 === 0.5 ? 2 * Math.round(value / 2) : Math.round(value))
const color = (values: number[]) =>
  '#' +
  values
    .map((v) => byte(v).toString(16).padStart(2, '0'))
    .join('')
    .toUpperCase()
const composite = (ink: string, ground: string) =>
  color(rgb(ink).map((v, i) => v * opacity(ink) + rgb(ground)[i] * (1 - opacity(ink))))
const light = (value: string) =>
  rgb(value)
    .map((v) => v / 255)
    .map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4))
    .reduce((sum, v, i) => sum + v * [0.2126, 0.7152, 0.0722][i], 0)
const ratio = (ink: string, ground: string) =>
  (Math.max(light(ink), light(ground)) + 0.05) / (Math.min(light(ink), light(ground)) + 0.05)
const ceiling = (measure: (percent: number) => number) => {
  for (let p = 0; p <= 100; p++) if (measure(p) < 4.5) return Math.max(0, p - 1)
  return 100
}
const hue = (value: string) => {
  const [r, g, b] = rgb(value),
    max = Math.max(r, g, b),
    delta = max - Math.min(r, g, b)
  if (!delta) return 0
  const h = 60 * (max === r ? (g - b) / delta : max === g ? (b - r) / delta + 2 : (r - g) / delta + 4)
  return (h + 360) % 360
}
const hueGap = (a: string, b: string) => Math.min(Math.abs(hue(a) - hue(b)), 360 - Math.abs(hue(a) - hue(b)))
const white = '#FFFFFF',
  dark = '#201F1E'
const layouts = ['floating', 'flush'] as const

describe.each(AVALON_PALETTES.slice(0, 4).map((p) => [p.id, p] as const))(
  '%s original theme contrast',
  (_, palette) => {
    const c = palette.colors
    const tokens = (percent: number, wall = true, layout: 'floating' | 'flush' = 'flush') =>
      avalonSurfaceTokens(palette, percent, wall, layout)
    const surfaces = (percent: number, desktop = white, layout: 'floating' | 'flush' = 'flush') => {
      const t = tokens(percent, true, layout),
        shell = composite(t.ShellGround, desktop)
      return { t, shell, pane: composite(t.PaneGround, shell), rail: composite(t.ChromeSurface, shell) }
    }
    const chrome = ceiling((p) =>
      Math.min(
        ...layouts.flatMap((layout) => {
          const { t, shell, rail } = surfaces(p, white, layout)
          return [composite(t.CaptionFill, shell), rail, composite(t.ChromeRaised, rail)].map((fill) =>
            ratio(t.TextDim, fill),
          )
        }),
      ),
    )

    it('reserves Flare for unread updates with the original danger and selection hue separation', () => {
      for (const [role, value] of Object.entries(c))
        if (role !== '--avalon-flare') expect(value, role).not.toBe(c['--avalon-flare'])
      expect(hueGap(c['--avalon-flare'], c['--avalon-danger'])).toBeGreaterThanOrEqual(24)
      expect(hueGap(c['--avalon-flare'], c['--accent'])).toBeGreaterThanOrEqual(60)
    })

    it('keeps tiles opaque and store marks on the cover at 82 percent in both reach states and layouts', () => {
      for (let p = 0; p <= 100; p++)
        for (const wall of [false, true])
          for (const layout of layouts) {
            const t = tokens(p, wall, layout)
            expect(t.TileGround).toBe(c['--bg'] + 'FF')
            expect(t.SurfaceRaised).toBe(c['--raised'] + 'FF')
            expect(t.TileChipGround).toBe(c['--bg'] + 'D1')
            expect(opacity(t.TileChipGround) * 255).toBeGreaterThanOrEqual(200)
            expect(opacity(t.TileChipGround) * 255).toBeLessThanOrEqual(254)
          }
      expect(avalonPaletteStyle(palette)).toHaveProperty('--avalon-tile-chip-ground', c['--bg'] + 'D1')
    })

    it('opens the field only on request with monotone alpha, unchanged ground and the rail above it', () => {
      for (const layout of layouts) {
        let previous = 1
        for (let p = 0; p <= 100; p++) {
          const { t, shell, pane, rail } = surfaces(p, white, layout)
          expect(tokens(p, false, layout).WallGround).toBe(c['--bg'] + 'FF')
          expect(t.WallGround.slice(0, 7)).toBe(c['--bg'])
          expect(t.PaneGround).toBe(t.WallGround)
          expect(opacity(t.WallGround)).toBe(opacity(t.ChromeSurface))
          expect(opacity(t.WallGround)).toBeLessThanOrEqual(previous)
          expect(light(rail)).toBeGreaterThan(light(pane))
          previous = opacity(t.WallGround)
          if (p === 0) expect(previous).toBe(1)
          if (p >= 25)
            expect(
              Math.abs((1 - opacity(t.ShellGround)) * (1 - previous) - (0.35 * p) / 100),
            ).toBeLessThanOrEqual(0.006)
          expect(composite(t.WallGround, shell)).toBe(pane)
        }
        expect(opacity(tokens(100, true, layout).WallGround) * 255).toBe(150)
      }
    })

    it('keeps the open field below dormant art until the independently measured AA ceiling', () => {
      expect(aaCeiling(palette)).toBe(chrome)
      let polarity = 0
      while (polarity < 100 && light(surfaces(polarity + 1).pane) <= light('#2C3237')) polarity++
      expect(polarity).toBeGreaterThanOrEqual(chrome)
      for (let p = 0; p <= 100; p++)
        expect(light(surfaces(p, dark).pane)).toBeLessThanOrEqual(light('#2C3237'))
    })

    it('keeps pane prose, metadata and selected rows legible further than window chrome', () => {
      const measures = [
        (p: number) => ratio(c['--text'], surfaces(p).pane),
        (p: number) => ratio(tokens(p).TextDim, surfaces(p).pane),
        (p: number) => ratio(tokens(p).TextDim, composite(tokens(p).ChromeRaised, surfaces(p).pane)),
        (p: number) =>
          Math.min(
            ratio(tokens(p).TextDim, surfaces(p).rail),
            ratio(tokens(p).TextDim, composite(tokens(p).ChromeRaised, surfaces(p).rail)),
          ),
      ]
      for (const measure of measures) expect(ceiling(measure)).toBeGreaterThan(chrome)
      for (let p = 0; p <= 100; p++)
        expect(ratio(tokens(p).TextDim, surfaces(p, dark).pane)).toBeGreaterThanOrEqual(4.5)
    })

    it('admits the art field share through both inputs without walking their ink or increasing alpha', () => {
      for (const layout of layouts) {
        let previousGround = 1,
          previousSurface = 1
        for (let p = 0; p <= 100; p++) {
          const t = tokens(p, true, layout),
            ground = 1 - opacity(t.ShellGround)
          const wall = ground * (1 - opacity(t.WallGround))
          for (const [field, container, ink] of [
            [t.ChromeFieldOnGround, t.PaneGround, c['--surface']],
            [t.ChromeFieldOnSurface, t.ChromeSurface, c['--bg']],
          ]) {
            const share = ground * (1 - opacity(container)),
              through = share * (1 - opacity(field))
            expect(field.slice(0, 7)).toBe(ink)
            expect(through).toBeLessThanOrEqual(share + 0.005)
            expect(through).toBeLessThanOrEqual(wall + 0.005)
            if (p >= 25) expect(Math.abs(through - wall)).toBeLessThanOrEqual(0.006)
          }
          expect(opacity(t.ChromeFieldOnGround)).toBeLessThanOrEqual(previousGround)
          expect(opacity(t.ChromeFieldOnSurface)).toBeLessThanOrEqual(previousSurface)
          previousGround = opacity(t.ChromeFieldOnGround)
          previousSurface = opacity(t.ChromeFieldOnSurface)
          expect(tokens(p, false, layout).ChromeFieldOnGround).toBe(c['--surface'] + 'FF')
          if (p >= 25) expect(opacity(tokens(p, false, layout).ChromeFieldOnSurface)).toBeLessThan(1)
        }
        expect(tokens(0, true, layout).ChromeFieldOnGround).toBe(c['--surface'] + 'FF')
        expect(tokens(0, true, layout).ChromeFieldOnSurface).toBe(c['--bg'] + 'FF')
      }
    })

    it('keeps typed text, placeholders and focus rings legible beyond chrome on both input surfaces', () => {
      for (const onBar of [false, true]) {
        const fill = (p: number) => {
          const { t, pane, rail } = surfaces(p)
          return composite(onBar ? t.ChromeFieldOnGround : t.ChromeFieldOnSurface, onBar ? pane : rail)
        }
        const typed = ceiling((p) => ratio(c['--text'], fill(p)))
        if (onBar) expect(typed).toBe(100)
        else expect(typed).toBeGreaterThanOrEqual(96)
        expect(ceiling((p) => ratio(tokens(p).TextDim, fill(p)))).toBeGreaterThan(chrome)
        expect(ceiling((p) => ratio(c['--accent'], fill(p)))).toBeGreaterThan(chrome)
      }
    })

    it('holds all four text inks above AA over all 256 artwork greys at every slider position', () => {
      for (let p = 0; p <= 100; p++) {
        const t = tokens(p),
          inks = [c['--text'], t.TextDim, c['--cool'], c['--avalon-amber']]
        let minimum = Infinity
        for (let grey = 0; grey <= 255; grey++) {
          const field = composite(t.ArtVeil, color([grey, grey, grey]))
          const hovered = composite(t.SurfaceRaisedFaint, field)
          for (const ink of inks) minimum = Math.min(minimum, ratio(ink, field), ratio(ink, hovered))
        }
        expect(minimum, `transparency ${p}`).toBeGreaterThanOrEqual(4.5)
      }
    })

    it('uses the readable heading, primary prose and reception inks on both flat and brightest artwork surfaces', () => {
      const t = tokens(0),
        field = composite(t.ArtVeil, white)
      for (const fill of [c['--surface'], field]) {
        expect(ratio(t.TextDim, fill)).toBeGreaterThanOrEqual(4.5)
        expect(ratio(c['--text'], fill)).toBeGreaterThan(ratio(t.TextDim, fill))
        expect(ratio(t.TextFaint, fill)).toBeLessThan(4.5)
      }
    })

    it('recovers the exact flat surface with no artwork and publishes the original veil and lightbox fills', () => {
      for (let p = 0; p <= 100; p++) {
        const t = tokens(p)
        expect(composite(t.ArtVeil, c['--surface'])).toBe(c['--surface'])
        expect(t.ArtVeil).toBe(c['--surface'] + 'EB')
        expect(t.LightboxControlFill).toBe(c['--surface'] + 'B2')
        expect(t.LightboxControlActiveFill).toBe(c['--raised'] + 'D9')
        expect(t.SurfaceRaisedFaint).toBe(c['--raised'] + '14')
      }
      const style = avalonPaletteStyle(palette)
      expect(style).toHaveProperty('--avalon-art-veil', c['--surface'] + 'EB')
      expect(style).toHaveProperty('--avalon-lightbox-fill', c['--surface'] + 'B2')
      expect(style).toHaveProperty('--avalon-lightbox-active-fill', c['--raised'] + 'D9')
    })
  },
)

it('rejects one less veil step because Winnow metadata then drops below AA over white artwork', () => {
  const c = AVALON_PALETTES[0].colors
  expect(ratio(c['--muted'], composite(c['--surface'] + 'E8', white))).toBeLessThan(4.5)
  expect(ratio(c['--muted'], composite(c['--surface'] + 'EB', white))).toBeGreaterThanOrEqual(4.5)
})
