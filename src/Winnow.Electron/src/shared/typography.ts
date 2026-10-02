export interface ThemeTypography {
  headingFont: string
  interfaceFont: string
  dataFont: string
  sizePercent: number
}

export const DEFAULT_TYPOGRAPHY: ThemeTypography = {
  headingFont: 'Bricolage Grotesque',
  interfaceFont: 'Plus Jakarta Sans',
  dataFont: 'IBM Plex Mono',
  sizePercent: 100,
}

const authoredTypography = new Map<string, ThemeTypography>()
/** Catalog defaults remain separate from explicit profile edits so reset and hot reload retain their meaning. */
export function setAuthoredTypography(entries: Array<{ id: string; typography?: ThemeTypography }>): void {
  authoredTypography.clear()
  for (const entry of entries) if (entry.typography) authoredTypography.set(entry.id, parseTypography(entry.typography))
}
export function defaultTypography(id: string): ThemeTypography { return authoredTypography.get(id) ?? DEFAULT_TYPOGRAPHY }

export function validFontFamily(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length > 0 &&
    value.length <= 128 &&
    value.trim() === value &&
    !/[\u0000-\u001f\u007f-\u009f:/\\#,;]/.test(value)
  )
}

/** A single family name cannot introduce a font URL or a CSS fallback list. */
export function parseTypography(value: unknown): ThemeTypography {
  if (!value || typeof value !== 'object' || Array.isArray(value))
    throw new Error('Typography must contain font names and a text size.')
  const input = value as Record<string, unknown>
  if (Object.keys(input).some((key) => !Object.hasOwn(DEFAULT_TYPOGRAPHY, key)))
    throw new Error('Typography contains an unknown field.')
  const result = { ...DEFAULT_TYPOGRAPHY, ...input }
  for (const key of ['headingFont', 'interfaceFont', 'dataFont'] as const)
    if (!validFontFamily(result[key])) throw new Error(`${key} must be a plain font family name.`)
  if (
    !Number.isInteger(result.sizePercent) ||
    Number(result.sizePercent) < 80 ||
    Number(result.sizePercent) > 120
  )
    throw new Error('Theme text size must be a whole percentage from 80 to 120.')
  return result as ThemeTypography
}

const bundled: Record<string, string> = {
  'bricolage grotesque': 'Avalon Display',
  'plus jakarta sans': 'Avalon Body',
  'ibm plex mono': 'Avalon Data',
}

export function fontFamilyStack(family: string, role: 'headingFont' | 'interfaceFont' | 'dataFont'): string {
  const requested = bundled[family.toLocaleLowerCase()] ?? family
  const fallback = bundled[DEFAULT_TYPOGRAPHY[role].toLocaleLowerCase()]
  return (
    [...new Set([requested, fallback])].map((name) => JSON.stringify(name)).join(', ') +
    (role === 'dataFont' ? ', monospace' : ', sans-serif')
  )
}
