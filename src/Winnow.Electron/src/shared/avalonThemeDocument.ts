import { parseTypography, type ThemeTypography } from './typography'

export const MAX_AVALON_THEME_BYTES = 256 * 1024
export const MAX_AVALON_THEMES = 64
export const AVALON_SEEDS = [
  'ground',
  'surface',
  'text',
  'flare',
  'volt',
  'amber',
  'azure',
  'danger',
] as const
export const AVALON_DERIVED = [
  'Well',
  'SurfaceRaised',
  'SurfaceHigh',
  'Line',
  'TextDim',
  'TextFaint',
  'VoltInk',
  'VoltHover',
  'VoltPress',
  'DangerHover',
  'DangerPress',
  'DangerInk',
  'TranslucentSurface',
  'TranslucentChromeGround',
  'TranslucentTextDim',
  'TranslucentTextFaint',
] as const
export const AVALON_SCALARS = {
  elevation: [0.005, 0.3, 0.05, 'structure'],
  wellDepth: [0.05, 1, 0.55, 'structure'],
  edge: [1.05, 21, 1.6, 'structure'],
  dimValue: [0.1, 1, 0.68, 'structure'],
  dimChroma: [0, 3, 0.41, 'structure'],
  voltInkContrast: [3, 21, 9.5, 'structure'],
  faintValue: [0.05, 1, 0.5, 'structure'],
  faintChroma: [0, 3, 0.65, 'structure'],
  chromeInk: [0.05, 1, 0.48, 'translucency'],
  groundInk: [0.05, 1, 0.44, 'translucency'],
  dimLift: [0.5, 2, 1.14, 'translucency'],
  faintLift: [0.5, 2, 1.2, 'translucency'],
} as const
export interface AvalonThemeDocument {
  schemaVersion: 1
  id: string
  name: string
  reason: string
  variant?: 'light' | 'dark'
  seeds: Record<string, string>
  structure?: Record<string, number>
  translucency?: Record<string, number>
  overrides?: Record<string, string>
  defaults?: {
    transparency?: number
    backdrop?: 'acrylic' | 'mica'
    reach?: 'chrome' | 'chrome-and-wall'
    layout?: 'floating' | 'flush'
  }
  typography?: ThemeTypography
}
export interface AvalonThemeDiagnostic {
  severity: 'error' | 'warning'
  file: string
  field: string
  message: string
}
export interface AvalonThemeFile {
  file: string
  document: AvalonThemeDocument
}
export interface AvalonThemeCatalogue {
  themes: AvalonThemeFile[]
  diagnostics: AvalonThemeDiagnostic[]
}
const calibrated = new Set(['winnow', 'nightshift', 'tungsten', 'box-art'])
const fields = [
  'schemaVersion',
  'id',
  'name',
  'reason',
  'variant',
  'seeds',
  'structure',
  'translucency',
  'defaults',
  'typography',
  'overrides',
]
const record = (value: unknown): value is Record<string, unknown> =>
  !!value && typeof value === 'object' && !Array.isArray(value)

/** JSON comments and trailing commas are authoring conveniences, never executable input. */
export function readAvalonJson(text: string): unknown {
  let result = '',
    quoted = false,
    escaped = false,
    depth = 0
  for (let i = 0; i < text.length; i++) {
    const char = text[i],
      next = text[i + 1]
    if (quoted) {
      result += char
      if (escaped) escaped = false
      else if (char === '\\') escaped = true
      else if (char === '"') quoted = false
    } else if (char === '"') {
      quoted = true
      result += char
    } else if (char === '/' && next === '/') {
      while (i < text.length && text[i] !== '\n') i++
      result += '\n'
    } else if (char === '/' && next === '*') {
      const end = text.indexOf('*/', i + 2)
      if (end < 0) throw new Error('Unclosed JSON comment.')
      result += ' '
      i = end + 1
    } else {
      if (char === '{' || char === '[') {
        if (++depth > 16) throw new Error('Theme nesting exceeds 16 levels.')
      }
      if (char === '}' || char === ']') depth--
      result += char
    }
  }
  // The second pass must also respect strings (including commas inside a reason).
  let clean = ''
  quoted = false
  escaped = false
  for (let i = 0; i < result.length; i++) {
    const char = result[i]
    if (!quoted && char === ',') {
      let next = i + 1
      while (/\s/.test(result[next] ?? '') && next < result.length) next++
      if (result[next] === '}' || result[next] === ']') continue
    }
    clean += char
    if (quoted) {
      if (escaped) escaped = false
      else if (char === '\\') escaped = true
      else if (char === '"') quoted = false
    } else if (char === '"') quoted = true
  }
  return JSON.parse(clean.replace(/^\uFEFF/, ''))
}

/** Returns diagnostics for bad files so one authoring error cannot disable the catalogue. */
export function parseAvalonTheme(
  file: string,
  text: string,
): { document: AvalonThemeDocument | null; diagnostics: AvalonThemeDiagnostic[] } {
  const diagnostics: AvalonThemeDiagnostic[] = []
  const log = (severity: 'error' | 'warning', field: string, message: string) =>
    diagnostics.push({ severity, file, field, message })
  const error = (field: string, message: string) => log('error', field, message)
  const warn = (field: string, message: string) => log('warning', field, message)
  const fail = () => ({ document: null, diagnostics })
  if (new TextEncoder().encode(text).length > MAX_AVALON_THEME_BYTES) {
    error('', 'Theme files cannot exceed 256 KB.')
    return fail()
  }
  let input: unknown
  try {
    input = readAvalonJson(text)
  } catch (problem) {
    error('', problem instanceof Error ? problem.message : 'Invalid JSON.')
    return fail()
  }
  if (!record(input)) {
    error('', 'A theme must be a JSON object.')
    return fail()
  }
  if (input.schemaVersion !== 1) {
    error(
      'schemaVersion',
      input.schemaVersion == null
        ? 'schemaVersion is missing; this build reads version 1.'
        : `This file uses version ${String(input.schemaVersion)}; this build reads version 1. Update Winnow or check the example file.`,
    )
    return fail()
  }
  const unknown = Object.keys(input).find((key) => !fields.includes(key))
  if (unknown) {
    error(unknown, `Unknown field. Expected ${fields.join(', ')}.`)
    return fail()
  }
  const identity = (key: 'id' | 'name' | 'reason', max: number) => {
    const value = input[key]
    if (typeof value !== 'string' || !value.trim()) {
      error(key, `${key} is required.`)
      return ''
    }
    const trimmed = value.trim()
    if (trimmed.length > max && key !== 'id')
      warn(key, `Longer than ${max} characters; the displayed value is shortened.`)
    return key === 'id' ? trimmed : trimmed.slice(0, max)
  }
  const id = identity('id', 48),
    name = identity('name', 48),
    reason = identity('reason', 400)
  if (!/^[a-z0-9][a-z0-9-]{0,47}$/.test(id))
    error(
      'id',
      'Use lower-case letters, digits and hyphens, starting with a letter or digit, at most 48 characters.',
    )
  else if (calibrated.has(id))
    error('id', 'This id belongs to a calibrated built-in theme; choose another id.')
  if (input.variant != null && input.variant !== 'light' && input.variant !== 'dark')
    error('variant', 'Expected light or dark.')
  const colors = (value: unknown, block: 'seeds' | 'overrides') => {
    const result: Record<string, string> = Object.create(null)
    if (value == null && block === 'overrides') return result
    if (!record(value)) {
      error(block, 'Expected an object of six-digit hex colors.')
      return result
    }
    const keys: readonly string[] = block === 'seeds' ? AVALON_SEEDS : AVALON_DERIVED
    for (const [key, color] of Object.entries(value)) {
      if (!keys.includes(key)) {
        const seed = AVALON_SEEDS.find((seed) => seed === key.toLowerCase())
        if (block === 'overrides' && seed)
          error(`${block}.${key}`, `Place this color in seeds.${seed}; seeds cannot be overridden.`)
        else {
          const suggestion = keys.find((known) => known.toLowerCase() === key.toLowerCase())
          warn(
            `${block}.${key}`,
            suggestion
              ? `Unknown color; did you mean ${suggestion}?`
              : `Unknown color. ${block === 'seeds' && (AVALON_DERIVED as readonly string[]).includes(key) ? 'Place derived colors in overrides.' : `Expected ${keys.join(', ')}.`}`,
          )
        }
        continue
      }
      if (typeof color !== 'string' || !/^#[\da-f]{6}$/i.test(color))
        error(
          `${block}.${key}`,
          typeof color === 'string' && /^#[\da-f]{8}$/i.test(color)
            ? 'Colors cannot contain alpha; use the transparency slider.'
            : 'Expected # followed by six hex digits.',
        )
      else result[key] = color.toUpperCase()
    }
    if (block === 'seeds')
      for (const key of AVALON_SEEDS)
        if (!Object.hasOwn(value, key)) error(`seeds.${key}`, 'This seed is missing.')
    return result
  }
  const seeds = colors(input.seeds, 'seeds'),
    overrides = colors(input.overrides, 'overrides')
  const shape = { structure: {} as Record<string, number>, translucency: {} as Record<string, number> }
  for (const block of ['structure', 'translucency'] as const) {
    if (input[block] == null) continue
    if (!record(input[block])) {
      error(block, 'Expected an object of numeric proportions.')
      continue
    }
    for (const [key, value] of Object.entries(input[block])) {
      const scalar = Object.hasOwn(AVALON_SCALARS, key)
        ? AVALON_SCALARS[key as keyof typeof AVALON_SCALARS]
        : undefined
      if (!scalar) {
        warn(`${block}.${key}`, `Unknown proportion; expected ${Object.keys(AVALON_SCALARS).join(', ')}.`)
        continue
      }
      if (typeof value !== 'number' || !Number.isFinite(value)) {
        error(`${block}.${key}`, 'Expected a finite number.')
        continue
      }
      const [min, max, , intended] = scalar
      if (block !== intended)
        warn(`${block}.${key}`, `This proportion belongs in ${intended}; its value is still read.`)
      if (value < min || value > max) warn(`${block}.${key}`, `Clamped to the supported range ${min}–${max}.`)
      shape[intended][key] = Math.min(max, Math.max(min, value))
    }
  }
  let defaults: AvalonThemeDocument['defaults']
  if (input.defaults != null) {
    if (!record(input.defaults)) error('defaults', 'Expected an object of opening preferences.')
    else {
      defaults = {}
      for (const [key, value] of Object.entries(input.defaults)) {
        if (key === 'transparency') {
          if (!Number.isInteger(value)) error('defaults.transparency', 'Expected a whole percentage.')
          else {
            if (Number(value) < 0 || Number(value) > 100) warn('defaults.transparency', 'Clamped to 0–100.')
            defaults.transparency = Math.min(100, Math.max(0, Number(value)))
          }
        } else if (['backdrop', 'reach', 'layout'].includes(key)) {
          const choices = {
            backdrop: ['acrylic', 'mica'],
            reach: ['chrome', 'chrome-and-wall'],
            layout: ['floating', 'flush'],
          }[key]!
          if (!choices.includes(value as string))
            warn(`defaults.${key}`, `Expected ${choices.join(' or ')}; this preference is ignored.`)
          else Object.assign(defaults, { [key]: value })
        } else error(`defaults.${key}`, 'Unknown opening preference.')
      }
    }
  }
  let typography: ThemeTypography | undefined
  try {
    typography = parseTypography(input.typography ?? {})
  } catch (problem) {
    const message = problem instanceof Error ? problem.message : 'Invalid typography.'
    const role = ['headingFont', 'interfaceFont', 'dataFont'].find((key) => message.startsWith(key))
    error(role ? `typography.${role}` : 'typography', message)
  }
  if (diagnostics.some((entry) => entry.severity === 'error')) return fail()
  return {
    document: {
      schemaVersion: 1,
      id,
      name,
      reason,
      seeds,
      ...shape,
      overrides,
      ...(input.variant ? { variant: input.variant as 'light' | 'dark' } : {}),
      ...(defaults && Object.keys(defaults).length ? { defaults } : {}),
      ...(typography ? { typography } : {}),
    },
    diagnostics,
  }
}
