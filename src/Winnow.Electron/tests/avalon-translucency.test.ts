import { describe, expect, it, vi } from 'vitest'
import { AVALON_PALETTES } from '../src/renderer/themes/avalon-palettes'
import {
  aaCeiling,
  alpha,
  avalonAppearance,
  avalonSurfaceTokens,
  contrast,
  over,
  parseTransparency,
  worstMetadataContrast,
} from '../src/renderer/themes/avalon-translucency'
import {
  supportsWindowMaterial,
  WindowAppearanceController,
  type AppearanceEnvironment,
} from '../src/main/window-appearance'
import { parseWindowAppearance } from '../src/shared/windowAppearance'

const opacity = (color: string) => parseInt(color.slice(7, 9), 16) / 255
const light = (color: string) => {
  const values = [1, 3, 5]
    .map((offset) => parseInt(color.slice(offset, offset + 2), 16) / 255)
    .map((value) => (value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4))
  return values[0] * 0.2126 + values[1] * 0.7152 + values[2] * 0.0722
}
describe('Original two-tier translucency', () => {
  it.each([
    ['0', 0],
    ['55', 55],
    ['100', 100],
    ['240', 100],
    ['-5', 0],
    ['true', 25],
    ['True', 25],
    ['false', 0],
    ['', 0],
    [null, 0],
  ] as const)('migrates each original stored transparency fixture %s to %s', (value, expected) => {
    expect(parseTransparency(value)).toBe(expected)
  })
  it.each([
    ['acrylic', 'acrylic'],
    ['mica', 'mica'],
    ['none', 'acrylic'],
    ['blur-behind-2029', 'acrylic'],
    [null, 'acrylic'],
  ] as const)('resolves the original stored backdrop %s as %s', (Backdrop, expected) => {
    expect(avalonAppearance({ Backdrop }, AVALON_PALETTES[0], true).backdrop).toBe(expected)
  })
  it.each(AVALON_PALETTES.map((palette) => [palette.id, palette] as const))(
    '%s moves only shell and caption with layout and keeps the AA marker valid in either layout',
    (_, palette) => {
      const ceiling = aaCeiling(palette)
      for (let percent = 0; percent <= 100; percent++)
        for (const wall of [false, true]) {
          const flush = avalonSurfaceTokens(palette, percent, wall, 'flush')
          const floating = avalonSurfaceTokens(palette, percent, wall, 'floating')
          for (const key of Object.keys(flush) as (keyof typeof flush)[]) {
            if (key !== 'ShellGround' && key !== 'CaptionFill') expect(flush[key]).toBe(floating[key])
          }
          if (percent <= ceiling)
            for (const tokens of [flush, floating]) {
              const shell = over(tokens.ShellGround, '#FFFFFF'),
                rail = over(tokens.ChromeSurface, shell)
              for (const field of [
                over(tokens.CaptionFill, shell),
                rail,
                over(tokens.ChromeRaised, rail),
                over(tokens.PaneGround, shell),
              ])
                expect(contrast(tokens.TextDim, field)).toBeGreaterThanOrEqual(4.5)
            }
          if (percent === 0) {
            expect(floating.CaptionFill).toBe(floating.ShellGround)
            expect(floating.ChromeFieldOnGround).toBe(alpha(palette.colors['--surface'], 1))
          } else {
            expect(opacity(floating.CaptionFill)).toBe(0)
            for (const desktop of ['#FFFFFF', '#201F1E', '#000000'])
              expect(over(floating.CaptionFill, over(floating.ShellGround, desktop))).toBe(
                over(floating.ShellGround, desktop),
              )
          }
          expect('ChromeGround' in floating).toBe(false)
          if (percent >= 25 && wall) {
            expect(opacity(floating.ChromeFieldOnGround)).toBe(0)
            expect(opacity(flush.ChromeFieldOnGround)).toBe(0)
          }
          for (const tokens of [floating, flush])
            for (const pane of ['ChromeSurface', 'WallGround', 'PaneGround'] as const) {
              const groundAdmission = 1 - opacity(tokens.ShellGround)
              const admitted = groundAdmission * (1 - opacity(tokens[pane]))
              expect(admitted).toBeLessThanOrEqual(groundAdmission + 0.001)
              if (percent >= 25 && (wall || pane === 'ChromeSurface'))
                expect(Math.abs(admitted - (0.35 * percent) / 100)).toBeLessThanOrEqual(0.006)
            }
        }
      if (ceiling < 100) expect(worstMetadataContrast(palette, ceiling + 1)).toBeLessThan(4.5)
      let flushCeiling = 0
      for (let percent = 0; percent <= 100; percent++) {
        const tokens = avalonSurfaceTokens(palette, percent, true, 'flush')
        if (contrast(tokens.TextDim, over(tokens.CaptionFill, over(tokens.ShellGround, '#FFFFFF'))) < 4.5)
          break
        flushCeiling = percent
      }
      expect(ceiling === 100 ? flushCeiling === 100 : flushCeiling > ceiling).toBe(true)
      if (AVALON_PALETTES.indexOf(palette) < 4) {
        expect(ceiling).toBeGreaterThanOrEqual(20)
        expect(ceiling).toBeLessThan(100)
      }
    },
  )
  it.each(AVALON_PALETTES.map((palette) => [palette.id, palette] as const))(
    '%s has exact opaque endpoints and opaque tiles/popovers at every slider position',
    (_, palette) => {
      const zero = avalonSurfaceTokens(palette, 0, true, 'flush')
      expect(zero.ShellGround).toBe(alpha(palette.colors['--bg'], 1))
      expect(zero.ChromeSurface).toBe(alpha(palette.colors['--surface'], 1))
      expect(zero.PaneGround).toBe(alpha(palette.colors['--bg'], 1))
      expect(zero.ChromeRaised).toBe(alpha(palette.colors['--raised'], 1))
      expect(zero.TextDim).toBe(palette.colors['--muted'])
      expect(zero.TextFaint).toBe(palette.colors['--avalon-faint'])
      expect(zero.ChromeFieldOnGround).toBe(alpha(palette.colors['--surface'], 1))
      expect(zero.ChromeFieldOnSurface).toBe(alpha(palette.colors['--bg'], 1))
      expect(light(zero.WallGround)).toBeLessThan(light(zero.CaptionFill))
      const floating = avalonSurfaceTokens(palette, 0, true, 'floating')
      expect(light(floating.ShellGround)).toBeLessThan(light(floating.WallGround))
      for (let percent = 0; percent <= 100; percent++)
        for (const wall of [false, true])
          for (const layout of ['floating', 'flush'] as const) {
            const tokens = avalonSurfaceTokens(palette, percent, wall, layout)
            expect(tokens.TileGround).toBe(alpha(palette.colors['--bg'], 1))
            expect(tokens.SurfaceRaised).toBe(alpha(palette.colors['--raised'], 1))
            if (layout === 'flush') expect(tokens.CaptionFill).toBe(tokens.ChromeSurface)
            expect(tokens.PaneGround).toBe(tokens.WallGround)
            if (!wall) expect(tokens.WallGround).toBe(alpha(palette.colors['--bg'], 1))
            else expect(opacity(tokens.WallGround)).toBe(opacity(tokens.ChromeSurface))
          }
    },
  )
  it.each(AVALON_PALETTES.slice(0, 4).map((palette) => [palette.id, palette] as const))(
    '%s selected rows stay above the rail against white dark and black desktops',
    (_, palette) => {
      for (let percent = 0; percent <= 100; percent++)
        for (const desktop of ['#FFFFFF', '#201F1E', '#000000']) {
          const tokens = avalonSurfaceTokens(palette, percent, true, 'floating')
          const rail = over(tokens.ChromeSurface, over(tokens.ShellGround, desktop))
          expect(light(over(tokens.ChromeRaised, rail))).toBeGreaterThan(light(rail))
        }
    },
  )
  it.each(AVALON_PALETTES.slice(0, 4).map((palette) => [palette.id, palette] as const))(
    '%s calibrated palette clears all original opaque floors and its dark desktop metadata never gets worse',
    (_, palette) => {
      const c = palette.colors
      for (const background of ['--surface', '--raised', '--bg'])
        expect(contrast(c['--muted'], c[background])).toBeGreaterThanOrEqual(5)
      expect(contrast(c['--text'], c['--surface'])).toBeGreaterThanOrEqual(12)
      expect(contrast(c['--accent'], c['--bg'])).toBeGreaterThanOrEqual(7)
      for (const role of ['--cool', '--avalon-amber', '--avalon-flare'])
        expect(contrast(c[role], c['--surface'])).toBeGreaterThanOrEqual(4.5)
      expect(contrast(c['--avalon-button-ink'], c['--accent'])).toBeGreaterThanOrEqual(7)
      expect(contrast(c['--avalon-danger-ink'], c['--avalon-danger'])).toBeGreaterThanOrEqual(2.9)
      const neutrals = ['--avalon-well', '--bg', '--surface', '--raised', '--avalon-high'].map((key) =>
        light(c[key]),
      )
      for (let index = 1; index < neutrals.length; index++)
        expect(neutrals[index]).toBeGreaterThan(neutrals[index - 1])
      const baseline = contrast(c['--muted'], c['--surface'])
      let shellAlpha = 1,
        paneAlpha = 1
      for (let percent = 0; percent <= 100; percent++) {
        const t = avalonSurfaceTokens(palette, percent, false, 'floating')
        expect(
          contrast(t.TextDim, over(t.ChromeSurface, over(t.ShellGround, '#201F1E'))),
        ).toBeGreaterThanOrEqual(baseline - 0.01)
        expect(opacity(t.ShellGround)).toBeLessThanOrEqual(shellAlpha)
        expect(opacity(t.ChromeSurface)).toBeLessThanOrEqual(paneAlpha)
        shellAlpha = opacity(t.ShellGround)
        paneAlpha = opacity(t.ChromeSurface)
      }
      for (const layout of ['floating', 'flush'] as const) {
        const t = avalonSurfaceTokens(palette, 100, false, layout)
        expect(t.ShellGround.slice(7)).toBe('26')
        expect(1 - opacity(t.ShellGround)).toBeGreaterThanOrEqual(0.8)
        for (const key of ['ChromeSurface', 'CaptionFill'] as const)
          expect((1 - opacity(t.ShellGround)) * (1 - opacity(t[key]))).toBeGreaterThanOrEqual(0.3)
        expect(opacity(t.PaneGround)).toBe(1)
      }
    },
  )
  it('keeps the floating caption and pane gaps as one surface and spends each pane alpha once', () => {
    const palette = AVALON_PALETTES[0]
    for (let percent = 1; percent <= 100; percent++) {
      const tokens = avalonSurfaceTokens(palette, percent, true, 'floating')
      expect(opacity(tokens.CaptionFill)).toBe(0)
      if (percent >= 25) {
        expect(opacity(tokens.ChromeFieldOnGround)).toBe(0)
        expect(opacity(tokens.ChromeFieldOnSurface)).toBe(0)
        const admitted = (1 - opacity(tokens.ShellGround)) * (1 - opacity(tokens.PaneGround))
        expect(Math.abs(admitted - (0.35 * percent) / 100)).toBeLessThan(0.003)
      }
    }
    const end = avalonSurfaceTokens(palette, 100, true, 'floating')
    expect(end.ShellGround).toBe('#040C0D26')
    expect(end.ChromeSurface).toBe('#16282A96')
    expect(end.TextDim).toBe('#A8BDB7')
  })
  it.each([
    [null, 0],
    ['true', 25],
    ['True', 25],
    [' false ', 0],
    ['47', 47],
    ['-2', 0],
    ['150', 100],
    ['broken', 0],
  ] as const)('migrates legacy transparency %s to %s', (value, expected) =>
    expect(parseTransparency(value)).toBe(expected),
  )
  it('retains stored choices over authored opening positions and has safe platform defaults', () => {
    const dawn = AVALON_PALETTES.find((palette) => palette.id === 'rose-pine-dawn')!
    expect(avalonAppearance({}, dawn, true).transparency).toBe(0)
    expect(avalonAppearance({}, AVALON_PALETTES[0], true)).toEqual({
      transparency: 30,
      backdrop: 'acrylic',
      wallTranslucent: true,
      layout: 'floating',
    })
    expect(avalonAppearance({}, AVALON_PALETTES[0], false).transparency).toBe(0)
    expect(
      avalonAppearance(
        { Transparency: '70', Backdrop: 'mica', TranslucentWall: 'false', Layout: 'flush' },
        dawn,
        true,
      ),
    ).toEqual({ transparency: 70, backdrop: 'mica', wallTranslucent: false, layout: 'flush' })
    expect(
      avalonAppearance({ Backdrop: 'none', TranslucentWall: 'nonsense', Layout: 'unknown' }, dawn, true),
    ).toMatchObject({ backdrop: 'acrylic', wallTranslucent: false, layout: 'floating' })
  })
})

describe('Native material policy', () => {
  const normal: AppearanceEnvironment = {
    platform: 'win32',
    release: '10.0.22621',
    highContrast: false,
    reducedTransparency: false,
    remoteSession: false,
  }
  it.each([
    { platform: 'linux' },
    { platform: 'darwin' },
    { release: '10.0.19045' },
    { highContrast: true },
    { reducedTransparency: true },
    { remoteSession: true },
  ])('keeps unsupported conditions solid: %j', (patch) => {
    expect(supportsWindowMaterial({ ...normal, ...patch })).toBe(false)
    const window = {
      isDestroyed: () => false,
      setBackgroundMaterial: vi.fn(),
      setBackgroundColor: vi.fn(),
      setOpacity: vi.fn(),
    }
    const controller = new WindowAppearanceController(window, () => ({ ...normal, ...patch }))
    expect(controller.apply({ enabled: true, material: 'acrylic', background: '#0F1C1E' }).requested).toBe(
      'none',
    )
    expect(window.setBackgroundColor).toHaveBeenLastCalledWith('#0F1C1E')
    expect(window.setOpacity).not.toHaveBeenCalled()
  })
  it('requests each supported material and restores solid background without changing whole-window opacity', () => {
    const window = {
      isDestroyed: () => false,
      setBackgroundMaterial: vi.fn(),
      setBackgroundColor: vi.fn(),
      setOpacity: vi.fn(),
    }
    const controller = new WindowAppearanceController(window, () => normal)
    for (const material of ['acrylic', 'mica'] as const) {
      expect(controller.apply({ enabled: true, material, background: '#0F1C1E' })).toEqual({
        requested: material,
        supported: true,
        platform: 'win32',
      })
      expect(window.setBackgroundMaterial).toHaveBeenLastCalledWith(material)
      expect(window.setBackgroundColor).toHaveBeenLastCalledWith('#00000000')
    }
    expect(controller.apply({ enabled: false, material: 'mica', background: '#FAF4ED' }).requested).toBe(
      'none',
    )
    expect(window.setBackgroundColor).toHaveBeenLastCalledWith('#FAF4ED')
    expect(window.setOpacity).not.toHaveBeenCalled()
  })
  it('recovers solid surfaces after a compositor request throws without modifying the requested preference', () => {
    const request = { enabled: true, material: 'mica', background: '#FAF4ED' }
    const window = {
      isDestroyed: () => false,
      setBackgroundMaterial: vi.fn(() => {
        throw Error('Unavailable')
      }),
      setBackgroundColor: vi.fn(),
    }
    expect(new WindowAppearanceController(window, () => normal).apply(request)).toEqual({
      requested: 'none',
      supported: false,
      platform: 'win32',
    })
    expect(window.setBackgroundColor).toHaveBeenLastCalledWith('#FAF4ED')
    expect(request.material).toBe('mica')
  })
  it.each([
    {},
    { enabled: 1, material: 'mica', background: '#FFFFFF' },
    { enabled: true, material: 'none', background: '#FFFFFF' },
    { enabled: true, material: 'mica', background: 'url(file:///private)' },
    { enabled: true, material: 'mica', background: '#FFFFFF', opacity: 0.3 },
  ])('refuses invalid native requests %j', (value) => expect(() => parseWindowAppearance(value)).toThrow())
})
