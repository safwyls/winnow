// @vitest-environment jsdom
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import type { ApiResult, WinnowBridge } from '../src/shared/bridge'
import {
  DEFAULT_PROFILE,
  parseThemeProfile,
  resolvedTypography,
  type ThemeDefinition,
  type ThemeProfile,
} from '../src/shared/theme'
import { migrateAvaloniaPalette } from '../src/renderer/themes/avalon-migration'
import { AVALON_PALETTES } from '../src/renderer/themes/avalon-palettes'
import { useThemeRuntime } from '../src/renderer/theming/runtime'

const builtins: ThemeDefinition[] = [
  { apiVersion: 1, id: 'avalon', name: 'Avalon' },
  { apiVersion: 1, id: 'afterglow', name: 'Afterglow' },
]
beforeEach(() => {
  window.winnow = {
    request: vi.fn(async () => ({
      ok: true,
      status: 200,
      data: [{ preference: 'Theme', value: 'rose-pine-dawn' }],
    })),
    loadPreferences: vi.fn(async () => null),
    listThemes: vi.fn(async () => []),
    savePreferences: vi.fn(async () => {}),
  } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

it.each(AVALON_PALETTES.map((palette) => palette.id))(
  'imports the original %s palette through the public Theme preference',
  (id) => {
    const { profile, unavailable } = migrateAvaloniaPalette([{ preference: 'Theme', value: id }])
    expect(profile.themeId).toBe('avalon')
    expect(profile.settings.avalon.palette).toBe(id)
    expect(unavailable).toBe(false)
    expect(DEFAULT_PROFILE.settings.avalon).toBeUndefined()
  },
)

it('reads the original appearance.theme contract and preserves the legacy house ID', () => {
  const source = readFileSync(
    resolve(process.cwd(), '../Winnow.Application/Services/PresentationPreferencesService.cs'),
    'utf8',
  )
  expect(source).toContain('PresentationPreference.Theme => "appearance.theme"')
  expect(
    migrateAvaloniaPalette([{ preference: 'Theme', value: 'hoard' }]).profile.settings.avalon.palette,
  ).toBe('winnow')
  expect(migrateAvaloniaPalette([{ preference: 'Theme', value: 'missing-user-theme' }])).toMatchObject({
    profile: DEFAULT_PROFILE,
    unavailable: true,
  })
  expect(migrateAvaloniaPalette([{ preference: 'FullscreenTheme', value: 'nightshift' }])).toMatchObject({
    profile: DEFAULT_PROFILE,
    unavailable: false,
  })
})

it('saves the imported first-launch palette and restores it without rereading backend appearance', async () => {
  const first = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(first.result.current.loading).toBe(false))
  expect(first.result.current.profile.settings.avalon.palette).toBe('rose-pine-dawn')
  expect(document.documentElement.style.colorScheme).toBe('light')
  await waitFor(() =>
    expect(window.winnow.savePreferences).toHaveBeenLastCalledWith(first.result.current.profile),
  )
  const saved = structuredClone(first.result.current.profile)
  first.unmount()
  vi.mocked(window.winnow.request).mockClear()
  vi.mocked(window.winnow.loadPreferences).mockResolvedValue(saved)
  const second = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(second.result.current.loading).toBe(false))
  expect(second.result.current.profile).toEqual(saved)
  expect(window.winnow.request).not.toHaveBeenCalled()
})

it('persists imported typography and explicit per-palette edits through a runtime restart', async () => {
  const typography = {
    headingFont: 'Georgia',
    interfaceFont: 'Segoe UI',
    dataFont: 'Consolas',
    sizePercent: 115,
  }
  vi.mocked(window.winnow.request).mockResolvedValue({
    ok: true,
    status: 200,
    data: [
      { preference: 'Theme', value: 'nightshift' },
      { preference: 'Typography', value: JSON.stringify({ nightshift: typography }) },
    ],
  })
  let saved: ThemeProfile | null = null
  vi.mocked(window.winnow.savePreferences).mockImplementation(async (profile) => {
    saved = parseThemeProfile(profile)
  })
  const first = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(saved).not.toBeNull())
  expect(resolvedTypography(first.result.current.profile)).toEqual(typography)
  act(() =>
    first.result.current.setProfile((profile) => ({
      ...profile,
      appearance: {
        ...profile.appearance,
        typography: { ...profile.appearance.typography, nightshift: { ...typography, sizePercent: 90 } },
      },
    })),
  )
  await waitFor(() => expect(saved!.appearance.typography!.nightshift.sizePercent).toBe(90))
  first.unmount()
  vi.mocked(window.winnow.loadPreferences).mockResolvedValue(saved)
  vi.mocked(window.winnow.request).mockClear()
  const second = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(resolvedTypography(second.result.current.profile).sizePercent).toBe(90))
  expect(window.winnow.request).not.toHaveBeenCalled()
})

it('gives an existing Electron profile priority over the original Avalonia theme', async () => {
  const saved = {
    ...structuredClone(DEFAULT_PROFILE),
    themeId: 'afterglow',
    appearance: { ...DEFAULT_PROFILE.appearance, palette: 'paper' as const },
  }
  vi.mocked(window.winnow.loadPreferences).mockResolvedValue(saved)
  const { result } = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(result.current.theme.id).toBe('afterglow'))
  expect(result.current.profile).toEqual(saved)
  expect(window.winnow.request).not.toHaveBeenCalled()
})

it('keeps a failed first-launch import retryable without saving an accidental default', async () => {
  vi.mocked(window.winnow.request).mockResolvedValue({ ok: false, status: 503, message: 'Offline' })
  const { result } = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(result.current.loading).toBe(false))
  expect(result.current.notice).toContain('restart to retry')
  expect(window.winnow.savePreferences).not.toHaveBeenCalled()
  act(() => result.current.resetProfile())
  await waitFor(() => expect(window.winnow.savePreferences).toHaveBeenCalledWith(DEFAULT_PROFILE))
})

it('does not replace an explicit edit with a slow initial palette import', async () => {
  let complete!: (result: ApiResult<unknown>) => void
  vi.mocked(window.winnow.request).mockReturnValue(
    new Promise((resolve) => {
      complete = resolve
    }),
  )
  const { result } = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(window.winnow.request).toHaveBeenCalled())
  act(() =>
    result.current.setProfile((current) => ({
      ...current,
      appearance: { ...current.appearance, accent: '#abcdef' },
    })),
  )
  await act(async () =>
    complete({ ok: true, status: 200, data: [{ preference: 'Theme', value: 'nightshift' }] }),
  )
  expect(result.current.profile.appearance.accent).toBe('#abcdef')
  expect(result.current.profile.settings.avalon).toBeUndefined()
})
