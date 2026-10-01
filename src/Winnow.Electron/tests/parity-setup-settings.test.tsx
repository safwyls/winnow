// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Setup } from '../src/renderer/features/Setup'
import { SetupAppearance } from '../src/renderer/features/SetupAppearance'
import type { Mode } from '../src/renderer/api/types'
import { useThemeRuntime } from '../src/renderer/theming/runtime'
import { DEFAULT_PROFILE, type ThemeDefinition } from '../src/shared/theme'
import type { ApiRequest, WinnowBridge } from '../src/shared/bridge'
import { useState } from 'react'

afterEach(cleanup)
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}
const themes: ThemeDefinition[] = [
  { apiVersion: 1, id: 'avalon', name: 'Avalon', Shell: () => null },
  { apiVersion: 1, id: 'afterglow', name: 'Afterglow', Shell: () => null },
]
function fixture(initial: number, appearance = false, mode: Mode = 'fullscreen') {
  let cursor: number | null = initial
  let pending: Promise<unknown> | null = null
  let fail = false
  const preferences = new Map<string, string>()
  let library = { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'adults_only' }
  const request = vi.fn(async (input: ApiRequest) => {
    let data: unknown = null
    if (input.route === 'setup.get') data = { step: cursor }
    if (input.route === 'setup.put') cursor = (input.body as { step: number | null }).step
    if (input.route === 'preferences.presentation.get')
      data = [...preferences].map(([preference, value]) => ({ preference, value }))
    if (input.route === 'preferences.library.get') data = library
    if (input.route === 'preferences.presentation.put' || input.route === 'preferences.library.put') {
      await pending
      if (fail) return { ok: false, status: 500 }
      if (input.route === 'preferences.library.put') library = input.body as typeof library
      else preferences.set(String(input.params?.preference), (input.body as { value: string }).value)
    }
    if (input.route === 'journal.preferences.get') data = { promptAfterPlay: false }
    if (input.route === 'connections.visibility.get')
      data = { accountConfirmed: true, ownAccountOnly: false, hiddenCount: 0 }
    if (input.route === 'library.visibility') data = { explicitHidden: 0, ratingCapHidden: 0 }
    if (input.route === 'library.get') data = { games: [], lists: [] }
    return { ok: true, status: 200, data }
  })
  const savePreferences = vi.fn(async (_profile: unknown) => {})
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      request,
      loadPreferences: vi.fn(async () => structuredClone(DEFAULT_PROFILE)),
      savePreferences,
      listThemes: vi.fn(async () => []),
      listFonts: vi.fn(async () => ['Fixture font']),
    } as unknown as WinnowBridge,
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  let changeMode: (value: Mode) => void = () => {}
  function AppearanceSetup() {
    const runtime = useThemeRuntime(themes)
    const [presentation, setPresentation] = useState(mode)
    changeMode = setPresentation
    return (
      <Setup
        mode={presentation}
        appearanceSaving={runtime.profileSaving}
        appearanceSaveError={runtime.profileSaveError}
        appearance={<SetupAppearance runtime={runtime} mode={presentation} />}
      />
    )
  }
  render(
    <QueryClientProvider client={client}>
      {appearance ? <AppearanceSetup /> : <Setup mode={mode} />}
    </QueryClientProvider>,
  )
  return {
    request,
    savePreferences,
    preferences,
    mode: (value: Mode) => changeMode(value),
    cursor: () => cursor,
    gate: (value: Promise<unknown> | null) => {
      pending = value
    },
    fail: () => {
      fail = true
    },
  }
}

it.each(['saved', 'failed'] as const)(
  'setup retains a %s profile write across presentation changes without mounting the provider',
  async (outcome) => {
    const f = fixture(5, true, 'desktop')
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Continue' }) as HTMLButtonElement).disabled).toBe(false),
    )
    const gate = deferred<void>()
    f.savePreferences.mockImplementationOnce(async () => gate.promise)
    fireEvent.change(screen.getByRole('combobox', { name: 'Theme' }), { target: { value: 'nightshift' } })
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Continue' }) as HTMLButtonElement).disabled).toBe(true),
    )
    act(() => f.mode('fullscreen'))
    const next = screen.getByRole('button', { name: 'Continue' }) as HTMLButtonElement
    expect(screen.getByRole('button', { name: 'Choose theme and appearance' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Theme' })).toBeNull()
    expect(next.disabled).toBe(true)
    fireEvent.click(next)
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    expect(f.request.mock.calls.some(([input]) => input.route === 'setup.put')).toBe(false)
    if (outcome === 'saved') await act(async () => gate.resolve())
    else await act(async () => gate.reject(Error('private disk failure')))
    await waitFor(() => expect(next.disabled).toBe(false))
    if (outcome === 'failed') {
      await screen.findByText('Appearance changes could not be saved. You can export a profile to keep them.')
      fireEvent.click(next)
      await screen.findByText(/A preference could not be saved/)
      expect(f.cursor()).toBe(5)
      act(() => f.mode('desktop'))
      act(() => f.mode('fullscreen'))
      fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
      expect(f.cursor()).toBe(5)
      fireEvent.click(screen.getByRole('button', { name: 'Skip this step' }))
    } else fireEvent.click(next)
    await waitFor(() => expect(f.cursor()).toBe(6))
  },
)
async function openProvider(label: string) {
  fireEvent.click(await screen.findByRole('button', { name: label }))
  return screen.getByRole('button', { name: 'Back to setup' }) as HTMLButtonElement
}

it('desktop setup composes ordinary appearance controls and waits for palette, material and typography saves', async () => {
  const f = fixture(5, true, 'desktop')
  const next = (await screen.findByRole('button', { name: 'Continue' })) as HTMLButtonElement
  await waitFor(() => expect(next.disabled).toBe(false))
  for (const name of ['Winnow design', 'Theme', 'Backdrop', 'Pane layout'])
    expect(screen.getByRole('combobox', { name })).toBeTruthy()
  for (const name of ['Transparency', 'Theme text size'])
    expect(screen.getByRole('slider', { name })).toBeTruthy()
  for (const name of ['Heading font', 'Interface font', 'Data font'])
    expect(screen.getByLabelText(name)).toBeTruthy()
  expect(screen.getByRole('checkbox', { name: /Include content panes/ })).toBeTruthy()
  const gate = deferred<void>()
  f.savePreferences.mockImplementationOnce(async () => gate.promise)
  fireEvent.change(screen.getByRole('combobox', { name: 'Theme' }), { target: { value: 'nightshift' } })
  await waitFor(() => expect(next.disabled).toBe(true))
  expect(f.cursor()).toBe(5)
  await act(async () => gate.resolve())
  await waitFor(() => expect(next.disabled).toBe(false))
  expect(f.savePreferences).toHaveBeenLastCalledWith(
    expect.objectContaining({
      settings: expect.objectContaining({ avalon: expect.objectContaining({ palette: 'nightshift' }) }),
    }),
  )
  fireEvent.change(screen.getByRole('combobox', { name: 'Pane layout' }), { target: { value: 'flush' } })
  await waitFor(() => expect(f.preferences.get('Layout')).toBe('flush'))
  await waitFor(() => expect(next.disabled).toBe(false))
  fireEvent.change(screen.getByRole('slider', { name: 'Transparency' }), { target: { value: '25' } })
  await waitFor(() => expect(f.preferences.get('Transparency')).toBe('25'))
  await waitFor(() => expect(next.disabled).toBe(false))
  fireEvent.change(screen.getByLabelText('Heading font'), { target: { value: 'Fixture Display' } })
  await waitFor(() =>
    expect(f.savePreferences).toHaveBeenLastCalledWith(
      expect.objectContaining({
        appearance: expect.objectContaining({
          typography: expect.objectContaining({
            nightshift: expect.objectContaining({ headingFont: 'Fixture Display' }),
          }),
        }),
      }),
    ),
  )
  await waitFor(() => expect(next.disabled).toBe(false))
  fireEvent.click(next)
  await waitFor(() => expect(f.cursor()).toBe(6))
})

it.each([
  [6, 'Choose app settings', 'Start in fullscreen', 'preferences.presentation.put'],
  [7, 'Choose library settings', 'Non-game entries', 'preferences.library.put'],
] as const)(
  'fullscreen setup step%i uses TV switches and holds failed or pending %s writes',
  async (step, entry, label, route) => {
    const f = fixture(step)
    const back = await openProvider(entry)
    const toggle = (await screen.findByRole('switch', { name: label })) as HTMLButtonElement
    await waitFor(() => expect(toggle.disabled).toBe(false))
    if (step === 7) {
      expect(screen.getByRole('button', { name: 'Default library sort' })).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Content age limit' })).toBeTruthy()
      expect(screen.queryByRole('slider')).toBeNull()
      for (const name of ['Library tools', 'Spending', 'Recommendations'])
        expect(screen.queryByRole('button', { name })).toBeNull()
    }
    const gate = deferred<void>()
    f.gate(gate.promise)
    f.fail()
    fireEvent.click(toggle)
    await waitFor(() => expect(back.disabled).toBe(true))
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    expect(screen.getByRole('dialog').getAttribute('data-setup-provider')).toBe(String(step))
    expect(f.cursor()).toBe(step)
    await act(async () => gate.resolve())
    await waitFor(() => expect(back.disabled).toBe(false))
    expect(f.request.mock.calls.some(([input]) => input.route === route)).toBe(true)
    fireEvent.click(back)
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    await screen.findByText(/A preference could not be saved/)
    expect(f.cursor()).toBe(step)
    fireEvent.click(screen.getByRole('button', { name: 'Skip this step' }))
    await waitFor(() => expect(f.cursor()).toBe(step + 1))
  },
)

it('fullscreen setup exposes appearance rows and traps theme, font and reset layers above its provider', async () => {
  const f = fixture(5, true)
  await openProvider('Choose theme and appearance')
  await waitFor(() =>
    expect((screen.getByRole('button', { name: 'Text size' }) as HTMLButtonElement).disabled).toBe(false),
  )
  for (const name of [
    'Text size',
    'Interface scale',
    'Screen margins',
    'Cover art',
    'Winnow design',
    'Theme',
    'Heading font',
    'Interface font',
    'Data font',
  ])
    expect(screen.getByRole('button', { name })).toBeTruthy()
  expect(screen.getByRole('switch', { name: 'Reduce motion' })).toBeTruthy()
  expect(screen.queryByRole('button', { name: 'Theme Studio' })).toBeNull()
  const hints = screen.getByRole('group', { name: 'Setup controls' })
  await waitFor(() => expect(hints.textContent).toContain('Reset page'))
  expect(hints.textContent).toContain('Left / Right Adjust')
  expect(hints.querySelector('[data-setup-glyph="Y"] svg')).toBeTruthy()
  for (const label of ['Theme', 'Heading font', 'Winnow design']) {
    const origin = screen.getByRole('button', { name: label })
    act(() => origin.focus())
    fireEvent.click(origin)
    const nested = await screen.findByRole('dialog', { name: label })
    expect(nested.classList.contains('setup-nested-dialog')).toBe(true)
    expect(document.querySelector('.setup-nested-overlay')).toBeTruthy()
    expect(nested.contains(document.activeElement)).toBe(true)
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: label })).toBeNull())
    await waitFor(() => expect(document.activeElement).toBe(origin))
    expect(f.cursor()).toBe(5)
    expect(screen.getByRole('dialog').getAttribute('data-setup-provider')).toBe('5')
  }
  const reset = screen.getByRole('button', { name: 'Reset fullscreen appearance…' })
  act(() => reset.focus())
  fireEvent.click(reset)
  const confirmation = screen.getByRole('alertdialog', { name: 'Reset fullscreen appearance?' })
  expect(confirmation.classList.contains('setup-nested-dialog')).toBe(true)
  fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
  await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
  expect(document.activeElement).toBe(reset)
  expect(f.cursor()).toBe(5)
})

it('theme profile saves hold setup navigation and a failed save can recover through a new selection', async () => {
  const f = fixture(5, true)
  const back = await openProvider('Choose theme and appearance')
  await waitFor(() => expect(back.disabled).toBe(false))
  const gate = deferred<void>()
  f.savePreferences.mockImplementationOnce(async () => gate.promise)
  fireEvent.click(screen.getByRole('button', { name: 'Theme' }))
  fireEvent.click(
    within(screen.getByRole('dialog', { name: 'Theme' })).getByRole('button', { name: 'Nightshift' }),
  )
  await waitFor(() => expect(back.disabled).toBe(true))
  expect(f.cursor()).toBe(5)
  await act(async () => gate.reject(Error('private disk failure')))
  await screen.findByText('Appearance changes could not be saved. You can export a profile to keep them.')
  expect(document.body.textContent).not.toContain('private disk failure')
  await waitFor(() => expect(back.disabled).toBe(false))
  fireEvent.click(back)
  fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
  await screen.findByText(/A preference could not be saved/)
  expect(f.cursor()).toBe(5)
  await openProvider('Choose theme and appearance')
  fireEvent.click(screen.getByRole('button', { name: 'Theme' }))
  fireEvent.click(
    within(screen.getByRole('dialog', { name: 'Theme' })).getByRole('button', { name: 'Winnow' }),
  )
  await waitFor(() =>
    expect((screen.getByRole('button', { name: 'Back to setup' }) as HTMLButtonElement).disabled).toBe(false),
  )
  await waitFor(() =>
    expect(
      screen.queryByText('Appearance changes could not be saved. You can export a profile to keep them.'),
    ).toBeNull(),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Back to setup' }))
  fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
  await waitFor(() => expect(f.cursor()).toBe(6))
})
