// @vitest-environment jsdom
import { useState } from 'react'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import {
  RatingCapControl,
  RatingCapPreference,
  ratingCapAt,
  ratingCapHiddenText,
  useLibraryPreferenceChange,
} from '../src/renderer/features/RatingCap'
import { DisplayPreferences } from '../src/renderer/features/DisplayPreferences'
import { useLibrary } from '../src/renderer/api/hooks'
import type { ApiRequest } from '../src/shared/bridge'
import type { LibraryPreferences, Mode } from '../src/renderer/api/types'

afterEach(cleanup)
beforeAll(() =>
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  ),
)
afterAll(() => vi.unstubAllGlobals())
const tiers = ['everyone', 'preteen', 'teen', 'mature', 'restricted18', 'adults_only']
const labels = ['All ages', 'Preteen', 'Teen', 'Mature', '18+', 'Adults only']
function LocalCap({ mode }: { mode: Mode }) {
  const [cap, setCap] = useState<string>()
  return (
    <>
      <RatingCapControl mode={mode} value={cap} change={setCap} hiddenCount={0} />
      <output data-testid="chosen-cap">{cap}</output>
    </>
  )
}
function fixture(
  initial: LibraryPreferences = {
    showNonGameEntries: false,
    showExplicitContent: false,
    maturityCap: 'adults_only',
  },
  override?: (input: ApiRequest) => unknown,
) {
  let stored = initial
  const presentation: Record<string, string> = {}
  let journal = { promptAfterPlay: false }
  const request = vi.fn(async (input: ApiRequest) => {
    const result = await override?.(input)
    if (result !== undefined) return result
    if (input.route === 'preferences.library.put') stored = input.body as LibraryPreferences
    if (input.route === 'preferences.presentation.put')
      presentation[String(input.params!.preference)] = (input.body as { value: string }).value
    if (input.route === 'journal.preferences.put') journal = input.body as typeof journal
    const data =
      input.route === 'preferences.library.get'
        ? stored
        : input.route === 'library.visibility'
          ? { ratingCapHidden: stored.maturityCap === 'teen' ? 1 : 0 }
          : input.route === 'library.get'
            ? { games: [], lists: [] }
            : input.route === 'preferences.presentation.get'
              ? Object.entries(presentation).map(([preference, value]) => ({ preference, value }))
              : input.route === 'journal.preferences.get'
                ? journal
                : null
    return { ok: true, status: 200, data }
  })
  Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return {
    request,
    client,
    stored: () => stored,
    update: (value: LibraryPreferences) => {
      stored = value
    },
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}
function LibraryObserver() {
  useLibrary()
  return null
}

describe.each(['desktop', 'fullscreen'] as const)('%s rating-cap preference parity', (mode) => {
  const name = mode === 'desktop' ? 'Rating cap' : 'Content age limit'
  it('spans the complete six-step scale and starts at the top without a settings store', () => {
    render(<LocalCap mode={mode} />)
    const slider = screen.getByRole('slider', { name }) as HTMLInputElement
    expect([slider.min, slider.max, slider.step, slider.value]).toEqual(['0', '5', '1', '5'])
    expect(slider.getAttribute('aria-valuetext')).toBe('Adults only')
    expect(screen.getByText('No titles hidden.')).toBeTruthy()
    expect(screen.getByText(/Adults-only content is still hidden/)).toBeTruthy()
  })
  it.each(tiers.map((tier, index) => [tier, index] as const))(
    'names and selects the %s step without IO',
    (tier, index) => {
      render(<LocalCap mode={mode} />)
      const slider = screen.getByRole('slider', { name }) as HTMLInputElement
      // The initial top step is already selected; move away first so this always exercises an input change.
      fireEvent.change(slider, { target: { value: '0' } })
      fireEvent.change(slider, { target: { value: String(index) } })
      expect(slider.value).toBe(String(index))
      expect(slider.getAttribute('aria-valuetext')).toBe(labels[index])
      expect(screen.getByTestId('chosen-cap').textContent).toBe(tier)
    },
  )
  it('shows the adult-toggle explanation only for a clamped top step', () => {
    const props = { mode, change: vi.fn(), hiddenCount: 0 }
    const view = render(<RatingCapControl {...props} value="adults_only" adultContentAllowed={false} />)
    expect(screen.getByText(/Adults-only content is still hidden/)).toBeTruthy()
    view.rerender(<RatingCapControl {...props} value="adults_only" adultContentAllowed />)
    expect(screen.queryByText(/Adults-only content is still hidden/)).toBeNull()
    view.rerender(<RatingCapControl {...props} value="restricted18" adultContentAllowed={false} />)
    expect(screen.queryByText(/Adults-only content is still hidden/)).toBeNull()
    expect(screen.getByText('Hides games rated above this level. Unrated games always stay.')).toBeTruthy()
    expect(props.change).not.toHaveBeenCalled()
  })
  it.each([null, '', 'pretty tame please'])(
    'loads missing or unreadable %s as no cap without writing it back',
    async (cap) => {
      const { wrapper, request } = fixture({
        showNonGameEntries: false,
        showExplicitContent: false,
        maturityCap: cap as string,
      })
      render(<RatingCapPreference mode={mode} />, { wrapper })
      const slider = screen.getByRole('slider', { name }) as HTMLInputElement
      await waitFor(() => expect(slider.disabled).toBe(false))
      expect(slider.value).toBe('5')
      expect(request.mock.calls.some(([input]) => input.route === 'preferences.library.put')).toBe(false)
    },
  )
  it('saves against the latest complete preference snapshot and refreshes the library and cap-only count', async () => {
    const fixture_ = fixture()
    render(
      <>
        <LibraryObserver />
        <RatingCapPreference mode={mode} />
      </>,
      { wrapper: fixture_.wrapper },
    )
    const slider = screen.getByRole('slider', { name }) as HTMLInputElement
    await waitFor(() => expect(slider.disabled).toBe(false))
    fixture_.update({ showNonGameEntries: true, showExplicitContent: true, maturityCap: 'adults_only' })
    fireEvent.change(slider, { target: { value: '2' } })
    await screen.findByText('Hiding 1 title.')
    await waitFor(() => expect(slider.disabled).toBe(false))
    expect(slider.value).toBe('2')
    expect(fixture_.stored()).toEqual({
      showNonGameEntries: true,
      showExplicitContent: true,
      maturityCap: 'teen',
    })
    expect(
      fixture_.request.mock.calls.filter(([input]) => input.route === 'preferences.library.put'),
    ).toHaveLength(1)
    expect(
      fixture_.request.mock.calls.filter(([input]) => input.route === 'library.get').length,
    ).toBeGreaterThan(1)
    expect(screen.queryByText(/Adults-only content is still hidden/)).toBeNull()
  })
  it('keeps a failed count honest and retries without writing preferences', async () => {
    let refuse = true
    const { wrapper, request } = fixture(undefined, (input) =>
      input.route === 'library.visibility' && refuse
        ? { ok: false, status: 503, message: 'Unavailable' }
        : undefined,
    )
    render(<RatingCapPreference mode={mode} />, { wrapper })
    await screen.findByText('Hidden-title count is unavailable.')
    expect(screen.queryByText('No titles hidden.')).toBeNull()
    refuse = false
    fireEvent.click(screen.getByRole('button', { name: 'Retry rating preference' }))
    await screen.findByText('No titles hidden.')
    expect(request.mock.calls.some(([input]) => input.route === 'preferences.library.put')).toBe(false)
  })
  it.each([false, true])(
    'restores focus after a cap save unless the user moved elsewhere: %s',
    async (moved) => {
      let release!: () => void
      const pending = new Promise<void>((resolve) => {
        release = resolve
      })
      const { wrapper } = fixture(undefined, async (input) => {
        if (input.route === 'preferences.library.put') await pending
      })
      render(
        <>
          <RatingCapPreference mode={mode} />
          <button>Another preference</button>
        </>,
        { wrapper },
      )
      const slider = screen.getByRole('slider', { name }) as HTMLInputElement
      const other = screen.getByRole('button', { name: 'Another preference' })
      await waitFor(() => expect(slider.disabled).toBe(false))
      slider.focus()
      fireEvent.change(slider, { target: { value: '2' } })
      await waitFor(() => expect(slider.disabled).toBe(true))
      // JSDOM does not perform Chromium's blur when a focused input becomes disabled.
      slider.blur()
      if (moved) other.focus()
      await act(async () => release())
      await waitFor(() => expect(slider.disabled).toBe(false))
      expect(document.activeElement).toBe(moved ? other : slider)
    },
  )
  it('reports a refused cap save without losing the stored value and permits a retry', async () => {
    let refuse = true
    const { wrapper, stored } = fixture(undefined, (input) =>
      input.route === 'preferences.library.put' && refuse
        ? { ok: false, status: 503, message: 'Could not save this preference.' }
        : undefined,
    )
    render(<RatingCapPreference mode={mode} />, { wrapper })
    const slider = screen.getByRole('slider', { name }) as HTMLInputElement
    await waitFor(() => expect(slider.disabled).toBe(false))
    fireEvent.change(slider, { target: { value: '2' } })
    await screen.findByRole('alert')
    await waitFor(() => expect(slider.disabled).toBe(false))
    expect(slider.value).toBe('5')
    expect(stored().maturityCap).toBe('adults_only')
    refuse = false
    fireEvent.change(slider, { target: { value: '2' } })
    await screen.findByText('Hiding 1 title.')
    expect(stored().maturityCap).toBe('teen')
  })
})

it.each([
  [-4, 'everyone'],
  [0.4, 'everyone'],
  [2.5, 'mature'],
  [99, 'adults_only'],
] as const)('snaps the original slider position %s onto its nearest bounded tier', (position, expected) => {
  expect(ratingCapAt(position)).toBe(expected)
})
it.each([
  [0, 'No titles hidden.'],
  [-1, 'No titles hidden.'],
  [1, 'Hiding 1 title.'],
  [2400, `Hiding ${(2400).toLocaleString()} titles.`],
] as const)('names the cap-only hidden count %s', (count, expected) => {
  expect(ratingCapHiddenText(count)).toBe(expected)
})

it('serializes separate preference controls and reads fresh fields when the next write starts', async () => {
  let release!: () => void
  let held = true
  const heldRead = new Promise<void>((resolve) => {
    release = resolve
  })
  const fixture_ = fixture(undefined, async (input) => {
    if (input.route === 'preferences.library.get' && held) {
      held = false
      await heldRead
    }
  })
  let first!: ReturnType<typeof useLibraryPreferenceChange>,
    second!: ReturnType<typeof useLibraryPreferenceChange>
  function Controls() {
    first = useLibraryPreferenceChange()
    second = useLibraryPreferenceChange()
    return <p>{first.pending && second.pending ? 'Both controls waiting' : 'Ready'}</p>
  }
  render(<Controls />, { wrapper: fixture_.wrapper })
  act(() => {
    first.apply('maturityCap', 'teen')
    second.apply('showExplicitContent', true)
  })
  await screen.findByText('Both controls waiting')
  expect(
    fixture_.request.mock.calls.filter(([input]) => input.route === 'preferences.library.get'),
  ).toHaveLength(1)
  await act(async () => release())
  await screen.findByText('Ready')
  expect(fixture_.stored()).toEqual({
    showNonGameEntries: false,
    showExplicitContent: true,
    maturityCap: 'teen',
  })
  expect(
    fixture_.request.mock.calls
      .filter(([input]) => input.route === 'preferences.library.put')
      .map(([input]) => input.body),
  ).toEqual([
    { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'teen' },
    { showNonGameEntries: false, showExplicitContent: true, maturityCap: 'teen' },
  ])
})

it('opens the desktop display controls together and returns focus to their command-bar trigger', async () => {
  const { wrapper } = fixture()
  render(<DisplayPreferences />, { wrapper })
  const trigger = screen.getByRole('button', { name: 'Display preferences' })
  trigger.focus()
  fireEvent.click(trigger)
  await screen.findByRole('dialog', { name: 'Display preferences' })
  expect(screen.queryByRole('slider', { name: 'Density' })).toBeNull()
  expect(screen.getByRole('combobox', { name: 'Cover art' })).toBeTruthy()
  expect(screen.getByRole('slider', { name: 'Rating cap' })).toBeTruthy()
  expect(screen.getByRole('checkbox', { name: 'Dim dormant covers' })).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'Close display preferences' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  await waitFor(() => expect(document.activeElement).toBe(trigger))
})

it('persists the source display preferences through closing and reopening their popover', async () => {
  const { wrapper, request, stored } = fixture()
  render(<DisplayPreferences />, { wrapper })
  fireEvent.click(screen.getByRole('button', { name: 'Display preferences' }))
  const fit = screen.getByRole('combobox', { name: 'Cover art' }) as HTMLSelectElement
  await waitFor(() => expect(fit.disabled).toBe(false))
  expect(fit.value).toBe('fit')
  fireEvent.change(fit, { target: { value: 'fill' } })
  await waitFor(() => {
    expect(fit.disabled).toBe(false)
    expect(fit.value).toBe('fill')
  })
  for (const [name, checked] of [
    ['Dim dormant covers', false],
    ['Show non-game entries', true],
    ['Group expansions under the base game', true],
    ['Ask for a note after playing', true],
  ] as const) {
    const checkbox = screen.getByRole('checkbox', { name: new RegExp(`^${name}`) }) as HTMLInputElement
    await waitFor(() => expect(checkbox.disabled).toBe(false))
    expect(checkbox.checked).toBe(!checked)
    fireEvent.click(checkbox)
    await waitFor(() => {
      expect(checkbox.disabled).toBe(false)
      expect(checkbox.checked).toBe(checked)
    })
  }
  expect(stored()).toEqual({
    showNonGameEntries: true,
    showExplicitContent: false,
    maturityCap: 'adults_only',
  })
  expect(
    request.mock.calls
      .filter(([input]) => input.route === 'preferences.presentation.put')
      .map(([input]) => [input.params!.preference, input.body]),
  ).toEqual([
    ['CoverArtMode', { value: 'fill' }],
    ['DimDormantCovers', { value: 'false' }],
    ['GroupExpansions', { value: 'true' }],
  ])
  fireEvent.click(screen.getByRole('button', { name: 'Close display preferences' }))
  fireEvent.click(screen.getByRole('button', { name: 'Display preferences' }))
  expect((screen.getByRole('combobox', { name: 'Cover art' }) as HTMLSelectElement).value).toBe('fill')
  expect((screen.getByRole('checkbox', { name: 'Dim dormant covers' }) as HTMLInputElement).checked).toBe(
    false,
  )
  expect(
    (screen.getByRole('checkbox', { name: /^Ask for a note after playing/ }) as HTMLInputElement).checked,
  ).toBe(true)
})
