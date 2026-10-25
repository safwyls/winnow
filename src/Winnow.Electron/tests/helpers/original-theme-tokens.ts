import { avalonPaletteStyle, type AvalonPalette } from '../../src/renderer/themes/avalon-palettes'
import {
  alpha,
  avalonSurfaceTokens,
  mix,
  type AvalonLayout,
} from '../../src/renderer/themes/avalon-translucency'

// This adapter names every original role. Extra alpha and Fluent scrollbar roles
// verify palette serialization arithmetic, not a claim about Chromium templates.
export function originalRoles(palette: AvalonPalette, percent: number, wall: boolean, layout: AvalonLayout) {
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
