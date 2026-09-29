import type { AvalonPalette } from './avalon-palettes'

export type AvalonLayout = 'floating' | 'flush'
export interface AvalonAppearance {
  transparency: number
  backdrop: 'acrylic' | 'mica'
  wallTranslucent: boolean
  layout: AvalonLayout
}
const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value))
export const roundEven = (value: number) =>
  value - Math.floor(value) === 0.5 ? Math.floor(value) + (Math.floor(value) % 2) : Math.round(value)
const channels = (color: string) => [1, 3, 5].map((offset) => parseInt(color.slice(offset, offset + 2), 16))
const toHex = (values: number[]) =>
  '#' +
  values
    .map((value) =>
      roundEven(clamp(value, 0, 255))
        .toString(16)
        .padStart(2, '0'),
    )
    .join('')
    .toUpperCase()
export const alpha = (color: string, value: number) => toHex([...channels(color), 255 * clamp(value, 0, 1)])
export const mix = (first: string, second: string, amount: number) =>
  toHex(channels(first).map((value, index) => value + (channels(second)[index] - value) * amount))
export function over(ink: string, background: string) {
  const amount = ink.length === 9 ? parseInt(ink.slice(7, 9), 16) / 255 : 1
  return toHex(
    channels(ink).map((value, index) => value * amount + channels(background)[index] * (1 - amount)),
  )
}

export function luminance(color: string): number {
  const values = channels(color).map((value) => {
    const channel = value / 255
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
  })
  return values[0] * 0.2126 + values[1] * 0.7152 + values[2] * 0.0722
}

export function contrast(first: string, second: string): number {
  const a = luminance(first),
    b = luminance(second)
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

/** Caption, rail and selected rows across both layouts bound the original AA marker. */
export function worstMetadataContrast(palette: AvalonPalette, percent: number, desktop = '#FFFFFF'): number {
  return Math.min(
    ...(['floating', 'flush'] as const).flatMap((layout) => {
      const tokens = avalonSurfaceTokens(palette, percent, true, layout)
      const shell = over(tokens.ShellGround, desktop),
        rail = over(tokens.ChromeSurface, shell)
      return [over(tokens.CaptionFill, shell), rail, over(tokens.ChromeRaised, rail)].map((background) =>
        contrast(tokens.TextDim, background),
      )
    }),
  )
}

export function aaCeiling(palette: AvalonPalette): number {
  for (let percent = 0; percent <= 100; percent++) {
    if (worstMetadataContrast(palette, percent) < 4.5) return Math.max(0, percent - 1)
  }
  return 100
}

export function parseTransparency(value: string | null | undefined): number {
  if (value != null && /^\s*[+-]?\d+\s*$/.test(value)) return clamp(Number(value), 0, 100)
  return value?.trim().toLocaleLowerCase() === 'true' ? 25 : 0
}

/** Stored choices win; bundled defaults are opening positions, never forced on a saved value. */
export function avalonAppearance(
  values: Record<string, string | null>,
  palette: AvalonPalette,
  windows: boolean,
): AvalonAppearance {
  return {
    transparency:
      values.Transparency != null
        ? parseTransparency(values.Transparency)
        : (palette.defaults?.transparency ?? (windows ? 30 : 0)),
    backdrop:
      values.Backdrop != null
        ? values.Backdrop === 'mica'
          ? 'mica'
          : 'acrylic'
        : (palette.defaults?.backdrop ?? 'acrylic'),
    wallTranslucent:
      values.TranslucentWall != null
        ? values.TranslucentWall.toLocaleLowerCase() === 'true'
        : (palette.defaults?.wallTranslucent ?? true),
    layout:
      values.Layout != null
        ? values.Layout === 'flush'
          ? 'flush'
          : 'floating'
        : (palette.defaults?.layout ?? 'floating'),
  }
}

/** WinnowTheme.Tokens' two compositing tiers; tiles and popup surfaces stay opaque. */
export function avalonSurfaceTokens(
  palette: AvalonPalette,
  percent: number,
  wallTranslucent: boolean,
  layout: AvalonLayout,
) {
  const colors = palette.colors
  const t = clamp(Number.isFinite(percent) ? percent / 100 : 0, 0, 1)
  const ink = Math.min(1, t / 0.25)
  const shellAlpha = 1 - t * 0.85
  const paneAlpha = 1 - ink * (0.35 / 0.85)
  const wallAlpha = wallTranslucent ? paneAlpha : 1
  const shellInk = mix(
    layout === 'floating' ? colors['--avalon-well'] : colors['--bg'],
    palette.translucentGround ?? colors['--bg'],
    ink,
  )
  const surface = colors['--surface'],
    raised = colors['--raised'],
    text = colors['--text']
  const strength = clamp(
    channels(raised).reduce((sum, value, index) => {
      const base = channels(surface)[index],
        high = channels(text)[index]
      return sum + (high === base ? 0 : (value - base) / (high - base))
    }, 0) / 3,
    0.02,
    0.1,
  )
  const raisedAlpha = strength + (0.1 - strength) * t
  return {
    ShellGround: alpha(shellInk, shellAlpha),
    CaptionFill: layout === 'floating' ? alpha(shellInk, t > 0 ? 0 : 1) : alpha(surface, paneAlpha),
    ChromeSurface: alpha(surface, paneAlpha),
    WallGround: alpha(colors['--bg'], wallAlpha),
    PaneGround: alpha(colors['--bg'], wallAlpha),
    ChromeRaised: t <= 0 ? alpha(raised, 1) : alpha(text, raisedAlpha),
    ChromeRaisedHalf: t <= 0 ? alpha(raised, 0.5) : alpha(text, raisedAlpha * 0.5),
    ChromeFieldOnGround: alpha(surface, wallTranslucent ? 1 - ink : 1),
    ChromeFieldOnSurface: alpha(colors['--bg'], 1 - ink),
    TextDim: mix(colors['--muted'], palette.translucentMuted ?? colors['--muted'], ink),
    TextFaint: mix(
      colors['--avalon-faint'] ?? colors['--muted'],
      palette.translucentFaint ?? colors['--avalon-faint'] ?? colors['--muted'],
      ink,
    ),
    TileGround: alpha(colors['--bg'], 1),
    SurfaceRaised: alpha(raised, 1),
  }
}
