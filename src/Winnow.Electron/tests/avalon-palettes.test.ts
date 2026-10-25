import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { contrastRatio } from '../src/shared/theme'
import {
  AVALON_PALETTES,
  avalonPaletteStyle,
  deriveAvalonPalette,
} from '../src/renderer/themes/avalon-palettes'

describe('Original Avalon palette and font assets', () => {
  it.each(['silkcircuit-dawn', 'rose-pine-dawn'])(
    'keeps %s text accents readable without changing authored fills',
    (id) => {
      const palette = AVALON_PALETTES.find((palette) => palette.id === id)!
      const style = avalonPaletteStyle(id) as Record<string, string>
      const surfaces = ['--avalon-well', '--bg', '--surface', '--raised'].map((key) => style[key])
      surfaces.push(palette.high!)
      for (const foreground of [
        '--accent-foreground',
        '--avalon-accent-hover-foreground',
        '--cool-foreground',
        '--avalon-amber-foreground',
        '--avalon-danger-foreground',
        '--text',
        '--muted',
      ])
        for (const surface of surfaces)
          expect(contrastRatio(style[foreground], surface)).toBeGreaterThanOrEqual(4.5)
      expect(style['--accent']).toBe(palette.colors['--accent'])
      expect(contrastRatio(style['--avalon-button-ink'], style['--accent'])).toBeGreaterThanOrEqual(4.5)
      for (const surface of surfaces) expect(contrastRatio(style['--line'], surface)).toBeGreaterThanOrEqual(3)
      for (const suffix of ['', '-hover', '-press']) {
        expect(contrastRatio(style['--avalon-button-ink'], style[suffix ? `--avalon-accent${suffix}` : '--accent'])).toBeGreaterThanOrEqual(4.5)
        expect(contrastRatio(style['--avalon-danger-ink'], style[`--avalon-danger${suffix}`])).toBeGreaterThanOrEqual(4.5)
      }
    },
  )
  it('keeps dark accent foregrounds equal to their authored colors', () => {
    for (const palette of AVALON_PALETTES.filter((palette) => !palette.light)) {
      const style = avalonPaletteStyle(palette.id) as Record<string, string>
      expect(style['--accent-foreground']).toBe(palette.colors['--accent'])
      expect(style['--cool-foreground']).toBe(palette.colors['--cool'])
      expect(style['--avalon-amber-foreground']).toBe(palette.colors['--avalon-amber'])
      expect(style['--avalon-danger-foreground']).toBe(palette.colors['--avalon-danger'])
      expect(style['--avalon-accent-hover-foreground']).toBe(palette.colors['--avalon-accent-hover'])
    }
  })
  it('retains all nine bundled palette identities and light variants', () => {
    expect(AVALON_PALETTES.map(({ id }) => id)).toEqual([
      'winnow',
      'nightshift',
      'tungsten',
      'box-art',
      'bottle-green',
      'silkcircuit',
      'silkcircuit-dawn',
      'rose-pine',
      'rose-pine-dawn',
    ])
    expect(avalonPaletteStyle('rose-pine-dawn')?.colorScheme).toBe('light')
    expect(avalonPaletteStyle('profile')).toBeUndefined()
  })
  it('preserves every opaque house palette value used by the Electron theme', () => {
    const source = readFileSync(new URL('../../Winnow.App/Themes/WinnowThemes.cs', import.meta.url), 'utf8')
    const fields = {
      '--bg': 'Ground',
      '--surface': 'Surface',
      '--raised': 'SurfaceRaised',
      '--line': 'Line',
      '--text': 'Text',
      '--muted': 'TextDim',
      '--accent': 'Volt',
      '--cool': 'Azure',
      '--avalon-flare': 'Flare',
      '--avalon-well': 'Well',
      '--avalon-button-ink': 'VoltInk',
      '--avalon-amber': 'Amber',
      '--avalon-danger': 'Danger',
      '--avalon-high': 'SurfaceHigh',
      '--avalon-faint': 'TextFaint',
      '--avalon-accent-hover': 'VoltHover',
      '--avalon-accent-press': 'VoltPress',
      '--avalon-danger-hover': 'DangerHover',
      '--avalon-danger-press': 'DangerPress',
      '--avalon-danger-ink': 'DangerInk',
    }
    for (const palette of AVALON_PALETTES.slice(0, 4)) {
      const block = source.split(`Id = "${palette.id}"`)[1].split('};')[0]
      for (const [css, field] of Object.entries(fields)) {
        expect(
          block.match(new RegExp(`\\b${field} = C\\("(#[A-Fa-f0-9]+)"\\)`))?.[1],
          `${palette.id} ${field}`,
        ).toBe(palette.colors[css])
      }
    }
  })
  it.each(['bottle-green', 'silkcircuit', 'silkcircuit-dawn', 'rose-pine', 'rose-pine-dawn'])(
    'keeps %s identical to the original authored palette',
    (name) => {
      const original = readFileSync(
        new URL(`../../Winnow.App/Themes/Bundled/${name}.json`, import.meta.url),
        'utf8',
      )
      const copy = readFileSync(
        new URL(`../src/renderer/themes/avalon/assets/${name}.json`, import.meta.url),
        'utf8',
      )
      expect(copy).toBe(original)
      const palette = deriveAvalonPalette(copy),
        file = JSON.parse(copy.replace(/^\s*\/\/.*$/gm, ''))
      expect(palette.colors['--accent']).toBe(file.seeds.volt)
      expect(palette.colors['--avalon-flare']).toBe(file.seeds.flare)
      if (file.overrides?.SurfaceRaised) expect(palette.colors['--raised']).toBe(file.overrides.SurfaceRaised)
      if (file.overrides?.TextDim) expect(palette.colors['--muted']).toBe(file.overrides.TextDim)
    },
  )
  it.each([
    'BricolageGrotesque-Bold.ttf',
    'PlusJakartaSans-Regular.ttf',
    'PlusJakartaSans-Medium.ttf',
    'PlusJakartaSans-SemiBold.ttf',
    'IBMPlexMono-Regular.ttf',
    'IBMPlexMono-Medium.ttf',
  ])('bundles the original %s static face', (name) => {
    expect(readFileSync(new URL(`../src/renderer/themes/avalon/assets/${name}`, import.meta.url))).toEqual(
      readFileSync(new URL(`../../Winnow.App/Assets/Fonts/${name}`, import.meta.url)),
    )
  })
})
