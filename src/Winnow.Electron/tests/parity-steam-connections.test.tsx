// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ApiRequest } from '../src/shared/bridge'
import type { StoreConnections } from '../src/renderer/api/types'
import { SteamConnectionPanel } from '../src/renderer/features/SteamConnectionPanel'
import { Settings, SteamAccount, SteamKeyForm } from '../src/renderer/features/Settings'
import { steamConnectionState, steamHealthMessages } from '../src/renderer/features/steamConnection'
import { AccountVisibility } from '../src/renderer/features/SettingsPreferences'

afterEach(cleanup)
const snapshot = (key = false, session = false, health = 0, managed = key): StoreConnections => ({
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
})
function fixture(
  native = {},
  respond: (input: ApiRequest) => unknown = () => ({
    accountConfirmed: false,
    ownAccountOnly: false,
    hiddenCount: 0,
  }),
) {
  const request = vi.fn(async (input: ApiRequest) => ({ ok: true, status: 200, data: respond(input) }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, steamSignIn: vi.fn(), ...native },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return {
    request,
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}
describe.each(['desktop', 'fullscreen'])('%s Steam connection presentation', (mode) => {
  function panel(value: StoreConnections) {
    const state = steamConnectionState(value),
      signOut = vi.fn(),
      clear = vi.fn(),
      harness = fixture()
    render(
      <div className={`mode-${mode}`}>
        <SteamConnectionPanel
          snapshot={value}
          busy={false}
          onSignOut={signOut}
          onClearKey={clear}
          signIn={<SteamAccount label={state.signInLabel} showAction={state.showSignIn} />}
          keyEditor={<SteamKeyForm />}
          purchase={<button>Saved-file import</button>}
        />
      </div>,
      { wrapper: harness.wrapper },
    )
    return { ...harness, signOut, clear }
  }
  it.each([
    [0, 'NO CONNECTION', 'Not signed in', false, false],
    [1, 'SIGNED IN', 'Working', false, true],
    [2, 'SIGN-IN NEEDS RENEWING', 'Renewing automatically', false, false],
    [3, 'SIGN-IN NEEDS RENEWING', 'Renewal failing', true, false],
    [4, 'SIGN-IN EXPIRED', 'Expired', true, false],
    [5, 'SIGNED IN', 'Working, not saved', false, true],
  ] as const)(
    'keeps health %s distinct in combined status and method state',
    (health, label, terse, attention, live) => {
      panel(snapshot(false, health !== 0, health))
      expect(screen.getByText(label).getAttribute('data-tone')).toBe(
        attention ? 'attention' : live ? 'live' : 'quiet',
      )
      expect(
        within(screen.getByRole('region', { name: 'Steam sign-in method' })).getByRole('heading').textContent,
      ).toContain(terse)
      const healthLine = screen.getByText(steamHealthMessages[health])
      expect(healthLine.closest('details') !== null).toBe(![3, 4, 5].includes(health))
      if (health !== 0) expect(screen.getByText('76561198000000001')).toBeTruthy()
      expect(
        screen.queryByRole('button', { name: health === 0 ? 'Sign in to Steam' : 'Sign in again' }),
      ).toEqual(live ? null : expect.any(HTMLButtonElement))
    },
  )
  it.each([
    [2, false, false],
    [3, true, false],
    [4, true, false],
    [1, false, true],
    [5, false, true],
  ] as const)(
    'only failing or expired health=%s takes top-level attention even with an API key',
    (health, attention, live) => {
      const state = steamConnectionState(snapshot(true, true, health))
      expect(state.attention).toBe(attention)
      expect(state.live).toBe(live)
    },
  )
  it.each([
    [false, false],
    [true, false],
    [false, true],
    [true, true],
  ] as const)(
    'keeps both methods present with key=%s session=%s and marks the credential carrying API calls',
    (key, session) => {
      const { signOut, clear } = panel(snapshot(key, session, session ? 1 : 0))
      expect(
        screen.getByText(
          session ? (key ? 'SIGNED IN, KEY SET' : 'SIGNED IN') : key ? 'KEY SET' : 'NO CONNECTION',
        ),
      ).toBeTruthy()
      expect(screen.getByRole('region', { name: 'Steam sign-in method' })).toBeTruthy()
      expect(screen.getByRole('region', { name: 'Steam API key method' })).toBeTruthy()
      expect(
        within(screen.getByRole('region', { name: 'Steam sign-in method' }))
          .getByRole('heading')
          .textContent?.includes('In use'),
      ).toBe(session && !key)
      expect(
        within(screen.getByRole('region', { name: 'Steam API key method' }))
          .getByRole('heading')
          .textContent?.includes('In use'),
      ).toBe(key)
      expect(screen.getByRole('heading', { name: 'Local files On' })).toBeTruthy()
      expect(screen.getByText(key ? 'On - API' : session ? 'On - Login' : 'Off')).toBeTruthy()
      if (key) {
        fireEvent.click(screen.getByRole('button', { name: 'Remove saved API key' }))
        expect(clear).toHaveBeenCalledOnce()
      } else expect(screen.queryByRole('button', { name: 'Remove saved API key' })).toBeNull()
      if (session) {
        fireEvent.click(screen.getByRole('button', { name: 'Sign out of Steam' }))
        expect(signOut).toHaveBeenCalledOnce()
        expect(screen.getByText('76561198000000001')).toBeTruthy()
      } else expect(screen.queryByRole('button', { name: 'Sign out of Steam' })).toBeNull()
      expect(!!screen.queryByText('Scheduled updates use the API key because keys do not expire.')).toBe(
        key && session,
      )
    },
  )
  it('keeps details folded independently and exposes environment-key restrictions before opening details', () => {
    panel(snapshot(true, true, 5, false))
    expect(screen.getByRole('heading', { name: /Set outside Winnow, can't be cleared here/ })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Remove saved API key' })).toBeNull()
    const summaries = ['What local files cover', 'About signing in', 'About API keys']
    for (const summary of summaries)
      expect((screen.getByText(summary).closest('details') as HTMLDetailsElement).open).toBe(false)
    fireEvent.click(screen.getByText('About signing in'))
    expect((screen.getByText('About signing in').closest('details') as HTMLDetailsElement).open).toBe(true)
    expect((screen.getByText('About API keys').closest('details') as HTMLDetailsElement).open).toBe(false)
    expect(screen.getByText(steamHealthMessages[5]).closest('details')).toBeNull()
    fireEvent.click(screen.getByText('Import purchase history'))
    expect(screen.getByRole('button', { name: 'Saved-file import' })).toBeTruthy()
  })
  it('opening the panel starts no sign-in and all six health messages and terse lines stay distinct', () => {
    panel(snapshot())
    expect(window.winnow.steamSignIn).not.toHaveBeenCalled()
    expect(new Set(steamHealthMessages).size).toBe(6)
    expect(
      new Set(
        Array.from(
          { length: 6 },
          (_, health) => steamConnectionState(snapshot(false, health !== 0, health)).terse,
        ),
      ).size,
    ).toBe(6)
  })
  it.each([
    [false, false, 'Signing in tells it immediately'],
    [true, false, 'next Steam import'],
    [false, true, 'Signing in again should resolve this'],
  ])('explains unconfirmed identity with key=%s session=%s', async (key, session, message) => {
    const harness = fixture()
    render(<AccountVisibility credentials={snapshot(Boolean(key), Boolean(session)).steam} />, {
      wrapper: harness.wrapper,
    })
    await waitFor(() => expect(screen.getByText(new RegExp(String(message)))).toBeTruthy())
    expect((screen.getByRole('checkbox') as HTMLInputElement).disabled).toBe(true)
  })
  it('does not display a blocked identity message after confirmation', async () => {
    const harness = fixture({}, () => ({ accountConfirmed: true, ownAccountOnly: false, hiddenCount: 4 }))
    render(<AccountVisibility credentials={snapshot(false, true, 1).steam} />, { wrapper: harness.wrapper })
    await screen.findByText('games from other accounts')
    expect(document.querySelector('.account-scope-count-value')?.textContent).toBe('4')
    expect(screen.queryByText(/Signing in again should resolve/)).toBeNull()
    expect((screen.getByRole('checkbox') as HTMLInputElement).disabled).toBe(false)
  })
  it('retains a refused key input and clears it only after a successful protected save', async () => {
    let result = 1
    const harness = fixture({}, () => result)
    render(<SteamKeyForm />, { wrapper: harness.wrapper })
    fireEvent.change(screen.getByLabelText('Steam Web API key'), { target: { value: 'fixture-key' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save API key' }))
    await screen.findByText('This computer could not protect the key. It was not saved.')
    expect((screen.getByLabelText('Steam Web API key') as HTMLInputElement).value).toBe('fixture-key')
    result = 0
    fireEvent.click(screen.getByRole('button', { name: 'Save API key' }))
    await screen.findByText('API key saved securely.')
    expect((screen.getByLabelText('Steam Web API key') as HTMLInputElement).value).toBe('')
  })
  it.each([true, false])(
    'uses the completed sign-in report account confirmation=%s without inferring identity',
    async (confirmed) => {
      let accountConfirmed = false
      const signIn = vi.fn(async () => {
          accountConfirmed = confirmed
          return {
            signedIn: true,
            persisted: true,
            refreshTokenCaptured: true,
            accountConfirmed: confirmed,
            detail: 'Fixture result.',
          }
        }),
        harness = fixture({ steamSignIn: signIn }, () => ({
          accountConfirmed,
          ownAccountOnly: false,
          hiddenCount: 0,
        }))
      render(
        <>
          <SteamAccount />
          <AccountVisibility credentials={snapshot(false, true, 1).steam} />
        </>,
        { wrapper: harness.wrapper },
      )
      expect(
        (screen.getByRole('checkbox', { name: 'Show only your account' }) as HTMLInputElement).disabled,
      ).toBe(true)
      fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
      fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
      await screen.findByText(
        new RegExp(
          confirmed
            ? 'Account confirmed. The account filter is available.'
            : 'The account filter is still unavailable.',
        ),
      )
      expect(screen.getByText(/Fixture result/)).toBeTruthy()
      await waitFor(() =>
        expect(
          (
            screen.getByRole('checkbox', {
              name: 'Show only your account',
            }) as HTMLInputElement
          ).disabled,
        ).toBe(!confirmed),
      )
      if (confirmed)
        expect(screen.queryByText(/The sign-in did not record which account is yours/)).toBeNull()
    },
  )
  it.each([true, false])(
    'an Epic session live=%s keeps its account identity and only offers reconnect when lapsed',
    async (live) => {
      const value = { ...snapshot(), epic: { isLive: live, displayName: 'Fixture account' } }
      const harness = fixture({}, (input) =>
        input.route === 'connections.get'
          ? value
          : input.route === 'connections.visibility.get'
            ? { accountConfirmed: false, ownAccountOnly: false, hiddenCount: 0 }
            : input.route === 'plugins.get' || input.route === 'operations.get'
              ? []
              : null,
      )
      render(<Settings mode={mode as 'desktop' | 'fullscreen'} />, { wrapper: harness.wrapper })
      fireEvent.click(screen.getByRole('button', { name: 'Platforms' }))
      fireEvent.click(await screen.findByRole('button', { name: /^Epic/i }))
      await screen.findByText(
        live
          ? 'Connected as Fixture account.'
          : 'Epic sign-in expired for Fixture account. Sign in again to reconnect.',
      )
      expect(screen.getByRole('button', { name: 'Sign out of Epic' })).toBeTruthy()
      expect(!!screen.queryByRole('button', { name: 'Sign in to Epic again' })).toBe(!live)
    },
  )
  it.each([1, 3, 5, 6])(
    'reports refused native outcome %s as an error and a closed window as a neutral fact',
    async (outcome) => {
      const signIn = vi
          .fn()
          .mockResolvedValueOnce({ signedIn: false, outcome, detail: 'No Steam credential arrived.' })
          .mockResolvedValueOnce({
            signedIn: false,
            outcome: 4,
            detail: 'Sign-in cancelled. Nothing was changed.',
          }),
        harness = fixture({ steamSignIn: signIn })
      render(<SteamAccount />, { wrapper: harness.wrapper })
      fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
      fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
      await screen.findByRole('alert')
      fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
      await screen.findByText('Sign-in cancelled. Nothing was changed.')
      expect(screen.queryByRole('alert')).toBeNull()
    },
  )
  it.each([2, 4])('presents closed-window outcome %s as a neutral fact', async (outcome) => {
    const harness = fixture({
      steamSignIn: vi.fn(async () => ({
        signedIn: false,
        outcome,
        detail: 'The window closed. Nothing was stored.',
      })),
    })
    render(<SteamAccount />, { wrapper: harness.wrapper })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
    fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
    await screen.findByText('The window closed. Nothing was stored.')
    expect(screen.queryByRole('alert')).toBeNull()
  })
  it('keeps the independent key method available when the native sign-in host is missing', () => {
    const harness = fixture({ steamSignIn: undefined })
    render(
      <>
        <SteamAccount />
        <SteamKeyForm />
      </>,
      { wrapper: harness.wrapper },
    )
    expect(screen.getByText(/The sign-in window cannot open in this frontend/)).toBeTruthy()
    expect(screen.getByLabelText('Steam Web API key')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Get a key' })).toBeTruthy()
  })
  it('allows cancelling a running native sign-in without presenting cancellation as a failure', async () => {
    let finish!: (value: unknown) => void
    const signIn = vi.fn(
        () =>
          new Promise((resolve) => {
            finish = resolve
          }),
      ),
      cancel = vi.fn(async () => {
        finish({ signedIn: false, outcome: 4, detail: 'Sign-in cancelled. Nothing was changed.' })
        return true
      }),
      harness = fixture({ steamSignIn: signIn, cancelSteamWindow: cancel })
    render(<SteamAccount />, { wrapper: harness.wrapper })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
    fireEvent.click(screen.getByRole('button', { name: 'Continue to Steam' }))
    expect((screen.getByRole('button', { name: 'Continue to Steam' }) as HTMLButtonElement).disabled).toBe(
      true,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Cancel sign-in' }))
    await screen.findByText('Sign-in cancelled. Nothing was changed.')
    expect(cancel).toHaveBeenCalledOnce()
  })
})
