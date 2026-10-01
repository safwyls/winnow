// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AccountVisibility } from '../src/renderer/features/SettingsPreferences'
import type { ApiRequest } from '../src/shared/bridge'

afterEach(cleanup)
const blocked =
  'Winnow does not know which Steam account is yours yet. Signing in tells it immediately; an API key finds out at the next Steam import.'
const caveat = "Games Winnow cannot attribute stay visible. Shown playtime becomes your account's."
function fixture(mode: 'desktop' | 'fullscreen') {
  const state = { accountConfirmed: false, ownAccountOnly: false, hiddenCount: 0 }
  let stored: boolean | undefined
  const request = vi.fn(async (input: ApiRequest) => {
    if (input.route === 'connections.visibility.put') {
      stored = (input.body as { ownAccountOnly: boolean }).ownAccountOnly
      state.ownAccountOnly = stored
    }
    return { ok: true, status: 200, data: { ...state } }
  })
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
  const view = render(<AccountVisibility mode={mode} />, { wrapper })
  const toggle = () =>
    screen.getByRole(mode === 'fullscreen' ? 'switch' : 'checkbox', {
      name: 'Show only your account',
    }) as HTMLInputElement
  const refresh = async () =>
    act(async () => {
      await client.invalidateQueries({ queryKey: ['api', 'connections.visibility.get'] })
    })
  return { state, client, request, invalidate, view, wrapper, toggle, refresh, stored: () => stored }
}

describe.each(['desktop', 'fullscreen'] as const)('original account membership on %s', (mode) => {
  it('The_toggle_is_disabled_until_an_account_is_confirmed', async () => {
    const f = fixture(mode)
    await screen.findByText(blocked)
    expect(f.toggle().disabled).toBe(true)
    f.state.accountConfirmed = true
    await f.refresh()
    await waitFor(() => expect(f.toggle().disabled).toBe(false))
    expect(screen.queryByText(blocked)).toBeNull()
    expect(screen.queryByText('Account confirmation pending')).toBeNull()
    expect(f.state.hiddenCount).toBe(0)
    expect(f.stored()).toBeUndefined()
    expect(f.request.mock.calls.some(([input]) => input.route.endsWith('.put'))).toBe(false)
  })

  it('The_toggle_defaults_to_every_account_and_persists_the_choice', async () => {
    const f = fixture(mode)
    await screen.findByText(blocked)
    f.state.accountConfirmed = true
    await f.refresh()
    await waitFor(() => expect(f.toggle().disabled).toBe(false))
    expect(mode === 'fullscreen' ? f.toggle().getAttribute('aria-checked') : String(f.toggle().checked)).toBe(
      'false',
    )
    expect(f.stored()).toBeUndefined()
    expect(screen.queryByText(caveat)).toBeNull()
    f.invalidate.mockClear()
    fireEvent.click(f.toggle())
    await screen.findByText(caveat)
    expect(f.stored()).toBe(true)
    expect(
      f.request.mock.calls
        .filter(([input]) => input.route === 'connections.visibility.put')
        .map(([input]) => input.body),
    ).toEqual([{ ownAccountOnly: true }])
    expect(f.invalidate).toHaveBeenCalledExactlyOnceWith({ queryKey: ['api'] })
    f.view.unmount()
    render(<AccountVisibility mode={mode} />, { wrapper: f.wrapper })
    expect(mode === 'fullscreen' ? f.toggle().getAttribute('aria-checked') : String(f.toggle().checked)).toBe(
      'true',
    )
    expect(screen.getByText(caveat)).toBeTruthy()
  })

  it('The_toggle_states_what_it_hides_with_the_figure_in_the_data_face', async () => {
    const f = fixture(mode)
    await screen.findByText(blocked)
    expect(f.toggle().getAttribute('aria-label') ?? f.toggle().closest('label')!.textContent).not.toMatch(
      /\d/,
    )
    expect(document.querySelector('.account-scope-count')).toBeNull()
    for (const [count, figure, unit] of [
      [1, '1', 'game from other accounts'],
      [1234, '1,234', 'games from other accounts'],
    ] as const) {
      f.state.hiddenCount = count
      await f.refresh()
      await screen.findByText(unit)
      expect(document.querySelector('.account-scope-count-value')?.textContent).toBe(figure)
      expect(document.querySelector('.account-scope-count-unit')?.textContent).toBe(unit)
      expect(f.toggle().getAttribute('aria-label') ?? f.toggle().closest('label')!.textContent).not.toMatch(
        /\d/,
      )
    }
    f.state.hiddenCount = 0
    await f.refresh()
    await waitFor(() => expect(document.querySelector('.account-scope-count')).toBeNull())
  })
})
