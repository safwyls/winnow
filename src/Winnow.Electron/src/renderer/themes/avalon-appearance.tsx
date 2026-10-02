import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { avalonPaletteId, resolvedThemeColors, type ThemeProfile } from '../../shared/theme'
import type { WindowAppearanceResult } from '../../shared/windowAppearance'
import { usePresentationPreferences } from '../features/SettingsPreferences'
import { useSetupBusy, useSetupPreferenceError } from '../features/settingsState'
import { avalonPalette, type AvalonPalette } from './avalon-palettes'
import {
  aaCeiling,
  avalonAppearance,
  avalonSurfaceTokens,
  worstMetadataContrast,
  type AvalonAppearance,
} from './avalon-translucency'

function paletteForProfile(profile: ThemeProfile): AvalonPalette {
  const id = avalonPaletteId(profile)
  const original = avalonPalette(id)
  if (original) return original
  const colors = resolvedThemeColors(profile)
  return {
    id: 'profile',
    name: 'Studio colors',
    colors: {
      '--bg': colors.background,
      '--surface': colors.surface,
      '--raised': colors.raised,
      '--text': colors.text,
      '--muted': colors.muted,
      '--avalon-well': colors.background,
    },
  }
}

const noMaterial: WindowAppearanceResult = { requested: 'none', supported: false, platform: '' }

/** A response to an older request must never reopen transparency after fullscreen or a theme switch. */
export function useAvalonAppearance(profile: ThemeProfile, fullscreen: boolean, profileHydrated = true) {
  const preferences = usePresentationPreferences()
  const palette = paletteForProfile(profile)
  const [material, setMaterial] = useState(noMaterial)
  const [nativeRevision, setNativeRevision] = useState(0)
  const previousPalette = useRef(palette.id)
  const previousHydrated = useRef(profileHydrated)
  useEffect(
    () => window.winnow?.onWindowAppearanceInvalidated?.(() => setNativeRevision((value) => value + 1)),
    [],
  )
  useEffect(() => {
    const wasHydrated = previousHydrated.current
    previousHydrated.current = profileHydrated
    const changed = previousPalette.current !== palette.id
    previousPalette.current = palette.id
    if (!changed || !profileHydrated || !wasHydrated) return
    if (!preferences.loaded || !palette.defaults) return
    const defaults = palette.defaults
    if (defaults.transparency != null) preferences.set('Transparency', String(defaults.transparency))
    if (defaults.backdrop != null) preferences.set('Backdrop', defaults.backdrop)
    if (defaults.wallTranslucent != null) preferences.set('TranslucentWall', String(defaults.wallTranslucent))
    if (defaults.layout != null) preferences.set('Layout', defaults.layout)
  }, [palette.id, preferences.loaded, profileHydrated])
  const appearance = avalonAppearance(preferences.values, palette, material.platform === 'win32')
  const enabled = !fullscreen && preferences.loaded && appearance.transparency > 0
  const background = palette.colors['--bg']
  useEffect(() => {
    let active = true
    setMaterial((current) => ({ ...current, requested: 'none' }))
    if (window.winnow?.windowAppearance)
      void window.winnow
        .windowAppearance({ enabled, material: appearance.backdrop, background })
        .then((value) => {
          if (active) setMaterial(value)
        })
        .catch(() => {
          if (active) setMaterial((current) => ({ ...current, requested: 'none', supported: false }))
        })
    return () => {
      active = false
    }
  }, [enabled, appearance.backdrop, background, nativeRevision])
  const active = enabled && material.requested === appearance.backdrop
  useEffect(() => {
    const root = document.documentElement
    root.dataset.avalonMaterial = active ? material.requested : 'none'
    return () => {
      delete root.dataset.avalonMaterial
    }
  }, [active, material.requested])
  useEffect(
    () => () => {
      void window.winnow
        ?.windowAppearance?.({ enabled: false, material: 'acrylic', background })
        .catch(() => {})
    },
    [],
  )
  const tokens = avalonSurfaceTokens(
    palette,
    active ? appearance.transparency : 0,
    appearance.wallTranslucent,
    appearance.layout,
  )
  const style = {
    '--avalon-shell-ground': tokens.ShellGround,
    '--avalon-caption-fill': tokens.CaptionFill,
    '--avalon-chrome-surface': tokens.ChromeSurface,
    '--avalon-pane-ground': tokens.PaneGround,
    '--avalon-chrome-raised': tokens.ChromeRaised,
    '--avalon-chrome-raised-half': tokens.ChromeRaisedHalf,
    '--avalon-field-ground': tokens.ChromeFieldOnGround,
    '--avalon-field-surface': tokens.ChromeFieldOnSurface,
    '--muted': tokens.TextDim,
    '--avalon-faint': tokens.TextFaint,
  } as CSSProperties
  return { appearance, material, active, style }
}

export function AvalonAppearanceControls({ profile }: { profile: ThemeProfile }) {
  const preferences = usePresentationPreferences(),
    palette = paletteForProfile(profile)
  useSetupBusy(preferences.pending)
  useSetupPreferenceError(preferences.error)
  const [material, setMaterial] = useState(noMaterial)
  useEffect(() => {
    // The shell owns material changes. Read its last capability result through the
    // platform's read-only application information instead of issuing a second request.
    let active = true
    void window.winnow
      .applicationInfo?.()
      .then((info) => {
        if (active) setMaterial((current) => ({ ...current, platform: info.platform }))
      })
      .catch(() => {})
    return () => {
      active = false
    }
  }, [])
  const appearance = avalonAppearance(preferences.values, palette, material.platform === 'win32')
  const disabled = !preferences.loaded || preferences.pending
  const ceiling = useMemo(() => aaCeiling(palette), [palette])
  const belowAa = appearance.transparency > 0 && worstMetadataContrast(palette, appearance.transparency) < 4.5
  const choose = (key: keyof AvalonAppearance, value: string) => {
    if (String(appearance[key]) !== value)
      preferences.set(
        {
          transparency: 'Transparency',
          backdrop: 'Backdrop',
          wallTranslucent: 'TranslucentWall',
          layout: 'Layout',
        }[key],
        value,
      )
  }
  return (
    <section className="studio-panel" aria-labelledby="avalon-window-heading">
      <h2 id="avalon-window-heading">Window appearance</h2>
      <p>Desktop materials keep covers opaque. Fullscreen uses solid surfaces.</p>
      <div className="studio-field-grid">
        <label className="studio-field">
          Transparency <span className="studio-value">{appearance.transparency}%</span>
          <input
            aria-label="Transparency"
            aria-describedby={belowAa ? 'avalon-transparency-contrast' : undefined}
            type="range"
            min={0}
            max={100}
            step={1}
            value={appearance.transparency}
            disabled={disabled}
            onChange={(event) => choose('transparency', event.target.value)}
          />
        </label>
        <label className="studio-field">
          Backdrop
          <select
            aria-label="Backdrop"
            value={appearance.backdrop}
            disabled={disabled}
            onChange={(event) => choose('backdrop', event.target.value)}
          >
            <option value="acrylic">Acrylic — blur behind the window</option>
            <option value="mica">Mica — wallpaper tint</option>
          </select>
        </label>
        <label className="studio-field">
          Pane layout
          <select
            aria-label="Pane layout"
            value={appearance.layout}
            disabled={disabled}
            onChange={(event) => choose('layout', event.target.value)}
          >
            <option value="floating">Floating — rounded panes with gaps</option>
            <option value="flush">Flush — panes meet edge to edge</option>
          </select>
        </label>
        <label className="studio-toggle">
          <input
            type="checkbox"
            checked={appearance.wallTranslucent}
            disabled={disabled}
            onChange={(event) => choose('wallTranslucent', String(event.target.checked))}
          />
          <span>
            Include content panes{' '}
            <small>Opens the library and other content panes along with the navigation.</small>
          </span>
        </label>
      </div>
      {belowAa && (
        <p id="avalon-transparency-contrast" className="avalon-contrast-warning">
          Past {ceiling}%, title-bar text may not clear 4.5:1 on a light desktop.
          {belowAa ? ' Reduce transparency for a more readable title bar.' : ''}
        </p>
      )}
      <p>
        Windows 11 22H2 or later supports these materials. If the platform declines the request, Winnow uses
        solid surfaces and keeps your choices.
      </p>
      {preferences.error && <p role="alert">Window appearance could not be saved.</p>}
    </section>
  )
}
