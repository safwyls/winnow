import * as React from 'react'
import {
  Component,
  useCallback,
  useEffect,
  useRef,
  useState,
  type Dispatch,
  type ReactNode,
  type SetStateAction,
} from 'react'
import type { ThemePackage } from '../../shared/bridge'
import {
  DEFAULT_PROFILE,
  THEME_API_VERSION,
  contrastRatio,
  parseThemeProfile,
  resolvedThemeColors,
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
  }
  for (const [key, value] of Object.entries(variables)) root.style.setProperty(`--${key}`, value)
  root.dataset.palette = profile.appearance.palette
  root.dataset.density = profile.appearance.density
  root.dataset.font = profile.appearance.font
  root.dataset.navigation = profile.layout.navigation
  root.dataset.cardStyle = profile.layout.cardStyle
  root.dataset.detailArrangement = profile.layout.detailArrangement
  root.dataset.theme = profile.themeId
  root.classList.toggle('reduced-motion', profile.appearance.reducedMotion)
  root.style.colorScheme =
    contrastRatio('#000000', palette.background) >= contrastRatio('#ffffff', palette.background)
      ? 'light'
      : 'dark'
}

export interface ThemeRuntime {
  profile: ThemeProfile
  setProfile: Dispatch<SetStateAction<ThemeProfile>>
  theme: ThemeDefinition
  packages: ThemePackage[]
  builtins: ThemeDefinition[]
  loading: boolean
  notice: string | null
  clearNotice(): void
  selectTheme(id: string): void
  installTheme(): Promise<void>
  importProfile(): Promise<void>
  exportProfile(): Promise<void>
  resetProfile(): void
  recoverTheme(): void
}

export function useThemeRuntime(
  builtins: ThemeDefinition[],
  options?: { loadTheme?: typeof loadExternalTheme },
): ThemeRuntime {
  const fallback = builtins.find((theme) => theme.id === DEFAULT_PROFILE.themeId) ?? builtins[0]
  if (!fallback) throw new Error('Register a default theme before loading Winnow.')
  const [profile, setProfile] = useState<ThemeProfile>(() => structuredClone(DEFAULT_PROFILE))
  const [packages, setPackages] = useState<ThemePackage[]>([])
  const [theme, setTheme] = useState<ThemeDefinition>(fallback)
  const [notice, setNotice] = useState<string | null>(null)
  const [hydrated, setHydrated] = useState(false)
  const [loading, setLoading] = useState(false)
  const writes = useRef<Promise<void>>(Promise.resolve())
  const loadTheme = options?.loadTheme ?? loadExternalTheme
  const profileRef = useRef(profile)
  profileRef.current = profile
  const pendingSelection = useRef<{ id: string; source: ThemeProfile; provisional: ThemeProfile } | null>(
    null,
  )

  useEffect(() => {
    installThemeSDK()
    let cancelled = false
    void Promise.allSettled([window.winnow.loadPreferences(), window.winnow.listThemes()]).then((results) => {
      if (cancelled) return
      if (results[1].status === 'fulfilled') setPackages(results[1].value)
      else setNotice('Installed themes could not be read. The built-in themes are still available.')
      if (results[0].status === 'fulfilled' && results[0].value != null) {
        try {
          setProfile(parseThemeProfile(results[0].value))
        } catch (error) {
          setNotice(`${message(error)} Default appearance restored.`)
        }
      } else if (results[0].status === 'rejected')
        setNotice('Appearance settings could not be read. Default appearance restored.')
      setHydrated(true)
    })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    applyThemeProfile(profile)
  }, [profile])

  useEffect(() => {
    if (!hydrated) return
    // Serialize saves so rapid sliders and theme switches cannot let an older write win.
    writes.current = writes.current
      .catch(() => {})
      .then(async () => {
        if (profileRef.current !== profile) return
        await window.winnow.savePreferences(profile)
      })
      .catch(() => {
        setNotice('Appearance changes could not be saved. You can export a profile to keep them.')
      })
  }, [profile, hydrated])

  const recoverTheme = useCallback(() => {
    setTheme(fallback)
    setProfile((current) => selectThemeProfile(current, fallback.id, fallback))
    setNotice(
      'The theme could not render this screen. Afterglow has been restored. Your library is unchanged.',
    )
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
        'This profile uses a theme that is not installed. Afterglow is available until you install it.',
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
        setNotice(`${message(error)} Afterglow has been restored.`)
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
