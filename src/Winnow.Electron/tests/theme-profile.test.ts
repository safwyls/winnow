import { describe, expect, it } from 'vitest'
import {
  DEFAULT_PROFILE,
  PALETTES,
  contrastRatio,
  parseThemeProfile,
  resolvedThemeColors,
  themeSettingValues,
  validateThemeDefinition,
  type ThemeDefinition,
} from '../src/shared/theme'
import { loadExternalTheme, validateThemeAssetUrl } from '../src/renderer/theming/runtime'

describe('portable appearance profiles', () => {
  it('round trips layout and appearance without retaining caller references', () => {
    const source = structuredClone(DEFAULT_PROFILE)
    source.layout.navigation = 'left'
    source.layout.discoverSections = ['shelves', 'returning', 'hero']
    source.appearance.palette = 'paper'
    const parsed = parseThemeProfile(JSON.parse(JSON.stringify(source)))
    expect(parsed).toEqual(source)
    parsed.layout.discoverSections.reverse()
    expect(source.layout.discoverSections[0]).toBe('shelves')
  })

  it.each([
    { schemaVersion: 2 },
    { extra: true },
    { themeId: 'https://untrusted.invalid/module.js' },
    { appearance: { ...DEFAULT_PROFILE.appearance, accent: 'url(javascript:alert(1))' } },
    { appearance: { ...DEFAULT_PROFILE.appearance, palette: 'unknown' } },
    { appearance: { ...DEFAULT_PROFILE.appearance, radius: 500 } },
    { appearance: { ...DEFAULT_PROFILE.appearance, scrim: Number.NaN } },
    { appearance: { ...DEFAULT_PROFILE.appearance, scale: 131 } },
    { appearance: { ...DEFAULT_PROFILE.appearance, scale: '100' } },
    {
      appearance: { ...DEFAULT_PROFILE.appearance, colors: { background: 'url(https://untrusted.invalid)' } },
    },
    { appearance: { ...DEFAULT_PROFILE.appearance, colors: { stylesheet: '#000000' } } },
    { appearance: { ...DEFAULT_PROFILE.appearance, colors: { text: '#fff' } } },
    { layout: { ...DEFAULT_PROFILE.layout, discoverSections: ['hero', 'hero', 'shelves'] } },
    { layout: { ...DEFAULT_PROFILE.layout, hiddenSections: ['hero', 'returning', 'shelves'] } },
    { layout: { ...DEFAULT_PROFILE.layout, cardStyle: 'code' } },
    { settings: { example: { source: 'https://untrusted.invalid/code.js' } } },
    { settings: { example: { source: { code: 'nested objects are not settings' } } } },
    { settings: JSON.parse('{"example":{"__proto__":{"polluted":true}}}') },
  ])('rejects unsafe, unsupported, or malformed fields: %j', (patch) => {
    expect(() => parseThemeProfile({ ...structuredClone(DEFAULT_PROFILE), ...patch })).toThrow()
    expect(({} as Record<string, unknown>).polluted).toBeUndefined()
  })

  it('accepts optional custom colors and scale while preserving older version-one profiles', () => {
    const oldProfile = parseThemeProfile(structuredClone(DEFAULT_PROFILE))
    expect(oldProfile.appearance.colors).toBeUndefined()
    expect(oldProfile.appearance.scale).toBeUndefined()
    const custom = parseThemeProfile({
      ...DEFAULT_PROFILE,
      appearance: {
        ...DEFAULT_PROFILE.appearance,
        scale: 120,
        colors: { background: '#020204', surface: '#161628', text: '#eeeeee' },
      },
    })
    expect(custom.appearance.scale).toBe(120)
    expect(resolvedThemeColors(custom)).toMatchObject({
      background: '#020204',
      surface: '#161628',
      text: '#eeeeee',
      muted: PALETTES.afterglow.muted,
    })
    expect(parseThemeProfile(JSON.parse(JSON.stringify(custom)))).toEqual(custom)
  })

  it('uses only declared theme settings and validates values against their schemas', () => {
    const definition: ThemeDefinition = {
      apiVersion: 1,
      id: 'sample',
      name: 'Sample',
      settings: [
        { id: 'rows', label: 'Rows', type: 'range', default: 4, min: 2, max: 8 },
        { id: 'cover', label: 'Covers', type: 'toggle', default: true },
        {
          id: 'style',
          label: 'Style',
          type: 'select',
          default: 'quiet',
          options: [
            { value: 'quiet', label: 'Quiet' },
            { value: 'bold', label: 'Bold' },
          ],
        },
      ],
    }
    const profile = structuredClone(DEFAULT_PROFILE)
    profile.settings.sample = { rows: 99, cover: false, style: 'bold', injected: true }
    expect(themeSettingValues(definition, profile)).toEqual({ rows: 4, cover: false, style: 'bold' })
  })

  it('ships readable panel text and sensible contrast warnings', () => {
    expect(contrastRatio('#ffffff', '#000000')).toBeCloseTo(21)
    expect(contrastRatio('#ffffff', '#ffffff')).toBe(1)
    for (const palette of Object.values(PALETTES)) {
      expect(contrastRatio(palette.text, palette.surface)).toBeGreaterThan(7)
      expect(contrastRatio(palette.muted, palette.surface)).toBeGreaterThan(4.5)
      expect(contrastRatio(palette.accent, palette.surface)).toBeGreaterThan(4.5)
    }
  })
})

describe('developer theme contract', () => {
  it('accepts partial screen replacement, leaving host fallbacks available', () => {
    const theme = { apiVersion: 1, id: 'reading-room', name: 'Reading room', Shell: () => null }
    expect(validateThemeDefinition(theme, 'reading-room')).toBe(theme)
  })

  it.each([
    { apiVersion: 2, id: 'reading-room', name: 'Room' },
    { apiVersion: 1, id: 'different', name: 'Room' },
    { apiVersion: 1, id: 'reading-room', name: 'Room', Library: 'not a component' },
    {
      apiVersion: 1,
      id: 'reading-room',
      name: 'Room',
      settings: [{ id: 'size', label: 'Size', type: 'range', min: 4, max: 1, default: 2 }],
    },
    {
      apiVersion: 1,
      id: 'reading-room',
      name: 'Room',
      settings: [
        {
          id: 'font',
          label: 'Font',
          type: 'select',
          default: 'absent',
          options: [{ value: 'serif', label: 'Serif' }],
        },
      ],
    },
  ])('rejects incompatible modules: %j', (module) => {
    expect(() => validateThemeDefinition(module, 'reading-room')).toThrow()
  })

  it.each([
    'https://reading-room/main.mjs',
    'file:///main.mjs',
    'winnow-theme://other/main.mjs',
    'winnow-theme://reading-room/main.mjs?script=1',
    'winnow-theme://reading-room/main.mjs#script',
    'winnow-theme://reading-room/main.mjs?v=one&v=two',
  ])('rejects an entry outside the installed package: %s', (url) => {
    expect(() => validateThemeAssetUrl(url, 'reading-room')).toThrow()
  })

  it('accepts installed assets with a bounded version cache key', () => {
    expect(validateThemeAssetUrl('winnow-theme://reading-room/main.mjs?v=1.0.0', 'reading-room')).toBe(
      'winnow-theme://reading-room/main.mjs?v=1.0.0',
    )
  })

  it('checks compatibility before any module is evaluated', async () => {
    let evaluated = false
    await expect(
      loadExternalTheme(
        {
          id: 'reading-room',
          name: 'Room',
          apiVersion: 2,
          version: '1.0.0',
          entry: 'winnow-theme://reading-room/main.mjs',
        },
        async () => {
          evaluated = true
          return {}
        },
      ),
    ).rejects.toThrow('different version')
    expect(evaluated).toBe(false)
  })

  it('requires a default export whose identity matches the installed package', async () => {
    const theme = {
      id: 'reading-room',
      name: 'Room',
      apiVersion: 1,
      version: '1.0.0',
      entry: 'winnow-theme://reading-room/main.mjs',
    }
    await expect(loadExternalTheme(theme, async () => ({}))).rejects.toThrow('API 1')
    await expect(
      loadExternalTheme(theme, async () => ({ default: { apiVersion: 1, id: 'other', name: 'Other' } })),
    ).rejects.toThrow('identifier')
  })
})
