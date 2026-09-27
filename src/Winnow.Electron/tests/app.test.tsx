// @vitest-environment jsdom
import React from 'react'
import { beforeEach, afterEach, describe, it, expect, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { WinnowBridge } from '../src/shared/bridge'
import { App } from '../src/renderer/App'

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
  window.matchMedia = vi
    .fn()
    .mockImplementation((query) => ({
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
              ? { externalIds: [], pluginActions: {}, epicLaunchKeys: {} }
              : route === 'artworkState'
                ? { current: null, revision: 'a' }
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
