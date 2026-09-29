// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import type { WinnowBridge } from '../src/shared/bridge'
import { DEFAULT_PROFILE, type ThemeProfile } from '../src/shared/theme'
import type { WindowAppearanceResult } from '../src/shared/windowAppearance'
import { AvalonAppearanceControls, useAvalonAppearance } from '../src/renderer/themes/avalon-appearance'
import { AVALON_PALETTES, registerAvalonThemes } from '../src/renderer/themes/avalon-palettes'
import { avalonSeeds } from '../src/renderer/themes/avalon-json'

let values: Record<string, string | null>
let writes: [string, string][]
let invalidate: () => void
let client: QueryClient
let refuseSave = false
const profile = (palette = 'winnow'): ThemeProfile => ({
  ...structuredClone(DEFAULT_PROFILE),
  settings: { avalon: { palette } },
})
const wrapper = ({ children }: { children: ReactNode }) => (
  <QueryClientProvider client={client}>{children}</QueryClientProvider>
)

beforeEach(() => {
  values = { Transparency: '60', Backdrop: 'acrylic', TranslucentWall: 'true', Layout: 'floating' }
  writes = []
  refuseSave = false
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  window.winnow = {
    request: vi.fn(async ({ route, params, body }) => {
      if (route === 'preferences.presentation.get')
        return {
          ok: true,
          status: 200,
          data: Object.entries(values).map(([preference, value]) => ({ preference, value })),
        }
      if (route === 'preferences.presentation.put') {
        if (refuseSave) return { ok: false, status: 500, problem: { detail: 'Disk unavailable' } }
        const preference = String(params!.preference),
          value = (body as { value: string }).value
        writes.push([preference, value])
        values[preference] = value
        return { ok: true, status: 200, data: null }
      }
      throw Error(`Unexpected route: ${route}`)
    }),
    windowAppearance: vi.fn(async (request) => ({
      requested: request.enabled ? request.material : 'none',
      supported: true,
      platform: 'win32',
    })),
    onWindowAppearanceInvalidated: vi.fn((callback) => {
      invalidate = callback
      return vi.fn()
    }),
    applicationInfo: vi.fn(async () => ({ platform: 'win32' })),
  } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  client.clear()
  registerAvalonThemes([])
  delete document.documentElement.dataset.avalonMaterial
  vi.restoreAllMocks()
})

it.each([false, true])(
  'preserves stored appearance through delayed authored profile hydration with fullscreen %s, then writes all explicit defaults',
  async (fullscreen) => {
    registerAvalonThemes([
      {
        file: 'mine.json',
        document: {
          schemaVersion: 1,
          id: 'startup-custom',
          name: 'Startup custom',
          reason: 'A saved authored theme.',
          seeds: avalonSeeds(AVALON_PALETTES[0]),
          defaults: { transparency: 40, backdrop: 'mica', reach: 'chrome', layout: 'flush' },
        },
      },
    ])
    const hook = renderHook(
      ({ selected, hydrated }) => useAvalonAppearance(profile(selected), fullscreen, hydrated),
      { wrapper, initialProps: { selected: 'winnow', hydrated: false } },
    )
    await waitFor(() => expect(hook.result.current.appearance.transparency).toBe(60))
    hook.rerender({ selected: 'startup-custom', hydrated: true })
    expect(hook.result.current.appearance).toEqual({
      transparency: 60,
      backdrop: 'acrylic',
      wallTranslucent: true,
      layout: 'floating',
    })
    expect(writes).toEqual([])
    hook.rerender({ selected: 'winnow', hydrated: true })
    expect(writes).toEqual([])
    hook.rerender({ selected: 'startup-custom', hydrated: true })
    await waitFor(() => expect(writes).toHaveLength(4))
    expect(writes).toEqual([
      ['Transparency', '40'],
      ['Backdrop', 'mica'],
      ['TranslucentWall', 'false'],
      ['Layout', 'flush'],
    ])
    await waitFor(() =>
      expect(hook.result.current.appearance).toEqual({
        transparency: 40,
        backdrop: 'mica',
        wallTranslucent: false,
        layout: 'flush',
      }),
    )
  },
)

it('imports saved reach/layout/material without writing and restores solid surfaces in fullscreen', async () => {
  values = { Transparency: '70', Backdrop: 'mica', TranslucentWall: 'false', Layout: 'flush' }
  const hook = renderHook(({ fullscreen }) => useAvalonAppearance(profile(), fullscreen), {
    wrapper,
    initialProps: { fullscreen: false },
  })
  await waitFor(() => expect(hook.result.current.active).toBe(true))
  expect(hook.result.current.appearance).toEqual({
    transparency: 70,
    backdrop: 'mica',
    wallTranslucent: false,
    layout: 'flush',
  })
  expect(hook.result.current.style).toHaveProperty('--avalon-pane-ground', '#0F1C1EFF')
  expect(document.documentElement.dataset.avalonMaterial).toBe('mica')
  expect(writes).toEqual([])
  hook.rerender({ fullscreen: true })
  await waitFor(() =>
    expect(window.winnow.windowAppearance).toHaveBeenLastCalledWith({
      enabled: false,
      material: 'mica',
      background: '#0F1C1E',
    }),
  )
  expect(hook.result.current.active).toBe(false)
  expect(hook.result.current.style).toHaveProperty('--avalon-shell-ground', '#0F1C1EFF')
  expect(document.documentElement.dataset.avalonMaterial).toBe('none')
  hook.rerender({ fullscreen: false })
  await waitFor(() => expect(hook.result.current.active).toBe(true))
  expect(writes).toEqual([])
})

it('ignores a late native material answer after entering fullscreen and releases material on unmount', async () => {
  const answers: ((value: WindowAppearanceResult) => void)[] = []
  vi.mocked(window.winnow.windowAppearance!).mockImplementation(
    () => new Promise((resolve) => answers.push(resolve)),
  )
  const hook = renderHook(({ fullscreen }) => useAvalonAppearance(profile(), fullscreen), {
    wrapper,
    initialProps: { fullscreen: false },
  })
  await waitFor(() =>
    expect(window.winnow.windowAppearance).toHaveBeenCalledWith({
      enabled: true,
      material: 'acrylic',
      background: '#0F1C1E',
    }),
  )
  const late = answers.at(-1)!
  hook.rerender({ fullscreen: true })
  await act(async () => answers.at(-1)!({ requested: 'none', supported: true, platform: 'win32' }))
  await act(async () => late({ requested: 'acrylic', supported: true, platform: 'win32' }))
  expect(hook.result.current.active).toBe(false)
  expect(document.documentElement.dataset.avalonMaterial).toBe('none')
  hook.unmount()
  expect(window.winnow.windowAppearance).toHaveBeenLastCalledWith({
    enabled: false,
    material: 'acrylic',
    background: '#0F1C1E',
  })
})

it('rechecks material after OS accessibility changes and keeps saved preferences when it becomes unavailable', async () => {
  const hook = renderHook(() => useAvalonAppearance(profile(), false), { wrapper })
  await waitFor(() => expect(hook.result.current.active).toBe(true))
  vi.mocked(window.winnow.windowAppearance!).mockResolvedValue({
    requested: 'none',
    supported: false,
    platform: 'win32',
  })
  act(() => invalidate())
  await waitFor(() => expect(hook.result.current.active).toBe(false))
  expect(hook.result.current.appearance.transparency).toBe(60)
  expect(hook.result.current.style).toHaveProperty('--avalon-pane-ground', '#0F1C1EFF')
  expect(writes).toEqual([])
})

it('applies a newly selected palette opening position once but preserves saved choices on initial load', async () => {
  const hook = renderHook(({ selected }) => useAvalonAppearance(profile(selected), false), {
    wrapper,
    initialProps: { selected: 'rose-pine-dawn' },
  })
  await waitFor(() => expect(hook.result.current.appearance.transparency).toBe(60))
  expect(writes).toEqual([])
  hook.rerender({ selected: 'winnow' })
  hook.rerender({ selected: 'rose-pine-dawn' })
  await waitFor(() => expect(writes).toContainEqual(['Transparency', '0']))
  expect(writes.filter(([key]) => key === 'Transparency')).toHaveLength(1)
  await waitFor(() => expect(hook.result.current.appearance.transparency).toBe(0))
})

it('writes each appearance choice once and exposes the original light-desktop contrast consequence', async () => {
  render(<AvalonAppearanceControls profile={profile()} />, { wrapper })
  const slider = screen.getByRole('slider', { name: 'Transparency' })
  await waitFor(() => expect((slider as HTMLInputElement).disabled).toBe(false))
  fireEvent.change(slider, { target: { value: '90' } })
  await waitFor(() => expect((slider as HTMLInputElement).value).toBe('90'))
  expect(document.getElementById(slider.getAttribute('aria-describedby')!)!.textContent).toContain(
    'Reduce transparency',
  )
  fireEvent.change(screen.getByRole('combobox', { name: 'Backdrop' }), { target: { value: 'mica' } })
  await waitFor(() =>
    expect((screen.getByRole('combobox', { name: 'Backdrop' }) as HTMLSelectElement).value).toBe('mica'),
  )
  fireEvent.change(screen.getByRole('combobox', { name: 'Pane layout' }), { target: { value: 'flush' } })
  await waitFor(() =>
    expect((screen.getByRole('combobox', { name: 'Pane layout' }) as HTMLSelectElement).value).toBe('flush'),
  )
  fireEvent.change(screen.getByRole('combobox', { name: 'Pane layout' }), { target: { value: 'flush' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Include content panes/ }))
  await waitFor(() =>
    expect(
      (screen.getByRole('checkbox', { name: /Include content panes/ }) as HTMLInputElement).checked,
    ).toBe(false),
  )
  expect(writes).toEqual([
    ['Transparency', '90'],
    ['Backdrop', 'mica'],
    ['Layout', 'flush'],
    ['TranslucentWall', 'false'],
  ])
})

it('preserves existing solid flush preferences over new Windows defaults without writing them back', async () => {
  values = { Transparency: '0', Backdrop: 'mica', TranslucentWall: 'false', Layout: 'flush' }
  const hook = renderHook(() => useAvalonAppearance(profile(), false), { wrapper })
  await waitFor(() => expect(hook.result.current.appearance.layout).toBe('flush'))
  expect(hook.result.current.appearance).toEqual({
    transparency: 0,
    backdrop: 'mica',
    wallTranslucent: false,
    layout: 'flush',
  })
  expect(hook.result.current.active).toBe(false)
  expect(writes).toEqual([])
})

it('keeps the prior saved appearance and reports a failed write', async () => {
  refuseSave = true
  render(<AvalonAppearanceControls profile={profile()} />, { wrapper })
  const select = screen.getByRole('combobox', { name: 'Pane layout' })
  await waitFor(() => expect((select as HTMLSelectElement).disabled).toBe(false))
  fireEvent.change(select, { target: { value: 'flush' } })
  await waitFor(() =>
    expect(screen.getByRole('alert').textContent).toContain('Window appearance could not be saved'),
  )
  expect((select as HTMLSelectElement).value).toBe('floating')
  expect(writes).toEqual([])
})
