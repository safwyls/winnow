// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Setup } from '../src/renderer/features/Setup'
import {
  ApplicationPreferences,
  LibraryPresentationPreferences,
} from '../src/renderer/features/SettingsPreferences'
import type { Mode, StoreConnections } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'

afterEach(cleanup)
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
function fixture(mode: Mode, cursor: number | null, node?: React.ReactNode) {
  let step = cursor
  let failProgress = false
  let failStartup = false
  let openAtLogin = false
  let commandGate: Promise<unknown> | null = null
  const preferences = new Map<string, string>()
  const stores: StoreConnections = {
    steam: {
      hasSession: true,
      sessionUsable: true,
      hasApiKey: true,
      apiKeyIsAppManaged: true,
      hasUsableCredential: true,
      sessionAccount: '76561198000000001',
      sessionExpiresAt: '2026-10-01T00:00:00Z',
    },
    steamHealth: 1,
    epic: { isLive: true, displayName: 'Fixture account' },
  }
  const request = vi.fn(async (input: ApiRequest) => {
    let data: unknown = null
    if (input.route === 'setup.get') data = { step }
    if (input.route === 'setup.put') {
      if (failProgress) return { ok: false, status: 500, message: 'Cannot write progress' }
      step = (input.body as { step: number | null }).step
    }
    if (input.route === 'connections.get') data = structuredClone(stores)
    if (input.route === 'connections.igdb.get') data = { clientId: '', hasSavedCredentials: false }
    if (input.route === 'connections.visibility.get')
      data = { accountConfirmed: true, ownAccountOnly: false, hiddenCount: 0 }
    if (input.route === 'connections.steam.signOut') {
      await commandGate
      stores.steam.hasSession = false
      stores.steam.sessionUsable = false
      stores.steam.sessionAccount = null
      stores.steamHealth = 0
    }
    if (input.route === 'connections.epic.signOut') {
      await commandGate
      stores.epic = null
    }
    if (input.route === 'connections.steam.key') {
      await commandGate
      stores.steam.hasApiKey = false
      stores.steam.apiKeyIsAppManaged = false
      data = 0
    }
    if (input.route === 'preferences.presentation.get')
      data = [...preferences].map(([preference, value]) => ({ preference, value }))
    if (input.route === 'preferences.presentation.put')
      preferences.set(String(input.params?.preference), (input.body as { value: string }).value)
    if (input.route === 'preferences.library.get')
      data = { showExplicitContent: false, showNonGameEntries: false, maturityCap: 'AdultsOnly' }
    return { ok: true, status: 200, data }
  })
  const bridge = {
    request,
    steamSignIn: vi.fn(async () => ({ signedIn: false, outcome: 4 })),
    prepareEpicSignIn: vi.fn(async () => ({
      attemptId: 'a'.repeat(32),
      consentNotice: 'Epic fixture consent',
      expiresAt: '2099-01-01T00:00:00Z',
    })),
    cancelEpicSignIn: vi.fn(async () => true),
    epicSignIn: vi.fn(),
    openEpicSignInInBrowser: vi.fn(),
    completeEpicSignIn: vi.fn(),
    applicationInfo: vi.fn(async () => ({
      platform: 'win32',
      version: 'test',
      packaged: true,
      autostartSupported: true,
      openAtLogin,
    })),
    setOpenAtLogin: vi.fn(async (value: boolean) => {
      if (failStartup) throw Error('Startup registration could not be changed.')
      openAtLogin = value
    }),
  }
  Object.defineProperty(window, 'winnow', { configurable: true, value: bridge })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const view = render(
    <QueryClientProvider client={client}>
      <div className={`mode-${mode}`}>{node ?? <Setup mode={mode} />}</div>
    </QueryClientProvider>,
  )
  return {
    ...view,
    client,
    request,
    bridge,
    stores,
    preferences,
    cursor: () => step,
    failProgress: (value: boolean) => {
      failProgress = value
    },
    failStartup: () => {
      failStartup = true
    },
    gate: (value: Promise<unknown>) => {
      commandGate = value
    },
    startup: (value: boolean) => {
      openAtLogin = value
    },
  }
}

describe.each(['desktop', 'fullscreen'] as const)('%s setup account and recovery contracts', (mode) => {
  it.each(['Steam', 'Epic'] as const)(
    'setup signs out of %s through the real named route and holds navigation until it finishes',
    async (platform) => {
      const h = fixture(mode, platform === 'Steam' ? 2 : 3)
      const gate = deferred<void>()
      h.gate(gate.promise)
      fireEvent.click(await screen.findByRole('button', { name: `Sign out of ${platform}` }))
      await waitFor(() =>
        expect((screen.getByRole('button', { name: 'Continue' }) as HTMLButtonElement).disabled).toBe(true),
      )
      expect(h.request).toHaveBeenCalledWith({
        route: `connections.${platform.toLowerCase()}.signOut`,
        params: undefined,
        body: undefined,
      })
      expect(h.cursor()).toBe(platform === 'Steam' ? 2 : 3)
      await act(async () => gate.resolve())
      await waitFor(() =>
        expect(screen.queryByRole('button', { name: `Sign out of ${platform}` })).toBeNull(),
      )
      expect((screen.getByRole('button', { name: 'Continue' }) as HTMLButtonElement).disabled).toBe(false)
    },
  )
  it('setup shares Steam key removal and omits purchase import without a composed importer', async () => {
    const h = fixture(mode, 2)
    fireEvent.click(await screen.findByRole('button', { name: 'Remove saved API key' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Remove saved API key' })).toBeNull())
    expect(h.stores.steam.hasSession).toBe(true)
    expect(screen.queryByText('Import purchase history')).toBeNull()
    expect(screen.getByRole('button', { name: 'Sign out of Steam' })).toBeTruthy()
  })
  it('an expired Epic account in setup retains its identity and offers reconnect without signing in automatically', async () => {
    const h = fixture(mode, 3)
    h.stores.epic!.isLive = false
    await h.client.invalidateQueries({ queryKey: ['api', 'connections.get'] })
    await screen.findByText(/Epic sign-in expired for Fixture account/)
    expect(h.bridge.prepareEpicSignIn).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Epic again' }))
    await screen.findByText('Epic fixture consent')
    expect(h.cursor()).toBe(3)
    expect(h.bridge.epicSignIn).not.toHaveBeenCalled()
  })
  it('Reopen_write_failure_still_displays_recovery_and_retains_previous_completion', async () => {
    const h = fixture(
      mode,
      null,
      <>
        <ApplicationPreferences />
        <Setup mode={mode} />
      </>,
    )
    h.failProgress(true)
    fireEvent.click(await screen.findByRole('button', { name: 'Run setup again' }))
    await screen.findByRole('heading', { name: 'Welcome to Winnow' })
    expect(screen.getByText(/Could not save setup progress/)).toBeTruthy()
    expect(h.cursor()).toBeNull()
    await act(async () => {
      await h.client.invalidateQueries({ queryKey: ['api'] })
    })
    expect(screen.getByRole('heading', { name: 'Welcome to Winnow' })).toBeTruthy()
    h.failProgress(false)
    fireEvent.click(screen.getByRole('button', { name: 'Get started' }))
    await screen.findByRole('heading', { name: 'Fill in the details' })
    expect(h.cursor()).toBe(1)
    expect(h.client.getQueryData(['setup-recovery'])).toBeNull()
  })
  it('restores the replay control even when its pending write removed focus before setup opened', async () => {
    fixture(
      mode,
      null,
      <>
        <ApplicationPreferences />
        <Setup mode={mode} />
      </>,
    )
    const replay = await screen.findByRole('button', { name: 'Run setup again' })
    replay.focus()
    fireEvent.click(replay)
    replay.blur()
    await screen.findByRole('heading', { name: 'Welcome to Winnow' })
    expect(within(screen.getByRole('dialog')).getAllByRole('button')).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: 'Skip setup' }))
    await waitFor(() => expect(document.activeElement).toBe(replay))
  })
  it('Every_optional_step_can_be_skipped_without_credentials_and_completion_persists', async () => {
    const h = fixture(mode, 0)
    fireEvent.click(await screen.findByRole('button', { name: 'Get started' }))
    for (let step = 1; step <= 7; step++) {
      await screen.findByText(`SETUP · ${step + 1} OF 9`)
      if (step === 1)
        fireEvent.change(await screen.findByLabelText('Client secret'), {
          target: { value: 'unsaved-secret' },
        })
      if (step === 2)
        fireEvent.change(await screen.findByLabelText('Steam Web API key'), {
          target: { value: 'unsaved-key' },
        })
      fireEvent.click(screen.getByRole('button', { name: 'Skip this step' }))
    }
    fireEvent.click(await screen.findByRole('button', { name: 'Open my library' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(h.cursor()).toBeNull()
    expect(
      h.request.mock.calls.some(([value]) =>
        ['connections.igdb.put', 'connections.steam.key'].includes(value.route),
      ),
    ).toBe(false)
  })
  it('Existing_database_on_second_launch_resumes_cursor_and_does_not_reset_it', async () => {
    const h = fixture(mode, 4)
    await screen.findByRole('heading', { name: 'Your GOG library' })
    expect(h.request.mock.calls.some(([value]) => value.route === 'setup.put')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Back' }))
    await screen.findByRole('heading', { name: 'Your Epic library' })
    expect(h.cursor()).toBe(3)
  })
  it('Skip_all_preserves_saved_preferences_and_application_entry_reopens', async () => {
    const h = fixture(
      mode,
      6,
      <>
        <ApplicationPreferences />
        <Setup mode={mode} />
      </>,
    )
    const close = (await screen.findByRole('checkbox', {
      name: /Close to notification area/,
    })) as HTMLInputElement
    await waitFor(() => expect(close.disabled).toBe(false))
    fireEvent.click(close)
    await waitFor(() => expect(close.checked).toBe(true))
    fireEvent.click(screen.getByRole('button', { name: 'Skip setup' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(h.preferences.get('CloseToTray')).toBe('true')
    fireEvent.click(screen.getByRole('button', { name: 'Run setup again' }))
    await screen.findByRole('heading', { name: 'Welcome to Winnow' })
    expect(h.cursor()).toBe(0)
    expect(h.preferences.get('CloseToTray')).toBe('true')
  })
})

describe.each(['desktop', 'fullscreen'] as const)('%s application preference contracts', (mode) => {
  it('Tray_preferences_are_off_when_absent_and_round_trip', async () => {
    const h = fixture(mode, null, <ApplicationPreferences />)
    for (const label of ['Minimize to notification area', 'Close to notification area']) {
      const input = (await screen.findByRole('checkbox', { name: new RegExp(label) })) as HTMLInputElement
      await waitFor(() => expect(input.disabled).toBe(false))
      expect(input.checked).toBe(false)
      fireEvent.click(input)
      await waitFor(() => expect(input.checked).toBe(true))
    }
    expect(h.preferences.get('MinimizeToTray')).toBe('true')
    expect(h.preferences.get('CloseToTray')).toBe('true')
    await act(async () => {
      await h.client.invalidateQueries({ queryKey: ['api'] })
    })
    expect(
      (screen.getByRole('checkbox', { name: /Close to notification area/ }) as HTMLInputElement).checked,
    ).toBe(true)
  })
  it('Fullscreen_startup_defaults_off_and_round_trips_both_values', async () => {
    const h = fixture(mode, null, <ApplicationPreferences />)
    const input = (await screen.findByRole('checkbox', { name: /Start in fullscreen/ })) as HTMLInputElement
    await waitFor(() => expect(input.disabled).toBe(false))
    expect(input.checked).toBe(false)
    for (const value of [true, false]) {
      fireEvent.click(input)
      await waitFor(() => expect(input.checked).toBe(value))
      expect(h.preferences.get('StartInFullscreen')).toBe(String(value))
    }
  })
  it('Startup_toggle_updates_the_operating_system_registration and load reflects it', async () => {
    const h = fixture(mode, null, <ApplicationPreferences />)
    await screen.findByRole('checkbox', { name: /Start with Windows/ })
    h.startup(true)
    await act(async () => {
      await h.client.invalidateQueries({ queryKey: ['native'] })
    })
    const input = (await screen.findByRole('checkbox', { name: /Start with Windows/ })) as HTMLInputElement
    await waitFor(() => expect(input.checked).toBe(true))
    for (const value of [false, true]) {
      fireEvent.click(input)
      await waitFor(() => expect(input.checked).toBe(value))
      expect(h.bridge.setOpenAtLogin).toHaveBeenLastCalledWith(value)
    }
  })
  it('Failed_startup_change_reverts_the_toggle_and_reports_the_problem', async () => {
    const h = fixture(mode, null, <ApplicationPreferences />)
    h.failStartup()
    const input = (await screen.findByRole('checkbox', { name: /Start with Windows/ })) as HTMLInputElement
    fireEvent.click(input)
    await screen.findByRole('alert')
    expect(input.checked).toBe(false)
    expect(input.disabled).toBe(false)
  })
  it.each(['ListOrder', 'unknown', '999'])('Invalid_default_sort_uses_dormancy: %s', async (stored) => {
    const h = fixture(mode, null, <LibraryPresentationPreferences />)
    h.preferences.set('DefaultSort', stored)
    await act(async () => {
      await h.client.invalidateQueries({ queryKey: ['api'] })
    })
    expect(((await screen.findByLabelText('Default library sort')) as HTMLSelectElement).value).toBe(
      'DormantLongest',
    )
    expect(h.preferences.get('DefaultSort')).toBe(stored)
  })
})
