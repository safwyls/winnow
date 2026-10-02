// @vitest-environment jsdom
import { cleanup, renderHook, waitFor } from '@testing-library/react'
import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonThemeStore } from '../src/main/avalon-theme-store'
import { migrateAvaloniaPalette } from '../src/renderer/themes/avalon-migration'
import { avalonPalette, registerAvalonThemes } from '../src/renderer/themes/avalon-palettes'
import { useThemeRuntime } from '../src/renderer/theming/runtime'
import { avalonPaletteId, DEFAULT_PROFILE, type ThemeDefinition } from '../src/shared/theme'
import type { WinnowBridge } from '../src/shared/bridge'

const builtins: ThemeDefinition[] = [{ apiVersion: 1, id: 'avalon', name: 'Avalon' }]
beforeEach(() => {
  registerAvalonThemes([])
  window.winnow = {
    request: vi.fn(async () => ({ ok: true, status: 200, data: [{ preference: 'Theme', value: 'hoard' }] })),
    loadPreferences: vi.fn(async () => null),
    listThemes: vi.fn(async () => []),
    savePreferences: vi.fn(async () => {}),
  } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  registerAvalonThemes([])
})

it('The_pre_rename_theme_id_still_resolves_to_the_house_theme', () => {
  const migrated = migrateAvaloniaPalette([{ preference: 'Theme', value: 'hoard' }])
  expect(migrated.unavailable).toBe(false)
  expect(avalonPalette(avalonPaletteId(migrated.profile))).toBe(avalonPalette('winnow'))
})

it('The_alias_resolves_through_the_service_catalogue_too', async () => {
  const { result } = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(result.current.loading).toBe(false))
  expect(avalonPalette(avalonPaletteId(result.current.profile))?.id).toBe('winnow')
  await waitFor(() => expect(window.winnow.savePreferences).toHaveBeenCalledWith(result.current.profile))
})

it('A_user_theme_that_claims_the_old_id_wins_it', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'winnow-legacy-id-'))
  const store = new AvalonThemeStore(async () => directory)
  try {
    await writeFile(
      join(directory, 'hoard.json'),
      JSON.stringify({
        schemaVersion: 1,
        id: 'hoard',
        name: 'My Hoard',
        reason: 'A theme written by a test.',
        seeds: {
          ground: '#0F1C1E',
          surface: '#16282A',
          text: '#F0EDE7',
          flare: '#FF4D93',
          volt: '#4DE8C2',
          amber: '#FFB63D',
          azure: '#57A8F0',
          danger: '#E04B45',
        },
      }),
    )
    window.winnow.listAvalonThemes = () => store.load()
    const { result, unmount } = renderHook(() => useThemeRuntime(builtins))
    await waitFor(() => expect(result.current.loading).toBe(false))
    const resolved = avalonPalette(avalonPaletteId(result.current.profile))
    expect(resolved?.id).toBe('hoard')
    expect(resolved?.name).toBe('My Hoard')
    expect(result.current.profile.settings.avalon.palette).toBe('hoard')
    expect(result.current.notice).toBeNull()
    unmount()
  } finally {
    store.dispose()
    await rm(directory, { recursive: true, force: true })
  }
})

it('An_id_that_is_neither_still_falls_through_to_the_default', () => {
  for (const value of ['phosphor', null]) {
    const migrated = migrateAvaloniaPalette([{ preference: 'Theme', value }])
    expect(migrated.profile).toEqual(DEFAULT_PROFILE)
    expect(avalonPalette(avalonPaletteId(migrated.profile))).toBe(avalonPalette('winnow'))
  }
})
