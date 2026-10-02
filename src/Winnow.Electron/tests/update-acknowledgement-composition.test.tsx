// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useDetails, useLibrary } from '../src/renderer/api/hooks'
import type { GameDetails, LibraryGame, Mode, UpdateEvent } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeContext } from '../src/shared/theme'
import { avalon, AvalonLibrary, AvalonShell } from '../src/renderer/themes/avalon'
import { UpdateSignals } from '../src/renderer/features/parity-details'
import { ActivityTimeline } from '../src/renderer/features/activity-timeline'
import { updateFlagState } from '../src/renderer/features/update-flags'
import { clearViewState } from '../src/renderer/viewState'
import { libraryRole, returnToLibrary } from './library-controls'

vi.mock('../src/renderer/features/SettingsPreferences', () => ({
  usePresentationPreferences: () => ({ values: { DefaultSort: 'NameAscending' } }),
}))
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
    scrollToIndex: vi.fn(),
    scrollToOffset: vi.fn(),
  }),
}))

const at = (date: string) => `${date}T00:00:00Z`
const pair = (releaseId: number, id: number, push: string, notes: string): UpdateEvent[] => [
  { releaseId, id, kind: 'build_push', occurredAt: at(push), title: `Build ${id}` },
  { releaseId, id: id + 1, kind: 'announcement', occurredAt: at(notes), title: `Notes ${id + 1}` },
]
// Matches UpdateAcknowledgementCompositionTests.Seed and its inline multi-selection additions.
const originalEvents = [
  ...pair(1, 1, '2024-07-01', '2024-07-02'),
  ...pair(1, 3, '2025-01-01', '2025-01-02'),
  ...pair(1, 5, '2026-06-01', '2026-06-02'),
  ...pair(2, 7, '2026-06-01', '2026-06-02'),
  ...pair(2, 9, '2026-08-01', '2026-08-02'),
  ...pair(3, 11, '2026-06-01', '2026-06-02'),
]
const originalGames: LibraryGame[] = [
  {
    workId: 1,
    title: 'Grouped game',
    bucket: 'stale_but_patched',
    playtimeMinutes: 1200,
    lastPlayedAt: at('2025-01-01'),
    entries: [
      {
        ownershipId: 1,
        releaseId: 1,
        workId: 1,
        title: 'Steam copy',
        store: 'steam',
        installed: false,
        playtimeMinutes: 600,
        lastPlayedAt: at('2024-01-01'),
      },
      {
        ownershipId: 2,
        releaseId: 2,
        workId: 1,
        title: 'Epic copy',
        store: 'epic',
        installed: false,
        playtimeMinutes: 600,
        lastPlayedAt: at('2025-01-01'),
      },
    ],
  },
  {
    workId: 2,
    title: 'Never played',
    bucket: 'never_played',
    playtimeMinutes: 0,
    entries: [
      {
        ownershipId: 3,
        releaseId: 3,
        workId: 2,
        title: 'Never played',
        store: 'gog',
        installed: false,
        playtimeMinutes: 0,
      },
    ],
  },
]
const clients: QueryClient[] = []
beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
})
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  vi.unstubAllGlobals()
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of [
      'query',
      'bucket',
      'store',
      'list',
      'sort',
      'default-sort',
      'sort-before-list',
      'list-base',
      'view',
      'density',
      'tools',
      'rows',
      'viewport',
      'selected',
      'selection',
      'rules',
      'filter-order',
      'filters-open',
    ])
      clearViewState(`avalon:library:${mode}:${key}`)
    for (const key of ['ownership', 'tracked']) clearViewState(`${mode}:timeline:1:${key}`)
  }
  clearViewState('updates:1:sending')
})

function setup(mode: Mode, surface: 'library' | 'details' = 'library', multiple = false) {
  const seedGames = structuredClone(originalGames)
  let events = structuredClone(originalEvents)
  if (multiple)
    for (const id of [4, 5]) {
      seedGames.push({
        workId: id,
        title: id === 4 ? 'Selected game' : 'Unselected game',
        bucket: 'stale_but_patched',
        playtimeMinutes: 600,
        lastPlayedAt: at('2025-01-01'),
        entries: [
          {
            ownershipId: id,
            releaseId: id,
            workId: id,
            title: 'Copy',
            store: 'steam',
            installed: false,
            playtimeMinutes: 600,
            lastPlayedAt: at('2025-01-01'),
          },
        ],
      })
      events.push(...pair(id, id === 4 ? 13 : 15, '2026-08-01', '2026-08-02'))
    }
  const acknowledgements: Record<string, string> = {}
  const refused = new Set<number>()
  const failed = new Set<number>()
  const detailGate = { wait: undefined as Promise<void> | undefined }
  const gameEvents = (game: LibraryGame) =>
    events.filter((event) => game.entries.some((entry) => entry.releaseId === event.releaseId))
  const games = () =>
    seedGames.map((game) => ({
      ...game,
      bucket:
        game.workId === 2
          ? 'never_played'
          : updateFlagState(gameEvents(game), acknowledgements, game.lastPlayedAt, game.playtimeMinutes)
                .unread
            ? 'stale_but_patched'
            : 'retired',
    }))
  const workspace = () => ({
    works: [],
    externalIds: [],
    epicLaunchKeys: {},
    pluginActions: {},
    buckets: seedGames.flatMap((game) =>
      game.entries.map((entry) => ({
        releaseId: entry.releaseId,
        resolvedWorkId: game.workId,
        majorUpdateAt: events
          .filter((event) => event.releaseId === entry.releaseId && event.kind === 'build_push')
          .map((event) => event.occurredAt)
          .sort()
          .at(-1),
        game: {
          unreadUpdateCount: updateFlagState(
            gameEvents(game),
            acknowledgements,
            game.lastPlayedAt,
            game.playtimeMinutes,
          ).unread,
        },
      })),
    ),
  })
  const details = (workId: number): GameDetails => {
    const game = seedGames.find((value) => value.workId === workId)!
    return {
      workId,
      readAtUtc: at('2026-09-01'),
      events: gameEvents(game),
      acknowledgements: Object.fromEntries(
        Object.entries(acknowledgements).filter(([id]) =>
          game.entries.some((entry) => entry.releaseId === Number(id)),
        ),
      ),
      sessions: {},
      journalEntries: [],
      ratings: [],
      achievements: [],
    }
  }
  const request = vi.fn(async (input: ApiRequest) => {
    let data: unknown = {}
    if (input.route === 'library.get') data = { games: games(), lists: [] }
    else if (input.route === 'library.workspace') data = workspace()
    else if (input.route === 'game.details') {
      await detailGate.wait
      data = details(Number(input.params!.workId))
    } else if (input.route === 'updates.acknowledge') {
      const releaseId = Number(input.params!.releaseId)
      if (failed.has(releaseId)) throw new Error('Persistence refused')
      if (refused.has(releaseId)) data = { result: 'NotStored' }
      else {
        const ids = (input.body as { observedEventIds: number[] }).observedEventIds
        const through = events
          .filter(
            (event) => event.releaseId === releaseId && event.kind === 'build_push' && ids.includes(event.id),
          )
          .map((event) => event.occurredAt)
          .sort()
          .at(-1)!
        acknowledgements[releaseId] = through
        data = { result: 'Stored', acknowledgedThrough: through }
      }
    } else if (input.route === 'updates.restore') {
      delete acknowledgements[Number(input.params!.releaseId)]
      data = { result: 'Stored' }
    }
    return { ok: true, status: 200, data }
  })
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  clients.push(client)
  client.setQueryData(['api', 'library.get'], { games: games(), lists: [] })
  client.setQueryData(['api', 'library.workspace', undefined], workspace())
  client.setQueryData(['api', 'game.details', { workId: 1 }], details(1))
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  function Library() {
    const library = useLibrary()
    const ctx: ThemeContext = {
      mode,
      page: 'library',
      games: library.data?.games ?? [],
      loading: false,
      selectedWorkId: null,
      feed: undefined,
      profile: selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon),
      children: null,
      setPage: vi.fn(),
      openGame: vi.fn(),
      toggleFullscreen: vi.fn(),
      renderScreen: () => null,
      actions: { launch: vi.fn() },
      components: {
        Artwork: () => <span />,
        Impression: ({ children }) => <>{children}</>,
        GameCard: () => null,
        GamePreview: () => null,
        ArtworkEffects: ({ children }) => <>{children}</>,
      },
    }
    return (
      <AvalonShell {...ctx}>
        <AvalonLibrary {...ctx} />
      </AvalonShell>
    )
  }
  function Details() {
    const current = useDetails(1).data!
    return (
      <>
        <UpdateSignals details={current} game={seedGames[0]} />
        <ActivityTimeline details={current} game={seedGames[0]} mode={mode} />
      </>
    )
  }
  const view = render(
    <QueryClientProvider client={client}>
      {surface === 'library' ? <Library /> : <Details />}
    </QueryClientProvider>,
  )
  return {
    ...view,
    client,
    request,
    invalidate,
    acknowledgements,
    refused,
    failed,
    detailGate,
    writes: () =>
      request.mock.calls.map(([input]) => input).filter((input) => input.route.startsWith('updates.')),
    refresh: () =>
      act(async () => {
        await client.invalidateQueries({ queryKey: ['api'] })
      }),
    push: (releaseId = 1) => {
      const id = Math.max(...events.map((event) => event.id)) + 1
      events = [...events, ...pair(releaseId, id, '2026-09-01', '2026-09-02')]
      return id
    },
  }
}
const card = (id: number) => document.querySelector<HTMLButtonElement>(`[data-avalon-game="${id}"]`)!
const visibleIds = () =>
  [...document.querySelectorAll<HTMLElement>('[data-avalon-game]')].map((element) =>
    Number(element.dataset.avalonGame),
  )
function patched() {
  fireEvent.click(libraryRole('button', { name: /^Patched,/ }))
  returnToLibrary()
}
function choose(...ids: number[]) {
  returnToLibrary()
  for (const id of ids) fireEvent.click(card(id), { ctrlKey: true })
}
function action() {
  return libraryRole('button', { name: 'Mark as read' }) as HTMLButtonElement
}

describe.each(['desktop', 'fullscreen'] as const)(
  'original update acknowledgement composition in %s',
  (mode) => {
    it('exposes Mark as read through the selected-game action route only in Patched', async () => {
      setup(mode)
      fireEvent.contextMenu(card(1))
      expect(screen.queryByRole('button', { name: 'Mark as read' })).toBeNull()
      returnToLibrary()
      patched()
      if (mode === 'desktop') {
        fireEvent.click(screen.getByRole('button', { name: 'Clear selection' }))
        expect(screen.queryByRole('button', { name: 'Mark as read' })).toBeNull()
      }
      fireEvent.contextMenu(card(1))
      expect(action().disabled).toBe(false)
      if (mode === 'fullscreen') expect(screen.getByRole('button', { name: 'Back to library' })).toBeDefined()
      fireEvent.click(action())
      await waitFor(() => expect(visibleIds()).toEqual([]))
      expect(screen.queryByRole('button', { name: 'Mark as read' })).toBeNull()
      returnToLibrary()
      fireEvent.click(libraryRole('button', { name: /^All games/ }))
      fireEvent.contextMenu(card(1))
      expect(screen.queryByRole('button', { name: 'Mark as read' })).toBeNull()
    })

    it('acknowledges only selected grouped and multiple games, keeps the unselected game and flags later patches', async () => {
      const view = setup(mode, 'library', true)
      patched()
      choose(1, 4)
      // Right-clicking a member preserves the whole selection.
      fireEvent.contextMenu(card(1))
      fireEvent.click(action())
      await waitFor(() => expect(view.writes().map((write) => write.params!.releaseId)).toEqual([1, 2, 4]))
      await waitFor(() => {
        returnToLibrary()
        expect(visibleIds()).toEqual([5])
      })
      expect(view.acknowledgements).toEqual({ 1: at('2026-06-01'), 2: at('2026-08-01'), 4: at('2026-08-01') })
      expect(view.invalidate).toHaveBeenCalledWith({ queryKey: ['api'] })
      view.push()
      await view.refresh()
      await waitFor(() => expect(visibleIds()).toEqual([1, 5]))
      expect(card(1).getAttribute('aria-label')).toContain('1 update')
    })

    it('freezes every selected watermark before waiting and sends a repeated same-turn action only once', async () => {
      const view = setup(mode, 'library', true)
      patched()
      choose(1, 4)
      let release!: () => void
      view.detailGate.wait = new Promise<void>((resolve) => {
        release = resolve
      })
      const button = action()
      act(() => {
        button.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        button.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      })
      expect(view.request.mock.calls.filter(([input]) => input.route === 'game.details')).toHaveLength(1)
      const laterGroup = view.push()
      const laterSelected = view.push(4)
      // A workspace refresh can arrive before the first acknowledgement is sent.
      await act(async () => {
        await view.client.invalidateQueries({ queryKey: ['api', 'library.workspace'] })
      })
      await act(async () => {
        release()
      })
      await waitFor(() => expect(view.writes()).toHaveLength(3))
      await waitFor(() => expect(button.disabled).toBe(false))
      expect(view.acknowledgements).toEqual({ 1: at('2026-06-01'), 2: at('2026-08-01'), 4: at('2026-08-01') })
      const observed = view
        .writes()
        .flatMap((write) => (write.body as { observedEventIds: number[] }).observedEventIds)
      expect(observed).not.toContain(laterGroup)
      expect(observed).not.toContain(laterSelected)
      returnToLibrary()
      expect(card(1).getAttribute('aria-label')).toContain('1 update')
      expect(card(4).getAttribute('aria-label')).toContain('1 update')
    })

    it('keeps a refused grouped selection unread and retries only the release left after partial success', async () => {
      const view = setup(mode)
      view.refused.add(1)
      view.failed.add(2)
      patched()
      choose(1)
      fireEvent.click(action())
      await waitFor(() =>
        expect(screen.getByRole('alert').textContent).toContain("Couldn't mark every patch read. Try again."),
      )
      const alert = screen.getByRole('alert')
      expect(alert.classList.contains('avalon-selection-error')).toBe(true)
      expect(Boolean(alert.closest('.avalon-actions-body'))).toBe(mode === 'fullscreen')
      expect(view.acknowledgements).toEqual({})
      returnToLibrary()
      expect(card(1).getAttribute('aria-label')).toContain('2 updates')
      view.refused.clear()
      fireEvent.click(action())
      await waitFor(() => expect(view.writes()).toHaveLength(4))
      await waitFor(() => expect(action().disabled).toBe(false))
      expect(view.acknowledgements).toEqual({ 1: at('2026-06-01') })
      view.failed.clear()
      fireEvent.click(action())
      await waitFor(() => expect(view.writes()).toHaveLength(5))
      expect(view.writes().map((write) => write.params!.releaseId)).toEqual([1, 2, 1, 2, 2])
      await waitFor(() => {
        returnToLibrary()
        expect(visibleIds()).toEqual([])
      })
    })

    it('Details marks and restores only the displayed group and updates its timeline from confirmed watermarks', async () => {
      const view = setup(mode, 'details')
      expect(document.querySelector('.update-gap-caption')!.textContent).toBe(
        '2 updates landed while you were away.',
      )
      expect(document.querySelectorAll('.update-row[data-unread="true"]')).toHaveLength(6)
      const historyCount = () => document.querySelector('.activity-update-summary')!.textContent
      expect(historyCount()).toBe('3 unread updates')
      fireEvent.change(screen.getByLabelText('Edition history'), { target: { value: '2' } })
      expect(historyCount()).toBe('2 unread updates')
      fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }))
      await waitFor(() => expect(screen.getByRole('button', { name: 'Show it again' })).toBeDefined())
      expect(view.acknowledgements).toEqual({ 1: at('2026-06-01'), 2: at('2026-08-01') })
      expect(document.querySelectorAll('.update-row[data-unread="true"]')).toHaveLength(0)
      expect(historyCount()).toBe('No unread updates')
      fireEvent.change(screen.getByLabelText('Edition history'), { target: { value: '1' } })
      expect(historyCount()).toBe('No unread updates')
      fireEvent.click(screen.getByRole('button', { name: 'Show it again' }))
      await waitFor(() => expect(screen.getByRole('button', { name: 'Mark as read' })).toBeDefined())
      expect(view.acknowledgements).toEqual({})
      expect(document.querySelectorAll('.update-row[data-unread="true"]')).toHaveLength(6)
      expect(historyCount()).toBe('3 unread updates')
      fireEvent.change(screen.getByLabelText('Edition history'), { target: { value: '2' } })
      expect(historyCount()).toBe('2 unread updates')
      expect(
        view
          .writes()
          .filter((write) => write.route === 'updates.restore')
          .map((write) => write.params!.releaseId)
          .sort(),
      ).toEqual([1, 2])
      expect(view.invalidate).toHaveBeenCalledWith({ queryKey: ['api'] })
    })

    it('Details acknowledges its displayed event batch and reads a later push on the next refresh', async () => {
      const view = setup(mode, 'details')
      const later = view.push()
      fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }))
      await waitFor(() => expect(view.writes()).toHaveLength(2))
      await waitFor(() =>
        expect(document.querySelector('.update-gap-caption')!.textContent).toBe(
          '1 update landed while you were away.',
        ),
      )
      expect(view.acknowledgements).toEqual({ 1: at('2026-06-01'), 2: at('2026-08-01') })
      expect(
        view.writes().flatMap((write) => (write.body as { observedEventIds: number[] }).observedEventIds),
      ).not.toContain(later)
      expect(document.querySelectorAll('.update-row[data-unread="true"]')).toHaveLength(2)
      expect(screen.getByRole('button', { name: 'Mark as read' })).toBeDefined()
    })
  },
)

it.each([
  [0, false, 0],
  [600, false, 3],
  [0, true, 1],
  [600, true, 1],
] as const)('exact play boundary: %i minutes, date %s, unread count %i', (minutes, hasDate, count) => {
  expect(
    updateFlagState(
      originalEvents.filter((event) => event.releaseId === 1),
      {},
      hasDate ? at('2025-01-01') : null,
      minutes,
    ).unread,
  ).toBe(count)
})
