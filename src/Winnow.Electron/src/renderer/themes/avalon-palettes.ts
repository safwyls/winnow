import type { CSSProperties } from 'react'
import { contrastRatio } from '../../shared/theme'
import {
  readAvalonJson,
  type AvalonThemeDocument,
  type AvalonThemeFile,
} from '../../shared/avalonThemeDocument'
import { setAuthoredTypography } from '../../shared/typography'
import bottle from './avalon/assets/bottle-green.json?raw'
import silk from './avalon/assets/silkcircuit.json?raw'
import silkDawn from './avalon/assets/silkcircuit-dawn.json?raw'
import rose from './avalon/assets/rose-pine.json?raw'
import roseDawn from './avalon/assets/rose-pine-dawn.json?raw'

export interface AvalonPalette {
  id: string
  name: string
  colors: Record<string, string>
  light?: boolean
  high?: string
  translucentGround?: string
  translucentMuted?: string
  translucentFaint?: string
  translucentSurface?: string
  document?: AvalonThemeDocument
  sourceFile?: string
  defaults?: {
    transparency?: number
    backdrop?: 'acrylic' | 'mica'
    wallTranslucent?: boolean
    layout?: 'floating' | 'flush'
  }
}
interface AuthoredPalette {
  id: string
  name: string
  variant?: string
  seeds: Record<string, string>
  structure?: Record<string, number>
  overrides?: Record<string, string>
  translucency?: Record<string, number>
  defaults?: AvalonThemeDocument['defaults']
}
type Hsv = [number, number, number]
export function hsv(hex: string): Hsv {
  const [r, g, b] = [1, 3, 5].map((offset) => parseInt(hex.slice(offset, offset + 2), 16) / 255)
  const max = Math.max(r, g, b),
    span = max - Math.min(r, g, b)
  const hue = !span
    ? 0
    : max === r
      ? ((((g - b) / span) % 6) + 6) % 6
      : max === g
        ? (b - r) / span + 2
        : (r - g) / span + 4
  return [hue / 6, max <= 0 ? 0 : span / max, max]
}
function hex([hue, saturation, value]: Hsv) {
  const h = ((hue % 1) + 1) % 1,
    s = Math.max(0, Math.min(1, saturation)),
    v = Math.max(0, Math.min(1, value))
  const sector = Math.floor(h * 6),
    f = h * 6 - sector,
    p = v * (1 - s),
    q = v * (1 - f * s),
    t = v * (1 - (1 - f) * s)
  const values = [
    [v, t, p],
    [q, v, p],
    [p, v, t],
    [p, q, v],
    [t, p, v],
    [v, p, q],
  ][sector % 6]
  // Math.Round in the original uses ties-to-even rather than JavaScript's ties-up rule.
  return (
    '#' +
    values
      .map((value) => {
        const x = value * 255,
          floor = Math.floor(x)
        return (x - floor === 0.5 ? floor + (floor % 2) : Math.round(x)).toString(16).padStart(2, '0')
      })
      .join('')
      .toUpperCase()
  )
}
function lighter(ink: Hsv, reference: string, ratio: number) {
  let low = ink[2],
    high = 1
  for (let i = 0; i < 40; i++) {
    const mid = (low + high) / 2
    if (contrastRatio(hex([ink[0], ink[1], mid]), reference) >= ratio) high = mid
    else low = mid
  }
  return hex([ink[0], ink[1], (low + high) / 2])
}
function darker(ink: Hsv, reference: string, ratio: number) {
  let low = 0,
    high = hsv(reference)[2]
  for (let index = 0; index < 40; index++) {
    const mid = (low + high) / 2
    if (contrastRatio(hex([ink[0], ink[1], mid]), reference) >= ratio) low = mid
    else high = mid
  }
  return hex([ink[0], ink[1], (low + high) / 2])
}
/** The opaque fields Avalon uses, derived with ThemeDerivation.cs HSV arithmetic. */
export function deriveAvalonPalette(raw: string | AvalonThemeDocument): AvalonPalette {
  const file = (typeof raw === 'string' ? readAvalonJson(raw) : raw) as AuthoredPalette
  const seed = file.seeds,
    shape = file.structure ?? {},
    override = file.overrides ?? {}
  const surface = hsv(seed.surface),
    ground = hsv(seed.ground),
    depth = Math.max(0.001, Math.min(1, shape.wellDepth ?? 0.55))
  const dim = hsv(
    override.TextDim ?? hex([surface[0], surface[1] * (shape.dimChroma ?? 0.41), shape.dimValue ?? 0.68]),
  )
  const faint = hsv(
    override.TextFaint ??
      hex([surface[0], surface[1] * (shape.faintChroma ?? 0.65), shape.faintValue ?? 0.5]),
  )
  const volt = hsv(seed.volt),
    danger = hsv(seed.danger)
  const groundInk = Math.max(0.001, Math.min(1, file.translucency?.groundInk ?? 0.44))
  const raised = hsv(
    override.SurfaceRaised ?? hex([surface[0], surface[1], surface[2] + (shape.elevation ?? 0.05)]),
  )
  return {
    id: file.id,
    name: file.name,
    light: file.variant
      ? file.variant === 'light'
      : contrastRatio('#000000', seed.ground) > contrastRatio('#000000', seed.text),
    document: file as AvalonThemeDocument,
    high: override.SurfaceHigh ?? hex([raised[0], raised[1], raised[2] + (shape.elevation ?? 0.05)]),
    translucentGround:
      override.TranslucentChromeGround ??
      hex([ground[0], Math.min(1, ground[1] * groundInk ** -0.42), ground[2] * groundInk]),
    translucentMuted:
      override.TranslucentTextDim ??
      hex([dim[0], dim[1] * 0.87, dim[2] * (file.translucency?.dimLift ?? 1.14)]),
    translucentFaint:
      override.TranslucentTextFaint ??
      hex([faint[0], faint[1] * 0.85, faint[2] * (file.translucency?.faintLift ?? 1.2)]),
    translucentSurface:
      override.TranslucentSurface ??
      hex([
        surface[0],
        Math.min(1, surface[1] * (file.translucency?.chromeInk ?? 0.48) ** -0.42),
        surface[2] * (file.translucency?.chromeInk ?? 0.48),
      ]),
    defaults: file.defaults
      ? {
          ...file.defaults,
          ...(file.defaults.reach ? { wallTranslucent: file.defaults.reach === 'chrome-and-wall' } : {}),
        }
      : undefined,
    colors: {
      '--bg': seed.ground,
      '--surface': seed.surface,
      '--text': seed.text,
      '--accent': seed.volt,
      '--cool': seed.azure,
      '--avalon-flare': seed.flare,
      '--avalon-amber': seed.amber,
      '--avalon-danger': seed.danger,
      '--raised':
        override.SurfaceRaised ?? hex([surface[0], surface[1], surface[2] + (shape.elevation ?? 0.05)]),
      '--avalon-high':
        override.SurfaceHigh ?? hex([raised[0], raised[1], raised[2] + (shape.elevation ?? 0.05)]),
      '--avalon-faint': hex(faint),
      '--avalon-accent-hover': override.VoltHover ?? hex([volt[0], volt[1] * 0.75, volt[2] * 1.03]),
      '--avalon-accent-press': override.VoltPress ?? hex([volt[0], volt[1] * 1.1, volt[2] * 0.89]),
      '--avalon-danger-hover': override.DangerHover ?? hex([danger[0], danger[1] * 0.865, danger[2] * 1.055]),
      '--avalon-danger-press': override.DangerPress ?? hex([danger[0], danger[1] * 1.027, danger[2] * 0.798]),
      '--avalon-danger-ink': override.DangerInk ?? hex([danger[0], 0.06, 1]),
      '--line': override.Line ?? lighter(surface, seed.surface, shape.edge ?? 1.6),
      '--muted':
        override.TextDim ?? hex([surface[0], surface[1] * (shape.dimChroma ?? 0.41), shape.dimValue ?? 0.68]),
      '--avalon-well':
        override.Well ?? hex([ground[0], Math.min(1, ground[1] * depth ** -0.42), ground[2] * depth]),
      '--avalon-button-ink':
        override.VoltInk ??
        darker([volt[0], Math.min(1, volt[1] * 1.15), 0], seed.volt, shape.voltInkContrast ?? 9.5),
    },
  }
}
const house = (id: string, name: string, colors: string[]): AvalonPalette => ({
  id,
  name,
  translucentGround: { winnow: '#040C0D', nightshift: '#020407', tungsten: '#0A0603', 'box-art': '#08090B' }[
    id
  ],
  translucentMuted: { winnow: '#A8BDB7', nightshift: '#A8B5CC', tungsten: '#C7B394', 'box-art': '#B2BAC2' }[
    id
  ],
  translucentFaint: { winnow: '#7A9CA0', nightshift: '#7C8AA6', tungsten: '#94805F', 'box-art': '#838C95' }[
    id
  ],
  translucentSurface: { winnow: '#071214', nightshift: '#03060B', tungsten: '#0E0904', 'box-art': '#0E1114' }[
    id
  ],
  high: { winnow: '#254042', nightshift: '#1A2231', tungsten: '#38291C', 'box-art': '#383E45' }[id],
  colors: {
    ...Object.fromEntries(
      [
        '--bg',
        '--surface',
        '--raised',
        '--line',
        '--text',
        '--muted',
        '--accent',
        '--cool',
        '--avalon-flare',
        '--avalon-well',
        '--avalon-button-ink',
        '--avalon-amber',
        '--avalon-danger',
      ].map((key, index) => [key, colors[index]]),
    ),
    ...Object.fromEntries(
      [
        '--avalon-high',
        '--avalon-faint',
        '--avalon-accent-hover',
        '--avalon-accent-press',
        '--avalon-danger-hover',
        '--avalon-danger-press',
        '--avalon-danger-ink',
      ].map((key, index) => [
        key,
        {
          winnow: ['#254042', '#5A8286', '#6FEDCE', '#3BD1AC', '#EF645E', '#B33A35', '#FFF2EF'],
          nightshift: ['#1A2231', '#5C6B87', '#6FEAFF', '#14C2E2', '#F2645A', '#B8342C', '#FFF2EF'],
          tungsten: ['#38291C', '#7C6950', '#FFD670', '#DCA92C', '#E85668', '#AE2C40', '#FFF0F2'],
          'box-art': ['#383E45', '#6B747C', '#F6FCFC', '#C2D8D8', '#EA6C6C', '#B33F3F', '#FFF2F2'],
        }[id]![index],
      ]),
    ),
  },
})
export const AVALON_PALETTES: AvalonPalette[] = [
  house('winnow', 'Winnow', [
    '#0F1C1E',
    '#16282A',
    '#1D3437',
    '#2B4A4C',
    '#F0EDE7',
    '#8FA5A0',
    '#4DE8C2',
    '#57A8F0',
    '#FF4D93',
    '#050D0E',
    '#0C2A24',
    '#FFB63D',
    '#E04B45',
  ]),
  house('nightshift', 'Nightshift', [
    '#070A10',
    '#0A0E15',
    '#121823',
    '#3E5275',
    '#E8EDF5',
    '#8D9AB4',
    '#2FE0FF',
    '#8AA9FF',
    '#FF3D8C',
    '#04060A',
    '#032430',
    '#FFC24A',
    '#E8483F',
  ]),
  house('tungsten', 'Tungsten', [
    '#17100A',
    '#221810',
    '#2D2116',
    '#40301F',
    '#F5EBDC',
    '#B09A7E',
    '#F7C544',
    '#7FAECF',
    '#FF3DBE',
    '#0C0704',
    '#2A1B06',
    '#FF6B33',
    '#D93B52',
  ]),
  house('box-art', 'Box art', [
    '#0B0C0D',
    '#202429',
    '#2C3137',
    '#474E56',
    '#ECEEF0',
    '#9BA3AA',
    '#E8F4F4',
    '#8FB4D6',
    '#FF4D9E',
    '#060708',
    '#12161A',
    '#DEC08C',
    '#E05252',
  ]),
  ...[bottle, silk, silkDawn, rose, roseDawn].map(deriveAvalonPalette),
]
let customPalettes: AvalonPalette[] = []
export function registerAvalonThemes(files: AvalonThemeFile[]): void {
  customPalettes = files.map(({ file, document }) => ({ ...deriveAvalonPalette(document), sourceFile: file }))
  setAuthoredTypography(
    avalonPalettes().map((palette) => ({ id: palette.id, typography: palette.document?.typography })),
  )
}
export function avalonPalettes(): AvalonPalette[] {
  return [
    ...AVALON_PALETTES.map((palette) => customPalettes.find((custom) => custom.id === palette.id) ?? palette),
    ...customPalettes.filter((custom) => !AVALON_PALETTES.some((palette) => palette.id === custom.id)),
  ]
}
export function avalonPalette(id: string): AvalonPalette | undefined {
  return avalonPalettes().find((palette) => palette.id === id)
}
export function avalonPaletteStyle(id: string | AvalonPalette): CSSProperties | undefined {
  const palette = typeof id === 'string' ? avalonPalette(id) : id
  if (!palette) return undefined
  const foreground = (color: string) => {
    if (!palette.light) return color
    const surfaces = ['--avalon-well', '--bg', '--surface', '--raised'].map((key) => palette.colors[key])
    if (palette.high) surfaces.push(palette.high)
    for (let step = 0; step <= 255; step++) {
      const candidate =
        '#' +
        [1, 3, 5]
          .map((offset) => {
            const x = parseInt(color.slice(offset, offset + 2), 16) * (1 - step / 255)
            const floor = Math.floor(x)
            return (x - floor === 0.5 ? floor + (floor % 2) : Math.round(x)).toString(16).padStart(2, '0')
          })
          .join('')
      if (surfaces.every((surface) => contrastRatio(candidate, surface) >= 4.5)) return candidate
    }
    return palette.colors['--text']
  }
  return {
    ...palette.colors,
    '--accent-foreground': foreground(palette.colors['--accent']),
    '--avalon-accent-hover-foreground': foreground(palette.colors['--avalon-accent-hover']),
    '--cool-foreground': foreground(palette.colors['--cool']),
    '--avalon-amber-foreground': foreground(palette.colors['--avalon-amber']),
    '--avalon-danger-foreground': foreground(palette.colors['--avalon-danger']),
    colorScheme: palette.light ? 'light' : 'dark',
  } as CSSProperties
}
