import { afterEach, expect, it } from 'vitest'
import { parseAvalonTheme } from '../src/shared/avalonThemeDocument'
import {
  AVALON_PALETTES,
  avalonPalette,
  avalonPalettes,
  deriveAvalonPalette,
  registerAvalonThemes,
} from '../src/renderer/themes/avalon-palettes'
import { avalonThemeDiagnostics, exportAvalonPalette } from '../src/renderer/themes/avalon-json'

afterEach(() => registerAvalonThemes([]))

it.each([
  ['bottle-green', 'Bottle green', '#0A140E', 40],
  ['silkcircuit', 'SilkCircuit', '#0A0A0F', undefined],
  ['silkcircuit-dawn', 'SilkCircuit Dawn', '#F4F0FA', 0],
  ['rose-pine', 'Rosé Pine', '#191724', undefined],
  ['rose-pine-dawn', 'Rosé Pine Dawn', '#FAF4ED', 0],
] as const)(
  'ships the original authored %s palette without a local theme directory',
  (id, name, ground, transparency) => {
    registerAvalonThemes([])
    const matches = avalonPalettes().filter((palette) => palette.id === id)
    expect(matches).toHaveLength(1)
    expect(matches[0].name).toBe(name)
    expect(matches[0].colors['--bg']).toBe(ground)
    expect(matches[0].defaults?.transparency).toBe(transparency)
    expect(matches[0].sourceFile).toBeUndefined()
    expect(avalonPalette(id)).toBe(matches[0])
    expect(avalonThemeDiagnostics().filter((item) => item.severity === 'error')).toEqual([])
  },
)

it.each(AVALON_PALETTES)(
  'infers the legacy $id variant and round trips an explicit opposite variant',
  (palette) => {
    const json = JSON.parse(exportAvalonPalette(palette))
    delete json.variant
    const legacy = parseAvalonTheme('legacy.json', JSON.stringify(json))
    expect(legacy.document).not.toBeNull()
    expect(deriveAvalonPalette(legacy.document!).light).toBe(Boolean(palette.light))
    json.variant = palette.light ? 'dark' : 'light'
    const explicit = parseAvalonTheme('explicit.json', JSON.stringify(json))
    expect(explicit.document).not.toBeNull()
    const opposite = deriveAvalonPalette(explicit.document!)
    expect(opposite.light).toBe(!palette.light)
    const roundTrip = parseAvalonTheme('roundtrip.json', exportAvalonPalette(opposite))
    expect(deriveAvalonPalette(roundTrip.document!).light).toBe(opposite.light)
    json.variant = 'dakr'
    const invalid = parseAvalonTheme('typo.json', JSON.stringify(json))
    expect(invalid.document).toBeNull()
    expect(invalid.diagnostics).toContainEqual(
      expect.objectContaining({ severity: 'error', field: 'variant' }),
    )
  },
)

it.each(['silkcircuit-dawn', 'rose-pine-dawn'])(
  'retains %s bundled audit findings and replaces them with the local same-ID findings',
  (id) => {
    const original = avalonPalette(id)!
    expect(avalonThemeDiagnostics()).toContainEqual(
      expect.objectContaining({ file: `${id}.json`, severity: 'warning' }),
    )
    const parsed = parseAvalonTheme('local.json', exportAvalonPalette(original))
    const document = { ...parsed.document!, id }
    const catalogue = { themes: [{ file: 'local.json', document }], diagnostics: [] }
    registerAvalonThemes(catalogue.themes)
    const diagnostics = avalonThemeDiagnostics(catalogue)
    expect(avalonPalette(id)?.sourceFile).toBe('local.json')
    expect(diagnostics.some((item) => item.file === `${id}.json`)).toBe(false)
    expect(diagnostics).toContainEqual(expect.objectContaining({ file: 'local.json', severity: 'warning' }))
  },
)
