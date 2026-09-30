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
import { clearViewState } from '../src/renderer/viewState'
import { mergeFixture } from './parity-merge-fixtures'

// Native startup tests exercise the worker; routing tests supply its completed-circuit signal.
vi.mock('../src/renderer/startup/LoadingDragon', () => ({
  LoadingDragon: ({ tracing, onFrame }: { tracing: boolean; onFrame(elapsed: number): void }) => {
    React.useEffect(() => {
      if (tracing) onFrame(1800)
    }, [tracing, onFrame])
    return <span data-testid="startup-trace" data-tracing={tracing} />
  },
}))

// Geometry and GPU lifetime have their own controlled-clock tests; this suite checks routes and data.
vi.mock('../src/renderer/components/portal-effects', () => ({
  PortalSurface: ({ children, onExpanded }: { children: React.ReactNode; onExpanded?: () => void }) => {
    React.useEffect(() => {
      onExpanded?.()
    }, [onExpanded])
    return <div className="winnow-portal-surface">{children}</div>
  },
}))
// jsdom has no viewport; native suites verify the real virtualizer and scroll geometry.
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
  clearViewState('setup:suspended')
  for (const mode of ['desktop', 'fullscreen']) {
    clearViewState(`${mode}:settings:tab`)
    clearViewState(`${mode}:plugins:installation`)
    clearViewState(`${mode}:plugins:installation-follow`)
    clearViewState(`${mode}:plugins:installed`)
    clearViewState(`${mode}:plugins:selected`)
  }
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
async function changeSurface(value: boolean) {
  act(() => fullscreen(value))
  await waitFor(() => expect(document.querySelector('.startup-presentation')).toBeNull())
}
function serveSnapshot(client: QueryClient, route: string) {
  const data = client.getQueryData(['api', route]),
    original = window.winnow.request
  window.winnow.request = vi.fn(async (value) =>
    value.route === route ? { ok: true, status: 200, data } : original(value),
  ) as WinnowBridge['request']
}
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
  it('desktop Merges is a dedicated screen with no rail count and Escape returns to Library even when its queue is empty', async () => {
    const original = window.winnow.request
    const review = mergeFixture()
    window.winnow.request = vi.fn(async (input) =>
      input.route === 'identity.get' ? { ok: true, status: 200, data: review } : original(input),
    ) as WinnowBridge['request']
    const client = mount()
    const merges = await screen.findByRole('button', { name: 'Merges' })
    expect(merges.textContent).toBe('Merges')
    expect(merges.title).toBe('Entries that might be one game, and what you have rolled up')
    expect(merges.querySelector('small')).toBeNull()
    fireEvent.click(merges)
    await screen.findByRole('article', { name: 'Bastion proposal' })
    expect(screen.getByRole('heading', { name: 'Merges', level: 1 })).toBeTruthy()
    expect(merges.getAttribute('aria-current')).toBe('page')
    expect(screen.queryByRole('navigation', { name: 'Library tools' })).toBeNull()
    act(() => client.setQueryData(['api', 'identity.get', undefined], { ...review, candidates: [] }))
    await screen.findByText('nothing waiting')
    expect(merges.textContent).toBe('Merges')
    expect(merges.style.opacity).toBe('')
    fireEvent.keyDown(screen.getByRole('combobox', { name: 'Sort proposals' }), { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Merges', level: 1 })).toBeNull())
    expect(
      within(screen.getByRole('navigation', { name: 'Main navigation' }))
        .getByRole('button', { name: 'Library' })
        .getAttribute('aria-current'),
    ).toBe('page')
    await changeSurface(true)
    expect(
      within(screen.getByRole('navigation', { name: 'Main navigation' })).queryByRole('button', {
        name: 'Merges',
      }),
    ).toBeNull()
  })

  it.each([false, true])(
    'initial fullscreen waits for saved motion preference %s before tracing',
    async (reduced) => {
      vi.mocked(window.winnow.isFullscreen).mockResolvedValue(true)
      let releasePreference!: () => void, releaseFeed!: () => void
      const preference = new Promise<void>((resolve) => {
          releasePreference = resolve
        }),
        feed = new Promise<void>((resolve) => {
          releaseFeed = resolve
        }),
        original = window.winnow.request
      window.winnow.request = vi.fn(async (input) => {
        if (input.route === 'preferences.presentation.get') {
          await preference
          return {
            ok: true,
            status: 200,
            data: [
              { preference: 'FullscreenReducedMotion', value: String(reduced) },
              { preference: 'FullscreenTextScale', value: '1.4' },
            ],
          }
        }
        if (input.route === 'feed.get') await feed
        return original(input)
      }) as WinnowBridge['request']
      mount()
      await screen.findByRole('dialog', { name: 'Preparing fullscreen' })
      await waitFor(() =>
        expect(document.querySelector('.startup-presentation')?.getAttribute('data-phase')).toBe('loading'),
      )
      expect(screen.getByTestId('startup-trace').getAttribute('data-tracing')).toBe('false')
      expect(screen.queryByRole('navigation', { name: 'Main navigation' })).toBeNull()
      await act(async () => releasePreference())
      await waitFor(() =>
        expect(screen.getByTestId('startup-trace').getAttribute('data-tracing')).toBe(String(!reduced)),
      )
      expect(document.documentElement.style.getPropertyValue('--fullscreen-text-scale')).toBe('1.4')
      expect(document.querySelector('.startup-presentation')?.getAttribute('data-phase')).toBe('loading')
      await act(async () => releaseFeed())
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Preparing fullscreen' })).toBeNull())
      expect(screen.queryByTestId('startup-trace')).toBeNull()
      expect(screen.getByRole('navigation', { name: 'Main navigation' })).not.toBeNull()
    },
  )
  it.each(['cancel', 'success', 'failure'] as const)(
    'fullscreen nested Hide %s retains the origin and dispatches at most one write',
    async (outcome) => {
      const original = vi.mocked(window.winnow.request).getMockImplementation()!
      vi.mocked(window.winnow.request).mockImplementation(async (input) =>
        input.route === 'hidden.put'
          ? outcome === 'failure'
            ? { ok: false, status: 503, message: 'Could not hide this game. Try again.' }
            : { ok: true, status: 200, data: {} }
          : original(input),
      )
      const client = mount()
      await screen.findByRole('navigation', { name: 'Main navigation' })
      await changeSurface(true)
      fireEvent.click(
        within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('button', {
          name: 'Library',
        }),
      )
      fireEvent.click(await screen.findByRole('button', { name: 'View A real API title' }))
      const origin = document.querySelector('.avalon-details.fullscreen')
      const more = await screen.findByRole('button', { name: 'More' })
      more.focus()
      fireEvent.click(more)
      fireEvent.click(await screen.findByRole('button', { name: 'Hide game…' }))
      const confirmation = await screen.findByRole('dialog', { name: 'Hide A real API title?' })
      expect(screen.getAllByRole('dialog')).toHaveLength(1)
      const cancel = within(confirmation).getByRole('button', { name: 'Cancel' })
      expect(document.activeElement).toBe(cancel)
      expect(document.querySelector('.avalon-details.fullscreen')).toBe(origin)
      expect(origin?.closest('[inert]')).not.toBeNull()
      fireEvent.click(
        outcome === 'cancel' ? cancel : within(confirmation).getByRole('button', { name: 'Hide game' }),
      )
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      if (outcome === 'success')
        await waitFor(() => expect(document.querySelector('.avalon-details.fullscreen')).toBeNull())
      else {
        expect(document.querySelector('.avalon-details.fullscreen')).toBe(origin)
        await waitFor(() => expect(document.activeElement).toBe(more))
        if (outcome === 'failure')
          expect((await screen.findByRole('alert')).textContent).toContain(
            'Could not hide this game. Try again.',
          )
      }
      expect(
        vi.mocked(window.winnow.request).mock.calls.filter(([input]) => input.route === 'hidden.put'),
      ).toHaveLength(outcome === 'cancel' ? 0 : 1)
      client.clear()
    },
  )
  it('fullscreen root Back opens Quick menu and right-click dismisses an action panel exactly once', async () => {
    vi.stubGlobal('PointerEvent', MouseEvent)
    const client = mount()
    await screen.findByRole('navigation', { name: 'Main navigation' })
    await changeSurface(true)
    fireEvent.click(
      within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('button', {
        name: 'Library',
      }),
    )
    const trigger = await screen.findByRole('button', { name: 'More' })
    trigger.focus()
    fireEvent.click(trigger)
    const panel = await screen.findByRole('dialog', { name: 'Library options' })
    expect(document.querySelector('#main-content')?.hasAttribute('inert')).toBe(true)
    fireEvent.pointerDown(within(panel).getByRole('button', { name: 'My lists' }), { button: 2 })
    fireEvent.contextMenu(document.body, { button: 2 })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(document.querySelector('#main-content')?.hasAttribute('inert')).toBe(false)
    expect(window.winnow.setFullscreen).not.toHaveBeenCalled()
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    const quick = await screen.findByRole('dialog', { name: 'Quick menu' })
    expect(within(quick).getByRole('button', { name: 'Resume' })).toBeTruthy()
    expect(within(quick).getByRole('button', { name: 'Settings' })).toBeTruthy()
    expect(window.winnow.setFullscreen).not.toHaveBeenCalled()
    client.clear()
  })
  it('fullscreen right-click lets a Details reading page handle Back before closing the game', async () => {
    vi.stubGlobal('PointerEvent', MouseEvent)
    const client = mount()
    await screen.findByRole('navigation', { name: 'Main navigation' })
    await changeSurface(true)
    fireEvent.click(
      within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('button', {
        name: 'Library',
      }),
    )
    fireEvent.click(await screen.findByRole('button', { name: 'View A real API title' }))
    const read = await screen.findByRole('button', { name: 'Read more →' })
    fireEvent.click(read)
    const back = await screen.findByRole('button', { name: 'Back to Overview' })
    back.focus()
    fireEvent.pointerDown(back, { button: 2 })
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Back to Overview' })).toBeNull())
    expect(document.querySelector('.avalon-details.fullscreen')).not.toBeNull()
    const returnedRead = screen.getByRole('button', { name: 'Read more →' })
    expect(document.activeElement).toBe(returnedRead)
    fireEvent.pointerDown(returnedRead, { button: 2 })
    await waitFor(() => expect(document.querySelector('.avalon-details')).toBeNull())
    expect(window.winnow.setFullscreen).not.toHaveBeenCalled()
    client.clear()
  })
  it.each(['desktop', 'fullscreen'] as const)(
    'keeps grouped Prey selection totals secondary updates and the installed Epic action together on %s',
    async (mode) => {
      for (const key of [
        'bucket',
        'query',
        'store',
        'list',
        'rules',
        'tools',
        'sort',
        'selection',
        'selected',
      ])
        clearViewState(`avalon:library:${mode}:${key}`)
      const original = window.winnow.request
      const entries = [
        {
          ...game.entries[0],
          title: 'Prey',
          store: 'steam',
          installed: false,
          playtimeMinutes: 300,
          lastPlayedAt: '2023-09-01T12:00:00Z',
        },
        {
          ...game.entries[0],
          ownershipId: 2,
          workId: 2,
          releaseId: 2,
          title: 'Prey Deluxe',
          store: 'epic',
          installed: true,
          playtimeMinutes: 90,
          lastPlayedAt: '2023-09-01T12:00:00Z',
        },
      ]
      const prey = {
        ...game,
        title: 'Prey',
        playtimeMinutes: 390,
        lastPlayedAt: entries[0].lastPlayedAt,
        bucket: 'stale_but_patched',
        entries,
      }
      const dishonored = {
        ...game,
        workId: 3,
        title: 'Dishonored',
        entries: [{ ...game.entries[0], workId: 3, releaseId: 3, ownershipId: 3 }],
      }
      window.winnow.request = vi.fn(async (input) => {
        const data =
          input.route === 'library.get'
            ? { games: [prey, dishonored], lists: [] }
            : input.route === 'library.workspace'
              ? {
                  works: [
                    { id: 1, name: 'Prey' },
                    { id: 2, name: 'Prey Deluxe' },
                    { id: 3, name: 'Dishonored' },
                  ],
                  externalIds: [
                    { releaseId: 1, provider: 'steam', providerId: '480' },
                    { releaseId: 2, provider: 'epic', providerId: 'Prey' },
                  ],
                  pluginActions: {},
                  epicLaunchKeys: { Prey: { namespace: 'prey', catalogItemId: 'prey', artifactId: 'Prey' } },
                  buckets: [{ workId: 1, ownershipId: 1, resolvedWorkId: 1, game: { unreadUpdateCount: 1 } }],
                }
              : input.route === 'game.details'
                ? {
                    workId: 1,
                    ownerships: entries,
                    sessions: {},
                    ratings: [],
                    journalEntries: [],
                    achievements: [],
                    events: [
                      {
                        id: 1,
                        releaseId: 2,
                        kind: 'build_push',
                        occurredAt: '2026-07-01T12:00:00Z',
                        buildId: 'epic-update',
                      },
                      {
                        id: 2,
                        releaseId: 2,
                        kind: 'announcement',
                        occurredAt: '2026-07-02T12:00:00Z',
                        title: 'Epic update notes',
                        url: 'https://example.com/patch',
                      },
                    ],
                  }
                : input.route === 'preferences.presentation.get'
                  ? [{ preference: 'DefaultSort', value: 'NameAscending' }]
                  : input.route === 'actions.execute'
                    ? 0
                    : undefined
        return data === undefined ? original(input) : { ok: true, status: 200, data }
      }) as WinnowBridge['request']
      const client = mount()
      await screen.findByRole('navigation', { name: 'Main navigation' })
      await changeSurface(mode === 'fullscreen')
      fireEvent.click(
        within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('button', {
          name: 'Library',
        }),
      )
      const first = await screen.findByRole('button', { name: 'View Dishonored' })
      act(() => first.focus())
      expect(first.getAttribute('data-selected')).toBe('true')
      fireEvent.keyDown(first, { key: 'ArrowRight' })
      const grouped = await screen.findByRole('button', {
        name: 'View Prey, patched since you played: 1 update. Owned on Steam, Epic',
      })
      await waitFor(() => expect(grouped.getAttribute('data-selected')).toBe('true'))
      fireEvent.keyDown(grouped, { key: 'ArrowRight' })
      expect(grouped.getAttribute('data-selected')).toBe('true')
      expect(grouped.querySelector('.avalon-unread')).not.toBeNull()
      expect(grouped.textContent).toContain(mode === 'desktop' ? '6h' : '6h played')
      fireEvent.click(grouped)
      const heading = await screen.findByRole('heading', { name: 'Prey', level: 1 })
      const details = within(heading.closest('.avalon-details') as HTMLElement)
      expect(details.getByText('6h', { exact: true })).toBeTruthy()
      expect(details.getByText('Steam · Epic Games · Installed')).toBeTruthy()
      fireEvent.click(details.getByRole('button', { name: 'Play' }))
      await waitFor(() =>
        expect(
          vi
            .mocked(window.winnow.request)
            .mock.calls.some(
              ([input]) => input.route === 'actions.execute' && input.params?.ownershipId === 2,
            ),
        ).toBe(true),
      )
      expect(
        vi
          .mocked(window.winnow.request)
          .mock.calls.some(([input]) => input.route === 'actions.execute' && input.params?.ownershipId === 1),
      ).toBe(false)
      fireEvent.click(details.getByRole('tab', { name: /Updates/ }))
      expect(await details.findByRole('heading', { name: 'Epic update notes' })).toBeTruthy()
      expect(details.getByRole('heading', { name: 'Build epic-update' })).toBeTruthy()
      fireEvent.click(details.getByRole('tab', { name: 'Library' }))
      const copies = document.querySelectorAll(
        '.avalon-details .entry-actions:not(:has([data-controller-play]))',
      )
      expect(copies).toHaveLength(2)
      expect([...copies].map((copy) => copy.querySelector('strong')?.textContent)).toEqual([
        'Steam',
        'Epic Games',
      ])
      fireEvent.click(
        screen.getByRole('button', {
          name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
        }),
      )
      await waitFor(() => expect(document.querySelector('.avalon-details')).toBeNull())
      expect(grouped.getAttribute('data-selected')).toBe('true')
      client.clear()
    },
  )
  it.each(['desktop', 'fullscreen'] as const)(
    'opens the original Steam 37-hour Details fixture with its gap and retains the same Library selection on %s',
    async (mode) => {
      for (const key of ['bucket', 'query', 'store', 'list', 'rules', 'tools'])
        clearViewState(`avalon:library:${mode}:${key}`)
      const original = window.winnow.request
      const recorded = {
        ...game,
        title: 'Empyrion',
        playtimeMinutes: 2220,
        lastPlayedAt: '2017-01-02T08:00:00Z',
        bucket: 'retired',
        entries: [
          {
            ...game.entries[0],
            title: 'Empyrion',
            store: 'steam',
            playtimeMinutes: 2220,
            lastPlayedAt: '2017-01-02T08:00:00Z',
          },
        ],
      }
      window.winnow.request = vi.fn(async (input) =>
        input.route === 'library.get'
          ? { ok: true, status: 200, data: { games: [recorded], lists: [] } }
          : original(input),
      ) as WinnowBridge['request']
      const client = mount()
      await screen.findByRole('navigation', { name: 'Main navigation' })
      await changeSurface(mode === 'fullscreen')
      fireEvent.click(
        within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('button', {
          name: 'Library',
        }),
      )
      const card = await screen.findByRole('button', { name: 'View Empyrion' })
      fireEvent.click(card)
      const heading = await screen.findByRole('heading', { name: 'Empyrion' })
      const details = within(heading.closest('.avalon-details') as HTMLElement)
      expect(details.getByText('37h', { exact: true })).toBeTruthy()
      expect(details.getByText('since last played')).toBeTruthy()
      expect(details.getByText('Steam · Not installed')).toBeTruthy()
      fireEvent.click(
        screen.getByRole('button', {
          name: mode === 'desktop' ? 'Close game details' : 'B · Back to Library',
        }),
      )
      await waitFor(() => expect(document.querySelector('.avalon-details')).toBeNull())
      expect(screen.getByRole('button', { name: 'View Empyrion' }).getAttribute('data-selected')).toBe('true')
      client.clear()
    },
  )
  it.each(['desktop', 'fullscreen'] as const)(
    'filters the Derelict collection and carries its original Offline IGDB evidence into Details on %s',
    async (mode) => {
      for (const key of ['bucket', 'query', 'store', 'list', 'rules', 'tools'])
        clearViewState(`avalon:library:${mode}:${key}`)
      const original = window.winnow.request
      const closed = { ...game, title: 'Closed world', bucket: 'derelict' }
      const waiting = {
        ...game,
        workId: 2,
        title: 'Still waiting',
        entries: [{ ...game.entries[0], workId: 2, ownershipId: 2, releaseId: 2 }],
      }
      window.winnow.request = vi.fn(async (input) =>
        input.route === 'library.get'
          ? { ok: true, status: 200, data: { games: [closed, waiting], lists: [] } }
          : input.route === 'library.workspace'
            ? {
                ok: true,
                status: 200,
                data: {
                  works: [
                    { id: 1, name: closed.title },
                    { id: 2, name: waiting.title },
                  ],
                  externalIds: [],
                  pluginActions: {},
                  epicLaunchKeys: {},
                  buckets: [
                    {
                      ownershipId: 1,
                      resolvedWorkId: 1,
                      game: {
                        lifecycle: {
                          status: 5,
                          confidence: 0.9,
                          reason: 'IGDB reports the game is offline.',
                        },
                      },
                    },
                  ],
                },
              }
            : original(input),
      ) as WinnowBridge['request']
      const client = mount()
      await screen.findByRole('navigation', { name: 'Main navigation' })
      await changeSurface(mode === 'fullscreen')
      fireEvent.click(
        within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('button', {
          name: 'Library',
        }),
      )
      if (mode === 'desktop') fireEvent.click(await screen.findByRole('button', { name: 'Derelict1' }))
      else {
        fireEvent.click(await screen.findByRole('button', { name: 'Filter & sort' }))
        const panel = within(screen.getByRole('dialog', { name: 'Library filters' }))
        fireEvent.change(panel.getByLabelText('Collection'), { target: { value: 'derelict' } })
        fireEvent.click(panel.getByRole('button', { name: 'Apply filters' }))
      }
      expect(screen.queryByRole('button', { name: 'View Still waiting' })).toBeNull()
      fireEvent.click(screen.getByRole('button', { name: 'View Closed world' }))
      await screen.findByRole('heading', { name: 'Closed world' })
      expect(screen.getByText('Offline · 90% confidence. IGDB reports the game is offline.')).toBeTruthy()
      client.clear()
    },
  )
  it.each(['desktop', 'fullscreen'])(
    'Home and Library have one active destination, retain bucket context, and Settings returns to its section on %s',
    async (mode) => {
      for (const key of ['bucket', 'query', 'store', 'list', 'rules'])
        clearViewState(`avalon:library:${mode}:${key}`)
      clearViewState(`${mode}:settings:tab`)
      const client = mount()
      await screen.findByRole('navigation', { name: 'Main navigation' })
      await changeSurface(mode === 'fullscreen')
      const navigation = () => within(screen.getByRole('navigation', { name: 'Main navigation' }))
      expect(navigation().getByRole('button', { name: 'For you' }).getAttribute('aria-current')).toBe('page')
      if (mode === 'desktop') {
        expect(screen.getByRole('button', { name: 'All games1' }).getAttribute('aria-pressed')).toBe('false')
        fireEvent.click(screen.getByRole('button', { name: 'All games1' }))
      } else fireEvent.click(navigation().getByRole('button', { name: 'Library' }))
      await screen.findByRole('button', { name: `View ${game.title}` })
      expect(navigation().getByRole('button', { name: 'For you' }).getAttribute('aria-current')).toBeNull()
      expect(screen.getByRole('button', { name: 'All games1' }).getAttribute('aria-pressed')).toBe('true')
      fireEvent.click(screen.getByRole('button', { name: 'Never played1' }))
      for (let click = 0; click < 2; click++) {
        fireEvent.click(navigation().getByRole('button', { name: 'For you' }))
        expect(navigation().getByRole('button', { name: 'For you' }).getAttribute('aria-current')).toBe(
          'page',
        )
        expect(document.querySelector('.avalon-library')).toBeNull()
        if (mode === 'desktop')
          for (const label of ['All games1', 'Never played1'])
            expect(screen.getByRole('button', { name: label }).getAttribute('aria-pressed')).toBe('false')
      }
      fireEvent.click(navigation().getByRole('button', { name: 'Library' }))
      expect(screen.getByRole('button', { name: 'Never played1' }).getAttribute('aria-pressed')).toBe('true')
      fireEvent.click(screen.getByRole('button', { name: 'Settings' }))
      const sections = () => within(screen.getByRole('navigation', { name: 'Settings section' }))
      expect(
        sections()
          .getByRole('button', { name: mode === 'fullscreen' ? 'Appearance' : 'Platforms' })
          .getAttribute('aria-pressed'),
      ).toBe('true')
      for (let click = 0; click < 2; click++)
        fireEvent.click(sections().getByRole('button', { name: 'Appearance' }))
      expect(sections().getByRole('button', { name: 'Appearance' }).getAttribute('aria-pressed')).toBe('true')
      expect(sections().getByRole('button', { name: 'Platforms' }).getAttribute('aria-pressed')).toBe('false')
      if (mode === 'desktop') fireEvent.click(screen.getByRole('button', { name: 'All games1' }))
      else fireEvent.click(navigation().getByRole('button', { name: 'Library' }))
      expect(screen.queryByRole('navigation', { name: 'Settings section' })).toBeNull()
      fireEvent.click(screen.getByRole('button', { name: 'Settings' }))
      expect(sections().getByRole('button', { name: 'Appearance' }).getAttribute('aria-pressed')).toBe('true')
      client.clear()
      clearViewState(`${mode}:settings:tab`)
      for (const key of ['bucket', 'query', 'store', 'list', 'rules'])
        clearViewState(`avalon:library:${mode}:${key}`)
    },
  )
  it.each([
    ['fit', 'contain'],
    ['fill', 'cover'],
  ])('applies cover art %s on both surfaces', async (value, expected) => {
    const original = window.winnow.request
    window.winnow.request = vi.fn(async (input) =>
      input.route === 'preferences.presentation.get'
        ? { ok: true, status: 200, data: [{ preference: 'CoverArtMode', value }] }
        : original(input),
    ) as WinnowBridge['request']
    const client = mount()
    await waitFor(() =>
      expect(document.documentElement.style.getPropertyValue('--cover-art-fit')).toBe(expected),
    )
    await changeSurface(true)
    await waitFor(() => expect(document.documentElement.dataset.mode).toBe('fullscreen'))
    expect(document.documentElement.style.getPropertyValue('--cover-art-fit')).toBe(expected)
    client.clear()
  })
  it.each([
    [undefined, 'true'],
    ['yes please', 'true'],
    ['false', 'false'],
    [' False ', 'false'],
    ['TRUE', 'true'],
  ])('reads stored cover dimming %s consistently without writing the preference', async (value, expected) => {
    const original = window.winnow.request
    window.winnow.request = vi.fn(async (input) =>
      input.route === 'preferences.presentation.get'
        ? {
            ok: true,
            status: 200,
            data: value === undefined ? [] : [{ preference: 'DimDormantCovers', value }],
          }
        : original(input),
    ) as WinnowBridge['request']
    const client = mount()
    await screen.findByRole('navigation', { name: 'Main navigation' })
    await waitFor(() => expect(document.documentElement.dataset.dimDormant).toBe(expected))
    await changeSurface(true)
    await waitFor(() => expect(document.documentElement.dataset.mode).toBe('fullscreen'))
    expect(document.documentElement.dataset.dimDormant).toBe(expected)
    expect(
      vi
        .mocked(window.winnow.request)
        .mock.calls.some(([input]) => input.route === 'preferences.presentation.put'),
    ).toBe(false)
    client.clear()
  })
  it.each(['desktop', 'fullscreen'])(
    'opening Spending from navigation refreshes its capture and another navigation row leaves it on %s',
    async (mode) => {
      clearViewState(`${mode}:stats:section`)
      const original = window.winnow.request
      const reads = vi.fn(() => ({ hasAnything: false }))
      window.winnow.request = vi.fn(async (input) =>
        input.route === 'statistics.account' ? { ok: true, status: 200, data: reads() } : original(input),
      ) as WinnowBridge['request']
      const client = mount()
      await screen.findByRole('navigation', { name: 'Main navigation' })
      await changeSurface(mode === 'fullscreen')
      const navigation = () => within(screen.getByRole('navigation', { name: 'Main navigation' }))
      expect(reads).not.toHaveBeenCalled()
      fireEvent.click(navigation().getByRole('button', { name: 'Activity' }))
      fireEvent.click(await screen.findByRole('button', { name: 'Library summary' }))
      fireEvent.click(await screen.findByRole('button', { name: 'Spending' }))
      await screen.findByText(/No Steam spending has been captured/)
      expect(reads).toHaveBeenCalledTimes(1)
      fireEvent.click(navigation().getByRole('button', { name: 'Library' }))
      expect(screen.queryByRole('region', { name: 'Account spending' })).toBeNull()
      expect(await screen.findByRole('button', { name: `View ${game.title}` })).toBeTruthy()
      expect(reads).toHaveBeenCalledTimes(1)
      fireEvent.click(navigation().getByRole('button', { name: 'Activity' }))
      await screen.findByRole('region', { name: 'Account spending' })
      await waitFor(() => expect(reads).toHaveBeenCalledTimes(2))
      client.clear()
      clearViewState(`${mode}:stats:section`)
    },
  )
  it.each([false, true])(
    'a winning visibility snapshot closes excluded details while keeping the remaining game, fullscreen %s',
    async (fullscreenMode) => {
      const second = {
        ...game,
        workId: 2,
        title: 'Remaining game',
        entries: [{ ...game.entries[0], workId: 2, releaseId: 2, ownershipId: 2 }],
      }
      const original = window.winnow.request
      window.winnow.request = vi.fn(async (input) =>
        input.route === 'library.get'
          ? { ok: true, status: 200, data: { games: [game, second], lists: [] } }
          : original(input),
      ) as WinnowBridge['request']
      const client = mount()
      await screen.findByRole('navigation', { name: 'Main navigation' })
      await changeSurface(fullscreenMode)
      fireEvent.click(
        within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('button', {
          name: 'Library',
        }),
      )
      fireEvent.click(await screen.findByRole('button', { name: `View ${game.title}` }))
      await screen.findByRole('tab', { name: 'Overview' })
      act(() => client.setQueryData(['api', 'library.get'], { games: [second], lists: [] }))
      await waitFor(() => expect(document.querySelector('.avalon-details')).toBeNull())
      expect(await screen.findByRole('button', { name: 'View Remaining game' })).toBeTruthy()
      expect(screen.queryByRole('button', { name: `View ${game.title}` })).toBeNull()
      client.clear()
    },
  )
  it.each(
    [false, true].flatMap((fullscreenMode) =>
      ['close', 'new selection', 'hidden', 'new facts'].map((change) => [fullscreenMode, change] as const),
    ),
  )(
    'pending details honor current navigation and visibility, fullscreen %s change %s',
    async (fullscreenMode, change) => {
      let finish!: (value: unknown) => void
      const pending = new Promise((resolve) => {
        finish = resolve
      })
      const second = {
        ...game,
        workId: 2,
        title: 'Next selection',
        entries: [{ ...game.entries[0], workId: 2, releaseId: 2, ownershipId: 2 }],
      }
      const original = window.winnow.request
      window.winnow.cancelRequest = vi.fn(async () => true)
      window.winnow.request = vi.fn(async (input) =>
        input.route === 'library.get'
          ? { ok: true, status: 200, data: { games: [game, second], lists: [] } }
          : input.route === 'game.details' && input.params?.workId === 1
            ? pending
            : original(input),
      ) as WinnowBridge['request']
      const client = mount()
      await screen.findByRole('navigation', { name: 'Main navigation' })
      await changeSurface(fullscreenMode)
      fireEvent.click(
        within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('button', {
          name: 'Library',
        }),
      )
      fireEvent.click(await screen.findByRole('button', { name: `View ${game.title}` }))
      await waitFor(() =>
        expect(
          vi
            .mocked(window.winnow.request)
            .mock.calls.some(([input]) => input.route === 'game.details' && input.params?.workId === 1),
        ).toBe(true),
      )
      if (change === 'close' || change === 'new selection') {
        fireEvent.click(
          screen.getByRole('button', { name: fullscreenMode ? 'B · Back to Library' : 'Close game details' }),
        )
        await waitFor(() => expect(document.querySelector('.avalon-details')).toBeNull())
        expect(window.winnow.cancelRequest).toHaveBeenCalled()
        if (change === 'new selection') {
          fireEvent.click(await screen.findByRole('button', { name: 'View Next selection' }))
          await screen.findByRole('heading', { name: 'Next selection', level: 1 })
        }
      } else {
        act(() =>
          client.setQueryData(['api', 'library.get'], {
            games: change === 'hidden' ? [second] : [{ ...game, summary: 'Fresh fact' }, second],
            lists: [],
          }),
        )
        if (change === 'hidden')
          await waitFor(() => expect(document.querySelector('.avalon-details')).toBeNull())
        else await screen.findByText('Fresh fact')
      }
      await act(async () => {
        finish({
          ok: true,
          status: 200,
          data: { workId: 1, events: [], sessions: {}, ratings: [], journalEntries: [], achievements: [] },
        })
        await pending
      })
      if (change === 'close' || change === 'hidden')
        expect(document.querySelector('.avalon-details')).toBeNull()
      else if (change === 'new selection')
        expect(screen.getByRole('heading', { name: 'Next selection', level: 1 })).toBeTruthy()
      else expect(screen.getByText('Fresh fact')).toBeTruthy()
      if (change !== 'new facts')
        expect(client.getQueryData(['api', 'game.details', { workId: 1 }])).toBeUndefined()
      client.clear()
    },
  )
  it('keeps desktop search inline and opens fullscreen Search with its own query and return origin', async () => {
    for (const key of ['query', 'rows', 'selected', 'in-results'])
      clearViewState(`avalon:search:fullscreen:${key}`)
    mount()
    await waitFor(() => expect(document.querySelector('.avalon-shell.desktop')).not.toBeNull())
    await waitFor(() => expect(document.querySelector('.startup-presentation')).toBeNull())
    fireEvent.keyDown(window, { key: 'k', ctrlKey: true })
    const desktop = await screen.findByRole('textbox', { name: 'Search games' })
    fireEvent.change(desktop, { target: { value: 'desktop query' } })
    expect(screen.queryByRole('heading', { name: 'Search' })).toBeNull()
    await changeSurface(true)
    await waitFor(() => expect(document.querySelector('.avalon-shell.fullscreen')).not.toBeNull())
    fireEvent.keyDown(window, { key: 'k', ctrlKey: true })
    await screen.findByRole('heading', { name: 'Search' })
    const query = screen.getByRole('searchbox', { name: 'Search games' })
    expect((query as HTMLInputElement).value).toBe('')
    fireEvent.change(query, { target: { value: 'real API' } })
    fireEvent.click(screen.getByRole('button', { name: 'View A real API title' }))
    await screen.findByRole('tab', { name: 'Overview' })
    fireEvent.click(screen.getByRole('button', { name: 'B · Back to Search' }))
    expect(((await screen.findByRole('searchbox', { name: 'Search games' })) as HTMLInputElement).value).toBe(
      'real API',
    )
    fireEvent.keyDown(window, { key: 'Escape' })
    await waitFor(() => expect(document.querySelector('.screen-discover')).not.toBeNull())
    expect(window.winnow.setFullscreen).not.toHaveBeenCalled()
    await changeSurface(false)
    await waitFor(() =>
      expect((screen.getByRole('textbox', { name: 'Search games' }) as HTMLInputElement).value).toBe(
        'desktop query',
      ),
    )
  })
  it('keeps fullscreen Ctrl+K on Library for themes without a Search composition', async () => {
    mountAfterglow()
    await waitFor(() => expect(document.querySelector('.afterglow-shell.desktop')).not.toBeNull())
    await changeSurface(true)
    fireEvent.keyDown(window, { key: 'k', ctrlKey: true })
    await screen.findByRole('textbox', { name: 'Search games' })
    expect(document.querySelector('.screen-library')).not.toBeNull()
    expect(screen.queryByRole('heading', { name: 'Search' })).toBeNull()
  })
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
    await changeSurface(true)
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
  it.each(['desktop', 'fullscreen'] as const)(
    '%s waits for saved setup progress, suspends it for an install handoff and resumes the same step',
    async (mode) => {
      vi.mocked(window.winnow.isFullscreen).mockResolvedValue(mode === 'fullscreen')
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
      mount()
      await screen.findByRole('dialog', {
        name: mode === 'fullscreen' ? 'Preparing fullscreen' : 'Preparing your library',
      })
      expect(screen.queryByRole('region', { name: 'Plugin installation' })).toBeNull()
      await act(async () => {
        complete({ step: 4 })
      })
      await screen.findByRole('region', { name: 'Plugin installation' })
      expect(screen.queryByRole('heading', { name: 'Your GOG library' })).toBeNull()
      expect(
        vi.mocked(window.winnow.request).mock.calls.some(([value]) => value.route === 'operations.plugin'),
      ).toBe(true)
      expect(vi.mocked(window.winnow.request).mock.calls.some(([value]) => value.route === 'setup.put')).toBe(
        false,
      )
      fireEvent.click(screen.getByRole('button', { name: 'Resume setup' }))
      await screen.findByRole('heading', { name: 'Your GOG library' })
      expect(document.querySelector(`.setup-dialog.mode-${mode}`)).not.toBeNull()
      expect(vi.mocked(window.winnow.request).mock.calls.some(([value]) => value.route === 'setup.put')).toBe(
        false,
      )
    },
  )
  it('delivers startup activations before a newer event received during the pending handshake', async () => {
    const original = window.winnow.request
    let finish!: (value: unknown) => void
    const installing = new Promise((resolve) => {
      finish = resolve
    })
    window.winnow.request = vi.fn(async (value) =>
      value.route === 'operations.plugin'
        ? { ok: true, status: 200, data: await installing }
        : original(value),
    ) as WinnowBridge['request']
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
    await screen.findByRole('region', { name: 'Plugin installation' })
    expect(window.winnow.setFullscreen).not.toHaveBeenCalled()
    await act(async () => finish({ state: 'failed' }))
    await waitFor(() => expect(window.winnow.setFullscreen).toHaveBeenCalledWith(true))
  })
  it.each(['desktop', 'fullscreen'] as const)(
    '%s waits for startup and sends an extended ownership ID without using rounded snapshot entries',
    async (mode) => {
      vi.mocked(window.winnow.isFullscreen).mockResolvedValue(mode === 'fullscreen')
      let complete!: (value: unknown) => void
      const setup = new Promise((resolve) => {
        complete = resolve
      })
      const original = window.winnow.request
      window.winnow.request = vi.fn(async (value) =>
        value.route === 'setup.get'
          ? { ok: true, status: 200, data: await setup }
          : value.route === 'actions.execute'
            ? { ok: true, status: 200, data: 0 }
            : original(value),
      ) as WinnowBridge['request']
      window.winnow.takeActivations = async () => [{ kind: 'game', ownershipId: '9223372036854775807' }]
      mount()
      await screen.findByRole('dialog', {
        name: mode === 'fullscreen' ? 'Preparing fullscreen' : 'Preparing your library',
      })
      expect(
        vi.mocked(window.winnow.request).mock.calls.filter(([value]) => value.route === 'actions.execute'),
      ).toHaveLength(0)
      await act(async () => complete({ step: null }))
      await waitFor(() =>
        expect(
          vi.mocked(window.winnow.request).mock.calls.filter(([value]) => value.route === 'actions.execute'),
        ).toHaveLength(1),
      )
      const request = vi
        .mocked(window.winnow.request)
        .mock.calls.find(([value]) => value.route === 'actions.execute')![0]
      expect(request.params).toEqual({ ownershipId: '9223372036854775807' })
      expect(request.body).toEqual({ operationId: expect.any(String), action: 'Primary' })
    },
  )
  it.each(['desktop', 'fullscreen'] as const)(
    '%s Plugin_handoff_shows_installation_over_a_fresh_optional_setup',
    async (mode) => {
      vi.mocked(window.winnow.isFullscreen).mockResolvedValue(mode === 'fullscreen')
      const original = window.winnow.request
      window.winnow.request = vi.fn(async (value) =>
        value.route === 'setup.get' ? { ok: true, status: 200, data: { step: 0 } } : original(value),
      ) as WinnowBridge['request']
      let receive!: (value: ApplicationActivation) => void
      window.winnow.onActivation = (callback) => {
        receive = callback
        return () => {}
      }
      mount()
      await screen.findByRole('heading', { name: 'Welcome to Winnow' })
      expect(document.querySelector('.startup-presentation')).toBeNull()
      expect(screen.queryByRole('navigation', { name: 'Main navigation' })).toBeNull()
      expect(document.querySelector('.prepared-surfaces')?.closest('[aria-hidden="true"]')).not.toBeNull()
      act(() => receive({ kind: 'plugin', pluginId: 'xbox', releaseTag: 'v1.2.3' }))
      await screen.findByRole('region', { name: 'Plugin installation' })
      expect(screen.queryByRole('heading', { name: 'Welcome to Winnow' })).toBeNull()
      fireEvent.click(screen.getByRole('button', { name: 'Resume setup' }))
      await screen.findByRole('heading', { name: 'Welcome to Winnow' })
      expect(document.querySelector('.plugin-installation-page')).toBeNull()
      expect(vi.mocked(window.winnow.request).mock.calls.some(([value]) => value.route === 'setup.put')).toBe(
        false,
      )
    },
  )
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
      await changeSurface(fullscreenMode)
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
      serveSnapshot(client, 'feed.get')
      await changeSurface(fullscreenMode)
      await waitFor(() =>
        expect(
          document.querySelector(`.avalon-shell.${fullscreenMode ? 'fullscreen' : 'desktop'}`),
        ).not.toBeNull(),
      )
      fireEvent.click(screen.getByRole('button', { name: 'For you' }))
      fireEvent.click(await screen.findByRole('button', { name: 'View A real API title' }))
      await screen.findByRole('heading', { name: 'A real API title' })
      await screen.findByRole('tab', { name: 'Overview' })
      expect(screen.getAllByRole('tab')).toHaveLength(fullscreenMode ? 4 : 5)
      fireEvent.click(screen.getByRole('button', { name: 'More' }))
      expect(screen.getByRole('button', { name: 'Artwork…' })).toBeDefined()
      expect(screen.getByRole('button', { name: 'Edit metadata…' })).toBeDefined()
      fireEvent.keyDown(screen.getByRole('button', { name: 'Wrong game?' }), { key: 'Escape' })
      fireEvent.click(
        screen.getByRole('button', { name: fullscreenMode ? 'B · Back to For you' : 'Close game details' }),
      )
      await waitFor(() => expect(document.querySelector('.avalon-details')).toBeNull())
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
    serveSnapshot(client, 'library.get')
    for (const fullscreenMode of [false, true]) {
      await changeSurface(fullscreenMode)
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
    await changeSurface(true)
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
    await changeSurface(true)
    await screen.findByRole('heading', { name: 'What draws you in?' })
    fireEvent.click(screen.getByRole('button', { name: 'Library' }))
    expect(((await screen.findByRole('textbox', { name: 'Search games' })) as HTMLInputElement).value).toBe(
      '',
    )
    fireEvent.change(screen.getByRole('textbox', { name: 'Search games' }), {
      target: { value: 'fullscreen filter' },
    })
    await changeSurface(false)
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
