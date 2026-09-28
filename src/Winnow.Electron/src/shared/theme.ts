import type { ComponentType, ReactNode, RefObject } from 'react'
import type { LibraryGame, FeedSnapshot } from '../renderer/api/types'
import { DEFAULT_ARTWORK_EFFECTS, type ArtworkEffectOptions } from './artworkEffects'

export type { ArtworkEffectOptions } from './artworkEffects'

/** Increment for breaking changes to the data, commands, or component props a theme receives. */
export const THEME_API_VERSION = 1 as const
export type ThemePage = 'discover' | 'library' | 'details' | 'journal' | 'settings' | 'studio'
export type ThemeMode = 'desktop' | 'fullscreen'
export type ThemeSettingValue = string | number | boolean
export type ThemeColorKey = 'background' | 'surface' | 'raised' | 'text' | 'muted' | 'line' | 'cool'
export interface ThemeProfile {
  schemaVersion: 1
  themeId: string
  appearance: {
    palette: 'afterglow' | 'paper' | 'bluehour'
    accent: string
    font: 'editorial' | 'modern' | 'mono'
    density: 'comfortable' | 'compact' | 'spacious'
    radius: number
    scrim: number
    reducedMotion: boolean
    colors?: Partial<Record<ThemeColorKey, string>>
    scale?: number
    artwork?: ArtworkEffectOptions
  }
  layout: {
    navigation: 'top' | 'left'
    discoverSections: string[]
    hiddenSections: string[]
    cardStyle: 'landscape' | 'poster' | 'record'
    detailArrangement: 'aside' | 'stacked'
  }
  settings: Record<string, Record<string, ThemeSettingValue>>
}
export interface ThemeGameCardProps {
  game: LibraryGame
  reason?: string
  onOpen?: () => void
  presentation?: 'poster' | 'landscape' | 'record'
  effects?: Partial<ArtworkEffectOptions> | false
  preview?: 'flyout' | 'inline' | 'overlay' | 'none'
}
export interface ThemeArtworkEffectsProps {
  children: ReactNode
  className?: string
  effects?: Partial<ArtworkEffectOptions> | false
  /** The untransformed interactive element; defaults to the wrapper itself. */
  interactionRef?: RefObject<HTMLElement | null>
}
export interface ThemeGamePreviewProps {
  game: LibraryGame
  reason?: string
  children: ReactNode
  disabled?: boolean
  className?: string
}
export interface ThemeContext {
  mode: ThemeMode
  page: ThemePage
  selectedWorkId: number | null
  setPage(page: ThemePage): void
  openGame(workId: number): void
  toggleFullscreen(): void
  games: LibraryGame[]
  feed: FeedSnapshot | undefined
  loading: boolean
  profile: ThemeProfile
  children: ReactNode
  /** Render a host screen; this deliberately bypasses theme overrides to prevent recursion. */
  renderScreen(page?: ThemePage): ReactNode
  actions: { launch(ownershipId: number): Promise<void> }
  components: {
    GameCard: ComponentType<ThemeGameCardProps>
    Impression: ComponentType<{ releaseId: number; shelfId: string; children: ReactNode }>
    Artwork: ComponentType<{ workId: number; hero?: boolean; className?: string; eager?: boolean }>
    ArtworkEffects: ComponentType<ThemeArtworkEffectsProps>
    GamePreview: ComponentType<ThemeGamePreviewProps>
  }
}
interface ThemeSettingBase {
  id: string
  label: string
  description?: string
}
export type ThemeSetting =
  | (ThemeSettingBase & { type: 'toggle'; default: boolean })
  | (ThemeSettingBase & { type: 'select'; default: string; options: { value: string; label: string }[] })
  | (ThemeSettingBase & { type: 'range'; default: number; min: number; max: number; step?: number })
export interface ThemeDefinition {
  apiVersion: 1
  id: string
  name: string
  Shell?: ComponentType<ThemeContext>
  Discover?: ComponentType<ThemeContext>
  Library?: ComponentType<ThemeContext>
  Details?: ComponentType<ThemeContext>
  Journal?: ComponentType<ThemeContext>
  Settings?: ComponentType<ThemeContext>
  settings?: ThemeSetting[]
}

export const DEFAULT_PROFILE: ThemeProfile = {
  schemaVersion: 1,
  themeId: 'afterglow',
  appearance: {
    palette: 'afterglow',
    accent: '#efad80',
    font: 'editorial',
    density: 'comfortable',
    radius: 18,
    scrim: 55,
    reducedMotion: false,
    artwork: { ...DEFAULT_ARTWORK_EFFECTS },
  },
  layout: {
    navigation: 'top',
    discoverSections: ['hero', 'returning', 'shelves'],
    hiddenSections: [],
    cardStyle: 'poster',
    detailArrangement: 'aside',
  },
  settings: {},
}

export const PALETTES = {
  afterglow: {
    name: 'Afterglow',
    background: '#18191b',
    surface: '#202124',
    raised: '#292a2d',
    text: '#f1ece4',
    muted: '#b2afa9',
    line: '#454548',
    accent: '#efad80',
    cool: '#b5c9e4',
  },
  paper: {
    name: 'Paper trail',
    background: '#f0ebe2',
    surface: '#faf7f0',
    raised: '#e7dfd1',
    text: '#302b26',
    muted: '#686058',
    line: '#c6bcae',
    accent: '#88502f',
    cool: '#426077',
  },
  bluehour: {
    name: 'Blue hour',
    background: '#141b2c',
    surface: '#1c263b',
    raised: '#28344d',
    text: '#ecedf5',
    muted: '#b0bbd2',
    line: '#424f6c',
    accent: '#c0b3f4',
    cool: '#a3c8e5',
  },
} as const

const isRecord = (value: unknown): value is Record<string, unknown> =>
  !!value && typeof value === 'object' && !Array.isArray(value)
const idPattern = /^[a-z0-9]+(?:[.-][a-z0-9]+)*$/
const settingIdPattern = /^[a-zA-Z][a-zA-Z0-9_.-]{0,63}$/
function keys(
  value: Record<string, unknown>,
  expected: string[],
  label: string,
  optional: string[] = [],
): void {
  if (
    Object.keys(value).some((key) => !expected.includes(key) && !optional.includes(key)) ||
    expected.some((key) => !Object.prototype.hasOwnProperty.call(value, key))
  )
    throw new Error(`${label} has missing or unrecognized fields.`)
}
function enumeration(value: unknown, values: readonly string[], label: string): void {
  if (typeof value !== 'string' || !values.includes(value)) throw new Error(`${label} is not supported.`)
}
function range(value: unknown, min: number, max: number, label: string): void {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < min || value > max)
    throw new Error(`${label} must be between ${min} and ${max}.`)
}

/** Profiles contain data only. Never evaluate CSS, URLs, code, or extra properties from a preset. */
export function parseThemeProfile(value: unknown): ThemeProfile {
  if (!isRecord(value)) throw new Error('Choose a Winnow appearance profile.')
  keys(value, ['schemaVersion', 'themeId', 'appearance', 'layout', 'settings'], 'Profile')
  if (value.schemaVersion !== 1)
    throw new Error('This appearance profile needs a different version of Winnow.')
  if (typeof value.themeId !== 'string' || value.themeId.length > 80 || !idPattern.test(value.themeId))
    throw new Error('The theme identifier is invalid.')
  if (!isRecord(value.appearance) || !isRecord(value.layout) || !isRecord(value.settings))
    throw new Error('This appearance profile is incomplete.')
  const appearance = value.appearance
  keys(
    appearance,
    ['palette', 'accent', 'font', 'density', 'radius', 'scrim', 'reducedMotion'],
    'Appearance',
    ['colors', 'scale', 'artwork'],
  )
  enumeration(appearance.palette, Object.keys(PALETTES), 'Palette')
  if (typeof appearance.accent !== 'string' || !/^#[\da-f]{6}$/i.test(appearance.accent))
    throw new Error('Accent must be a six-digit hex color.')
  enumeration(appearance.font, ['editorial', 'modern', 'mono'], 'Typography')
  enumeration(appearance.density, ['comfortable', 'compact', 'spacious'], 'Density')
  range(appearance.radius, 0, 32, 'Corner radius')
  range(appearance.scrim, 20, 90, 'Artwork shade')
  if (typeof appearance.reducedMotion !== 'boolean') throw new Error('Reduced motion must be on or off.')
  if ('scale' in appearance) range(appearance.scale, 85, 130, 'Interface scale')
  if ('artwork' in appearance) {
    if (!isRecord(appearance.artwork)) throw new Error('Artwork effects must be a settings map.')
    const artwork = appearance.artwork
    keys(artwork, Object.keys(DEFAULT_ARTWORK_EFFECTS), 'Artwork effects')
    enumeration(artwork.finish, ['off', 'matte', 'satin', 'foil'], 'Cover finish')
    enumeration(artwork.foilMetal, ['silver', 'gold', 'holographic'], 'Highlight material')
    range(artwork.intensity, 0, 100, 'Finish intensity')
    range(artwork.tilt, 0, 12, 'Maximum tilt')
    range(artwork.foilStrength, 0, 100, 'Foil strength')
    range(artwork.foilThreshold, 40, 95, 'Brightness cutoff')
    for (const key of ['followPointer', 'floating', 'highlightFoil'])
      if (typeof artwork[key] !== 'boolean') throw new Error('Artwork toggles must be on or off.')
  }
  if ('colors' in appearance) {
    if (!isRecord(appearance.colors)) throw new Error('Custom palette colors must be a color map.')
    const colorKeys = ['background', 'surface', 'raised', 'text', 'muted', 'line', 'cool']
    for (const [key, color] of Object.entries(appearance.colors)) {
      if (!colorKeys.includes(key) || typeof color !== 'string' || !/^#[\da-f]{6}$/i.test(color))
        throw new Error('Custom palette colors must use known color names and six-digit hex values.')
    }
  }
  const layout = value.layout
  keys(
    layout,
    ['navigation', 'discoverSections', 'hiddenSections', 'cardStyle', 'detailArrangement'],
    'Layout',
  )
  enumeration(layout.navigation, ['top', 'left'], 'Navigation')
  enumeration(layout.cardStyle, ['landscape', 'poster', 'record'], 'Card style')
  enumeration(layout.detailArrangement, ['aside', 'stacked'], 'Details layout')
  const sections = ['hero', 'returning', 'shelves']
  if (
    !Array.isArray(layout.discoverSections) ||
    layout.discoverSections.length !== 3 ||
    new Set(layout.discoverSections).size !== 3 ||
    layout.discoverSections.some((section) => !sections.includes(section))
  )
    throw new Error('Discover must contain each section once.')
  if (
    !Array.isArray(layout.hiddenSections) ||
    layout.hiddenSections.length > 2 ||
    new Set(layout.hiddenSections).size !== layout.hiddenSections.length ||
    layout.hiddenSections.some((section) => !sections.includes(section))
  )
    throw new Error('Keep at least one Discover section visible.')
  if (Object.keys(value.settings).length > 32)
    throw new Error('The profile contains too many theme settings.')
  for (const [id, settings] of Object.entries(value.settings)) {
    if (!idPattern.test(id) || id.length > 80 || !isRecord(settings) || Object.keys(settings).length > 32)
      throw new Error('A theme settings group is invalid.')
    for (const [key, setting] of Object.entries(settings)) {
      if (!settingIdPattern.test(key) || ['__proto__', 'constructor', 'prototype'].includes(key))
        throw new Error('A theme setting identifier is invalid.')
      if (typeof setting === 'boolean') continue
      if (typeof setting === 'number' && Number.isFinite(setting) && Math.abs(setting) <= 1e6) continue
      if (typeof setting === 'string' && /^[\w .#-]{0,120}$/.test(setting)) continue
      throw new Error('Theme settings must contain simple values, without links or code.')
    }
  }
  return structuredClone(value) as unknown as ThemeProfile
}

export function contrastRatio(first: string, second: string): number {
  const luminance = (hex: string): number => {
    const values = [1, 3, 5]
      .map((offset) => parseInt(hex.slice(offset, offset + 2), 16) / 255)
      .map((value) => (value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4))
    return values[0] * 0.2126 + values[1] * 0.7152 + values[2] * 0.0722
  }
  const a = luminance(first),
    b = luminance(second)
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

export function resolvedThemeColors(profile: ThemeProfile): Record<ThemeColorKey | 'accent', string> {
  return {
    ...PALETTES[profile.appearance.palette],
    ...profile.appearance.colors,
    accent: profile.appearance.accent,
  }
}

export function themeSettingValues(
  theme: ThemeDefinition,
  profile: ThemeProfile,
): Record<string, ThemeSettingValue> {
  const values: Record<string, ThemeSettingValue> = {}
  for (const field of theme.settings ?? []) {
    const candidate = profile.settings[theme.id]?.[field.id]
    const valid =
      field.type === 'toggle'
        ? typeof candidate === 'boolean'
        : field.type === 'select'
          ? typeof candidate === 'string' && field.options.some((option) => option.value === candidate)
          : typeof candidate === 'number' &&
            Number.isFinite(candidate) &&
            candidate >= field.min &&
            candidate <= field.max
    values[field.id] = valid ? candidate! : field.default
  }
  return values
}

export function validateThemeDefinition(value: unknown, expectedId?: string): ThemeDefinition {
  if (!isRecord(value) || value.apiVersion !== 1)
    throw new Error('This theme does not support Winnow theme API 1.')
  if (typeof value.id !== 'string' || !idPattern.test(value.id) || (expectedId && value.id !== expectedId))
    throw new Error('The theme identifier does not match its package.')
  if (typeof value.name !== 'string' || !value.name.trim() || value.name.length > 100)
    throw new Error('The theme needs a readable name.')
  const screens = ['Shell', 'Discover', 'Library', 'Details', 'Journal', 'Settings']
  for (const screen of screens)
    if (value[screen] !== undefined && typeof value[screen] !== 'function')
      throw new Error(`${screen} must be a React component function.`)
  if (value.settings !== undefined) {
    if (!Array.isArray(value.settings) || value.settings.length > 32)
      throw new Error('Theme settings must be a list of at most 32 fields.')
    const seen = new Set<string>()
    for (const field of value.settings) {
      if (
        !isRecord(field) ||
        typeof field.id !== 'string' ||
        !settingIdPattern.test(field.id) ||
        seen.has(field.id) ||
        ['constructor', 'prototype'].includes(field.id)
      )
        throw new Error('Theme setting identifiers must be valid and unique.')
      seen.add(field.id)
      if (typeof field.label !== 'string' || !field.label.trim() || field.label.length > 100)
        throw new Error('Every theme setting needs a label.')
      if (
        field.description !== undefined &&
        (typeof field.description !== 'string' || field.description.length > 400)
      )
        throw new Error('Theme setting description is too long.')
      if (field.type === 'toggle') {
        if (typeof field.default !== 'boolean') throw new Error('A toggle needs an on/off default.')
      } else if (field.type === 'range') {
        range(field.min, -1e6, 1e6, 'Minimum')
        range(field.max, Number(field.min), 1e6, 'Maximum')
        range(field.default, Number(field.min), Number(field.max), 'Default')
        if (
          field.step !== undefined &&
          (typeof field.step !== 'number' || !Number.isFinite(field.step) || field.step <= 0)
        )
          throw new Error('Range step must be positive.')
      } else if (field.type === 'select') {
        if (
          !Array.isArray(field.options) ||
          field.options.length < 1 ||
          field.options.length > 32 ||
          field.options.some(
            (option) =>
              !isRecord(option) ||
              typeof option.value !== 'string' ||
              !/^[\w .#-]{1,120}$/.test(option.value) ||
              typeof option.label !== 'string' ||
              !option.label.trim() ||
              option.label.length > 100,
          ) ||
          !field.options.some((option) => option.value === field.default) ||
          new Set(field.options.map((option) => option.value)).size !== field.options.length
        )
          throw new Error('A choice setting needs unique options and a matching default.')
      } else throw new Error('Theme setting type is not supported.')
    }
  }
  return value as unknown as ThemeDefinition
}
