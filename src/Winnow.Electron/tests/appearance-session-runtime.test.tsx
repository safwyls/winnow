// @vitest-environment jsdom
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { useThemeRuntime } from '../src/renderer/theming/runtime'
import { registerAvalonThemes } from '../src/renderer/themes/avalon-palettes'
import { resolvedTypography } from '../src/shared/theme'
import { DEFAULT_TYPOGRAPHY } from '../src/shared/typography'
import type { WinnowBridge } from '../src/shared/bridge'
import { SessionAppearance } from '../src/main/appearance-session'
import { appearanceSession } from '../src/shared/appearance-session'
const builtins = [{ apiVersion: 1 as const, id: 'avalon', name: 'Avalon' }]
afterEach(() => {
  cleanup()
  registerAvalonThemes([])
})
it.each([
  ['tungsten', false, 'tungsten'],
  ['hoard', false, 'winnow'],
  ['hoard', true, 'hoard'],
  ['retired-theme', false, 'winnow'],
] as const)(
  'resolves capture palette %s with authored catalog %s before hydration and never imports stored fonts',
  async (requested, custom, expected) => {
    const session = new SessionAppearance(appearanceSession([`--theme=${requested}`], false)!)
    window.winnow = {
      appearanceSession: async () => session.options,
      loadPreferences: async () => session.loadProfile(),
      savePreferences: vi.fn(async (value) => session.saveProfile(value)),
      listThemes: async () => [],
      request: vi.fn(async () => {
        throw Error('Stored appearance must not be imported')
      }),
      listAvalonThemes: async () => ({
        themes: custom
          ? [
              {
                file: 'custom.json',
                document: {
                  schemaVersion: 1,
                  id: 'hoard',
                  name: 'Authored',
                  reason: 'Fixture',
                  seeds: {
                    ground: '#131018',
                    surface: '#1D1926',
                    text: '#EFEAF5',
                    flare: '#FF4D93',
                    volt: '#A98CFF',
                    amber: '#FFB63D',
                    azure: '#57A8F0',
                    danger: '#E04B45',
                  },
                  typography: {
                    ...DEFAULT_TYPOGRAPHY,
                    headingFont: 'Georgia',
                    sizePercent: 110,
                  },
                  defaults: { transparency: 88, backdrop: 'mica' },
                },
              },
            ]
          : [],
        diagnostics: [],
      }),
    } as unknown as WinnowBridge
    const view = renderHook(() => useThemeRuntime(builtins))
    await waitFor(() => expect(view.result.current.loading).toBe(false))
    expect(view.result.current.profile.settings.avalon?.palette ?? 'winnow').toBe(expected)
    expect(resolvedTypography(view.result.current.profile).sizePercent).toBe(custom ? 110 : 100)
    expect(window.winnow.request).not.toHaveBeenCalled()
    expect(view.result.current.notice).toBe('Appearance changes apply only to this session.')
    act(() =>
      view.result.current.setProfile((current) => ({
        ...current,
        settings: { ...current.settings, avalon: { palette: 'box-art' } },
      })),
    )
    await waitFor(() =>
      expect(session.loadProfile()).toMatchObject({ settings: { avalon: { palette: 'box-art' } } }),
    )
    view.unmount()
    const reopened = renderHook(() => useThemeRuntime(builtins))
    await waitFor(() => expect(reopened.result.current.loading).toBe(false))
    expect(reopened.result.current.profile.settings.avalon.palette).toBe('box-art')
  },
)
