// @vitest-environment jsdom
import React from 'react'
import { act, cleanup, render, renderHook, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { WinnowBridge, ThemePackage } from '../src/shared/bridge'
import { DEFAULT_PROFILE, type ThemeDefinition } from '../src/shared/theme'
import { ThemeBoundary, applyThemeProfile, useThemeRuntime } from '../src/renderer/theming/runtime'

const builtin: ThemeDefinition = { apiVersion: 1, id: 'avalon', name: 'Avalon', Shell: () => null }
const afterglow: ThemeDefinition = { apiVersion: 1, id: 'afterglow', name: 'Afterglow', Shell: () => null }
const alternative: ThemeDefinition = { apiVersion: 1, id: 'catalogue', name: 'Catalogue', Shell: () => null }
const builtins = [builtin, afterglow, alternative]
const installed: ThemePackage = {
  id: 'reading-room',
  apiVersion: 1,
  name: 'Reading room',
  version: '1.0.0',
  entry: 'winnow-theme://reading-room/index.mjs',
  css: 'winnow-theme://reading-room/style.css',
}

beforeEach(() => {
  window.winnow = {
    loadPreferences: vi.fn(async () => null),
    savePreferences: vi.fn(async () => {}),
    listThemes: vi.fn(async () => [installed]),
    importProfile: vi.fn(async () => null),
    exportProfile: vi.fn(async () => true),
    installTheme: vi.fn(async () => null),
  } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('theme runtime recovery and lifecycle', () => {
  it('starts and resets fresh installs with the original Avalon appearance', async () => {
    const { result } = renderHook(() => useThemeRuntime(builtins))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.theme.id).toBe('avalon')
    expect(result.current.profile).toEqual(DEFAULT_PROFILE)
    expect(result.current.profile.appearance).toMatchObject({
      palette: 'winnow',
      accent: '#4DE8C2',
      font: 'modern',
      radius: 6,
    })
    expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#0F1C1E')
    act(() => result.current.selectTheme('catalogue'))
    act(() => result.current.resetProfile())
    await waitFor(() => expect(result.current.theme.id).toBe('avalon'))
    expect(result.current.profile).toEqual(DEFAULT_PROFILE)
  })

  it('hydrates a saved Afterglow profile without replacing any appearance or layout choice', async () => {
    const saved = {
      ...structuredClone(DEFAULT_PROFILE),
      themeId: 'afterglow',
      appearance: {
        ...DEFAULT_PROFILE.appearance,
        palette: 'paper' as const,
        accent: '#aabbcc',
        font: 'editorial' as const,
        radius: 21,
      },
      layout: { ...DEFAULT_PROFILE.layout, navigation: 'top' as const, cardStyle: 'landscape' as const },
      settings: { afterglow: { quiet: true } },
    }
    vi.mocked(window.winnow.loadPreferences).mockResolvedValue(saved)
    const { result } = renderHook(() => useThemeRuntime(builtins))
    await waitFor(() => expect(result.current.theme.id).toBe('afterglow'))
    expect(result.current.profile).toEqual(saved)
  })

  it.each([false, true])(
    'applies external first-selection defaults without overwriting edits during load (edited=%s)',
    async (edited) => {
      const loadTheme = vi.fn(async (): Promise<ThemeDefinition> => ({
        apiVersion: 1,
        id: installed.id,
        name: 'Room',
        defaults: { appearance: { palette: 'paper', accent: '#88502f' } },
      }))
      const { result } = renderHook(() => useThemeRuntime(builtins, { loadTheme }))
      await waitFor(() => expect(result.current.loading).toBe(false))
      act(() => result.current.selectTheme(installed.id))
      await waitFor(() => expect(document.querySelector('link[data-winnow-theme]')).not.toBeNull())
      if (edited)
        act(() =>
          result.current.setProfile((profile) => ({
            ...profile,
            appearance: { ...profile.appearance, accent: '#abcdef' },
          })),
        )
      act(() => document.querySelector('link[data-winnow-theme]')!.dispatchEvent(new Event('load')))
      await waitFor(() => expect(result.current.theme.id).toBe(installed.id))
      expect(result.current.profile.appearance.accent).toBe(edited ? '#abcdef' : '#88502f')
      expect(result.current.profile.appearance.palette).toBe(edited ? 'winnow' : 'paper')
    },
  )

  it('persists Rift selection and restores the saved Afterglow appearance on return', async () => {
    vi.mocked(window.winnow.loadPreferences).mockResolvedValue({
      ...structuredClone(DEFAULT_PROFILE),
      themeId: 'afterglow',
      appearance: {
        ...DEFAULT_PROFILE.appearance,
        palette: 'afterglow',
        accent: '#efad80',
        font: 'editorial',
        radius: 18,
      },
      layout: { ...DEFAULT_PROFILE.layout, navigation: 'top' },
    })
    const rift: ThemeDefinition = {
      apiVersion: 1,
      id: 'rift',
      name: 'Rift',
      defaults: { appearance: { palette: 'rift', accent: '#a1e6d3', font: 'modern' } },
    }
    const themes = [...builtins, rift]
    const { result, unmount } = renderHook(() => useThemeRuntime(themes))
    await waitFor(() => expect(result.current.loading).toBe(false))
    act(() =>
      result.current.setProfile((p) => ({ ...p, appearance: { ...p.appearance, accent: '#ffaaaa' } })),
    )
    act(() => result.current.selectTheme('rift'))
    await waitFor(() => expect(result.current.theme.id).toBe('rift'))
    expect(result.current.profile.appearance.palette).toBe('rift')
    await waitFor(() =>
      expect(window.winnow.savePreferences).toHaveBeenLastCalledWith(result.current.profile),
    )
    const saved = structuredClone(result.current.profile)
    unmount()
    vi.mocked(window.winnow.loadPreferences).mockResolvedValue(saved)
    const resumed = renderHook(() => useThemeRuntime(themes))
    await waitFor(() => expect(resumed.result.current.theme.id).toBe('rift'))
    act(() => resumed.result.current.selectTheme('afterglow'))
    await waitFor(() => expect(resumed.result.current.theme.id).toBe('afterglow'))
    expect(resumed.result.current.profile.appearance.accent).toBe('#ffaaaa')
  })

  it('restores a safe profile when stored appearance is incompatible', async () => {
    vi.mocked(window.winnow.loadPreferences).mockResolvedValue({ schemaVersion: 9000 })
    const { result } = renderHook(() => useThemeRuntime(builtins))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.profile).toEqual(DEFAULT_PROFILE)
    expect(result.current.notice).toContain('Default appearance restored')
  })

  it('does not lose appearance when an imported theme is unavailable', async () => {
    vi.mocked(window.winnow.loadPreferences).mockResolvedValue({
      ...structuredClone(DEFAULT_PROFILE),
      themeId: 'uninstalled',
    })
    const { result } = renderHook(() => useThemeRuntime(builtins))
    await waitFor(() => expect(result.current.profile.themeId).toBe('avalon'))
    await waitFor(() => expect(result.current.notice).toContain('not installed'))
    expect(result.current.profile.appearance).toEqual(DEFAULT_PROFILE.appearance)
  })

  it('leaves the current appearance intact after a rejected import', async () => {
    vi.mocked(window.winnow.importProfile).mockResolvedValue({
      ...DEFAULT_PROFILE,
      appearance: { ...DEFAULT_PROFILE.appearance, accent: 'javascript:bad' },
    })
    const { result } = renderHook(() => useThemeRuntime(builtins))
    await waitFor(() => expect(result.current.loading).toBe(false))
    await act(() => result.current.importProfile())
    expect(result.current.profile).toEqual(DEFAULT_PROFILE)
    expect(result.current.notice).toContain('Profile not imported')
  })

  it('a slow external module cannot replace a newer theme selection', async () => {
    let resolve!: (value: ThemeDefinition) => void
    const loadTheme = vi.fn(
      () =>
        new Promise<ThemeDefinition>((done) => {
          resolve = done
        }),
    )
    const { result } = renderHook(() => useThemeRuntime(builtins, { loadTheme }))
    await waitFor(() => expect(result.current.loading).toBe(false))
    act(() => result.current.selectTheme('reading-room'))
    await waitFor(() => expect(loadTheme).toHaveBeenCalledOnce())
    act(() => result.current.selectTheme('catalogue'))
    await act(async () => {
      resolve({ apiVersion: 1, id: 'reading-room', name: 'Room' })
    })
    expect(result.current.theme.id).toBe('catalogue')
    expect(document.querySelector('link[data-winnow-theme]')).toBeNull()
  })

  it('removes package styles when another theme is selected', async () => {
    const loadTheme = vi.fn(async (): Promise<ThemeDefinition> => ({
      apiVersion: 1,
      id: 'reading-room',
      name: 'Room',
    }))
    const { result } = renderHook(() => useThemeRuntime(builtins, { loadTheme }))
    await waitFor(() => expect(result.current.loading).toBe(false))
    act(() => result.current.selectTheme('reading-room'))
    await waitFor(() => expect(document.querySelector('link[data-winnow-theme]')).not.toBeNull())
    act(() => document.querySelector('link[data-winnow-theme]')!.dispatchEvent(new Event('load')))
    await waitFor(() => expect(result.current.theme.id).toBe('reading-room'))
    act(() => result.current.selectTheme('afterglow'))
    expect(document.querySelector('link[data-winnow-theme]')).toBeNull()
  })

  it('returns to the built-in composition after a module load error', async () => {
    const loadTheme = vi.fn(async (): Promise<ThemeDefinition> => {
      throw new Error('Missing theme module.')
    })
    const { result } = renderHook(() => useThemeRuntime(builtins, { loadTheme }))
    await waitFor(() => expect(result.current.loading).toBe(false))
    act(() => result.current.selectTheme('reading-room'))
    await waitFor(() => expect(result.current.notice).toContain('Missing theme module'))
    expect(result.current.theme.id).toBe('avalon')
    expect(result.current.profile.themeId).toBe('avalon')
  })

  it('catches a crashing screen and permits recovery after the theme changes', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const onError = vi.fn()
    function Broken(): React.ReactNode {
      throw new Error('Bad theme screen')
    }
    const { rerender } = render(
      <ThemeBoundary resetKey="broken" onError={onError} fallback={<p>Restore Afterglow</p>}>
        <Broken />
      </ThemeBoundary>,
    )
    expect(screen.getByText('Restore Afterglow')).toBeDefined()
    expect(onError).toHaveBeenCalledOnce()
    rerender(
      <ThemeBoundary resetKey="afterglow" fallback={<p>Restore Afterglow</p>}>
        <p>Your library</p>
      </ThemeBoundary>,
    )
    expect(screen.getByText('Your library')).toBeDefined()
  })

  it('applies accessible tokens and layout data without injecting a stylesheet', () => {
    const profile = structuredClone(DEFAULT_PROFILE)
    profile.appearance.reducedMotion = true
    profile.appearance.palette = 'paper'
    profile.layout.navigation = 'left'
    applyThemeProfile(profile)
    expect(document.documentElement.classList.contains('reduced-motion')).toBe(true)
    expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#f0ebe2')
    expect(document.documentElement.dataset.navigation).toBe('left')
    expect(document.documentElement.style.colorScheme).toBe('light')
  })

  it('applies custom colors and scale with contrasting accent text and a matching native color scheme', () => {
    const profile = structuredClone(DEFAULT_PROFILE)
    profile.appearance.colors = { background: '#ffffff', surface: '#eeeeee', text: '#111111' }
    profile.appearance.accent = '#ffff00'
    profile.appearance.scale = 125
    applyThemeProfile(profile)
    expect(document.documentElement.style.getPropertyValue('--surface')).toBe('#eeeeee')
    expect(document.documentElement.style.getPropertyValue('--interface-scale')).toBe('1.25')
    expect(document.documentElement.style.getPropertyValue('--accent-text')).toBe('#000000')
    expect(document.documentElement.style.colorScheme).toBe('light')
    profile.appearance.accent = '#000000'
    applyThemeProfile(profile)
    expect(document.documentElement.style.getPropertyValue('--accent-text')).toBe('#ffffff')
  })
})
