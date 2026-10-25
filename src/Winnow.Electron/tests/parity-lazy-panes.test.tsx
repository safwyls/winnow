// @vitest-environment jsdom
import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { WinnowBridge } from '../src/shared/bridge'
import { App } from '../src/renderer/App'
import { clearViewState } from '../src/renderer/viewState'
import { mergeFixture } from './parity-merge-fixtures'

configure({ asyncUtilTimeout: 5000 })

// Routes retain the startup gates; their clocks, worker and GPU effects have native coverage.
vi.mock('../src/renderer/startup/preparation', async (importOriginal) => {
  const original = await importOriginal<typeof import('../src/renderer/startup/preparation')>()
  return {
    ...original,
    createBrowserPreparationClock: () => {
      let elapsed = 0
      return {
        hidden: () => false,
        frame: async (signal: AbortSignal) => {
          await new Promise<void>((resolve) => setTimeout(resolve, 0))
          signal.throwIfAborted()
          return (elapsed += 200)
        },
      }
    },
  }
})
vi.mock('../src/renderer/startup/LoadingDragon', () => ({
  LoadingDragon: ({ tracing, onFrame }: { tracing: boolean; onFrame(elapsed: number): void }) => {
    React.useEffect(() => {
      if (tracing) onFrame(1800)
    }, [tracing, onFrame])
    return <span />
  },
}))
vi.mock('../src/renderer/components/portal-effects', () => ({
  PortalSurface: ({ children, onExpanded }: { children: React.ReactNode; onExpanded?(): void }) => {
    React.useEffect(() => onExpanded?.(), [onExpanded])
    return <div>{children}</div>
  },
}))
// jsdom has no layout; native route tests own actual pane bounds and cover virtualization.
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

const games = ['Stardew Valley', 'A different game'].map((title, index) => ({
  workId: index + 1,
  title,
  bucket: 'never_played',
  playtimeMinutes: 0,
  entries: [
    {
      ownershipId: index + 1,
      releaseId: index + 1,
      workId: index + 1,
      title,
      store: 'manual',
      installed: false,
      playtimeMinutes: 0,
    },
  ],
}))
let fullscreen: (value: boolean) => void = () => {}
let client: QueryClient
let preferences: Record<string, string>
beforeEach(() => {
  clearViewState('setup:suspended')
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of ['settings:tab', 'library-tools:tab', 'library:search', 'library:list'])
      clearViewState(`${mode}:${key}`)
  }
  preferences = {}
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
    request: vi.fn(async ({ route, params, body }) => {
      let data: unknown = []
      if (route === 'library.get') data = { games, lists: [] }
      else if (route === 'library.workspace')
        data = {
          works: games.map((game) => ({ id: game.workId, name: game.title })),
          externalIds: [],
          pluginActions: {},
          epicLaunchKeys: {},
        }
      else if (route === 'feed.get') data = { shelves: [], candidateCount: 0, confidence: 0, failed: false }
      else if (route === 'artworkState') data = { current: null, revision: 'artwork' }
      else if (route === 'game.details')
        data = {
          workId: params?.workId,
          events: [],
          sessions: {},
          ratings: [],
          achievements: [],
          journalEntries: [
            {
              sessionId: Number(params?.workId),
              sessionAt: '2026-09-01T12:00:00Z',
              note:
                params?.workId === 1
                  ? 'Stardew journal from the selected work.'
                  : 'A different work’s journal.',
            },
          ],
        }
      else if (route === 'activity.query') data = { rows: [], next: null }
      else if (route === 'statistics.gameplay')
        data = {
          recordedSeconds: 0,
          gamesPlayedCount: 0,
          startedSessionCount: 0,
          periods: [],
          topGames: [],
        }
      else if (route === 'preferences.library.get')
        data = {
          showNonGameEntries: false,
          showExplicitContent: false,
          maturityCap: 'all',
        }
      else if (route === 'connections.get') data = { steam: { hasUsableCredential: false } }
      else if (route === 'connections.igdb.get') data = { clientId: '', hasSavedCredentials: false }
      else if (route === 'preferences.presentation.get')
        data = Object.entries(preferences).map(([preference, value]) => ({ preference, value }))
      else if (route === 'preferences.presentation.put') {
        preferences[String(params?.preference)] = (body as { value: string }).value
        data = { preference: params?.preference, value: (body as { value: string }).value }
      } else if (route === 'identity.get') data = mergeFixture()
      return { ok: true, status: 200, data }
    }),
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
  client?.clear()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

async function mount(mode: 'desktop' | 'fullscreen') {
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <App />
    </QueryClientProvider>,
  )
  await waitFor(() => expect(document.querySelector('.startup-presentation')).toBeNull())
  if (mode === 'fullscreen') {
    act(() => fullscreen(true))
    await waitFor(() => expect(document.querySelector('.startup-presentation')).toBeNull())
  }
  navigate('Library')
  await screen.findByRole('button', { name: /^View Stardew Valley/ })
}
function navigate(name: string) {
  fireEvent.click(
    (name === 'Settings'
      ? screen
      : within(screen.getByRole('navigation', { name: 'Main navigation' }))
    ).getByRole('button', { name }),
  )
}
function section(name: string) {
  fireEvent.click(
    within(screen.getByRole('navigation', { name: 'Settings section' })).getByRole('button', { name }),
  )
}
function requested(route: string) {
  return vi.mocked(window.winnow.request).mock.calls.filter(([request]) => request.route === route)
}
function noDetailsOrLightbox() {
  expect(document.querySelector('.avalon-details')).toBeNull()
  expect(document.querySelector('.screenshot-dialog')).toBeNull()
}

describe.each(['desktop', 'fullscreen'] as const)('%s deferred panes', (mode) => {
  it('mounts only activated panes and restores Appearance state after returning from Library', async () => {
    await mount(mode)
    noDetailsOrLightbox()
    for (const selector of ['.merge-queue', '.account-statistics', '.platform-settings', '.settings-page'])
      expect(document.querySelector(selector)).toBeNull()
    for (const route of ['game.details', 'identity.get', 'statistics.account', 'connections.get'])
      expect(requested(route)).toHaveLength(0)

    if (mode === 'desktop') navigate('Merges')
    else {
      navigate('Settings')
      section('Library')
      fireEvent.click(screen.getByRole('button', { name: 'Library tools' }))
      fireEvent.click(screen.getByRole('button', { name: 'Identity review' }))
    }
    await waitFor(() => expect(document.querySelectorAll('.merge-queue')).toHaveLength(1))
    await screen.findByRole('heading', { name: 'Are these the same game?' })
    expect(requested('identity.get')).toHaveLength(1)
    expect(requested('statistics.account')).toHaveLength(0)
    expect(document.querySelector('.account-statistics')).toBeNull()
    noDetailsOrLightbox()

    if (mode === 'desktop') navigate('Settings')
    section('Appearance')
    expect(document.querySelectorAll('.settings-page')).toHaveLength(1)
    for (const selector of ['.merge-queue', '.account-statistics', '.platform-settings', '.library-tools'])
      expect(document.querySelector(selector)).toBeNull()
    expect(screen.queryByRole('heading', { name: 'Library visibility' })).toBeNull()
    const motion = screen.getByRole(mode === 'desktop' ? 'checkbox' : 'switch', { name: 'Reduce motion' })
    await waitFor(() => expect((motion as HTMLInputElement).disabled).toBe(false))
    fireEvent.click(motion)
    await waitFor(() => expect(preferences.FullscreenReducedMotion).toBe('true'))
    await waitFor(() => expect((motion as HTMLInputElement).disabled).toBe(false))
    expect(
      mode === 'desktop'
        ? (motion as HTMLInputElement).checked
        : motion.getAttribute('aria-checked') === 'true',
    ).toBe(true)

    navigate('Library')
    expect(document.querySelector('.settings-page')).toBeNull()
    expect(motion.isConnected).toBe(false)
    noDetailsOrLightbox()
    navigate('Settings')
    expect(
      within(screen.getByRole('navigation', { name: 'Settings section' }))
        .getByRole('button', {
          name: 'Appearance',
        })
        .getAttribute('aria-pressed'),
    ).toBe('true')
    const restored = screen.getByRole(mode === 'desktop' ? 'checkbox' : 'switch', { name: 'Reduce motion' })
    expect(
      mode === 'desktop'
        ? (restored as HTMLInputElement).checked
        : restored.getAttribute('aria-checked') === 'true',
    ).toBe(true)
    expect(document.querySelectorAll('.settings-page')).toHaveLength(1)
    for (const selector of ['.merge-queue', '.account-statistics', '.platform-settings', '.library-tools'])
      expect(document.querySelector(selector)).toBeNull()
    expect(requested('statistics.account')).toHaveLength(0)
  })

  it('constructs one detail pane with the selected Stardew Valley work and no stale data on the next game', async () => {
    await mount(mode)
    noDetailsOrLightbox()
    expect(requested('game.details')).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: /^View Stardew Valley/ }))
    await screen.findByRole('heading', { name: 'Stardew Valley' })
    await waitFor(() =>
      expect(requested('game.details').map(([request]) => request.params)).toEqual([{ workId: 1 }]),
    )
    expect(document.querySelectorAll('.avalon-details')).toHaveLength(1)
    fireEvent.click(screen.getByRole('tab', { name: 'Journal' }))
    await screen.findByText('Stardew journal from the selected work.')
    expect(screen.queryByText('A different work’s journal.')).toBeNull()
    fireEvent.click(
      screen.getByRole('button', { name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library' }),
    )
    await waitFor(noDetailsOrLightbox)

    fireEvent.click(screen.getByRole('button', { name: /^View A different game/ }))
    await screen.findByRole('heading', { name: 'A different game' })
    await waitFor(() =>
      expect(requested('game.details').map(([request]) => request.params)).toEqual([
        { workId: 1 },
        { workId: 2 },
      ]),
    )
    expect(document.querySelectorAll('.avalon-details')).toHaveLength(1)
    fireEvent.click(screen.getByRole('tab', { name: 'Journal' }))
    await screen.findByText('A different work’s journal.')
    expect(screen.queryByText('Stardew journal from the selected work.')).toBeNull()
    expect(document.querySelector('.screenshot-dialog')).toBeNull()
  })
})
