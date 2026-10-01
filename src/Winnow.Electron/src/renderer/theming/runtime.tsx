import * as React from 'react'
import {
  Component,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type Dispatch,
  type ReactNode,
  type SetStateAction,
} from 'react'
import type { ThemePackage } from '../../shared/bridge'
import {
  avalonPalette,
  avalonPalettes,
  avalonPaletteStyle,
  registerAvalonThemes,
} from '../themes/avalon-palettes'
import type { AvalonThemeCatalogue } from '../../shared/avalonThemeDocument'
import { migrateAvaloniaPalette } from '../themes/avalon-migration'
import { fontFamilyStack } from '../../shared/typography'
import {
  DEFAULT_PROFILE,
  avalonPaletteId,
  THEME_API_VERSION,
  contrastRatio,
  parseThemeProfile,
  resolvedThemeColors,
  resolvedTypography,
  selectThemeProfile,
  themeSettingValues,
  validateThemeDefinition,
  type ThemeDefinition,
  type ThemeProfile,
} from '../../shared/theme'

export interface PublicThemeSDK {
  apiVersion: 1
  React: typeof React
  createElement: typeof React.createElement
  defineTheme: typeof validateThemeDefinition
  settings: typeof themeSettingValues
}
declare global {
  interface Window {
    WinnowThemeSDK: PublicThemeSDK
  }
}

/** External modules share the host's React instance, including its hooks dispatcher. */
export function installThemeSDK(): void {
  if (!window.WinnowThemeSDK)
    Object.defineProperty(window, 'WinnowThemeSDK', {
      value: Object.freeze({
        apiVersion: THEME_API_VERSION,
        React,
        createElement: React.createElement,
        defineTheme: validateThemeDefinition,
        settings: themeSettingValues,
      }),
      configurable: false,
      writable: false,
    })
}

export function validateThemeAssetUrl(url: string, id: string): string {
  const parsed = new URL(url)
  const query = [...parsed.searchParams.entries()]
  const validQuery =
    query.length === 0 || (query.length === 1 && query[0][0] === 'v' && /^[\w.+-]{1,80}$/.test(query[0][1]))
  if (
    parsed.protocol !== 'winnow-theme:' ||
    parsed.hostname !== id ||
    parsed.username ||
    parsed.password ||
    parsed.port ||
    !validQuery ||
    parsed.hash ||
    !parsed.pathname ||
    parsed.pathname === '/'
  )
    throw new Error('The theme asset must come from its installed package.')
  return parsed.href
}

export async function loadExternalTheme(
  theme: ThemePackage,
  importer: (url: string) => Promise<{ default?: unknown }> = (url) => import(/* @vite-ignore */ url),
): Promise<ThemeDefinition> {
  if (theme.apiVersion !== THEME_API_VERSION)
    throw new Error(`${theme.name} needs a different version of Winnow.`)
  const url = validateThemeAssetUrl(theme.entry, theme.id)
  if (theme.css) validateThemeAssetUrl(theme.css, theme.id)
  const module = await importer(url)
  return validateThemeDefinition(module.default, theme.id)
}

export function applyThemeProfile(profile: ThemeProfile, root: HTMLElement = document.documentElement): void {
  const palette = resolvedThemeColors(profile)
  const typography = resolvedTypography(profile)
  const avalonStyle = profile.themeId === 'avalon' ? avalonPaletteStyle(avalonPaletteId(profile)) : undefined
  const rgb = (hex: string) =>
    [1, 3, 5].map((offset) => parseInt(hex.slice(offset, offset + 2), 16) / 255).join(' ')
  const variables: Record<string, string> = {
    bg: palette.background,
    surface: palette.surface,
    raised: palette.raised,
    text: palette.text,
    muted: palette.muted,
    line: palette.line,
    accent: profile.appearance.accent,
    'accent-text':
      contrastRatio('#000000', profile.appearance.accent) >=
      contrastRatio('#ffffff', profile.appearance.accent)
        ? '#000000'
        : '#ffffff',
    cool: palette.cool,
    'accent-foreground': profile.appearance.accent,
    'cool-foreground': palette.cool,
    'portal-rim-a': rgb(palette.accent),
    'portal-rim-b': rgb(palette.cool),
    'font-display':
      profile.appearance.font === 'editorial'
        ? 'Newsreader, Georgia, serif'
        : profile.appearance.font === 'mono'
          ? '"JetBrains Mono", Consolas, monospace'
          : '"DM Sans", "Segoe UI", sans-serif',
    'font-body':
      profile.appearance.font === 'mono'
        ? '"JetBrains Mono", Consolas, monospace'
        : '"DM Sans", "Segoe UI", sans-serif',
    'font-mono': '"JetBrains Mono", Consolas, monospace',
    radius: `${profile.appearance.radius}px`,
    density: String(
      profile.appearance.density === 'compact' ? 0.8 : profile.appearance.density === 'spacious' ? 1.2 : 1,
    ),
    scrim: String(profile.appearance.scrim / 100),
    'interface-scale': String((profile.appearance.scale ?? 100) / 100),
    'theme-text-scale': profile.themeId === 'avalon' ? String(typography.sizePercent / 100) : '1',
  }
  for (const [key, value] of Object.entries(variables)) root.style.setProperty(`--${key}`, value)
  // Dialogs and previews render under body, outside a theme's shell.
  for (const key of [
    'flare',
    'well',
    'button-ink',
    'amber',
    'danger',
    'amber-foreground',
    'danger-foreground',
    'tile-chip-ground',
    'art-veil',
    'raised-faint',
    'lightbox-fill',
    'lightbox-active-fill',
  ])
    root.style.removeProperty(`--avalon-${key}`)
  if (profile.themeId === 'avalon') {
    root.style.setProperty('--font-display', fontFamilyStack(typography.headingFont, 'headingFont'))
    root.style.setProperty('--font-body', fontFamilyStack(typography.interfaceFont, 'interfaceFont'))
    root.style.setProperty('--font-mono', fontFamilyStack(typography.dataFont, 'dataFont'))
  }
  if (avalonStyle) {
    for (const [key, value] of Object.entries(avalonStyle))
      if (key.startsWith('--')) root.style.setProperty(key, String(value))
    const accent = root.style.getPropertyValue('--accent'),
      cool = root.style.getPropertyValue('--cool')
    root.style.setProperty(
      '--accent-text',
      contrastRatio('#000000', accent) >= contrastRatio('#ffffff', accent) ? '#000000' : '#ffffff',
    )
    root.style.setProperty('--portal-rim-a', rgb(accent))
    root.style.setProperty('--portal-rim-b', rgb(cool))
  }
  root.dataset.palette = profile.appearance.palette
  root.dataset.density = profile.appearance.density
  root.dataset.font = profile.appearance.font
  root.dataset.navigation = profile.layout.navigation
  root.dataset.cardStyle = profile.layout.cardStyle
  root.dataset.detailArrangement = profile.layout.detailArrangement
  root.dataset.theme = profile.themeId
  root.classList.toggle('reduced-motion', profile.appearance.reducedMotion)
  root.style.colorScheme =
    avalonStyle?.colorScheme ??
    (contrastRatio('#000000', palette.background) >= contrastRatio('#ffffff', palette.background)
      ? 'light'
      : 'dark')
}

export interface ThemeRuntime {
  profile: ThemeProfile
  setProfile: Dispatch<SetStateAction<ThemeProfile>>
  theme: ThemeDefinition
  packages: ThemePackage[]
  builtins: ThemeDefinition[]
  loading: boolean
  profileSaving: boolean
  profileSaveError: string | null
  notice: string | null
  clearNotice(): void
  selectTheme(id: string): void
  installTheme(): Promise<void>
  importProfile(): Promise<void>
  exportProfile(): Promise<void>
  resetProfile(): void
  recoverTheme(): void
  avalonCatalogue?: AvalonThemeCatalogue
  reloadAvalonThemes?(): Promise<void>
}

export function useThemeRuntime(
  registeredBuiltins: ThemeDefinition[],
  options?: { loadTheme?: typeof loadExternalTheme },
): ThemeRuntime {
  const [avalonCatalogue, setAvalonCatalogue] = useState<AvalonThemeCatalogue>({
    themes: [],
    diagnostics: [],
  })
  const builtins = useMemo(
    () =>
      registeredBuiltins.map((theme) =>
        theme.id !== 'avalon'
          ? theme
          : {
              ...theme,
              settings: theme.settings?.map((field) =>
                field.id !== 'palette' || field.type !== 'select'
                  ? field
                  : {
                      ...field,
                      description:
                        'Bundled and authored Winnow palettes. Studio colors uses your custom color choices.',
                      options: [
                        { value: 'profile', label: 'Studio colors' },
                        ...avalonPalettes().map(({ id, name }) => ({ value: id, label: name })),
                      ],
                    },
              ),
            },
      ),
    [registeredBuiltins, avalonCatalogue],
  )
  const fallback = builtins.find((theme) => theme.id === DEFAULT_PROFILE.themeId) ?? builtins[0]
  if (!fallback) throw new Error('Register a default theme before loading Winnow.')
  const [profile, setProfile] = useState<ThemeProfile>(() => structuredClone(DEFAULT_PROFILE))
  const [packages, setPackages] = useState<ThemePackage[]>([])
  const [theme, setTheme] = useState<ThemeDefinition>(fallback)
  const [notice, setNotice] = useState<string | null>(null)
  const [hydrated, setHydrated] = useState(false)
  const [loading, setLoading] = useState(false)
  const [profileSaving, setProfileSaving] = useState(false)
  const [profileSaveError, setProfileSaveError] = useState<string | null>(null)
  const profileWriteRevision = useRef(0)
  const writes = useRef<Promise<void>>(Promise.resolve())
  const loadTheme = options?.loadTheme ?? loadExternalTheme
  const profileRef = useRef(profile)
  const unsavedInitialProfile = useRef<ThemeProfile | null>(null)
  profileRef.current = profile
  const pendingSelection = useRef<{ id: string; source: ThemeProfile; provisional: ThemeProfile } | null>(
    null,
  )

  useEffect(() => {
    installThemeSDK()
    let cancelled = false
    void Promise.allSettled([
      window.winnow.loadPreferences(),
      window.winnow.listThemes(),
      window.winnow.listAvalonThemes?.(),
      window.winnow.appearanceSession?.(),
    ]).then(async (results) => {
      if (cancelled) return
      const catalogue =
        results[2].status === 'fulfilled' && results[2].value
          ? results[2].value
          : { themes: [], diagnostics: [] }
      registerAvalonThemes(catalogue.themes)
      setAvalonCatalogue(catalogue)
      if (results[1].status === 'fulfilled') setPackages(results[1].value)
      else setNotice('Installed themes could not be read. The built-in themes are still available.')
      const capture = results[3].status === 'fulfilled' ? results[3].value : null
      if (results[0].status === 'fulfilled' && results[0].value != null) {
        try {
          setProfile(parseThemeProfile(results[0].value))
        } catch (error) {
          setNotice(`${message(error)} Default appearance restored.`)
        }
      } else if (capture) {
        setProfile(migrateAvaloniaPalette([{ preference: 'Theme', value: capture.palette }]).profile)
      } else if (results[0].status === 'rejected')
        setNotice('Appearance settings could not be read. Default appearance restored.')
      else if (typeof window.winnow.request === 'function') {
        const initial = profileRef.current
        try {
          const previous = await window.winnow.request({ route: 'preferences.presentation.get' })
          if (cancelled) return
          if (!previous.ok) throw new Error('Previous appearance could not be read.')
          if (profileRef.current !== initial) {
            setHydrated(true)
            return
          }
          const migrated = migrateAvaloniaPalette(previous.data)
          setProfile(migrated.profile)
          if (migrated.unavailable)
            setNotice(
              'The saved Avalonia theme is unavailable in Electron. Choose an Avalon palette in Theme Studio.',
            )
        } catch {
          if (cancelled) return
          // Keep first-run recovery retryable. A user's explicit profile edit or
          // reset creates a new object and may still be saved while offline.
          unsavedInitialProfile.current = initial
          setNotice(
            'Your previous palette could not be read. Choose one in Theme Studio or restart to retry.',
          )
        }
      }
      if (capture) setNotice('Appearance changes apply only to this session.')
      setHydrated(true)
    })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    applyThemeProfile(profile)
  }, [profile, avalonCatalogue])

  const catalogueRead = useRef(0)
  const reloadAvalonThemes = useCallback(async () => {
    if (!window.winnow.listAvalonThemes) return
    const revision = ++catalogueRead.current
    try {
      const catalogue = await window.winnow.listAvalonThemes()
      if (revision !== catalogueRead.current) return
      registerAvalonThemes(catalogue.themes)
      setAvalonCatalogue(catalogue)
    } catch {
      if (revision === catalogueRead.current)
        setNotice('Authored themes could not be reloaded. Check the themes folder and try again.')
    }
  }, [])
  useEffect(() => {
    const unsubscribe = window.winnow.onAvalonThemesChanged?.(() => {
      void reloadAvalonThemes()
    })
    return () => {
      ++catalogueRead.current
      unsubscribe?.()
    }
  }, [reloadAvalonThemes])
  useEffect(() => {
    if (!hydrated || profile.themeId !== 'avalon') return
    const id = avalonPaletteId(profile)
    if (id === 'profile' || avalonPalette(id)) return
    setProfile((current) => ({
      ...current,
      settings: { ...current.settings, avalon: { ...current.settings.avalon, palette: 'winnow' } },
    }))
    setNotice('The selected palette is no longer available. The Winnow palette has been restored.')
  }, [hydrated, avalonCatalogue, profile.themeId, profile.settings.avalon?.palette])

  useEffect(() => {
    if (!hydrated || profile === unsavedInitialProfile.current) return
    const revision = ++profileWriteRevision.current
    setProfileSaving(true)
    setProfileSaveError(null)
    // Serialize saves so rapid sliders and theme switches cannot let an older write win.
    writes.current = writes.current
      .catch(() => {})
      .then(async () => {
        if (profileRef.current !== profile) return
        await window.winnow.savePreferences(profile)
      })
      .catch(() => {
        const failure = 'Appearance changes could not be saved. You can export a profile to keep them.'
        setNotice(failure)
        if (revision === profileWriteRevision.current) setProfileSaveError(failure)
      })
      .finally(() => {
        if (revision === profileWriteRevision.current) setProfileSaving(false)
      })
  }, [profile, hydrated])

  const recoverTheme = useCallback(() => {
    setTheme(fallback)
    setProfile((current) => selectThemeProfile(current, fallback.id, fallback))
    setNotice(`The theme could not render this screen. ${fallback.name} has been restored.`)
  }, [fallback])

  useEffect(() => {
    if (!hydrated) return
    const builtin = builtins.find((candidate) => candidate.id === profile.themeId)
    if (builtin) {
      setTheme(builtin)
      setLoading(false)
      return
    }
    const installed = packages.find((candidate) => candidate.id === profile.themeId)
    if (!installed) {
      setTheme(fallback)
      setProfile((current) => selectThemeProfile(current, fallback.id, fallback))
      setNotice(
        `This profile uses a theme that is not installed. ${fallback.name} is available until you install it.`,
      )
      return
    }
    let cancelled = false
    let style: HTMLLinkElement | undefined
    let timer: ReturnType<typeof setTimeout> | undefined
    setLoading(true)
    void loadTheme(installed)
      .then(async (definition) => {
        if (cancelled) return
        if (installed.css) {
          style = document.createElement('link')
          style.rel = 'stylesheet'
          style.href = validateThemeAssetUrl(installed.css, installed.id)
          style.dataset.winnowTheme = installed.id
          const loaded = new Promise<void>((resolve, reject) => {
            style!.onload = () => {
              clearTimeout(timer)
              resolve()
            }
            style!.onerror = () => {
              clearTimeout(timer)
              reject(new Error('The theme stylesheet could not be loaded.'))
            }
            timer = setTimeout(() => reject(new Error('The theme stylesheet took too long to load.')), 8000)
          })
          document.head.append(style)
          await loaded
        }
        if (!cancelled) {
          const pending = pendingSelection.current
          // Apply authored defaults only to an explicit, untouched first selection. Imports,
          // startup hydration and edits made while the module loads must retain their values.
          if (pending?.id === installed.id) {
            if (profileRef.current === pending.provisional) {
              const resolved = selectThemeProfile(pending.source, installed.id, definition)
              profileRef.current = resolved
              setProfile(resolved)
            }
            pendingSelection.current = null
          }
          setTheme(definition)
          setLoading(false)
        }
      })
      .catch((error) => {
        if (cancelled) return
        style?.remove()
        setTheme(fallback)
        setProfile((current) => selectThemeProfile(current, fallback.id, fallback))
        setLoading(false)
        setNotice(`${message(error)} ${fallback.name} has been restored.`)
      })
    return () => {
      cancelled = true
      clearTimeout(timer)
      style?.remove()
    }
  }, [profile.themeId, packages, builtins, fallback, hydrated, loadTheme])

  const selectTheme = (id: string) => {
    setNotice(null)
    const source = profileRef.current
    if (source.themeId === id) return
    const definition = builtins.find((item) => item.id === id)
    const provisional = selectThemeProfile(source, id, definition)
    pendingSelection.current = definition ? null : { id, source, provisional }
    profileRef.current = provisional
    setProfile(provisional)
  }
  const importProfile = async () => {
    try {
      const imported = await window.winnow.importProfile()
      if (imported === null) return
      const validated = parseThemeProfile(imported)
      setProfile(validated)
      setNotice('Appearance profile imported.')
    } catch (error) {
      setNotice(`Profile not imported. ${message(error)}`)
    }
  }
  const exportProfile = async () => {
    try {
      if (await window.winnow.exportProfile(parseThemeProfile(profile)))
        setNotice('Appearance profile exported.')
    } catch (error) {
      setNotice(`Profile not exported. ${message(error)}`)
    }
  }
  const installTheme = async () => {
    try {
      const installed = await window.winnow.installTheme()
      if (!installed) return
      if (installed.apiVersion !== 1) throw new Error('This theme needs a different version of Winnow.')
      setPackages((current) => [...current.filter((item) => item.id !== installed.id), installed])
      selectTheme(installed.id)
    } catch (error) {
      setNotice(`Theme not installed. ${message(error)}`)
    }
  }
  return {
    profile,
    setProfile,
    theme,
    packages,
    builtins,
    loading: loading || !hydrated,
    profileSaving,
    profileSaveError,
    notice,
    clearNotice: () => setNotice(null),
    selectTheme,
    installTheme,
    importProfile,
    exportProfile,
    resetProfile: () => {
      setProfile(structuredClone(DEFAULT_PROFILE))
      setNotice('Default appearance restored.')
    },
    recoverTheme,
    avalonCatalogue,
    reloadAvalonThemes,
  }
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : 'An unexpected error occurred.'
}

interface BoundaryProps {
  children: ReactNode
  fallback: ReactNode
  resetKey: string
  onError?: () => void
}
export class ThemeBoundary extends Component<BoundaryProps, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError(): { failed: boolean } {
    return { failed: true }
  }
  componentDidCatch(): void {
    this.props.onError?.()
  }
  componentDidUpdate(previous: BoundaryProps): void {
    if (previous.resetKey !== this.props.resetKey && this.state.failed) this.setState({ failed: false })
  }
  render(): ReactNode {
    return this.state.failed ? this.props.fallback : this.props.children
  }
}
