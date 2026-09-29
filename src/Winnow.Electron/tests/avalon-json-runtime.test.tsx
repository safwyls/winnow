// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { type WinnowBridge } from '../src/shared/bridge'
import { type AvalonThemeCatalogue, type AvalonThemeDocument } from '../src/shared/avalonThemeDocument'
import {
  DEFAULT_PROFILE,
  parseThemeProfile,
  resolvedTypography,
  type ThemeDefinition,
} from '../src/shared/theme'
import { DEFAULT_TYPOGRAPHY } from '../src/shared/typography'
import { avalonPalette, registerAvalonThemes } from '../src/renderer/themes/avalon-palettes'
import { AvalonTypographyControls } from '../src/renderer/themes/avalon-typography'
import { AvalonJsonControls } from '../src/renderer/themes/avalon-json-controls'
import { useThemeRuntime } from '../src/renderer/theming/runtime'

const custom: AvalonThemeDocument = {
  schemaVersion: 1,
  id: 'hoard',
  name: 'My original theme',
  reason: 'Preserve my own library colors.',
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
  typography: { ...DEFAULT_TYPOGRAPHY, headingFont: 'Georgia', sizePercent: 110 },
}
const builtins: ThemeDefinition[] = [
  {
    apiVersion: 1,
    id: 'avalon',
    name: 'Avalon',
    settings: [
      {
        id: 'palette',
        type: 'select',
        label: 'Palette',
        default: 'profile',
        options: [{ value: 'profile', label: 'Studio colors' }],
      },
    ],
  },
  { apiVersion: 1, id: 'afterglow', name: 'Afterglow' },
]
let catalogue: AvalonThemeCatalogue, changed: () => void
beforeEach(() => {
  catalogue = { themes: [{ file: 'mine.json', document: structuredClone(custom) }], diagnostics: [] }
  window.winnow = {
    loadPreferences: vi.fn(async () => null),
    savePreferences: vi.fn(async () => {}),
    listThemes: vi.fn(async () => []),
    request: vi.fn(async () => ({ ok: true, status: 200, data: [{ preference: 'Theme', value: 'hoard' }] })),
    listAvalonThemes: vi.fn(async () => structuredClone(catalogue)),
    onAvalonThemesChanged: vi.fn((callback) => {
      changed = callback
      return vi.fn()
    }),
    exportAvalonTheme: vi.fn(async () => ({ file: 'hoard-2.json', diagnostics: [] })),
    openDataFolder: vi.fn(async () => {}),
  } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  registerAvalonThemes([])
  vi.restoreAllMocks()
})

it('loads the custom catalog before migrating the legacy alias and authored typography', async () => {
  const { result } = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(result.current.loading).toBe(false))
  expect(result.current.profile.settings.avalon.palette).toBe('hoard')
  expect(resolvedTypography(result.current.profile)).toEqual(custom.typography)
  expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#131018')
  expect(document.documentElement.style.getPropertyValue('--font-display')).toContain('Georgia')
  await waitFor(() => {
    const setting = result.current.theme.settings?.find((field) => field.id === 'palette')
    expect(setting?.type === 'select' && setting.options).toContainEqual({
      label: custom.name,
      value: 'hoard',
    })
  })
  await waitFor(() => expect(window.winnow.savePreferences).toHaveBeenCalledWith(result.current.profile))
})
it('imports saved custom role overrides while an existing Electron profile wins on restart', async () => {
  const savedRoles = { ...DEFAULT_TYPOGRAPHY, interfaceFont: 'Arial', sizePercent: 120 }
  vi.mocked(window.winnow.request).mockResolvedValue({
    ok: true,
    status: 200,
    data: [
      { preference: 'Theme', value: 'hoard' },
      { preference: 'Typography', value: JSON.stringify({ hoard: savedRoles }) },
    ],
  })
  const first = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(resolvedTypography(first.result.current.profile)).toEqual(savedRoles))
  const profile = structuredClone(first.result.current.profile)
  first.unmount()
  vi.mocked(window.winnow.loadPreferences).mockResolvedValue(profile)
  vi.mocked(window.winnow.request).mockClear()
  const second = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(second.result.current.loading).toBe(false))
  expect(second.result.current.profile).toEqual(profile)
  expect(window.winnow.request).not.toHaveBeenCalled()
})
it('repaints an edited active theme by id, preserves explicit font edits, and reset follows authored fonts', async () => {
  function TestControls() {
    const runtime = useThemeRuntime(builtins)
    return (
      <AvalonTypographyControls
        profile={runtime.profile}
        onChange={(typography) =>
          runtime.setProfile((profile) => ({ ...profile, appearance: { ...profile.appearance, typography } }))
        }
      />
    )
  }
  render(<TestControls />)
  await waitFor(() =>
    expect((screen.getByLabelText('Heading font') as HTMLInputElement).value).toBe('Georgia'),
  )
  fireEvent.change(screen.getByLabelText('Heading font'), { target: { value: 'Arial' } })
  catalogue.themes[0].document = {
    ...custom,
    name: 'Renamed file theme',
    seeds: { ...custom.seeds, ground: '#212030' },
    typography: { ...DEFAULT_TYPOGRAPHY, headingFont: 'Cambria', sizePercent: 90 },
  }
  act(() => changed())
  await waitFor(() => expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#212030'))
  expect(document.documentElement.style.getPropertyValue('--font-display')).toContain('Arial')
  fireEvent.click(screen.getByText('Reset theme typography'))
  await waitFor(() =>
    expect(document.documentElement.style.getPropertyValue('--font-display')).toContain('Cambria'),
  )
  expect(document.documentElement.style.getPropertyValue('--theme-text-scale')).toBe('0.9')
})
it('falls back when the active custom file is deleted while keeping its saved font preference', async () => {
  const saved = structuredClone(DEFAULT_PROFILE)
  saved.settings.avalon = { palette: 'hoard' }
  saved.appearance.typography = { hoard: { ...DEFAULT_TYPOGRAPHY, headingFont: 'Arial' } }
  vi.mocked(window.winnow.loadPreferences).mockResolvedValue(saved)
  const { result } = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(result.current.loading).toBe(false))
  catalogue = { themes: [], diagnostics: [] }
  act(() => changed())
  await waitFor(() => expect(result.current.profile.settings.avalon.palette).toBe('winnow'))
  expect(result.current.profile.appearance.typography?.hoard.headingFont).toBe('Arial')
  expect(result.current.notice).toContain('no longer available')
})
it('uses a local bundled palette replacement and preserves its saved selection through reload', async () => {
  catalogue.themes[0].document.id = 'rose-pine'
  vi.mocked(window.winnow.loadPreferences).mockResolvedValue({
    ...structuredClone(DEFAULT_PROFILE),
    settings: { avalon: { palette: 'rose-pine' } },
  })
  const { result } = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(result.current.loading).toBe(false))
  expect(avalonPalette('rose-pine')?.sourceFile).toBe('mine.json')
  expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#131018')
  act(() => changed())
  await waitFor(() => expect(window.winnow.listAvalonThemes).toHaveBeenCalledTimes(2))
  expect(result.current.profile.settings.avalon.palette).toBe('rose-pine')
})
it('retains the last successful catalog on reload failure and ignores a stale reload response', async () => {
  const { result } = renderHook(() => useThemeRuntime(builtins))
  await waitFor(() => expect(result.current.loading).toBe(false))
  vi.mocked(window.winnow.listAvalonThemes!).mockRejectedValueOnce(Error('Unavailable'))
  await act(() => result.current.reloadAvalonThemes!())
  expect(result.current.profile.settings.avalon.palette).toBe('hoard')
  expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#131018')
  let finish!: (catalogue: AvalonThemeCatalogue) => void
  vi.mocked(window.winnow.listAvalonThemes!).mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }),
  )
  const pending = result.current.reloadAvalonThemes!()
  catalogue.themes[0].document.seeds.ground = '#252030'
  await act(() => result.current.reloadAvalonThemes!())
  await act(async () => {
    finish({ themes: [], diagnostics: [] })
    await pending
  })
  expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#252030')
  expect(result.current.profile.settings.avalon.palette).toBe('hoard')
})
it('shows file diagnostics, opens the library themes folder and exports effective typography', async () => {
  catalogue.diagnostics.push({
    severity: 'error',
    file: 'broken.json',
    field: 'seeds.danger',
    message: 'This seed is missing.',
  })
  function TestControls() {
    const runtime = useThemeRuntime(builtins)
    return <AvalonJsonControls runtime={runtime} />
  }
  render(<TestControls />)
  await waitFor(() => expect(screen.getByText(custom.reason)).toBeTruthy())
  expect(screen.getByText(/broken.json.*seeds.danger/)).toBeTruthy()
  fireEvent.click(screen.getByText('Open themes folder'))
  await waitFor(() => expect(window.winnow.openDataFolder).toHaveBeenCalledWith('themes'))
  await waitFor(() =>
    expect((screen.getByText('Export palette as JSON') as HTMLButtonElement).disabled).toBe(false),
  )
  fireEvent.click(screen.getByText('Export palette as JSON'))
  await waitFor(() => expect(screen.getByRole('status').textContent).toContain('Exported hoard-2.json'))
  const exported = JSON.parse(vi.mocked(window.winnow.exportAvalonTheme!).mock.calls[0][0])
  expect(exported.typography).toEqual(custom.typography)
})
it('keeps independent typography for every bounded catalog entry in a valid profile', () => {
  const profile = structuredClone(DEFAULT_PROFILE)
  profile.appearance.typography = Object.fromEntries(
    Array.from({ length: 73 }, (_, index) => [
      `palette-${index}`,
      { ...DEFAULT_TYPOGRAPHY, sizePercent: 80 + (index % 41) },
    ]),
  )
  expect(parseThemeProfile(profile).appearance.typography).toEqual(profile.appearance.typography)
})
