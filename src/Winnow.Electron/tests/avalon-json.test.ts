import { afterEach, expect, it } from 'vitest'
import {
  AVALON_DERIVED,
  AVALON_SCALARS,
  parseAvalonTheme,
  readAvalonJson,
  type AvalonThemeDocument,
} from '../src/shared/avalonThemeDocument'
import { DEFAULT_TYPOGRAPHY } from '../src/shared/typography'
import {
  AVALON_PALETTES,
  avalonPalette,
  avalonPaletteStyle,
  deriveAvalonPalette,
  registerAvalonThemes,
} from '../src/renderer/themes/avalon-palettes'
import {
  avalonDerivedFields,
  avalonSeeds,
  avalonThemeReport,
  exportAvalonPalette,
  inspectAvalonTheme,
} from '../src/renderer/themes/avalon-json'
import {
  aaCeiling,
  avalonSurfaceTokens,
  contrast,
  luminance,
} from '../src/renderer/themes/avalon-translucency'

export const document: AvalonThemeDocument = {
  schemaVersion: 1,
  id: 'test-palette',
  name: 'Test palette',
  reason: 'A palette made for testing.',
  seeds: avalonSeeds(AVALON_PALETTES[0]),
}
const parse = (patch: Record<string, unknown> = {}) =>
  parseAvalonTheme('test.json', JSON.stringify({ ...document, ...patch }))
afterEach(() => registerAvalonThemes([]))

it.each(AVALON_PALETTES)('round trips all 24 colors and every surface token for $id', (palette) => {
  const parsed = parseAvalonTheme('export.json', exportAvalonPalette(palette))
  expect(parsed.diagnostics.filter((item) => item.severity === 'error')).toEqual([])
  const loaded = deriveAvalonPalette(parsed.document!)
  expect(loaded.light).toBe(Boolean(palette.light))
  expect(avalonSeeds(loaded)).toEqual(avalonSeeds(palette))
  expect(avalonDerivedFields(loaded)).toEqual(avalonDerivedFields(palette))
  for (const wall of [true, false])
    for (const layout of ['floating', 'flush'] as const)
      for (let percentage = 0; percentage <= 100; percentage += 5)
        expect({
          ...avalonPaletteStyle(loaded),
          ...avalonSurfaceTokens(loaded, percentage, wall, layout),
        }).toEqual({
          ...avalonPaletteStyle(palette),
          ...avalonSurfaceTokens(palette, percentage, wall, layout),
        })
})
it('exports fitted structural proportions and only necessary residual overrides', () => {
  expect(JSON.parse(exportAvalonPalette(AVALON_PALETTES[1])).structure.edge).toBe(2.46)
  expect(JSON.parse(exportAvalonPalette(AVALON_PALETTES[2])).structure.edge).toBe(1.38)
  for (const palette of AVALON_PALETTES.slice(0, 4)) {
    const exported = JSON.parse(exportAvalonPalette(palette)) as AvalonThemeDocument
    const derived = avalonDerivedFields(deriveAvalonPalette({ ...exported, overrides: {} })),
      actual = avalonDerivedFields(palette)
    for (const field of AVALON_DERIVED)
      for (const offset of [1, 3, 5])
        expect(
          Math.abs(
            parseInt(actual[field].slice(offset, offset + 2), 16) -
              parseInt(derived[field].slice(offset, offset + 2), 16),
          ),
        ).toBeLessThanOrEqual(12)
    const seeds = avalonSeeds(palette),
      ramp = [derived.Well, seeds.ground, seeds.surface, derived.SurfaceRaised, derived.SurfaceHigh].map(
        luminance,
      )
    for (let index = 1; index < ramp.length; index++) expect(ramp[index]).toBeGreaterThan(ramp[index - 1])
    expect(
      Math.abs(contrast(actual.Line, seeds.surface) - contrast(derived.Line, seeds.surface)),
    ).toBeLessThanOrEqual(0.02)
    expect(contrast(derived.TextDim, seeds.surface)).toBeGreaterThanOrEqual(4.5)
    expect(contrast(derived.VoltInk, seeds.volt)).toBeGreaterThanOrEqual(7)
    for (const field of Object.keys(exported.overrides!)) {
      const omitted = { ...exported.overrides }
      delete omitted[field]
      expect(avalonDerivedFields(deriveAvalonPalette({ ...exported, overrides: omitted }))[field]).not.toBe(
        actual[field],
      )
    }
  }
})
it('derives a legible neutral ramp from only eight seeds', () => {
  const parsed = parse({
    seeds: { ...document.seeds, ground: '#131018', surface: '#1D1926', text: '#EFEAF5', volt: '#A98CFF' },
  })
  const palette = deriveAvalonPalette(parsed.document!),
    c = palette.colors
  expect(contrast(c['--muted'], c['--surface'])).toBeGreaterThanOrEqual(4.5)
  expect(contrast(c['--muted'], c['--bg'])).toBeGreaterThanOrEqual(4.5)
  expect(contrast(c['--text'], c['--surface'])).toBeGreaterThanOrEqual(7)
  expect(contrast(c['--avalon-button-ink'], c['--accent'])).toBeGreaterThanOrEqual(7)
  const ramp = [c['--avalon-well'], c['--bg'], c['--surface'], c['--raised'], palette.high!].map(luminance)
  for (let index = 1; index < ramp.length; index++) expect(ramp[index]).toBeGreaterThan(ramp[index - 1])
  expect(aaCeiling(palette)).toBeGreaterThanOrEqual(20)
})
it.each([undefined, 0, 2, 'one'])(
  'refuses unsupported schema version %s before using any colors',
  (schemaVersion) => {
    const parsed = parse({ schemaVersion, seeds: {} })
    expect(parsed.document).toBeNull()
    expect(parsed.diagnostics).toHaveLength(1)
    expect(parsed.diagnostics[0]).toMatchObject({
      file: 'test.json',
      field: 'schemaVersion',
      severity: 'error',
    })
  },
)
it.each([
  '',
  '{',
  '[1,2,3]',
  'null',
  '"just a string"',
  '{"schemaVersion":1,"seeds":4}',
  JSON.stringify({ ...document, structure: { edge: 'wide' } }),
  JSON.stringify({ ...document, seeds: { ...document.seeds, ground: 12 } }),
  '/* never closed',
])('returns a file diagnostic without throwing for malformed input %s', (text) => {
  const result = parseAvalonTheme('broken.json', text)
  expect(result.document).toBeNull()
  expect(result.diagnostics.length).toBeGreaterThan(0)
  expect(result.diagnostics.every((entry) => entry.file === 'broken.json' && entry.message.length > 0)).toBe(
    true,
  )
})
it('bounds bytes and nesting, identifies unknown root fields and rejects executable data', () => {
  expect(parseAvalonTheme('large.json', ' '.repeat(256 * 1024 + 1)).document).toBeNull()
  expect(
    parseAvalonTheme('deep.json', '['.repeat(17) + '0' + ']'.repeat(17)).diagnostics[0].message,
  ).toContain('16')
  expect(parse({ strucutre: {} }).diagnostics).toEqual([
    expect.objectContaining({
      field: 'strucutre',
      severity: 'error',
      message: expect.stringContaining('schemaVersion, id, name'),
    }),
  ])
  expect(parse({ entry: 'file:///private.js' }).document).toBeNull()
  expect(parse({ structure: JSON.parse('{"__proto__": 4, "constructor": 3}') }).document).not.toBeNull()
})
it('names missing seeds and rejects alpha, named colors and seed overrides', () => {
  const seeds = { ...document.seeds }
  delete seeds.danger
  expect(parse({ seeds }).diagnostics).toContainEqual(
    expect.objectContaining({ field: 'seeds.danger', severity: 'error' }),
  )
  expect(parse({ seeds: { ...document.seeds, ground: '#CC0F1C1E' } }).diagnostics[0].message).toMatch(
    /alpha.*transparency slider/,
  )
  expect(parse({ seeds: { ...document.seeds, ground: 'papayawhip' } }).diagnostics[0].message).toContain(
    'six hex digits',
  )
  expect(parse({ overrides: { Ground: '#000000' } }).diagnostics[0]).toMatchObject({
    field: 'overrides.Ground',
    severity: 'error',
    message: expect.stringContaining('seeds.ground'),
  })
})
it('warns about unknown and case-misspelled overrides while keeping a valid theme', () => {
  const result = parse({
    overrides: { Sparkle: '#FF0000', surfaceraised: '#333333' },
    seeds: { ...document.seeds, SurfaceRaised: '#111111' },
  })
  expect(result.document).not.toBeNull()
  expect(result.diagnostics).toHaveLength(3)
  expect(result.diagnostics.every((entry) => entry.severity === 'warning')).toBe(true)
  expect(result.diagnostics.find((entry) => entry.field === 'overrides.surfaceraised')?.message).toContain(
    'SurfaceRaised',
  )
  expect(result.diagnostics.find((entry) => entry.field === 'seeds.SurfaceRaised')?.message).toContain(
    'overrides',
  )
})
it('clamps every scalar range and reads known proportions in the wrong block with a warning', () => {
  for (const [key, [min, max, , block]] of Object.entries(AVALON_SCALARS)) {
    const other = block === 'structure' ? 'translucency' : 'structure'
    for (const [input, expected] of [
      [min - 1, min],
      [max + 1, max],
    ]) {
      const result = parse({ [other]: { [key]: input } })
      expect(result.document?.[block]?.[key]).toBe(expected)
      expect(result.diagnostics).toHaveLength(2)
      expect(result.diagnostics.every((entry) => entry.severity === 'warning')).toBe(true)
    }
  }
})
it.each(['nightshift', 'winnow', 'box-art', 'tungsten', 'My Theme', '../escape', '-prefix', 'a'.repeat(49)])(
  'refuses unsafe or calibrated id %s',
  (id) => expect(parse({ id }).document).toBeNull(),
)
it('permits authored bundled replacements and hoard before the legacy alias', () => {
  for (const id of ['bottle-green', 'rose-pine', 'hoard']) {
    const result = parse({ id })
    expect(result.document).not.toBeNull()
    registerAvalonThemes([{ file: `${id}.json`, document: result.document! }])
    expect(avalonPalette(id)?.sourceFile).toBe(`${id}.json`)
  }
})
it('reads comments and trailing commas without changing JSON strings or escape sequences', () => {
  const raw = JSON.stringify({
    ...document,
    reason: 'https://example.test/a,} /* literal */ "quoted" \\path',
  })
  expect(readAvalonJson('/* before */\n// comment\n' + raw.slice(0, -1) + ', /* tail */}')).toEqual(
    JSON.parse(raw),
  )
})
it('keeps authored opening position and effective typography through export without mutating defaults', () => {
  const result = parse({
    defaults: { transparency: 40, backdrop: 'mica', reach: 'chrome', layout: 'flush' },
    typography: { headingFont: 'Georgia', sizePercent: 110 },
  })
  const palette = { ...deriveAvalonPalette(result.document!), sourceFile: 'mine.json' }
  expect(palette.defaults).toMatchObject({
    transparency: 40,
    backdrop: 'mica',
    wallTranslucent: false,
    layout: 'flush',
  })
  const typography = { ...DEFAULT_TYPOGRAPHY, dataFont: 'Consolas', sizePercent: 120 }
  const again = parseAvalonTheme('again.json', exportAvalonPalette(palette, typography))
  expect(again.document?.defaults).toEqual(result.document?.defaults)
  expect(again.document?.typography).toEqual(typography)
  expect(palette.document?.typography?.headingFont).toBe('Georgia')
  expect(again.document?.id).toBe(palette.id)
})
it('uses bundled role defaults for absent and partial authored typography', () => {
  expect(parse().document?.typography).toEqual(DEFAULT_TYPOGRAPHY)
  expect(parse({ typography: { sizePercent: 110 } }).document?.typography).toEqual({
    ...DEFAULT_TYPOGRAPHY,
    sizePercent: 110,
  })
})
it.each([
  ['headingFont', ''],
  ['interfaceFont', 'https://example.test/font.ttf'],
  ['dataFont', 'C:\\font.ttf'],
  ['headingFont', 'Arial, Georgia'],
  ['dataFont', 'font#Family'],
  ['interfaceFont', 'Arial\n'],
])('reports the exact invalid typography field %s for %s', (field, value) => {
  const result = parse({ typography: { [field]: value } })
  expect(result.document).toBeNull()
  expect(result.diagnostics).toContainEqual(
    expect.objectContaining({ field: `typography.${field}`, severity: 'error' }),
  )
})
it.each([
  { sizePercent: 79 },
  { sizePercent: 121 },
  { sizePercent: 100.5 },
  { headingFont: null },
  { unknownFont: 'Arial' },
])('rejects malformed authored typography %j', (typography) =>
  expect(parse({ typography }).document).toBeNull(),
)
it('ignores unknown default choices, clamps percentages, and calibrated palettes have no opening preferences', () => {
  const result = parse({ defaults: { transparency: 400, backdrop: 'velvet', reach: 'everything' } })
  expect(result.document?.defaults).toEqual({ transparency: 100 })
  expect(result.diagnostics).toHaveLength(3)
  for (const palette of AVALON_PALETTES.slice(0, 4)) {
    expect(palette.defaults).toBeUndefined()
    expect(palette.sourceFile).toBeUndefined()
  }
})
it('warns about Flare reuse, danger/selection hue gaps and dormant-cover inversion without suppressing colors', () => {
  const palette = deriveAvalonPalette(
    parse({
      seeds: {
        ...document.seeds,
        amber: document.seeds.flare,
        flare: '#F0504A',
        ground: '#F4F1EC',
        surface: '#FFFFFF',
        text: '#101010',
      },
    }).document!,
  )
  const audit = inspectAvalonTheme(palette)
  expect(audit).toContainEqual(
    expect.objectContaining({ field: 'seeds.flare', message: expect.stringMatching(/Danger.*close button/) }),
  )
  expect(audit).toContainEqual(
    expect.objectContaining({
      field: 'seeds.ground',
      message: expect.stringMatching(/dormant cover.*hole punched/),
    }),
  )
  const repeated = inspectAvalonTheme(
    deriveAvalonPalette(
      parse({ seeds: { ...document.seeds, amber: document.seeds.flare, volt: document.seeds.flare } })
        .document!,
    ),
  )
  expect(repeated).toContainEqual(
    expect.objectContaining({
      field: 'seeds.amber',
      message: expect.stringMatching(/patched since you played.*unread count/),
    }),
  )
  expect(repeated).toContainEqual(
    expect.objectContaining({ field: 'seeds.flare', message: expect.stringContaining('Volt') }),
  )
})
it.each(AVALON_PALETTES.slice(0, 4))(
  'uses the slider AA arithmetic in the $id report with later wall inversion',
  (palette) => {
    const report = avalonThemeReport(palette)
    expect(report.aaCeiling).toBe(aaCeiling(palette))
    expect(report.headline).toContain(String(report.aaCeiling))
    expect(report.wallCeiling).toBeGreaterThanOrEqual(report.aaCeiling)
  },
)
