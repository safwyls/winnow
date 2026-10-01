// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Settings } from '../src/renderer/features/Settings'
import { titlesByStore } from '../src/renderer/features/Platforms'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'
import type { LibraryGame, Mode, StoreConnections } from '../src/renderer/api/types'

const game = (id: number, stores: string[]): LibraryGame => ({
  workId: id,
  title: `Game ${id}`,
  bucket: 'NeverPlayed',
  playtimeMinutes: 0,
  entries: stores.map((store, index) => ({
    ownershipId: id * 10 + index,
    workId: id,
    releaseId: id * 10 + index,
    title: `Game ${id}`,
    store,
    installed: false,
    playtimeMinutes: 0,
  })),
})
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
function fixture(
  mode: Mode,
  options: {
    games?: LibraryGame[]
    accounts?: number
    snapshot?: StoreConnections
    countUnavailable?: boolean
    grouped?: boolean
  } = {},
) {
  const state = { snapshot: options.snapshot ?? disconnected, confirmed: false }
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      input.route === 'connections.get'
        ? state.snapshot
        : input.route === 'library.get'
          ? options.countUnavailable
            ? null
            : { games: options.games ?? [], lists: [] }
          : input.route === 'connections.visibility.get'
            ? {
                accountConfirmed: state.confirmed,
                ownAccountOnly: false,
                hiddenCount: 0,
                accountCount: options.accounts ?? 0,
              }
            : input.route === 'connections.igdb.get'
              ? {
                  clientId: '',
                  hasSavedCredentials: false,
                  isReadable: false,
                  hasConfigurationCredentials: false,
                }
              : input.route === 'library.workspace'
                ? {
                    works: [],
                    externalIds: [],
                    epicLaunchKeys: {},
                    pluginActions: {},
                    identityLinks: options.grouped
                      ? [{ parentWorkId: 1, childWorkId: 2, kind: 'expansion_of' }]
                      : [],
                  }
                : input.route === 'preferences.presentation.get'
                  ? [{ preference: 'GroupExpansions', value: options.grouped ? ' True ' : 'false' }]
                  : input.route === 'preferences.library.get'
                    ? { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'Off' }
                    : [],
  }))
  const steamSignIn = vi.fn(),
    steamCapturePages = vi.fn()
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, steamSignIn, steamCapturePages },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <Settings mode={mode} />
    </QueryClientProvider>,
  )
  fireEvent.click(
    within(screen.getByRole('navigation', { name: 'Settings section' })).getByRole('button', {
      name: 'Platforms',
    }),
  )
  return { request, client, state, steamSignIn, steamCapturePages }
}

it('counts each grouped title once per store before screen filtering', () => {
  expect(titlesByStore([game(1, ['steam', 'steam', 'gog']), game(2, ['Steam']), game(3, ['epic'])])).toEqual({
    steam: 2,
    gog: 1,
    epic: 1,
  })
})
describe.each<Mode>(['desktop', 'fullscreen'])('%s Platforms source contracts', (mode) => {
  it.each([false, true])('states which library coverage a saved API key=%s adds', async (key) => {
    fixture(mode, {
      snapshot: {
        ...disconnected,
        steam: { ...disconnected.steam, hasApiKey: key, hasUsableCredential: key },
      },
    })
    const steam = await screen.findByRole('region', { name: 'Steam connection' })
    const state = within(steam).getByText(key ? 'KEY SET' : 'NO CONNECTION')
    expect(state.getAttribute('data-tone')).toBe(key ? 'live' : 'quiet')
    expect(
      within(steam).getByText(
        key
          ? 'Adds games never installed on this PC.'
          : 'Games never touched on this PC are not in your library yet.',
      ),
    ).toBeTruthy()
    expect(within(steam).getByText(key ? 'On - API' : 'Off')).toBeTruthy()
  })
  it('refreshes folded capture availability and keeps saved files reachable without sign-in or a browser', async () => {
    const h = fixture(mode)
    await screen.findByRole('region', { name: 'Steam connection' })
    delete window.winnow.steamCapturePages
    await h.client.invalidateQueries({ queryKey: ['api', 'connections.get'] })
    fireEvent.click(screen.getByRole('button', { name: 'Import purchase history' }))
    expect(screen.getByText(/Steam capture window is unavailable in this frontend/)).toBeTruthy()
    expect((screen.getByLabelText('Saved Steam pages') as HTMLInputElement).disabled).toBe(false)
    expect(screen.queryByRole('button', { name: 'Capture account pages in Winnow' })).toBeNull()
    expect(
      (screen.getByText('Before you save the pages').closest('details') as HTMLDetailsElement).open,
    ).toBe(false)
    expect(h.steamCapturePages).not.toHaveBeenCalled()
    expect(h.steamSignIn).not.toHaveBeenCalled()
  })
  it('uses the shared expansion projection without adding a folded pack store to its base', async () => {
    fixture(mode, {
      games: [game(1, ['steam', 'steam']), game(2, ['epic']), game(3, ['steam', 'gog'])],
      grouped: true,
    })
    await screen.findByText('2 games in your library')
    await waitFor(() => expect(document.querySelector('#platform-epic .platform-title-count')).toBeNull())
    fireEvent.click(screen.getByRole('button', { name: 'GOG' }))
    expect(
      within(screen.getByRole('region', { name: 'GOG connection' }).parentElement!).getByText(
        '1 game in your library',
      ),
    ).toBeTruthy()
  })
  it('opens on Steam and shows exactly one platform card while switching in either direction', async () => {
    fixture(mode)
    expect(screen.getByRole('button', { name: 'Platforms' }).getAttribute('aria-pressed')).toBe('true')
    await screen.findByRole('region', { name: 'Steam connection' })
    expect(screen.queryByRole('region', { name: 'Epic connection' })).toBeNull()
    expect(screen.queryByRole('region', { name: 'GOG connection' })).toBeNull()
    for (const [tab, card] of [
      ['EPIC', 'Epic connection'],
      ['GOG', 'GOG connection'],
      ['STEAM', 'Steam connection'],
      ['GOG', 'GOG connection'],
    ]) {
      fireEvent.click(screen.getByRole('button', { name: tab }))
      expect(screen.getByRole('region', { name: card })).toBeTruthy()
      expect(
        ['Steam connection', 'Epic connection', 'GOG connection'].filter((name) =>
          screen.queryByRole('region', { name }),
        ),
      ).toEqual([card])
    }
    const gog = screen.getByRole('region', { name: 'GOG connection' })
    expect(within(gog).getByText('Not needed')).toBeTruthy()
    expect(within(gog).queryByRole('button')).toBeNull()
    expect(within(gog).getByText(/nothing to sign into/)).toBeTruthy()
  })
  it('keeps lapsed Epic and failing Steam attention visible on other tabs without starting a connection', async () => {
    const h = fixture(mode, {
      snapshot: { ...disconnected, epic: { isLive: false, displayName: 'Expired account' } },
    })
    const epic = await screen.findByRole('button', { name: /^EPIC/ })
    expect(within(epic).getByLabelText('Needs attention')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'GOG' }))
    h.state.snapshot = {
      ...h.state.snapshot,
      steamHealth: 3,
      steam: { ...disconnected.steam, hasSession: true, hasApiKey: true },
    }
    await h.client.invalidateQueries({ queryKey: ['api', 'connections.get'] })
    await waitFor(() =>
      expect(
        within(screen.getByRole('button', { name: /^STEAM/ })).getByLabelText('Needs attention'),
      ).toBeTruthy(),
    )
    expect(within(screen.getByRole('button', { name: 'GOG' })).queryByLabelText('Needs attention')).toBeNull()
    expect(h.steamSignIn).not.toHaveBeenCalled()
    expect(h.steamCapturePages).not.toHaveBeenCalled()
  })
  it('shows formatted whole-library totals and account summary with tabular figure styling', async () => {
    const games = Array.from({ length: 1247 }, (_, index) =>
      game(index + 1, ['steam', ...(index < 67 ? ['epic'] : []), ...(index < 14 ? ['gog'] : [])]),
    )
    fixture(mode, { games, accounts: 2 })
    const count = await screen.findByText('1,247 games in your library')
    expect(count.className).toBe('platform-title-count')
    expect(await screen.findByText('1,247 games across 2 accounts')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'EPIC' }))
    expect(
      within(screen.getByRole('region', { name: 'Epic connection' }).parentElement!).getByText(
        '67 games in your library',
      ),
    ).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'GOG' }))
    expect(
      within(screen.getByRole('region', { name: 'GOG connection' }).parentElement!).getByText(
        '14 games in your library',
      ),
    ).toBeTruthy()
  })
  it.each([
    [[], 0, null],
    [[game(1, ['steam'])], 1, '1 game across 1 account'],
  ] as const)('omits empty counts and uses singular account nouns', async (games, accounts, summary) => {
    fixture(mode, { games: [...games], accounts })
    await screen.findByRole('region', { name: 'Steam connection' })
    if (summary) expect(await screen.findByText(summary)).toBeTruthy()
    else {
      expect(document.querySelector('.platform-title-count')).toBeNull()
      expect(document.querySelector('.steam-accounts-summary')).toBeNull()
    }
  })
  it('composes with an unavailable count source and marks account confirmation pending until confirmed', async () => {
    const h = fixture(mode, { countUnavailable: true })
    await screen.findByRole('region', { name: 'Steam connection' })
    expect(document.querySelector('.platform-title-count')).toBeNull()
    expect(screen.getByText('Account confirmation pending')).toBeTruthy()
    expect(
      (screen.getByRole('checkbox', { name: 'Show only your account' }) as HTMLInputElement).disabled,
    ).toBe(true)
    h.state.confirmed = true
    await h.client.invalidateQueries({ queryKey: ['api', 'connections.visibility.get'] })
    await waitFor(() => expect(screen.queryByText('Account confirmation pending')).toBeNull())
    expect(
      (screen.getByRole('checkbox', { name: 'Show only your account' }) as HTMLInputElement).disabled,
    ).toBe(false)
  })
  it('starts all Steam layers closed and replaces help, purchase, consent and account layers without stacking', async () => {
    const h = fixture(mode)
    const methods = await screen.findByRole('button', { name: 'Which one should I use?' })
    const purchase = screen.getByRole('button', { name: 'Import purchase history' })
    const consent = screen.getByRole('button', { name: 'Sign in to Steam' })
    const accounts = screen.getByRole('button', { name: 'What the account filter covers' })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByLabelText('Saved Steam pages')).toBeNull()
    for (const [button, title] of [
      [methods, 'Ways to connect Steam'],
      [purchase, 'Import Steam purchase history'],
      [consent, 'Before you sign in'],
      [accounts, 'Steam account scope'],
    ] as const) {
      fireEvent.click(button)
      expect(screen.getAllByRole('dialog')).toHaveLength(1)
      expect(screen.getByRole('dialog', { name: title })).toBeTruthy()
    }
    expect(screen.getByText(/cannot attribute/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    fireEvent.click(consent)
    expect(
      (
        screen.getByRole('checkbox', {
          name: 'Also capture purchase history and licences',
        }) as HTMLInputElement
      ).checked,
    ).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Cancel sign-in' }))
    expect(h.steamSignIn).not.toHaveBeenCalled()
  })
  it('shows both purchase import explanations before choosing a route, with no saved credential required', async () => {
    const h = fixture(mode)
    fireEvent.click(await screen.findByRole('button', { name: 'Import purchase history' }))
    expect(screen.getByText(/Save your Steam account pages as HTML/).closest('details')).toBeNull()
    expect(screen.getByText(/Sign in to Steam in a private window to capture/).closest('details')).toBeNull()
    expect((screen.getByLabelText('Saved Steam pages') as HTMLInputElement).disabled).toBe(false)
    expect(
      (screen.getByRole('button', { name: 'Capture account pages in Winnow' }) as HTMLButtonElement).disabled,
    ).toBe(false)
    expect(h.steamSignIn).not.toHaveBeenCalled()
    expect(h.steamCapturePages).not.toHaveBeenCalled()
  })
})
