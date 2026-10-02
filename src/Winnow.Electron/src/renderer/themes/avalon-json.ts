import {
  AVALON_DERIVED,
  AVALON_SCALARS,
  type AvalonThemeCatalogue,
  type AvalonThemeDiagnostic,
  type AvalonThemeDocument,
} from '../../shared/avalonThemeDocument'
import { DEFAULT_TYPOGRAPHY, type ThemeTypography } from '../../shared/typography'
import {
  AVALON_PALETTES,
  avalonPaletteStyle,
  deriveAvalonPalette,
  hsv,
  type AvalonPalette,
} from './avalon-palettes'
import {
  aaCeiling,
  alpha,
  avalonSurfaceTokens,
  contrast,
  luminance,
  over,
  roundEven,
} from './avalon-translucency'

export function avalonDerivedFields(palette: AvalonPalette): Record<string, string> {
  const c = palette.colors
  return {
    Well: c['--avalon-well'],
    SurfaceRaised: c['--raised'],
    SurfaceHigh: palette.high!,
    Line: c['--line'],
    TextDim: c['--muted'],
    TextFaint: c['--avalon-faint'],
    VoltInk: c['--avalon-button-ink'],
    VoltHover: c['--avalon-accent-hover'],
    VoltPress: c['--avalon-accent-press'],
    DangerHover: c['--avalon-danger-hover'],
    DangerPress: c['--avalon-danger-press'],
    DangerInk: c['--avalon-danger-ink'],
    TranslucentSurface: palette.translucentSurface!,
    TranslucentChromeGround: palette.translucentGround!,
    TranslucentTextDim: palette.translucentMuted!,
    TranslucentTextFaint: palette.translucentFaint!,
  }
}
export function avalonSeeds(palette: AvalonPalette): Record<string, string> {
  const c = palette.colors
  return {
    ground: c['--bg'],
    surface: c['--surface'],
    text: c['--text'],
    flare: c['--avalon-flare'],
    volt: c['--accent'],
    amber: c['--avalon-amber'],
    azure: c['--cool'],
    danger: c['--avalon-danger'],
  }
}

/** Fit proportions first, then retain only colors those proportions cannot reproduce. */
export function exportAvalonPalette(
  palette: AvalonPalette,
  typography: ThemeTypography = palette.document?.typography ?? DEFAULT_TYPOGRAPHY,
): string {
  const actual = avalonDerivedFields(palette),
    seeds = avalonSeeds(palette)
  const surface = hsv(seeds.surface),
    ground = hsv(seeds.ground),
    dim = hsv(actual.TextDim),
    faint = hsv(actual.TextFaint)
  const safe = (a: number, b: number) => (b <= 0 ? 0.5 : a / b)
  const raw: Record<string, number> = {
    elevation: hsv(actual.SurfaceRaised)[2] - surface[2],
    wellDepth: safe(hsv(actual.Well)[2], ground[2]),
    edge: contrast(actual.Line, seeds.surface),
    dimValue: dim[2],
    dimChroma: safe(dim[1], surface[1]),
    voltInkContrast: contrast(actual.VoltInk, seeds.volt),
    faintValue: faint[2],
    faintChroma: safe(faint[1], surface[1]),
    chromeInk: safe(hsv(actual.TranslucentSurface)[2], surface[2]),
    groundInk: safe(hsv(actual.TranslucentChromeGround)[2], ground[2]),
    dimLift: safe(hsv(actual.TranslucentTextDim)[2], dim[2]),
    faintLift: safe(hsv(actual.TranslucentTextFaint)[2], faint[2]),
  }
  const document: AvalonThemeDocument = {
    schemaVersion: 1,
    id: palette.sourceFile ? palette.id : `${palette.id}-copy`,
    name: palette.sourceFile ? palette.name : `${palette.name} (copy)`,
    reason: palette.document?.reason ?? `The ${palette.name} palette, ready to make your own.`,
    variant: palette.light ? 'light' : 'dark',
    seeds,
    structure: {},
    translucency: {},
    overrides: {},
    typography: { ...typography },
    ...(palette.document?.defaults ? { defaults: { ...palette.document.defaults } } : {}),
  }
  for (const [key, [min, max, , block]] of Object.entries(AVALON_SCALARS))
    document[block]![key] = Math.max(min, Math.min(max, raw[key]))
  const targets: Record<string, string[]> = {
    elevation: ['SurfaceRaised', 'SurfaceHigh'],
    wellDepth: ['Well'],
    edge: ['Line'],
    dimValue: ['TextDim'],
    dimChroma: ['TextDim'],
    voltInkContrast: ['VoltInk'],
    faintValue: ['TextFaint'],
    faintChroma: ['TextFaint'],
    chromeInk: ['TranslucentSurface'],
    groundInk: ['TranslucentChromeGround'],
    dimLift: ['TranslucentTextDim'],
    faintLift: ['TranslucentTextFaint'],
  }
  for (const [key, [, , , block]] of Object.entries(AVALON_SCALARS)) {
    const exact = document[block]![key]
    const score = (value: number) => {
      const upstream: Record<string, string> = {
        SurfaceRaised: actual.SurfaceRaised,
        TextDim: actual.TextDim,
        TextFaint: actual.TextFaint,
      }
      for (const field of targets[key]) delete upstream[field]
      const candidate = deriveAvalonPalette({
        ...document,
        [block]: { ...document[block], [key]: value },
        overrides: upstream,
      })
      const values = avalonDerivedFields(candidate)
      return targets[key].filter((field) => values[field] === actual[field]).length
    }
    const target = score(exact)
    for (let places = 2; places <= 6; places++) {
      const candidate = roundEven(exact * 10 ** places) / 10 ** places
      if (score(candidate) >= target) {
        document[block]![key] = candidate
        break
      }
    }
  }
  for (const field of AVALON_DERIVED)
    if (avalonDerivedFields(deriveAvalonPalette(document))[field] !== actual[field])
      document.overrides![field] = actual[field]
  return JSON.stringify(document, null, 2)
}

export function avalonThemeReport(palette: AvalonPalette) {
  const ceiling = aaCeiling(palette)
  let wallCeiling = 100
  for (let percent = 0; percent <= 100; percent++) {
    const tokens = avalonSurfaceTokens(palette, percent, true, 'floating')
    if (luminance(over(tokens.WallGround, over(tokens.ShellGround, '#FFFFFF'))) > luminance('#2C3237')) {
      wallCeiling = Math.max(0, percent - 1)
      break
    }
  }
  return {
    aaCeiling: ceiling,
    wallCeiling,
    headline:
      ceiling === 100
        ? 'Labels stay over AA at every transparency.'
        : ceiling === 0
          ? 'Labels drop under AA the moment transparency leaves zero.'
          : `Labels stay over AA to ${ceiling}% transparency.`,
  }
}

/** Advisory checks retain the author's colors; warnings never suppress a valid theme. */
export function inspectAvalonTheme(palette: AvalonPalette): AvalonThemeDiagnostic[] {
  const diagnostics: AvalonThemeDiagnostic[] = [],
    seeds = avalonSeeds(palette),
    c = palette.colors
  const warn = (field: string, message: string) =>
    diagnostics.push({
      severity: 'warning',
      file: palette.sourceFile ?? `${palette.id}.json`,
      field,
      message,
    } as const)
  const floor = (name: string, ink: string, field: string, threshold: number, source: string) => {
    const ratio = contrast(ink, field)
    if (ratio < threshold)
      warn(
        source,
        `${name} measures ${ratio.toFixed(2)}:1, below ${threshold}:1. The theme loads; these labels may be hard to read.`,
      )
  }
  for (const [role, color] of Object.entries(seeds))
    if (role !== 'flare' && color === seeds.flare)
      warn(
        `seeds.${role}`,
        'This repeats Flare, which means patched since you played and carries the unread count. A second use makes unread updates harder to recognize.',
      )
  for (const [role, minimum] of [
    ['danger', 24],
    ['volt', 60],
  ] as const) {
    const difference = Math.abs(hsv(seeds.flare)[0] - hsv(seeds[role])[0]) * 360,
      gap = Math.min(difference, 360 - difference)
    if (gap < minimum)
      warn(
        'seeds.flare',
        `Flare sits ${gap.toFixed(0)}° from ${role === 'danger' ? 'Danger' : 'Volt'}, below ${minimum}°. ${role === 'danger' ? 'The close button can be mistaken for an unread alarm.' : 'Selection can be mistaken for an unread update.'}`,
      )
  }
  floor('TextDim on Surface', c['--muted'], seeds.surface, 4.5, 'seeds.surface')
  floor('TextDim on SurfaceRaised', c['--muted'], c['--raised'], 4.5, 'structure.elevation')
  floor('TextDim on Ground', c['--muted'], seeds.ground, 4.5, 'structure.dimValue')
  floor('Text on Surface', seeds.text, seeds.surface, 7, 'seeds.text')
  floor('VoltInk on Volt', c['--avalon-button-ink'], seeds.volt, 7, 'overrides.VoltInk')
  floor('Flare on Surface', seeds.flare, seeds.surface, 4.5, 'seeds.flare')
  if (palette.light) {
    const style = avalonPaletteStyle(palette) as Record<string, string> | undefined
    for (const surface of [seeds.ground, seeds.surface, c['--raised'], palette.high!]) {
      for (const [role, token] of [
        ['volt', '--accent-foreground'],
        ['amber', '--avalon-amber-foreground'],
        ['azure', '--cool-foreground'],
        ['danger', '--avalon-danger-foreground'],
      ])
        floor(`${role} foreground`, style?.[token] ?? seeds[role], surface, 4.5, `seeds.${role}`)
      floor('Line', c['--line'], surface, 3, 'overrides.Line')
    }
    for (const state of ['', '-hover', '-press']) {
      floor(
        `VoltInk on Volt${state}`,
        c['--avalon-button-ink'],
        state ? c[`--avalon-accent${state}`] : seeds.volt,
        4.5,
        'overrides.VoltInk',
      )
      floor(
        `DangerInk on Danger${state}`,
        c['--avalon-danger-ink'],
        state ? c[`--avalon-danger${state}`] : seeds.danger,
        4.5,
        'overrides.DangerInk',
      )
    }
  }
  let artContrast = Infinity
  for (let grey = 0; grey <= 255; grey++) {
    const field = over(alpha(seeds.surface, 0.92), '#' + grey.toString(16).padStart(2, '0').repeat(3))
    for (const ink of [seeds.text, c['--muted'], seeds.azure, seeds.amber])
      artContrast = Math.min(artContrast, contrast(ink, field))
  }
  if (artContrast < 4.5)
    warn('seeds.surface', `Detail labels over cover art can reach ${artContrast.toFixed(2)}:1, below 4.5:1.`)
  if (luminance(seeds.ground) > luminance('#2C3237'))
    warn(
      'seeds.ground',
      'The field is lighter than a dormant cover. Dimmed tiles can look like a hole punched into a lit field instead of faded art.',
    )
  if (luminance(seeds.surface) < luminance(seeds.ground))
    warn(
      'seeds.surface',
      'The chrome is darker than the art field; the cover wall can read as a lid rather than a recess.',
    )
  return diagnostics
}

export function avalonThemeDiagnostics(catalogue?: AvalonThemeCatalogue): AvalonThemeDiagnostic[] {
  const local = new Set(catalogue?.themes.map(({ document }) => document.id))
  return [
    // Local authored copies replace the bundled palette and its audit findings together.
    ...AVALON_PALETTES.filter((palette) => palette.document && !local.has(palette.id)).flatMap(
      inspectAvalonTheme,
    ),
    ...(catalogue?.diagnostics ?? []),
    ...(catalogue?.themes.flatMap(({ file, document }) =>
      inspectAvalonTheme({ ...deriveAvalonPalette(document), sourceFile: file }),
    ) ?? []),
  ]
}
