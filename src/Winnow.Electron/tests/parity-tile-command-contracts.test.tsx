// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ApiRequest } from '../src/shared/bridge'
import type { LibraryGame, Mode, Workspace } from '../src/renderer/api/types'
import type { ThemeContext } from '../src/shared/theme'
import { noActionSentence, primaryAction, primaryEntry } from '../src/shared/game-actions'
import { AvalonCover, AvalonLibrary, AvalonShell } from '../src/renderer/themes/avalon'
import { AvalonCoverWorkspace } from '../src/renderer/themes/avalon-desktop-cover'
import { Details } from '../src/renderer/features/Details'
import { PrimaryActionsContext } from '../src/renderer/features/PrimaryActions'
import { detailsGame } from '../src/renderer/features/details-layout'
import { libraryBucketLabel } from '../src/renderer/themes/avalon-library-chrome'
import { clearViewState } from '../src/renderer/viewState'
import { coverProfile, FixtureCoverArt } from './cover-fixtures'

// TileActionsTests at cf45d9f1127243a987d3cf6e664a32fc767ecb67.
const now = '2026-08-26T12:00:00Z'
function fixture(title = 'Anvil', store = 'steam', installed = false) {
  const game: LibraryGame = {
    workId: 1,
    title,
    bucket: 'never_played',
    playtimeMinutes: 0,
    lastPlayedAt: null,
    entries: [{ ownershipId: 1, releaseId: 1, workId: 1, title, store, installed, playtimeMinutes: 0 }],
  }
  const workspace: Workspace = {
    preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
    works: [{ id: 1, name: title }],
    externalIds:
      title === 'Anvil'
        ? [{ releaseId: 1, provider: 'steam', providerId: '700001' }]
        : [
            { releaseId: 1, provider: 'steam', providerId: '620' },
            { releaseId: 1, provider: 'gog', providerId: '1971477531' },
            { releaseId: 1, provider: 'epic', providerId: '7a70b499513441c792b541d53505e0b2' },
          ],
    epicLaunchKeys: {
      '7a70b499513441c792b541d53505e0b2': {
        namespace: '41f47fd0d3e248bc938a5815d6d64daa',
        catalogItemId: '7a70b499513441c792b541d53505e0b2',
        artifactId: 'Bluebird',
      },
    },
    pluginActions: {},
  }
  return { game, workspace }
}
const clients: QueryClient[] = []
vi.mock('@tanstack/react-virtual', () => ({
  useVirtualizer: (options: { count: number; estimateSize(): number }) => ({
    getTotalSize: () => options.count * options.estimateSize(),
    getVirtualItems: () =>
      Array.from({ length: options.count }, (_, index) => ({
        key: index,
        index,
        start: index * options.estimateSize(),
      })),
    measure: vi.fn(),
    scrollToOffset: vi.fn(),
    scrollToIndex: vi.fn(),
  }),
}))
beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
      unobserve() {}
    },
  )
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)
})
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of [
      'query',
      'bucket',
      'store',
      'list',
      'sort',
      'view',
      'tools',
      'selected',
      'selection',
      'rules',
      'rows',
    ])
      clearViewState(`avalon:library:${mode}:${key}`)
  }
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})
function setup(mode: Mode, data = fixture()) {
  const { game, workspace } = data
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      input.route === 'library.get'
        ? { games: [game], lists: [] }
        : input.route === 'library.workspace'
          ? workspace
          : input.route === 'game.details'
            ? {
                workId: 1,
                readAtUtc: now,
                events: [],
                sessions: {},
                ratings: [],
                journalEntries: [],
                achievements: [],
              }
            : input.route === 'actions.execute'
              ? 0
              : input.route === 'preferences.presentation.get'
                ? []
                : {},
  }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, artwork: vi.fn(async () => null), openExternal: vi.fn() },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  client.setQueryData(['api', 'library.get'], { games: [game], lists: [] })
  client.setQueryData(['api', 'library.workspace', undefined], workspace)
  clients.push(client)
  const sharedLaunch = vi.fn(async (_ownershipId: number) => 0)
  const launch = vi.fn(async (ownershipId: number) => {
    await sharedLaunch(ownershipId)
  })
  const openGame = vi.fn()
  const context: ThemeContext = {
    mode,
    page: 'library',
    games: [game],
    loading: false,
    selectedWorkId: null,
    profile: coverProfile(),
    feed: undefined,
    children: null,
    setPage: vi.fn(),
    openGame,
    toggleFullscreen: vi.fn(),
    renderScreen: () => null,
    actions: { launch },
    components: {
      Artwork: FixtureCoverArt,
      Impression: ({ children }) => <>{children}</>,
      GameCard: () => null,
      GamePreview: () => null,
      ArtworkEffects: ({ children }) => <>{children}</>,
    },
  }
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>
      <PrimaryActionsContext.Provider value={{ launch: sharedLaunch, dismiss: vi.fn() }}>
        {children}
      </PrimaryActionsContext.Provider>
    </QueryClientProvider>
  )
  return { ...data, client, request, context, launch, sharedLaunch, openGame, wrapper }
}

describe('frozen tile command contracts', () => {
  it.each(['None', 'InstallStateUnknown', 'NoStoreId'] as const)(
    'never exposes placeholder copy for %s',
    (reason) => {
      const { game, workspace } = fixture('Fez', 'epic', true)
      if (reason === 'NoStoreId') workspace.epicLaunchKeys = {}
      const sentence = noActionSentence(
        { ...game.entries[0], installed: reason === 'InstallStateUnknown' ? null : true },
        workspace,
      )
      if (reason === 'None') expect(sentence).toBeNull()
      else {
        expect(sentence?.trim()).toBeTruthy()
        expect(sentence).not.toMatch(/TODO|PLACEHOLDER/i)
        expect(sentence).toBe(
          reason === 'InstallStateUnknown'
            ? "Winnow has not read this copy's install state yet."
            : 'Winnow does not yet hold the identifier this store needs to reach this game.',
        )
      }
    },
  )

  it('the Anvil grid forwards its primary and Details actions to the supplied library commands', async () => {
    const f = setup('desktop')
    render(
      <AvalonShell {...f.context}>
        <AvalonLibrary {...f.context} />
      </AvalonShell>,
      { wrapper: f.wrapper },
    )
    const tile = await screen.findByRole('button', { name: 'View Anvil' })
    fireEvent.mouseMove(tile.closest('.avalon-desktop-cover')!)
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Install' })))
    expect(f.context.actions.launch).toBe(f.launch)
    expect(f.launch).toHaveBeenCalledExactlyOnceWith(1)
    expect(f.sharedLaunch).toHaveBeenCalledExactlyOnceWith(1)
    expect(f.openGame).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Details' }))
    expect(f.openGame).toHaveBeenCalledExactlyOnceWith(1)
    expect(f.launch).toHaveBeenCalledTimes(1)
    expect(f.request.mock.calls.filter(([input]) => input.route === 'actions.execute')).toHaveLength(0)
  })

  for (const mode of ['desktop', 'fullscreen'] as const) {
    it(`${mode} Anvil tile names Never played with the rail vocabulary`, async () => {
      const f = setup(mode)
      render(
        <AvalonShell {...f.context}>
          <AvalonLibrary {...f.context} />
        </AvalonShell>,
        { wrapper: f.wrapper },
      )
      const tile = await screen.findByRole('button', { name: 'View Anvil' })
      expect(tile.getAttribute('aria-description')).toBe('Never played')
      expect(libraryBucketLabel(f.game.bucket)).toBe('Never played')
      expect(screen.getByRole('button', { name: /^Never played/ })).toBeTruthy()
      fireEvent.click(tile)
      expect(f.openGame).toHaveBeenCalledExactlyOnceWith(1)
      expect(f.launch).not.toHaveBeenCalled()
    })

    it.each(['gog', 'steam'])(
      `${mode} Fez %s details uses the tile's exact selected ownership and primary policy`,
      async (store) => {
        const f = setup(mode, fixture('Fez', store, true))
        const tileEntry = primaryEntry(f.game.entries, f.workspace)
        expect(detailsGame(1, [f.game], f.workspace)).toBe(f.game)
        expect(primaryEntry(detailsGame(1, [f.game], f.workspace)!.entries, f.workspace)).toBe(tileEntry)
        expect(primaryAction(tileEntry!, f.workspace)).toBe('Play')
        const view = render(
          <AvalonCoverWorkspace.Provider value={f.workspace}>
            <AvalonCover game={f.game} context={{ ...f.context, mode: 'desktop' }} />
          </AvalonCoverWorkspace.Provider>,
          { wrapper: f.wrapper },
        )
        fireEvent.mouseMove(view.container.querySelector('.avalon-desktop-cover')!)
        expect(screen.getByRole('button', { name: 'Play' }).title).toBe(
          `Launch through ${store === 'gog' ? 'GOG' : 'Steam'}`,
        )
        await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Play' })))
        expect(f.sharedLaunch).toHaveBeenCalledExactlyOnceWith(1)
        expect(f.openGame).not.toHaveBeenCalled()
        view.unmount()
        render(<Details workId={1} mode={mode} presentation="avalon" onClose={vi.fn()} />, {
          wrapper: f.wrapper,
        })
        const primary = await screen.findByRole('button', { name: 'Play' })
        fireEvent.click(primary)
        await waitFor(() => expect(f.sharedLaunch).toHaveBeenCalledTimes(2))
        expect(f.sharedLaunch.mock.calls).toEqual([[1], [1]])
        expect(f.request.mock.calls.filter(([input]) => input.route === 'actions.execute')).toHaveLength(0)
        fireEvent.click(screen.getByRole('button', { name: 'More' }))
        expect(
          screen.getByRole('button', {
            name: store === 'gog' ? 'Show in GOG Galaxy' : 'Store page',
          }),
        ).toBeTruthy()
        expect(f.sharedLaunch).toHaveBeenCalledTimes(2)
        expect(f.request.mock.calls.filter(([input]) => input.route === 'actions.execute')).toHaveLength(0)
      },
    )
  }
})
