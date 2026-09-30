// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ApiRequest, SteamSignInResult } from '../src/shared/bridge'
import type { Mode, StoreConnections } from '../src/renderer/api/types'
import { Settings, SteamAccount, SteamKeyForm } from '../src/renderer/features/Settings'

afterEach(cleanup)
const account = '76561198000000001'
const initial: StoreConnections = {
  steam: {
    hasApiKey: false,
    apiKeyIsAppManaged: false,
    hasSession: false,
    sessionUsable: false,
    hasUsableCredential: false,
    sessionAccount: null,
    sessionExpiresAt: null,
  },
  steamHealth: 0,
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
function fixture(
  mode: Mode,
  setup: {
    signedIn?: SteamSignInResult
    gate?: Promise<SteamSignInResult>
    key?: boolean
    failKey?: boolean
    failSignOut?: boolean
  } = {},
) {
  let state = structuredClone(initial)
  if (setup.key)
    state.steam = { ...state.steam, hasApiKey: true, apiKeyIsAppManaged: true, hasUsableCredential: true }
  let confirmed = false
  const result = setup.signedIn ?? {
    signedIn: true,
    persisted: true,
    refreshTokenCaptured: true,
    accountConfirmed: true,
  }
  const signIn = vi.fn(async () => {
    const value = setup.gate ? await setup.gate : result
    if (value.signedIn) {
      confirmed = !!value.accountConfirmed
      state = {
        ...state,
        steamHealth: value.persisted ? 1 : 5,
        steam: {
          ...state.steam,
          hasSession: true,
          sessionUsable: true,
          hasUsableCredential: true,
          sessionAccount: account,
          sessionExpiresAt: '2026-09-30T01:00:00Z',
        },
      }
    }
    return value
  })
  const request = vi.fn(async (input: ApiRequest) => {
    let data: unknown = null
    if (input.route === 'connections.get') data = structuredClone(state)
    if (input.route === 'connections.visibility.get')
      data = { accountConfirmed: confirmed, ownAccountOnly: false, hiddenCount: 0 }
    if (input.route === 'connections.steam.key') {
      const key = (input.body as { key: string | null }).key
      if (setup.failKey && key) return { ok: true, status: 200, data: 1 }
      state.steam = {
        ...state.steam,
        hasApiKey: !!key,
        apiKeyIsAppManaged: !!key,
        hasUsableCredential: !!key || state.steam.sessionUsable,
      }
      data = 0
    }
    if (input.route === 'connections.steam.signOut') {
      if (setup.failSignOut)
        return { ok: false, status: 503, error: { message: 'The session could not be removed.' } }
      confirmed = false
      state = {
        ...initial,
        steam: {
          ...initial.steam,
          hasApiKey: state.steam.hasApiKey,
          apiKeyIsAppManaged: state.steam.apiKeyIsAppManaged,
          hasUsableCredential: state.steam.hasApiKey,
        },
      }
    }
    if (input.route === 'plugins.get' || input.route === 'operations.get') data = []
    if (input.route === 'preferences.presentation.get') data = []
    if (input.route === 'library.get') data = { games: [], lists: [] }
    if (input.route === 'library.workspace') data = { works: [], externalIds: [], epicLaunchKeys: {}, pluginActions: {}, identityLinks: [] }
    return { ok: true, status: 200, data }
  })
  const openExternal = vi.fn(async () => ({ opened: true }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, steamSignIn: signIn, openExternal, cancelSteamWindow: vi.fn(async () => true) },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
  const view = render(<Settings mode={mode} />, { wrapper })
  fireEvent.click(screen.getByRole('button', { name: 'Platforms' }))
  return { ...view, request, signIn, openExternal, client, wrapper }
}
async function begin(capture = false) {
  fireEvent.click(await screen.findByRole('button', { name: 'Sign in to Steam' }))
  if (capture)
    fireEvent.click(screen.getByRole('checkbox', { name: 'Also capture purchase history and licences' }))
  fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
}
async function completedSignIn() {
  await screen.findByText(account)
  await waitFor(() =>
    expect((screen.getByRole('button', { name: 'Sign out of Steam' }) as HTMLButtonElement).disabled).toBe(
      false,
    ),
  )
}

describe.each(['desktop', 'fullscreen'] as const)('%s Steam connection transitions', (mode) => {
  it('Saving_a_key_hands_it_over_empties_the_field_and_says_it_is_in_use', async () => {
    const { request } = fixture(mode)
    const input = (await screen.findByLabelText('Steam Web API key')) as HTMLInputElement
    fireEvent.change(input, { target: { value: 'sanitized-key-fixture' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save API key' }))
    await screen.findByText('KEY SET')
    expect(screen.getByText('On - API')).toBeTruthy()
    await screen.findByText('API key saved securely.')
    expect(input.value).toBe('')
    expect(request).toHaveBeenCalledWith({
      route: 'connections.steam.key',
      params: undefined,
      body: { key: 'sanitized-key-fixture' },
    })
  })
  it('Clearing_removes_the_key_and_withdraws_the_clear_command', async () => {
    const { request } = fixture(mode, { key: true })
    fireEvent.click(await screen.findByRole('button', { name: 'Remove saved API key' }))
    await screen.findByText('NO CONNECTION')
    expect(screen.getByText('Saved API key removed.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Remove saved API key' })).toBeNull()
    expect(request).toHaveBeenCalledWith({
      route: 'connections.steam.key',
      params: undefined,
      body: { key: null },
    })
  })
  it('a refused credential save leaves the field and connection state unchanged', async () => {
    fixture(mode, { failKey: true })
    const input = (await screen.findByLabelText('Steam Web API key')) as HTMLInputElement
    fireEvent.change(input, { target: { value: 'unsaved-fixture' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save API key' }))
    await screen.findByText('This computer could not protect the key. It was not saved.')
    expect(input.value).toBe('unsaved-fixture')
    expect(screen.getByText('NO CONNECTION')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Remove saved API key' })).toBeNull()
  })
  it('A_successful_sign_in_renders_the_reports_own_account_confirmation', async () => {
    fixture(mode)
    await begin()
    await completedSignIn()
    expect(screen.getByText(/Account confirmed. The account filter is available/)).toBeTruthy()
    expect(screen.getByText('SIGNED IN')).toBeTruthy()
    expect(
      (screen.getByRole('checkbox', { name: 'Only show games from my Steam account' }) as HTMLInputElement)
        .disabled,
    ).toBe(false)
  })
  it('A_sign_in_with_no_refresh_token_is_reported_rather_than_dressed_up', async () => {
    fixture(mode, {
      signedIn: { signedIn: true, persisted: true, refreshTokenCaptured: false, accountConfirmed: true },
    })
    await begin()
    await completedSignIn()
    expect(screen.getByText(/Steam did not provide a renewable session/)).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
  })
  it('a run-only session keeps its persistence warning after the connection refresh hides sign-in', async () => {
    fixture(mode, {
      signedIn: { signedIn: true, persisted: false, refreshTokenCaptured: true, accountConfirmed: true },
    })
    await begin()
    await completedSignIn()
    expect(screen.getByText(/This session was not saved to disk/)).toBeTruthy()
    expect(screen.getByText(/Working, not saved/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Sign in to Steam' })).toBeNull()
  })
  it('Signing_out_clears_the_identity_the_session_earned and withdraws capture permission', async () => {
    const { signIn } = fixture(mode)
    await begin(true)
    await completedSignIn()
    fireEvent.click(screen.getByRole('button', { name: 'Sign out of Steam' }))
    await screen.findByText('NO CONNECTION')
    expect(screen.queryByText(account)).toBeNull()
    expect(screen.queryByText(/Steam connected/)).toBeNull()
    expect(
      (screen.getByRole('checkbox', { name: 'Only show games from my Steam account' }) as HTMLInputElement)
        .disabled,
    ).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
    expect(
      (
        screen.getByRole('checkbox', {
          name: 'Also capture purchase history and licences',
        }) as HTMLInputElement
      ).checked,
    ).toBe(false)
    expect(signIn).toHaveBeenCalledExactlyOnceWith({
      consentGranted: true,
      staySignedIn: true,
      capturePurchaseHistory: true,
    })
  })
  it('signing out retains the independent API key and its scheduler state', async () => {
    fixture(mode, { key: true })
    await begin()
    await completedSignIn()
    expect(screen.getByText('SIGNED IN, KEY SET')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Sign out of Steam' }))
    await screen.findByText('KEY SET')
    expect(screen.getByText('On - API')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Remove saved API key' })).toBeTruthy()
  })
  it('a failed sign-out keeps the account identity and reports the error', async () => {
    fixture(mode, { failSignOut: true })
    await begin()
    await completedSignIn()
    fireEvent.click(screen.getByRole('button', { name: 'Sign out of Steam' }))
    await screen.findByRole('alert')
    expect(screen.getByText(account)).toBeTruthy()
    expect(screen.getByText('SIGNED IN')).toBeTruthy()
  })
  it('an active sign-in holds key changes and the purchase-file route', async () => {
    const gate = deferred<SteamSignInResult>()
    const { request } = fixture(mode, { key: true, gate: gate.promise })
    await begin()
    expect(
      (screen.getByRole('button', { name: 'Remove saved API key', hidden: true }) as HTMLButtonElement)
        .disabled,
    ).toBe(true)
    expect((screen.getByRole('button', { name: 'Import purchase history', hidden: true }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.queryByLabelText('Saved Steam pages')).toBeNull()
    expect(request.mock.calls.some(([input]) => input.route.startsWith('imports.steam'))).toBe(false)
    await act(async () => gate.resolve({ signedIn: false, outcome: 4 }))
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Remove saved API key' }) as HTMLButtonElement).disabled,
      ).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Import purchase history' }))
    expect((screen.getByLabelText('Saved Steam pages') as HTMLInputElement).disabled).toBe(false)
  })
  it('The_key_page_is_opened_through_the_shared_dispatcher and failures retain its address', async () => {
    const { openExternal } = fixture(mode)
    fireEvent.click(await screen.findByRole('button', { name: 'Get a key' }))
    await waitFor(() => expect(openExternal).toHaveBeenCalledWith('https://steamcommunity.com/dev/apikey'))
    openExternal.mockResolvedValueOnce({ opened: false })
    fireEvent.click(screen.getByRole('button', { name: 'Get a key' }))
    await screen.findByRole('alert')
    expect(
      screen.getByText(
        /You can open this address in your browser: https:\/\/steamcommunity.com\/dev\/apikey/,
      ),
    ).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Get a key' }))
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
  })
  it('leaving the settings host cancels a pending Steam sign-in and ignores a late completion', async () => {
    const gate = deferred<SteamSignInResult>()
    const { unmount, request } = fixture(mode, { gate: gate.promise })
    await begin()
    const before = request.mock.calls.length
    unmount()
    expect(window.winnow.cancelSteamWindow).toHaveBeenCalledOnce()
    await act(async () => gate.resolve({ signedIn: false, outcome: 4 }))
    expect(request).toHaveBeenCalledTimes(before)
  })
})
