// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Settings } from '../src/renderer/features/Settings'
import { SteamPageImport } from '../src/renderer/features/SteamAccountImport'
import type { Mode, StoreConnections } from '../src/renderer/api/types'
import type { ApiRequest, SavedSteamPage } from '../src/shared/bridge'
import { clearViewState } from '../src/renderer/viewState'
import { isControllerNavigationEvent, moveControllerFocus } from '../src/renderer/controller'

const disconnected: StoreConnections = {
  steam: {
    hasApiKey: false,
    apiKeyIsAppManaged: false,
    hasSession: false,
    sessionUsable: false,
    hasUsableCredential: false,
  },
  steamHealth: 0,
  epic: null,
}
afterEach(() => {
  cleanup()
  for (const mode of ['desktop', 'fullscreen']) clearViewState(`${mode}:settings:tab`)
})
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { resolve, promise }
}
function fixture(mode: Mode = 'fullscreen', initial = disconnected) {
  const state = { snapshot: initial }
  const bridge = {
    request: vi.fn<(input: ApiRequest) => Promise<{ ok: boolean; status: number; data: unknown }>>(
      async (input) => {
        if (input.route === 'connections.epic.signOut') state.snapshot = { ...state.snapshot, epic: null }
        return {
          ok: true,
          status: 200,
          data:
            input.route === 'imports.steam.load'
              ? { pages: {}, anythingLoaded: false, files: [] }
              : input.route === 'connections.get'
                ? state.snapshot
                : input.route === 'connections.visibility.get'
                  ? { accountConfirmed: false, ownAccountOnly: false, hiddenCount: 0 }
                  : input.route === 'library.get'
                    ? { games: [], lists: [] }
                    : input.route === 'library.workspace'
                      ? { works: [], externalIds: [], epicLaunchKeys: {}, pluginActions: {} }
                      : input.route === 'preferences.library.get'
                        ? { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'Off' }
                        : [],
        }
      },
    ),
    steamSignIn: vi.fn(async () => ({ signedIn: false, outcome: 2 })),
    prepareEpicSignIn: vi.fn(async () => ({
      attemptId: 'a'.repeat(32),
      expiresAt: '2099-01-01T00:00:00Z',
      consentNotice: 'Consent fixture',
    })),
    epicSignIn: vi.fn(async () => ({ succeeded: false, failure: 6, persisted: false })),
    cancelEpicSignIn: vi.fn(async () => true),
    openEpicSignInInBrowser: vi.fn(),
    completeEpicSignIn: vi.fn(),
    chooseSavedSteamPage: vi.fn<() => Promise<SavedSteamPage | null>>(async () => ({
      id: 'history',
      name: 'history.html',
    })),
    readSavedSteamPages: vi.fn(async () => [{ name: 'history.html', content: btoa('fixture') }]),
    clearSavedSteamPages: vi.fn(async () => {}),
    cancelRequest: vi.fn(async () => true),
  }
  Object.defineProperty(window, 'winnow', { configurable: true, value: bridge })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
  const view = render(<Settings mode={mode} />, { wrapper })
  async function platforms() {
    fireEvent.click(screen.getByRole('button', { name: 'Platforms' }))
    await screen.findByRole('button', { name: mode === 'fullscreen' ? 'Epic' : /^EPIC/ })
  }
  async function provider(name: 'Steam' | 'Epic' | 'GOG') {
    await platforms()
    fireEvent.click(
      screen.getByRole('button', { name: mode === 'fullscreen' ? name : new RegExp(`^${name}`, 'i') }),
    )
  }
  return { ...view, bridge, client, state, platforms, provider, wrapper }
}

it('Settings_platform_summary_refreshes_on_open_and_tracks_shared_account_state', async () => {
  const h = fixture('fullscreen', { ...disconnected, epic: { isLive: true, displayName: 'Test player' } })
  await waitFor(() =>
    expect(h.bridge.request).toHaveBeenCalledWith(expect.objectContaining({ route: 'connections.get' })),
  )
  const before = h.bridge.request.mock.calls.filter(([input]) => input.route === 'connections.get').length
  await h.platforms()
  await waitFor(() =>
    expect(
      h.bridge.request.mock.calls.filter(([input]) => input.route === 'connections.get').length,
    ).toBeGreaterThan(before),
  )
  const epic = screen.getByRole('button', { name: 'Epic' })
  expect(epic.textContent).toContain('SIGNED IN')
  expect(epic.textContent).toContain('Open')
  expect(epic.querySelectorAll('.fullscreen-setting-cue > span')).toHaveLength(1)
  act(() => epic.focus())
  h.state.snapshot = { ...disconnected, steamHealth: 4, steam: { ...disconnected.steam, hasSession: true } }
  await act(() => h.client.invalidateQueries({ queryKey: ['api', 'connections.get'] }))
  await waitFor(() => expect(epic.textContent).toContain('NOT SIGNED IN'))
  expect(document.activeElement).toBe(epic)
  expect(screen.getByRole('button', { name: 'Steam' }).textContent).toContain('SIGN-IN EXPIRED')
})

it('Api_key_draft_is_masked_local_and_erased_on_close', async () => {
  const h = fixture()
  await h.provider('Steam')
  fireEvent.click(screen.getByRole('button', { name: 'Steam Web API key' }))
  const input = screen.getByLabelText<HTMLInputElement>('Steam Web API key', { selector: 'input' })
  expect(input.type).toBe('password')
  fireEvent.change(input, { target: { value: 'unsaved-local-draft' } })
  act(() => input.focus())
  expect(within(screen.getByRole('group', { name: 'Platform controls' })).getByText('Keyboard')).toBeTruthy()
  expect(h.bridge.request).not.toHaveBeenCalledWith(
    expect.objectContaining({ route: 'connections.steam.key' }),
  )
  fireEvent.keyDown(input, { key: 'Escape' })
  await waitFor(() =>
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Steam Web API key' })),
  )
  expect(input.value).toBe('')
  fireEvent.click(screen.getByRole('button', { name: 'Steam Web API key' }))
  expect(screen.getByLabelText<HTMLInputElement>('Steam Web API key', { selector: 'input' }).value).toBe('')
})

it.each(['Save API key', 'Remove saved API key'])(
  'a held %s excludes the other credential write',
  async (action) => {
    const h = fixture('fullscreen', {
      ...disconnected,
      steam: { ...disconnected.steam, hasApiKey: true, apiKeyIsAppManaged: true, hasUsableCredential: true },
    })
    const write = deferred<{ ok: boolean; status: number; data: unknown }>()
    const original = h.bridge.request.getMockImplementation()!
    h.bridge.request.mockImplementation((input) =>
      input.route === 'connections.steam.key' ? write.promise : original(input),
    )
    await h.provider('Steam')
    fireEvent.click(screen.getByRole('button', { name: 'Steam Web API key' }))
    fireEvent.change(screen.getByLabelText('Steam Web API key', { selector: 'input' }), {
      target: { value: 'replacement-test-key' },
    })
    fireEvent.click(screen.getByRole('button', { name: action }))
    const other = screen.getByRole<HTMLButtonElement>('button', {
      name: action === 'Save API key' ? 'Remove saved API key' : 'Save API key',
    })
    await waitFor(() => expect(other.disabled).toBe(true))
    fireEvent.click(other)
    expect(
      h.bridge.request.mock.calls.filter(([input]) => input.route === 'connections.steam.key'),
    ).toHaveLength(1)
    await act(async () => write.resolve({ ok: true, status: 200, data: 0 }))
  },
)

it('Saved_page_selection_requires_read_and_cancel_returns_no_paths', async () => {
  const h = fixture()
  await h.provider('Steam')
  fireEvent.click(screen.getByRole('button', { name: 'Purchase history' }))
  fireEvent.click(screen.getByRole('button', { name: 'Read saved pages' }))
  fireEvent.click(screen.getByRole('button', { name: 'Choose a page' }))
  await screen.findByText('history.html')
  expect(h.bridge.readSavedSteamPages).not.toHaveBeenCalled()
  expect(h.bridge.request).not.toHaveBeenCalledWith(expect.objectContaining({ route: 'imports.steam.load' }))
  fireEvent.click(screen.getByRole('button', { name: 'Back to Purchase history' }))
  expect(h.bridge.clearSavedSteamPages).toHaveBeenCalled()
  await waitFor(() =>
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Read saved pages' })),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Read saved pages' }))
  expect(screen.queryByText('history.html')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Choose a page' }))
  await screen.findByText('history.html')
  await waitFor(() =>
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Read selected pages' }).disabled).toBe(
      false,
    ),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Read selected pages' }))
  await waitFor(() =>
    expect(h.bridge.request).toHaveBeenCalledWith(
      expect.objectContaining({
        route: 'imports.steam.load',
        body: { files: [{ name: 'history.html', content: btoa('fixture') }] },
      }),
    ),
  )
  expect(h.bridge.readSavedSteamPages).toHaveBeenCalledWith(['history'])
})

it('closing a saved-page tool cancels a pending read before any load or import', async () => {
  const h = fixture()
  const read = deferred<{ name: string; content: string }[]>()
  h.bridge.readSavedSteamPages.mockReturnValue(read.promise)
  h.rerender(<SteamPageImport mode="fullscreen" />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose a page' }))
  await screen.findByText('history.html')
  await waitFor(() =>
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Read selected pages' }).disabled).toBe(
      false,
    ),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Read selected pages' }))
  h.unmount()
  await act(async () => read.resolve([{ name: 'history.html', content: btoa('fixture') }]))
  expect(h.bridge.clearSavedSteamPages).toHaveBeenCalled()
  expect(h.bridge.request).not.toHaveBeenCalledWith(expect.objectContaining({ route: 'imports.steam.load' }))
})

it('saved-page selection accumulates two files, deduplicates and preserves both when an additional picker is cancelled', async () => {
  const h = fixture()
  h.rerender(<SteamPageImport mode="fullscreen" />)
  h.bridge.chooseSavedSteamPage
    .mockResolvedValueOnce({ id: 'history', name: 'history.html' })
    .mockResolvedValueOnce({ id: 'licences', name: 'licences.htm' })
    .mockResolvedValueOnce({ id: 'history', name: 'history.html' })
    .mockResolvedValueOnce(null)
  for (let i = 0; i < 4; i++) {
    fireEvent.click(screen.getByRole('button', { name: 'Choose a page' }))
    await waitFor(() =>
      expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Choose a page' }).disabled).toBe(false),
    )
  }
  expect(
    within(screen.getByRole('list', { name: 'Selected Steam pages' }))
      .getAllByRole('listitem')
      .map((item) => item.textContent),
  ).toEqual(['history.html', 'licences.htm'])
  expect(h.bridge.readSavedSteamPages).not.toHaveBeenCalled()
  expect(h.bridge.clearSavedSteamPages).not.toHaveBeenCalled()
  await waitFor(() =>
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Read selected pages' }).disabled).toBe(
      false,
    ),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Read selected pages' }))
  await waitFor(() => expect(h.bridge.readSavedSteamPages).toHaveBeenCalledWith(['history', 'licences']))
})

it('Platform_tool_directional_focus_matches_the_vertical_layout', async () => {
  const h = fixture()
  await h.provider('Steam')
  fireEvent.click(screen.getByRole('button', { name: 'Sign in to Steam' }))
  const consent = screen.getByRole('dialog', { name: 'Before you sign in' })
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Cancel sign-in' }))
  check(consent)
  fireEvent.click(screen.getByRole('button', { name: 'Cancel sign-in' }))
  fireEvent.click(screen.getByRole('button', { name: 'Steam Web API key' }))
  fireEvent.change(screen.getByLabelText('Steam Web API key', { selector: 'input' }), {
    target: { value: 'unsaved-local-draft' },
  })
  check(screen.getByRole('region', { name: 'Steam Web API key' }))
  fireEvent.click(screen.getByRole('button', { name: 'Back to Steam' }))
  fireEvent.click(screen.getByRole('button', { name: 'Purchase history' }))
  fireEvent.click(screen.getByRole('button', { name: 'Read saved pages' }))
  fireEvent.click(screen.getByRole('button', { name: 'Choose a page' }))
  await screen.findByText('history.html')
  check(screen.getByRole('region', { name: 'Saved Steam pages' }))
  function check(scope: HTMLElement) {
    const controls = [
      ...scope.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), summary'),
    ].filter((control) => !control.closest('details:not([open])') || control.tagName === 'SUMMARY')
    for (let index = 0; index < controls.length - 1; index++) {
      act(() => controls[index].focus())
      act(() => moveControllerFocus('down'))
      expect(document.activeElement).toBe(controls[index + 1])
      act(() => moveControllerFocus('up'))
      expect(document.activeElement).toBe(controls[index])
      act(() => moveControllerFocus('right'))
      expect(document.activeElement).toBe(controls[index])
    }
  }
})

it('controller horizontal movement stays in the masked key field while physical arrow defaults remain available', async () => {
  const h = fixture()
  await h.provider('Steam')
  fireEvent.click(screen.getByRole('button', { name: 'Steam Web API key' }))
  const field = screen.getByLabelText<HTMLInputElement>('Steam Web API key', { selector: 'input' })
  const neighbor = screen.getByRole('button', { name: 'Get a key' })
  vi.spyOn(field, 'getBoundingClientRect').mockReturnValue(new DOMRect(100, 100, 100, 40))
  vi.spyOn(neighbor, 'getBoundingClientRect').mockReturnValue(new DOMRect(300, 100, 100, 40))
  const observed: KeyboardEvent[] = []
  field.addEventListener('keydown', (event) => observed.push(event))
  act(() => field.focus())
  act(() => moveControllerFocus('right'))
  expect(document.activeElement).toBe(field)
  expect(observed.at(-1)?.defaultPrevented).toBe(true)
  expect(isControllerNavigationEvent(observed.at(-1)!)).toBe(true)
  for (const key of ['ArrowLeft', 'ArrowRight']) {
    const physical = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })
    act(() => field.dispatchEvent(physical))
    expect(physical.defaultPrevented).toBe(false)
    expect(isControllerNavigationEvent(physical)).toBe(false)
    expect(document.activeElement).toBe(field)
  }
})

describe.each<Mode>(['desktop', 'fullscreen'])('%s exact account state', (mode) => {
  it('A_host_with_no_sign_in_still_offers_the_key_and_says_the_window_cannot_open', async () => {
    const h = fixture(mode, {
      ...disconnected,
      steam: { ...disconnected.steam, hasApiKey: true, apiKeyIsAppManaged: true, hasUsableCredential: true },
    })
    delete window.winnow.steamSignIn
    await h.provider('Steam')
    expect(screen.getByRole('status', { name: 'KEY SET' })).toBeTruthy()
    expect(
      screen.getByText('The sign-in window cannot open in this frontend. A Web API key works without it.'),
    ).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Sign in to Steam' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Sign out of Steam' })).toBeNull()
    if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Steam Web API key' }))
    const input = screen.getByLabelText<HTMLInputElement>('Steam Web API key', { selector: 'input' })
    expect(input.disabled).toBe(false)
    fireEvent.change(input, { target: { value: 'ABCDEF' } })
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Save API key' }).disabled).toBe(false)
    expect(
      h.bridge.request.mock.calls.some(
        ([call]) => call.route !== 'connections.get' && call.route.startsWith('connections.steam'),
      ),
    ).toBe(false)
  })
  it.each(['Steam', 'Epic', 'GOG'] as const)(
    'Optional_provider_states_are_named_and_neutral_on_both_surfaces: %s',
    async (name) => {
      const h = fixture(mode)
      await h.provider(name)
      const region = screen.getByRole('region', { name: `${name} connection` })
      const label = name === 'Steam' ? 'NO CONNECTION' : name === 'Epic' ? 'NOT SIGNED IN' : 'Not needed'
      const status = within(region).getByRole('status', { name: label })
      expect(status.getAttribute('aria-live')).toBe('polite')
      expect(status.dataset.tone).toBe(name === 'GOG' ? 'live' : 'quiet')
      expect(within(region).getAllByRole('status', { name: label })).toHaveLength(1)
    },
  )
  it('Platform_reads_existing_sessions_and_updates_after_epic_signout_and_signin', async () => {
    const h = fixture(mode, { ...disconnected, epic: { isLive: true, displayName: 'Test player' } })
    await h.provider('Epic')
    const signOut = screen.getByRole('button', { name: 'Sign out of Epic' })
    act(() => signOut.focus())
    fireEvent.click(signOut)
    const dialog = screen.getByRole('dialog', { name: 'Sign out of Epic?' })
    expect(
      within(dialog).getByText(
        'Signing out deletes the stored credential. Your Epic games stay — they come from local files.',
      ),
    ).toBeTruthy()
    fireEvent.keyDown(screen.getByRole('button', { name: 'Cancel' }), { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(screen.getByText('Connected as Test player.')).toBeTruthy()
    expect(document.activeElement).toBe(signOut)
    expect(h.bridge.request).not.toHaveBeenCalledWith(
      expect.objectContaining({ route: 'connections.epic.signOut' }),
    )
    const refresh = deferred<{ ok: boolean; status: number; data: unknown }>()
    const originalRequest = h.bridge.request.getMockImplementation()!
    let signingOut = false
    h.bridge.request.mockImplementation((input) => {
      if (input.route === 'connections.epic.signOut') signingOut = true
      if (input.route === 'connections.get' && signingOut) return refresh.promise
      return originalRequest(input)
    })
    fireEvent.click(signOut)
    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    signingOut = false
    await act(async () => refresh.resolve({ ok: true, status: 200, data: { ...disconnected, epic: null } }))
    await screen.findByRole('status', { name: 'NOT SIGNED IN' })
    await waitFor(() =>
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Connect Epic Games' })),
    )
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.getByRole('region', { name: 'Epic connection' })).toBeTruthy()
    h.bridge.epicSignIn.mockImplementation(async () => {
      h.state.snapshot = { ...disconnected, epic: { isLive: true, displayName: 'New test player' } }
      return { succeeded: true, failure: 0, persisted: true }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Connect Epic Games' }))
    await screen.findByText('Consent fixture')
    fireEvent.click(screen.getByLabelText('I agree to connect this account'))
    fireEvent.click(screen.getByRole('button', { name: 'Open Epic sign-in window' }))
    await screen.findByText('Connected as New test player.')
    expect(screen.queryByText('Connected as Test player.')).toBeNull()
  })
  it('Epic_expiry_and_failed_signin_remain_truthful', async () => {
    const h = fixture(mode, { ...disconnected, epic: { isLive: false, displayName: 'Test player' } })
    await h.provider('Epic')
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to Epic again' }))
    await screen.findByText('Consent fixture')
    fireEvent.click(screen.getByLabelText('I agree to connect this account'))
    fireEvent.click(screen.getByRole('button', { name: 'Open Epic sign-in window' }))
    await screen.findByText('Sign-in cancelled. Nothing was changed.')
    expect(screen.getByRole('status', { name: 'SESSION EXPIRED' })).toBeTruthy()
    expect(screen.getByText(/expired for Test player/)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Sign in to Epic again' })).toBeTruthy()
  })
})

it('Steam_renders_health_identity_and_actions_as_shared_state_changes', async () => {
  const h = fixture('fullscreen', {
    ...disconnected,
    steam: {
      ...disconnected.steam,
      hasSession: true,
      sessionUsable: true,
      sessionAccount: '76561198000000000',
      sessionExpiresAt: new Date(Date.now() + 3600000).toISOString(),
    },
    steamHealth: 1,
  })
  await h.provider('Steam')
  expect(screen.getByText('76561198000000000')).toBeTruthy()
  expect(screen.getByRole('button', { name: 'Sign out of Steam' })).toBeTruthy()
  expect(screen.queryByRole('button', { name: 'Sign in again' })).toBeNull()
  h.state.snapshot = { ...h.state.snapshot, steamHealth: 4 }
  await act(() => h.client.invalidateQueries({ queryKey: ['api', 'connections.get'] }))
  expect(await screen.findByRole('status', { name: 'SIGN-IN EXPIRED' })).toBeTruthy()
  expect(screen.getByRole('button', { name: 'Sign in again' })).toBeTruthy()
  h.state.snapshot = disconnected
  await act(() => h.client.invalidateQueries({ queryKey: ['api', 'connections.get'] }))
  await waitFor(() => expect(screen.queryByText('76561198000000000')).toBeNull())
  expect(screen.queryByRole('button', { name: 'Sign out of Steam' })).toBeNull()
})
