// @vitest-environment jsdom
import React from 'react'
import { beforeEach, afterEach, describe, it, expect, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ApplicationActivation, WinnowBridge } from '../src/shared/bridge'
import { App } from '../src/renderer/App'
import { DEFAULT_PROFILE, selectThemeProfile } from '../src/shared/theme'
import { afterglow } from '../src/renderer/themes/afterglow'
import { AVALON_PALETTES } from '../src/renderer/themes/avalon-palettes'

// Geometry and GPU lifetime have their own controlled-clock tests; this suite checks routes and data.
vi.mock('../src/renderer/components/portal-effects', () => ({
  PortalSurface: ({ children, onExpanded }: { children: React.ReactNode; onExpanded?: () => void }) => {
    React.useEffect(() => {
      onExpanded?.()
    }, [onExpanded])
    return <div className="winnow-portal-surface">{children}</div>
  },
}))

const game = {
  workId: 1,
  title: 'A real API title',
  bucket: 'never_played',
  playtimeMinutes: 0,
  entries: [
    {
      ownershipId: 1,
      releaseId: 1,
      workId: 1,
      title: 'A real API title',
      store: 'manual',
      installed: false,
      playtimeMinutes: 0,
    },
  ],
}
let fullscreen: (value: boolean) => void = () => {}
beforeEach(() => {
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
      unobserve() {}
    },
  )
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      observe() {}
      disconnect() {}
      unobserve() {}
    },
  )
  window.matchMedia = vi.fn().mockImplementation((query) => ({
    matches: false,
    media: query,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
  }))
  window.winnow = {
    request: vi.fn(async ({ route }) => ({
      ok: true,
      status: 200,
      data:
        route === 'library.get'
          ? { games: [game], lists: [] }
          : route === 'feed.get'
            ? { shelves: [], candidateCount: 0, confidence: 0, failed: false }
            : route === 'library.workspace'
              ? {
                  works: [{ id: 1, name: game.title }],
                  externalIds: [],
                  pluginActions: {},
                  epicLaunchKeys: {},
                }
              : route === 'artworkState'
                ? { current: null, revision: 'a' }
                : route === 'game.details'
                  ? { workId: 1, events: [], sessions: {}, ratings: [], journalEntries: [], achievements: [] }
                  : route === 'activity.query'
                    ? { rows: [], next: null }
                    : route === 'statistics.gameplay'
                      ? {
                          recordedSeconds: 0,
                          gamesPlayedCount: 0,
                          startedSessionCount: 0,
                          periods: [],
                          topGames: [],
                        }
                      : route === 'preferences.library.get'
                        ? { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' }
                        : route === 'connections.get'
                          ? { steam: { hasUsableCredential: false } }
                          : route === 'connections.igdb.get'
                            ? { clientId: '', hasSavedCredentials: false }
                            : [],
    })),
    connection: vi.fn(async () => ({ connected: true, message: 'Connected' })),
    onConnection: () => () => {},
    onEvent: () => () => {},
    isFullscreen: vi.fn(async () => false),
    onFullscreen: (callback: (value: boolean) => void) => {
      fullscreen = callback
      return () => {}
    },
    setFullscreen: vi.fn(async (value) => fullscreen(value)),
    artwork: vi.fn(async () => null),
    loadPreferences: vi.fn(async () => null),
    savePreferences: vi.fn(async () => {}),
    listThemes: vi.fn(async () => []),
    importProfile: vi.fn(async () => null),
    exportProfile: vi.fn(async () => true),
    installTheme: vi.fn(async () => null),
    openExternal: vi.fn(async () => {}),
  } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})
function mount() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <App />
    </QueryClientProvider>,
  )
  return client
}
function mountAfterglow() {
  vi.mocked(window.winnow.loadPreferences).mockResolvedValue(
    selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'afterglow', afterglow),
  )
  return mount()
}
describe('integrated frontend', () => {
  it('returns from each original palette change with the loaded fullscreen shelf still navigable', async () => {
    const original = window.winnow.request
    const library = {
      games: [
        game,
        {
          ...game,
          workId: 2,
          title: 'Another world',
          entries: [{ ...game.entries[0], workId: 2, releaseId: 2, ownershipId: 2 }],
        },
      ],
      lists: [],
    }
    const feed = {
      candidateCount: 2,
      confidence: 1,
      failed: false,
      shelves: [
        {
          id: 'unopened',
          title: 'Waiting for you',
          blurb: '',
          supportsFeedback: false,
          reserve: [],
          items: [
            { ownershipId: 1, releaseId: 1, title: game.title, reason: 'First reason.' },
            { ownershipId: 2, releaseId: 2, title: 'Another world', reason: 'Second reason.' },
          ],
        },
      ],
    }
    // Preference writes refresh snapshots. The fixture backend must retain the
    // loaded library too, rather than replacing seeded cache data with an empty feed.
    window.winnow.request = vi.fn(async (value) =>
      value.route === 'library.get'
        ? { ok: true, status: 200, data: library }
        : value.route === 'feed.get'
          ? { ok: true, status: 200, data: feed }
          : original(value),
    ) as WinnowBridge['request']
    mount()
    await screen.findByRole('button', { name: 'View A real API title' })
    act(() => fullscreen(true))
    for (const palette of AVALON_PALETTES) {
      fireEvent.click(screen.getByRole('button', { name: 'Theme Studio' }))
      fireEvent.change(await screen.findByRole('combobox', { name: /^Avalon palette/ }), {
        target: { value: palette.id },
      })
      fireEvent.click(screen.getByRole('button', { name: 'For you' }))
      const first = await screen.findByRole('button', { name: 'View A real API title' })
      first.focus()
      fireEvent.keyDown(first, { key: 'ArrowRight' })
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'View Another world' }))
      fireEvent.keyDown(document.activeElement!, { key: 'ArrowLeft' })
      expect(document.activeElement).toBe(first)
    }
  }, 15000)
  it('keeps a cold-start activation behind setup until the saved progress has loaded and completed', async () => {
    const original = window.winnow.request
    let complete!: (value: unknown) => void
    const pending = new Promise((resolve) => {
      complete = resolve
    })
    window.winnow.request = vi.fn(async (value) =>
      value.route === 'setup.get' ? { ok: true, status: 200, data: await pending } : original(value),
    ) as WinnowBridge['request']
    window.winnow.takeActivations = vi.fn(async (): Promise<ApplicationActivation[]> => [
      { kind: 'plugin', pluginId: 'xbox', releaseTag: 'v1.2.3' },
    ])
    const client = mount()
    await screen.findByRole('button', { name: 'Winnow home' })
    expect(screen.queryByRole('dialog', { name: 'Review provider installation' })).toBeNull()
    await act(async () => {
      complete({ step: 0 })
    })
    await screen.findByRole('dialog', { name: 'Welcome to Winnow' })
    expect(screen.queryByRole('dialog', { name: 'Review provider installation' })).toBeNull()
    act(() => client.setQueryData(['api', 'setup.get', undefined], { step: null }))
    await screen.findByRole('dialog', { name: 'Review provider installation' })
    expect(
      vi
        .mocked(window.winnow.request)
        .mock.calls.some(([value]) => value.route === 'plugins.official.install'),
    ).toBe(false)
  })
  it('delivers startup activations before a newer event received during the pending handshake', async () => {
    let complete!: (value: ApplicationActivation[]) => void
    let receive!: (value: ApplicationActivation) => void
    window.winnow.takeActivations = vi.fn(
      () =>
        new Promise<ApplicationActivation[]>((resolve) => {
          complete = resolve
        }),
    )
    window.winnow.onActivation = (callback) => {
      receive = callback
      return () => {}
    }
    mount()
    await screen.findByRole('button', { name: 'Winnow home' })
    act(() => receive({ kind: 'fullscreen' }))
    expect(window.winnow.setFullscreen).not.toHaveBeenCalled()
    await act(async () => complete([{ kind: 'plugin', pluginId: 'psn', releaseTag: 'v1.2.3' }]))
    const installer = await screen.findByRole('dialog', { name: 'Review provider installation' })
    expect(window.winnow.setFullscreen).not.toHaveBeenCalled()
    fireEvent.click(within(installer).getByRole('button', { name: /^Close$/ }))
    await waitFor(() => expect(window.winnow.setFullscreen).toHaveBeenCalledWith(true))
  })
  it('imports the original palette once and shares it between desktop and fullscreen', async () => {
    const originalRequest = vi.mocked(window.winnow.request).getMockImplementation()!
    vi.mocked(window.winnow.request).mockImplementation(async (input) =>
      input.route === 'preferences.presentation.get'
        ? ({ ok: true, status: 200, data: [{ preference: 'Theme', value: 'nightshift' }] } as never)
        : originalRequest(input),
    )
    mount()
    await waitFor(() => expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#070A10'))
    for (const fullscreenMode of [true, false]) {
      act(() => fullscreen(fullscreenMode))
      await waitFor(() =>
        expect(
          document.querySelector(`.avalon-shell.${fullscreenMode ? 'fullscreen' : 'desktop'}`),
        ).not.toBeNull(),
      )
      expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#070A10')
      expect(document.documentElement.style.colorScheme).toBe('dark')
    }
    expect(window.winnow.request).not.toHaveBeenCalledWith(
      expect.objectContaining({ route: 'preferences.presentation.put' }),
    )
  })
  it('starts fresh in Avalon and renders recommendations and complete details on both surfaces', async () => {
    const client = mount()
    await waitFor(() => expect(document.querySelector('.avalon-shell.desktop')).not.toBeNull())
    await screen.findByText('Recommendations appear here as Winnow learns about your library.')
    expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#0F1C1E')
    act(() =>
      client.setQueryData(['api', 'feed.get'], {
        candidateCount: 1,
        confidence: 1,
        failed: false,
        shelves: [
          {
            id: 'unopened',
            title: 'Waiting for you',
            blurb: '',
            supportsFeedback: false,
            reserve: [],
            items: [{ ownershipId: 1, releaseId: 1, title: game.title, reason: 'A first visit awaits.' }],
          },
        ],
      }),
    )
    for (const fullscreenMode of [false, true]) {
      act(() => fullscreen(fullscreenMode))
      await waitFor(() =>
        expect(
          document.querySelector(`.avalon-shell.${fullscreenMode ? 'fullscreen' : 'desktop'}`),
        ).not.toBeNull(),
      )
      fireEvent.click(screen.getByRole('button', { name: 'For you' }))
      fireEvent.click(await screen.findByRole('button', { name: 'View A real API title' }))
      await screen.findByRole('heading', { name: 'A real API title' })
      expect(screen.getByRole('button', { name: 'History', hidden: true })).toBeDefined()
      expect(screen.getByRole('button', { name: 'Artwork', hidden: true })).toBeDefined()
      expect(screen.getByRole('button', { name: 'Metadata', hidden: true })).toBeDefined()
      fireEvent.click(screen.getByRole('button', { name: 'Back to your library' }))
    }
  })
  it('keeps the Discover portal mounted while cycling recommendations on both surfaces', async () => {
    const client = mountAfterglow()
    await screen.findByRole('heading', { name: 'A real API title' })
    act(() =>
      client.setQueryData(['api', 'library.get'], {
        games: [
          game,
          {
            ...game,
            workId: 2,
            title: 'Another world',
            entries: [{ ...game.entries[0], workId: 2, releaseId: 2, ownershipId: 2 }],
          },
        ],
        lists: [],
      }),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Theme Studio' }))
    fireEvent.click(await screen.findByRole('button', { name: /Rift.*Floating covers/i }))
    for (const fullscreenMode of [false, true]) {
      act(() => fullscreen(fullscreenMode))
      fireEvent.click(screen.getByRole('button', { name: 'Discover' }))
      await waitFor(() =>
        expect(document.querySelector('.rift-world-portal .winnow-portal-surface')).not.toBeNull(),
      )
      const portal = document.querySelector('.rift-world-portal .winnow-portal-surface')
      fireEvent.click(screen.getAllByRole('button', { name: 'Next recommendation' }).at(-1)!)
      expect(screen.getByRole('heading', { name: 'Another world' })).toBeDefined()
      expect(document.querySelector('.rift-world-portal .winnow-portal-surface')).toBe(portal)
      expect(screen.getByRole('button', { name: 'View Another world' })).toBeDefined()
      fireEvent.click(screen.getByRole('button', { name: 'View game' }))
      await screen.findByRole('button', { name: 'History', hidden: true })
      fireEvent.click(screen.getByRole('button', { name: 'Back to your library' }))
    }
  })

  it('offers Rift beside quiet Afterglow and retains full game details on both surfaces', async () => {
    mountAfterglow()
    await screen.findByRole('heading', { name: 'A real API title' })
    fireEvent.click(screen.getByRole('button', { name: 'Theme Studio' }))
    fireEvent.click(await screen.findByRole('button', { name: /Rift.*Floating covers/i }))
    await waitFor(() => expect(document.querySelector('.rift-shell.desktop')).not.toBeNull())
    expect(document.documentElement.dataset.palette).toBe('rift')
    fireEvent.click(screen.getByRole('button', { name: 'Discover' }))
    fireEvent.click(await screen.findByRole('button', { name: 'View game' }))
    await screen.findByRole('heading', { name: 'A real API title' })
    expect(screen.getByRole('button', { name: 'History', hidden: true })).toBeDefined()
    expect(screen.getByRole('button', { name: 'Artwork', hidden: true })).toBeDefined()
    act(() => fullscreen(true))
    await waitFor(() => expect(document.querySelector('.rift-shell.fullscreen')).not.toBeNull())
    fireEvent.click(await screen.findByRole('button', { name: 'View game' }))
    await screen.findByRole('heading', { name: 'A real API title' })
    expect(screen.getByRole('button', { name: 'Metadata', hidden: true })).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Theme Studio' }))
    fireEvent.click(await screen.findByRole('button', { name: /Afterglow.*Cinematic artwork/i }))
    await waitFor(() => expect(document.querySelector('.afterglow-shell.fullscreen')).not.toBeNull())
    expect(document.documentElement.dataset.palette).toBe('afterglow')
    expect(document.querySelector('.winnow-portal-surface')).toBeNull()
  })

  it('renders real snapshots, retains separate desktop/fullscreen filters, and handles native fullscreen changes', async () => {
    mountAfterglow()
    await screen.findByRole('heading', { name: 'A real API title' })
    fireEvent.click(screen.getByRole('button', { name: 'Library' }))
    fireEvent.change(await screen.findByRole('textbox', { name: 'Search games' }), {
      target: { value: 'desktop filter' },
    })
    act(() => fullscreen(true))
    await screen.findByRole('heading', { name: 'What draws you in?' })
    fireEvent.click(screen.getByRole('button', { name: 'Library' }))
    expect(((await screen.findByRole('textbox', { name: 'Search games' })) as HTMLInputElement).value).toBe(
      '',
    )
    fireEvent.change(screen.getByRole('textbox', { name: 'Search games' }), {
      target: { value: 'fullscreen filter' },
    })
    act(() => fullscreen(false))
    await waitFor(() =>
      expect((screen.getByRole('textbox', { name: 'Search games' }) as HTMLInputElement).value).toBe(
        'desktop filter',
      ),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Journal' }))
    await screen.findByRole('heading', { name: 'A little history.' })
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }))
    await screen.findByRole('heading', { name: 'Make yourself at home.' })
  })
  it('switches composition through the same theme interface and retains navigation', async () => {
    mountAfterglow()
    await screen.findByRole('heading', { name: 'A real API title' })
    fireEvent.click(screen.getByRole('button', { name: 'Theme Studio' }))
    const catalogue = await screen.findByRole('button', { name: /Catalogue.*compact/i })
    fireEvent.click(catalogue)
    await waitFor(() => expect(document.querySelector('.catalogue-shell')).not.toBeNull())
    fireEvent.click(screen.getByRole('button', { name: 'Discover' }))
    await screen.findByRole('heading', { name: /Good things,\s*rediscovered/ })
    expect(window.winnow.savePreferences).toHaveBeenCalled()
  })
})
