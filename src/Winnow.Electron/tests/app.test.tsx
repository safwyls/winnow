// @vitest-environment jsdom
import React from 'react'
import { beforeEach, afterEach, describe, it, expect, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { WinnowBridge } from '../src/shared/bridge'
import { App } from '../src/renderer/App'

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
                  works: [{ id: 1, title: game.title }],
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
describe('integrated frontend', () => {
  it('keeps the Discover portal mounted while cycling recommendations on both surfaces', async () => {
    const client = mount()
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
    mount()
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
    mount()
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
    mount()
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
