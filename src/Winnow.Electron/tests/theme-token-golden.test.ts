import { readFileSync } from 'node:fs'
import { expect, it } from 'vitest'
import { parseAvalonTheme } from '../src/shared/avalonThemeDocument'
import { AVALON_PALETTES, deriveAvalonPalette } from '../src/renderer/themes/avalon-palettes'
import { exportAvalonPalette } from '../src/renderer/themes/avalon-json'
import type { AvalonLayout } from '../src/renderer/themes/avalon-translucency'
import { originalRoles } from './helpers/original-theme-tokens'

const golden = JSON.parse(
  readFileSync(new URL('./fixtures/original-theme-tokens.json', import.meta.url), 'utf8'),
) as {
  sourceRevision: string
  themes: {
    id: string
    theme: unknown
    matrix: { wall: boolean; layout: AvalonLayout; percent: number; tokens: Record<string, string> }[]
  }[]
}

it.each(golden.themes)(
  'preserves all 61 frozen $id tokens through both JSON directions at every source slider position',
  (fixture) => {
    expect(golden.sourceRevision).toBe('cf45d9f1127243a987d3cf6e664a32fc767ecb67')
    const builtin = AVALON_PALETTES.find((palette) => palette.id === fixture.id)!
    const imported = parseAvalonTheme('original.json', JSON.stringify(fixture.theme))
    const exported = parseAvalonTheme('electron.json', exportAvalonPalette(builtin))
    expect(imported.document).not.toBeNull()
    expect(exported.document).not.toBeNull()
    const candidates = [
      builtin,
      deriveAvalonPalette(imported.document!),
      deriveAvalonPalette(exported.document!),
    ]
    expect(fixture.matrix).toHaveLength(84)
    for (const row of fixture.matrix) {
      expect(Object.keys(row.tokens)).toHaveLength(61)
      for (const palette of candidates) {
        const tokens = originalRoles(palette, row.percent, row.wall, row.layout)
        expect(Object.keys(tokens).sort()).toEqual(Object.keys(row.tokens).sort())
        for (const [name, expected] of Object.entries(row.tokens))
          expect(tokens[name], `${palette.id}/${row.layout}/${row.wall}/${row.percent}/${name}`).toBe(
            expected,
          )
      }
    }
  },
)
