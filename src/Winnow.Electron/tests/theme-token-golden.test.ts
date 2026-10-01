import { readFileSync } from 'node:fs'
import { expect, it } from 'vitest'
import { parseAvalonTheme } from '../src/shared/avalonThemeDocument'
import {
  AVALON_PALETTES,
  avalonPaletteStyle,
  deriveAvalonPalette,
  type AvalonPalette,
} from '../src/renderer/themes/avalon-palettes'
import { exportAvalonPalette } from '../src/renderer/themes/avalon-json'
import {
  alpha,
  avalonSurfaceTokens,
  mix,
  type AvalonLayout,
} from '../src/renderer/themes/avalon-translucency'

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

// This adapter names every original role. Extra alpha and Fluent scrollbar roles
// verify palette serialization arithmetic, not a claim about Chromium templates.
function originalRoles(palette: AvalonPalette, percent: number, wall: boolean, layout: AvalonLayout) {
  const style = avalonPaletteStyle(palette) as Record<string, string>
  const surfaces = avalonSurfaceTokens(palette, percent, wall, layout)
  const names = {
    Well: '--avalon-well',
    Ground: '--bg',
    Surface: '--surface',
    SurfaceHigh: '--avalon-high',
    Line: '--line',
    Text: '--text',
    Flare: '--avalon-flare',
    Volt: '--accent',
    VoltInk: '--avalon-button-ink',
    VoltHover: '--avalon-accent-hover',
    VoltPress: '--avalon-accent-press',
    Amber: '--avalon-amber',
    Azure: '--cool',
    Danger: '--avalon-danger',
    DangerHover: '--avalon-danger-hover',
    DangerPress: '--avalon-danger-press',
    DangerInk: '--avalon-danger-ink',
    VoltForeground: '--accent-foreground',
    VoltHoverForeground: '--avalon-accent-hover-foreground',
    AmberForeground: '--avalon-amber-foreground',
    AzureForeground: '--cool-foreground',
    DangerForeground: '--avalon-danger-foreground',
  }
  const roles: Record<string, string> = {
    ...Object.fromEntries(Object.entries(names).map(([role, css]) => [role, style[css]])),
    ...surfaces,
    VoltSelection: alpha(style['--accent'], 0.3),
    VoltSelectionSoft: alpha(style['--accent'], 0.24),
    VoltEdgeSoft: alpha(style['--accent'], 0.4),
    FlareSoft: alpha(style['--avalon-flare'], 0.7),
    FlareGlow: alpha(style['--avalon-flare'], 0.85),
    LineSoft: alpha(style['--line'], 0.6),
    SurfaceRaisedHalf: alpha(style['--raised'], 0.5),
    SurfaceRaisedGhost: alpha(style['--raised'], 0.12),
    GroundVeil: alpha(style['--bg'], 0.3),
    ModalScrim: alpha(style['--avalon-well'], 0.84),
    ScrollBarForeground: surfaces.TextDim,
    ScrollBarBackgroundPointerOver: alpha(style['--avalon-well'], 0.85),
    ScrollBarTrackFillPointerOver: alpha(style['--avalon-well'], 0.85),
    ScrollBarPanningThumbBackground: mix(style['--line'], style['--muted'], 0.35),
    ScrollBarThumbFillPointerOver: mix(style['--line'], style['--muted'], 0.62),
    ScrollBarThumbFillPressed: surfaces.TextDim,
    ScrollBarThumbFillDisabled: style['--raised'],
    ScrollBarButtonArrowForeground: surfaces.TextFaint,
    ScrollBarButtonArrowForegroundPointerOver: surfaces.TextDim,
    ScrollBarButtonArrowForegroundPressed: style['--text'],
    ScrollBarButtonArrowForegroundDisabled: style['--raised'],
  }
  return Object.fromEntries(
    Object.entries(roles).map(([role, color]) => [
      role,
      (color.length === 7 ? color + 'FF' : color).toUpperCase(),
    ]),
  )
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
