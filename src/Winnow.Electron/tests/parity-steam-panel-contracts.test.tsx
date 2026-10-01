// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ApiRequest, SteamSignInOptions, SteamSignInResult } from '../src/shared/bridge'
import type { Mode, StoreConnections } from '../src/renderer/api/types'
import { Settings } from '../src/renderer/features/Settings'
import { steamConnectionState, steamHealthMessages } from '../src/renderer/features/steamConnection'

afterEach(cleanup)
function fixture(
  mode: Mode,
  key = false,
  session = false,
  managed = key,
  health = session ? 1 : 0,
  confirmed = session,
) {
  const state: StoreConnections = {
    steam: {
      hasApiKey: key,
      apiKeyIsAppManaged: managed,
      hasSession: session,
      sessionUsable: [1, 5].includes(health),
      hasUsableCredential: key || [1, 5].includes(health),
      sessionAccount: session ? '76561198000000001' : null,
      sessionExpiresAt: session ? '2026-10-01T12:00:00Z' : null,
    },
    steamHealth: health,
  }
  const signIn = vi.fn(async (_options: SteamSignInOptions): Promise<SteamSignInResult> => {
    Object.assign(state.steam, {
      hasSession: true,
      sessionUsable: true,
      hasUsableCredential: true,
      sessionAccount: '76561198000000001',
    })
    state.steamHealth = 1
    confirmed = true
    return { signedIn: true, persisted: true, refreshTokenCaptured: true, accountConfirmed: true }
  })
  const request = vi.fn(async ({ route, body }: ApiRequest) => {
    let data: unknown = null
    if (route === 'connections.get') data = structuredClone(state)
    if (route === 'connections.visibility.get')
      data = { accountConfirmed: confirmed, ownAccountOnly: false, hiddenCount: 0 }
    if (route === 'connections.steam.key') {
      const value = (body as { key: string | null }).key
      Object.assign(state.steam, {
        hasApiKey: !!value,
        apiKeyIsAppManaged: !!value,
        hasUsableCredential: !!value || state.steam.sessionUsable,
      })
      confirmed = false
      data = 0
    }
    if (route === 'connections.steam.signOut') {
      Object.assign(state.steam, {
        hasSession: false,
        sessionUsable: false,
        sessionAccount: null,
        hasUsableCredential: state.steam.hasApiKey,
      })
      state.steamHealth = 0
      confirmed = false
    }
    if (['plugins.get', 'operations.get', 'preferences.presentation.get'].includes(route)) data = []
    if (route === 'library.get') data = { games: [], lists: [] }
    if (route === 'library.workspace')
      data = { works: [], externalIds: [], epicLaunchKeys: {}, pluginActions: {}, identityLinks: [] }
    return { ok: true, status: 200, data }
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, steamSignIn: signIn } })
  render(
    <QueryClientProvider client={client}>
      <Settings mode={mode} />
    </QueryClientProvider>,
  )
  fireEvent.click(screen.getByRole('button', { name: 'Platforms' }))
  return { request, signIn, state }
}
async function ready() {
  return (await screen.findByLabelText('Steam Web API key')) as HTMLInputElement
}
const details = (name: string) => screen.getByText(name).closest('details') as HTMLDetailsElement

// The requests are mocked at the named bridge here; SteamSessionParityTests exercises storage and reconciliation.
describe.each(['desktop', 'fullscreen'] as const)('%s original Steam connection panel contracts', (mode) => {
  it('The_permission_is_unticked_and_a_declined_sign_in_still_succeeds', async () => {
    const { signIn } = fixture(mode)
    await ready()
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
    expect(
      (
        screen.getByRole('checkbox', {
          name: 'Also capture purchase history and licences',
        }) as HTMLInputElement
      ).checked,
    ).toBe(false)
    expect(
      within(screen.getByRole('dialog')).getByText(/Declining is a complete answer/).textContent,
    ).toContain('never opened')
    fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
    await screen.findByText('SIGNED IN')
    expect(signIn.mock.calls[0][0].consentGranted).toBe(true)
    expect(signIn.mock.calls[0][0].capturePurchaseHistory ?? false).toBe(false)
    expect(screen.getByRole('heading', { name: /Signed in Working/ })).toBeTruthy()
    expect(
      (screen.getByRole('checkbox', { name: 'Show only your account' }) as HTMLInputElement).disabled,
    ).toBe(false)
    expect(screen.queryByRole('alert')).toBeNull()
  })
  it('Only_the_permission_control_sets_the_capture_flag', async () => {
    const { signIn } = fixture(mode)
    fireEvent.change(await ready(), { target: { value: 'unsubmitted-key' } })
    for (const title of ['What local files cover', 'About signing in', 'About API keys'])
      fireEvent.click(screen.getByText(title))
    fireEvent.click(screen.getByRole('button', { name: 'Which one should I use?' }))
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Stay signed in on this computer' }))
    expect(
      (
        screen.getByRole('checkbox', {
          name: 'Also capture purchase history and licences',
        }) as HTMLInputElement
      ).checked,
    ).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
    await screen.findByText('SIGNED IN')
    expect(signIn.mock.calls[0][0]).toMatchObject({ consentGranted: true, staySignedIn: false })
    expect(signIn.mock.calls[0][0].capturePurchaseHistory ?? false).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Sign out of Steam' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in to Steam' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Also capture purchase history and licences' }))
    fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
    await waitFor(() => expect(signIn).toHaveBeenCalledTimes(2))
    expect(signIn.mock.calls[1][0].capturePurchaseHistory).toBe(true)
  })
  it('The_save_command_refuses_an_empty_field even through direct form submission', async () => {
    const { request } = fixture(mode)
    const input = await ready()
    for (const value of ['', '   ']) {
      fireEvent.change(input, { target: { value } })
      expect((screen.getByRole('button', { name: 'Save API key' }) as HTMLButtonElement).disabled).toBe(true)
      fireEvent.submit(input.closest('form')!)
    }
    expect(request.mock.calls.some(([call]) => call.route === 'connections.steam.key')).toBe(false)
    fireEvent.change(input, { target: { value: 'ABCDEF' } })
    expect((screen.getByRole('button', { name: 'Save API key' }) as HTMLButtonElement).disabled).toBe(false)
  })
  it('A_key_from_the_environment_cannot_be_cleared_here_and_says_so', async () => {
    const { request } = fixture(mode, true, false, false)
    const input = await ready()
    expect(screen.getByRole('heading', { name: /Set outside Winnow, can't be cleared here/ })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Remove saved API key' })).toBeNull()
    fireEvent.click(screen.getByText('About API keys'))
    const copy = details('About API keys').textContent!
    expect(copy).toContain('Steam__ApiKey')
    expect(copy).toContain('precedence')
    expect(copy).toContain('cannot remove')
    fireEvent.change(input, { target: { value: 'MINE' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save API key' }))
    await screen.findByRole('button', { name: 'Remove saved API key' })
    expect(request).toHaveBeenCalledWith({
      route: 'connections.steam.key',
      params: undefined,
      body: { key: 'MINE' },
    })
  })
  it('Renewal_copy_states_automatic_renewal_and_names_its_limits and Each_method_states_what_it_gives_up', async () => {
    fixture(mode, true, true, true, 2)
    await ready()
    fireEvent.click(screen.getByText('About signing in'))
    const signIn = details('About signing in').textContent!
    for (const fact of ['renews it automatically', 'may not work', 'API key', 'about a day'])
      expect(signIn).toContain(fact)
    expect(steamHealthMessages[2]).not.toMatch(/sign in again/i)
    fireEvent.click(screen.getByText('About API keys'))
    for (const fact of ['account filter', 'purchase history'])
      expect(details('About API keys').textContent).toContain(fact)
    const both = screen.getByText(/Scheduled updates/).textContent!
    for (const fact of ['key', 'Scheduled', 'not expire']) expect(both).toContain(fact)
  })
  it.each([
    [false, false],
    [true, false],
    [false, true],
    [true, true],
  ] as const)(
    'Every_credential_combination_shows_each_methods_state_and_control key=%s session=%s',
    async (key, session) => {
      fixture(mode, key, session)
      expect((await ready()).disabled).toBe(false)
      expect(screen.getByRole('heading', { name: 'Local files On' })).toBeTruthy()
      expect(screen.getByRole('region', { name: 'Steam sign-in method' }).textContent).toBeTruthy()
      expect(screen.getByRole('region', { name: 'Steam API key method' }).textContent).toBeTruthy()
      expect(
        screen.getByRole('region', { name: 'Steam connection' }).querySelector('.connection-state')!
          .textContent,
      ).toBeTruthy()
      expect(!!screen.queryByRole('button', { name: 'Sign in to Steam' })).toBe(!session)
      expect(!!screen.queryByRole('button', { name: 'Sign out of Steam' })).toBe(session)
      expect(!!screen.queryByRole('button', { name: 'Remove saved API key' })).toBe(key)
      for (const title of ['What local files cover', 'About signing in', 'About API keys'])
        expect(details(title).open).toBe(false)
      expect(screen.queryByRole('dialog')).toBeNull()
    },
  )
  it.each([3, 4, 5])('A_session_that_cannot_renew_surfaces_at_the_top_level health=%s', async (health) => {
    fixture(mode, true, true, true, health)
    await ready()
    expect(details('About signing in').open).toBe(false)
    const warning = screen.getByText(steamHealthMessages[health])
    expect(warning.closest('details')).toBeNull()
    expect(warning.classList.contains('connection-warning')).toBe(true)
    expect(warning.getAttribute('role')).toBe('status')
    expect(
      screen.getByRole('button', { name: health === 5 ? 'Sign out of Steam' : 'Sign in again' }),
    ).toBeTruthy()
  })
  it('The_disclosures_still_carry_everything_that_left_the_top_level', async () => {
    fixture(mode, true, true)
    await ready()
    expect(screen.getByText('Adds games never installed on this PC.')).toBeTruthy()
    expect(screen.getByText(/Two ways to connect/)).toBeTruthy()
    expect(screen.getByText(/Scheduled updates use the API key because keys do not expire/)).toBeTruthy()
    const local = details('What local files cover')
    const signIn = details('About signing in')
    const key = details('About API keys')
    for (const element of [local, signIn, key]) expect(element.open).toBe(false)
    for (const element of [local, signIn, key]) {
      fireEvent.click(element.querySelector('summary')!)
      expect(element.open).toBe(true)
    }
    expect(local.textContent).toMatch(/playtime and last-played/)
    for (const fact of [
      'Identifies your account',
      'purchase history',
      'about a day',
      'renews it automatically',
      'Signing out deletes',
      'local Steam games stay',
      'API key keeps working',
      'complete answer',
      'never opened',
    ])
      expect(signIn.textContent).toContain(fact)
    for (const fact of [
      'Never expires',
      'account filter',
      'import confirms your account',
      'cannot read your purchase history',
    ])
      expect(key.textContent).toContain(fact)
    fireEvent.click(screen.getByRole('button', { name: 'Which one should I use?' }))
    const methods = screen.getByRole('dialog', { name: 'Ways to connect Steam' })
    expect(within(methods).getByText(/Sign-in identifies your account/)).toBeTruthy()
    expect(within(methods).getByText(/An API key never expires/)).toBeTruthy()
    fireEvent.click(within(methods).getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    for (const element of [local, signIn, key]) {
      fireEvent.click(element.querySelector('summary')!)
      expect(element.open).toBe(false)
    }
  })
  it('The_terse_state_lines_are_one_per_state', async () => {
    const { state } = fixture(mode)
    await ready()
    expect(
      new Set(
        Array.from({ length: 6 }, (_, steamHealth) => steamConnectionState({ ...state, steamHealth }).terse),
      ).size,
    ).toBe(6)
    expect(
      new Set([
        steamConnectionState(state).keyState,
        steamConnectionState({ ...state, steam: { ...state.steam, hasApiKey: true } }).keyState,
        steamConnectionState({
          ...state,
          steam: { ...state.steam, hasApiKey: true, apiKeyIsAppManaged: true },
        }).keyState,
      ]).size,
    ).toBe(3)
  })
  it('Replacing_key_clears_old_confirmation_and_disables_account_scope_on_both_surfaces', async () => {
    fixture(mode, true, false, true, 0, true)
    const input = await ready()
    const account = screen.getByRole('checkbox', {
      name: 'Show only your account',
    }) as HTMLInputElement
    await waitFor(() => expect(account.disabled).toBe(false))
    fireEvent.change(input, { target: { value: 'replacement-key' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save API key' }))
    await screen.findByText(/This happens automatically during the next Steam import/)
    expect(account.disabled).toBe(true)
    expect(screen.getByText('KEY SET')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Remove saved API key' }))
    await screen.findByText(/Signing in tells it immediately/)
    expect(account.disabled).toBe(true)
    expect(screen.getByText('NO CONNECTION')).toBeTruthy()
  })
})
