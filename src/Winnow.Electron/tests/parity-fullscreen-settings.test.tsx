// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ApiRequest } from '../src/shared/bridge'
import { FullscreenAppearance, boundedSetting } from '../src/renderer/features/FullscreenAppearance'
import { LibraryPresentationPreferences } from '../src/renderer/features/SettingsPreferences'
import { Settings } from '../src/renderer/features/Settings'
import { clearViewState } from '../src/renderer/viewState'

afterEach(() => {
  cleanup()
  clearViewState('fullscreen:settings:tab')
  clearViewState('desktop:settings:tab')
})
function fixture(initial: Record<string, string> = {}) {
  const values = { ...initial }
  let refusal = false
  const request = vi.fn(async (input: ApiRequest) => {
    let data: unknown = null
    if (input.route === 'preferences.presentation.get')
      data = Object.entries(values).map(([preference, value]) => ({ preference, value }))
    if (input.route === 'preferences.presentation.put') {
      if (refusal) return { ok: false, status: 500, message: 'Could not save this preference.' }
      values[String(input.params!.preference)] = (input.body as { value: string }).value
    }
    if (input.route === 'journal.preferences.get') data = { promptAfterPlay: false }
    if (input.route === 'preferences.library.get')
      data = { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'adults_only' }
    if (input.route === 'operations.get') data = []
    if (input.route === 'library.get') data = { games: [], lists: [] }
    return { ok: true, status: 200, data }
  })
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return {
    values,
    request,
    client,
    refuse: (value: boolean) => {
      refusal = value
    },
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}
async function ready(label: string, role: 'button' | 'switch' = 'button') {
  const control = (await screen.findByRole(role, { name: label })) as HTMLButtonElement
  await waitFor(() => expect(control.disabled).toBe(false))
  return control
}
async function direction(control: HTMLElement, key: 'ArrowLeft' | 'ArrowRight', expected: string) {
  fireEvent.keyDown(control, { key })
  await waitFor(() => expect(control.textContent).toContain(expected))
  await waitFor(() => expect((control as HTMLButtonElement).disabled).toBe(false))
}

describe('fullscreen source setting rows', () => {
  it('returns from additional settings pages to their parent action without losing focus', async () => {
    const f = fixture()
    render(<Settings mode="fullscreen" />, { wrapper: f.wrapper })
    const sections = screen.getByRole('navigation', { name: 'Settings section' })
    fireEvent.click(within(sections).getByRole('button', { name: 'Library' }))
    fireEvent.click(screen.getByRole('button', { name: 'Recommendations' }))
    const back = screen.getByRole('button', { name: 'Back to Library' })
    await waitFor(() => expect(document.activeElement).toBe(back))
    fireEvent.keyDown(back, { key: 'Escape' })
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Recommendations' })),
    )
    expect(within(sections).getByRole('button', { name: 'Library' }).getAttribute('aria-pressed')).toBe(
      'true',
    )
  })
  it('uses ten-point text steps with bounded adjustment and pointer steppers outside sequential focus', async () => {
    const f = fixture()
    render(<FullscreenAppearance />, { wrapper: f.wrapper })
    const row = await ready('Text size')
    row.focus()
    await direction(row, 'ArrowRight', '110%')
    expect(f.values.FullscreenTextScale).toBe('1.1')
    for (const value of ['120%', '130%', '140%']) await direction(row, 'ArrowRight', value)
    const writes = f.request.mock.calls.filter(
      ([input]) => input.route === 'preferences.presentation.put',
    ).length
    fireEvent.keyDown(row, { key: 'ArrowRight' })
    expect(
      f.request.mock.calls.filter(([input]) => input.route === 'preferences.presentation.put'),
    ).toHaveLength(writes)
    for (const value of ['130%', '120%', '110%', '100%', '90%', '80%', '70%'])
      await direction(row, 'ArrowLeft', value)
    const increase = screen.getByRole('button', { name: 'Increase text size' })
    expect(increase.tabIndex).toBe(-1)
    expect(screen.getByRole('button', { name: 'Decrease text size' }).tabIndex).toBe(-1)
    fireEvent.click(increase)
    await waitFor(() => expect(f.values.FullscreenTextScale).toBe('0.8'))
    await waitFor(() => expect(row.disabled).toBe(false))
    fireEvent.click(screen.getByRole('button', { name: 'Decrease text size' }))
    await waitFor(() => expect(f.values.FullscreenTextScale).toBe('0.7'))
    expect(Object.keys(f.values)).toEqual(['FullscreenTextScale'])
  })
  it('sets switch directions idempotently and exposes the confirmed on/off state', async () => {
    const f = fixture()
    render(<FullscreenAppearance />, { wrapper: f.wrapper })
    const toggle = await ready('Fit ultrawide displays', 'switch')
    expect(toggle.getAttribute('aria-checked')).toBe('false')
    await direction(toggle, 'ArrowRight', 'On')
    const count = f.request.mock.calls.length
    fireEvent.keyDown(toggle, { key: 'ArrowRight' })
    expect(f.request.mock.calls).toHaveLength(count)
    expect(toggle.getAttribute('aria-checked')).toBe('true')
    await direction(toggle, 'ArrowLeft', 'Off')
    fireEvent.click(toggle)
    await waitFor(() => expect(toggle.getAttribute('aria-checked')).toBe('true'))
  })
  it('persists five-point interface adjustments across reloads and clamps at both limits without changing text size', async () => {
    const f = fixture({ FullscreenTextScale: '1.3', FullscreenSafeMargin: '3' })
    const first = render(<FullscreenAppearance />, { wrapper: f.wrapper })
    let row = await ready('Interface scale')
    expect(row.textContent).toContain('100%')
    row.focus()
    for (const value of ['105%', '110%', '115%']) await direction(row, 'ArrowRight', value)
    expect(f.values.FullscreenInterfaceScale).toBe('1.15')
    expect(document.activeElement).toBe(row)
    first.unmount()
    f.client.clear()
    render(<FullscreenAppearance />, { wrapper: f.wrapper })
    row = await ready('Interface scale')
    expect(row.textContent).toContain('115%')
    row.focus()
    await direction(row, 'ArrowRight', '120%')
    const writeCount = () =>
      f.request.mock.calls.filter(([input]) => input.route === 'preferences.presentation.put').length
    let count = writeCount()
    fireEvent.keyDown(row, { key: 'ArrowRight' })
    expect(writeCount()).toBe(count)
    for (const value of ['115%', '110%', '105%', '100%', '95%', '90%', '85%', '80%'])
      await direction(row, 'ArrowLeft', value)
    count = writeCount()
    fireEvent.keyDown(row, { key: 'ArrowLeft' })
    expect(writeCount()).toBe(count)
    const increase = screen.getByRole('button', { name: 'Increase interface scale' })
    const decrease = screen.getByRole('button', { name: 'Decrease interface scale' })
    expect(increase.tabIndex).toBe(-1)
    expect(decrease.tabIndex).toBe(-1)
    fireEvent.click(increase)
    await waitFor(() => expect(f.values.FullscreenInterfaceScale).toBe('0.85'))
    await waitFor(() => expect(row.disabled).toBe(false))
    fireEvent.click(decrease)
    await waitFor(() => expect(f.values.FullscreenInterfaceScale).toBe('0.8'))
    await waitFor(() => expect(row.disabled).toBe(false))
    expect(document.activeElement).toBe(row)
    expect(f.values.FullscreenTextScale).toBe('1.3')
    expect(f.values.FullscreenSafeMargin).toBe('3')
    expect(
      f.request.mock.calls
        .filter(([input]) => input.route === 'preferences.presentation.put')
        .every(([input]) => input.params?.preference === 'FullscreenInterfaceScale'),
    ).toBe(true)
  })
  it.each([
    ['5', '120%'],
    ['0', '80%'],
    ['NaN', '100%'],
    ['Infinity', '100%'],
    ['-Infinity', '100%'],
    ['', '100%'],
    ['   ', '100%'],
  ])(
    'reads saved interface scale %s as %s without overwriting it or the independent text size',
    async (value, expected) => {
      const f = fixture({ FullscreenInterfaceScale: value, FullscreenTextScale: '1.3' })
      render(<FullscreenAppearance />, { wrapper: f.wrapper })
      expect((await ready('Interface scale')).textContent).toContain(expected)
      expect((await ready('Text size')).textContent).toContain('130%')
      expect(f.values.FullscreenInterfaceScale).toBe(value)
      expect(f.request.mock.calls.filter(([input]) => input.route.endsWith('.put'))).toHaveLength(0)
    },
  )
  it('reflects desktop dormancy changes in the fullscreen switch and writes back the shared preference', async () => {
    const f = fixture()
    render(
      <>
        <LibraryPresentationPreferences />
        <FullscreenAppearance />
      </>,
      { wrapper: f.wrapper },
    )
    const television = await ready('Dim dormant covers', 'switch')
    const desktop = screen.getByRole('checkbox', { name: 'Dim dormant covers' }) as HTMLInputElement
    expect(desktop.checked).toBe(true)
    fireEvent.click(desktop)
    await waitFor(() => expect(television.getAttribute('aria-checked')).toBe('false'))
    await waitFor(() => expect(television.disabled).toBe(false))
    await direction(television, 'ArrowRight', 'On')
    expect(desktop.checked).toBe(true)
    await direction(television, 'ArrowLeft', 'Off')
    expect(desktop.checked).toBe(false)
  })
  it('reflects the current shared cover crop and uses either direction or accept to change it', async () => {
    const f = fixture({ CoverArtMode: 'fill' })
    render(<FullscreenAppearance />, { wrapper: f.wrapper })
    const row = await ready('Cover art')
    expect(row.textContent).toContain('Fill')
    f.values.CoverArtMode = 'fit'
    await act(async () => {
      await f.client.invalidateQueries({ queryKey: ['api'] })
    })
    await waitFor(() => expect(row.textContent).toContain('Fit'))
    await direction(row, 'ArrowRight', 'Fill')
    await direction(row, 'ArrowLeft', 'Fit')
    fireEvent.click(row)
    await waitFor(() => expect(f.values.CoverArtMode).toBe('fill'))
  })
  it('opens reset without writes starts on cancel and resets only the five fullscreen-owned settings', async () => {
    const f = fixture({
      FullscreenTextScale: '1.3',
      FullscreenInterfaceScale: '1.1',
      FullscreenSafeMargin: '8',
      FullscreenReducedMotion: 'true',
      FullscreenFitUltrawide: 'true',
      DimDormantCovers: 'false',
      CoverArtMode: 'fill',
      DefaultSort: 'NameAscending',
    })
    render(<FullscreenAppearance />, { wrapper: f.wrapper })
    const reset = await ready('Reset fullscreen appearance…')
    expect(reset.hasAttribute('data-controller-context')).toBe(true)
    fireEvent.click(reset)
    let dialog = screen.getByRole('alertdialog', { name: 'Reset fullscreen appearance?' })
    expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Cancel' }))
    expect(f.request.mock.calls.filter(([input]) => input.route.endsWith('.put'))).toHaveLength(0)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    expect(f.values.FullscreenTextScale).toBe('1.3')
    fireEvent.click(reset)
    dialog = screen.getByRole('alertdialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reset fullscreen appearance' }))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect(f.values).toEqual({
      FullscreenTextScale: '1',
      FullscreenInterfaceScale: '1',
      FullscreenSafeMargin: '5',
      FullscreenReducedMotion: 'false',
      FullscreenFitUltrawide: 'false',
      DimDormantCovers: 'false',
      CoverArtMode: 'fill',
      DefaultSort: 'NameAscending',
    })
  })
  it('preserves the saved value after refusal and allows a deliberate retry', async () => {
    const f = fixture({ FullscreenTextScale: '1' })
    render(<FullscreenAppearance />, { wrapper: f.wrapper })
    const row = await ready('Text size')
    f.refuse(true)
    fireEvent.keyDown(row, { key: 'ArrowRight' })
    await screen.findByRole('alert')
    expect(f.values.FullscreenTextScale).toBe('1')
    await waitFor(() => expect(row.disabled).toBe(false))
    f.refuse(false)
    await direction(row, 'ArrowRight', '110%')
  })
  it('opens the original seven sections in source order and keeps desktop section selection independent', async () => {
    const f = fixture()
    render(<Settings mode="fullscreen" />, { wrapper: f.wrapper })
    const sections = screen.getByRole('navigation', { name: 'Settings section' })
    expect(
      within(sections)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual([
      'Appearance',
      'Controller',
      'Library',
      'Platforms',
      'Metadata & artwork',
      'Plugins',
      'Application',
    ])
    expect(within(sections).getByRole('button', { name: 'Appearance' }).getAttribute('aria-pressed')).toBe(
      'true',
    )
    fireEvent.click(within(sections).getByRole('button', { name: 'Controller' }))
    const guide = screen.getByRole('region', { name: 'Controller guide' })
    expect(within(guide).getAllByRole('definition')).toHaveLength(10)
    for (const label of ['Move', 'Select', 'Tabs & shelves', 'Scroll long content'])
      expect(within(guide).getByText(label)).toBeTruthy()
    cleanup()
    render(<Settings mode="desktop" />, { wrapper: f.wrapper })
    expect(screen.getByRole('button', { name: 'Platforms' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.queryByRole('button', { name: 'Controller' })).toBeNull()
    cleanup()
    render(<Settings mode="fullscreen" />, { wrapper: f.wrapper })
    expect(screen.getByRole('region', { name: 'Controller guide' })).toBeTruthy()
  })
  it.each([
    [null, 1],
    ['', 1],
    ['unknown', 1],
    ['Infinity', 1],
    ['0.2', 0.7],
    ['8', 1.4],
  ])('uses a valid bounded reading for %s', (input, expected) => {
    expect(boundedSetting(input as string | null, 1, 0.7, 1.4)).toBe(expected)
  })
})
