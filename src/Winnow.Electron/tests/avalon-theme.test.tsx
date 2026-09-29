// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ComponentType, ReactNode } from 'react'
import {
  DEFAULT_PROFILE,
  selectThemeProfile,
  resolvedThemeColors,
  validateThemeDefinition,
  type ThemeContext,
} from '../src/shared/theme'
import {
  avalon,
  AvalonCover,
  AvalonDiscover,
  AvalonLibrary,
  AvalonShell,
} from '../src/renderer/themes/avalon'
import { clearViewState, useViewState } from '../src/renderer/viewState'
import type { LibraryFilter, LibraryGame } from '../src/renderer/api/types'

const fixtures = vi.hoisted(() => ({ workspace: undefined as unknown }))
vi.mock('../src/renderer/api/hooks', () => ({
  useLibrary: () => ({ data: { lists: [] } }),
  useWorkspace: () => ({ data: fixtures.workspace }),
}))
vi.mock('../src/renderer/features/LibraryTools', () => ({
  LibraryTools: () => <div>Library management</div>,
  CreateListButton: () => <button>New list…</button>,
}))
vi.mock('../src/renderer/features/SettingsPreferences', () => ({
  usePresentationPreferences: () => ({ values: { DefaultSort: 'NameAscending' } }),
}))
vi.mock('@tanstack/react-virtual', () => ({
  useVirtualizer: (options: { count: number; estimateSize(): number }) => ({
    getTotalSize: () => 320,
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

function game(id = 1, extra: Partial<LibraryGame> = {}): LibraryGame {
  return {
    workId: id,
    title: `Library game ${id}`,
    bucket: 'never_played',
    playtimeMinutes: 0,
    entries: [
      {
        ownershipId: id,
        releaseId: id,
        workId: id,
        title: `Library game ${id}`,
        store: 'Steam',
        installed: true,
        playtimeMinutes: 0,
      },
    ],
    ...extra,
  }
}
function context(mode: ThemeContext['mode']): ThemeContext {
  return {
    mode,
    page: 'discover',
    selectedWorkId: null,
    setPage: vi.fn(),
    openGame: vi.fn(),
    toggleFullscreen: vi.fn(),
    games: [game()],
    loading: false,
    profile: selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon),
    children: <p>Active screen</p>,
    renderScreen: () => null,
    actions: { launch: vi.fn() },
    components: {
      Artwork: ({ workId }) => (
        <div className="artwork">
          <img alt="" src={`${workId}.jpg`} />
        </div>
      ),
      Impression: ({ children, releaseId }) => <div data-impression={releaseId}>{children}</div>,
    } as ThemeContext['components'],
    feed: {
      candidateCount: 1,
      confidence: 1,
      failed: false,
      shelves: [
        {
          id: 'for-you',
          title: 'A new beginning',
          blurb: 'Waiting for a first visit.',
          supportsFeedback: false,
          reserve: [],
          items: [
            {
              ownershipId: 1,
              releaseId: 1,
              title: 'Library game 1',
              reason: 'Still waiting for your first visit.',
            },
          ],
        },
      ],
    },
  }
}
function mount(ctx: ThemeContext, Screen: ComponentType<ThemeContext>) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const tree = (value: ThemeContext) => (
    <QueryClientProvider client={client}>
      <Screen {...value} />
    </QueryClientProvider>
  )
  const mounted = render(tree(ctx))
  return { ...mounted, update: (value: ThemeContext) => mounted.rerender(tree(value)) }
}
beforeEach(() => {
  fixtures.workspace = undefined
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of [
      'query',
      'bucket',
      'store',
      'list',
      'sort',
      'view',
      'density',
      'selected',
      'selection',
      'rules',
    ])
      clearViewState(`avalon:library:${mode}:${key}`)
    for (const key of ['shelf', 'column']) clearViewState(`avalon:home:${mode}:${key}`)
  }
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe.each(['desktop', 'fullscreen'] as const)('Avalon in %s', (mode) => {
  it('retains navigation, library operations and full screen controls', () => {
    const ctx = context(mode)
    mount(ctx, AvalonShell)
    fireEvent.click(screen.getByRole('button', { name: 'Activity' }))
    expect(ctx.setPage).toHaveBeenCalledWith('journal')
    fireEvent.click(screen.getByRole('button', { name: 'Settings' }))
    expect(ctx.setPage).toHaveBeenCalledWith('settings')
    fireEvent.click(
      screen.getByRole('button', { name: mode === 'fullscreen' ? 'Leave fullscreen' : 'Enter fullscreen' }),
    )
    expect(ctx.toggleFullscreen).toHaveBeenCalled()
    expect(screen.getByRole('main').textContent).toContain('Active screen')
    expect(document.querySelector('.avalon-rail') !== null).toBe(mode === 'desktop')
  })
  it('opens a game without launching it and exposes its unread state', () => {
    const ctx = context(mode),
      item = game(1, { bucket: 'stale_but_patched', playtimeMinutes: 60 })
    render(<AvalonCover context={ctx} game={item} />)
    fireEvent.click(screen.getByRole('button', { name: `View ${item.title}, patched since you played` }))
    expect(ctx.openGame).toHaveBeenCalledWith(1)
    expect(ctx.actions.launch).not.toHaveBeenCalled()
    expect(document.querySelector('.avalon-unread')).not.toBeNull()
  })
  it('never adds unread state to an unplayed game', () => {
    const ctx = context(mode)
    render(<AvalonCover context={ctx} game={game(1, { bucket: 'stale_but_patched' })} />)
    expect(document.querySelector('.avalon-unread')).toBeNull()
  })
  it('shows real recommendations with reasons and retains the complete title', () => {
    const ctx = context(mode)
    mount(ctx, AvalonDiscover)
    const cover = screen.getByRole('button', { name: 'View Library game 1' })
    expect(screen.getAllByText('Still waiting for your first visit.').length).toBeGreaterThan(0)
    fireEvent.click(cover)
    expect(ctx.openGame).toHaveBeenCalledWith(1)
    expect(document.querySelectorAll('[data-impression]')).toHaveLength(1)
  })
  it('exposes empty and failed recommendation states with a recovery route', () => {
    const ctx = context(mode)
    ctx.feed = { candidateCount: 0, confidence: 0, failed: true, shelves: [] }
    mount(ctx, AvalonDiscover)
    expect(
      screen.getByText('Recommendations could not be loaded. Your library is still available.'),
    ).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Browse library' }))
    expect(ctx.setPage).toHaveBeenCalledWith('library')
  })
  it('searches, clears filters and keeps management available', () => {
    const ctx = context(mode)
    mount(ctx, AvalonLibrary)
    expect(screen.getByRole('button', { name: 'View Library game 1' })).toBeDefined()
    fireEvent.change(screen.getByRole('textbox', { name: 'Search games' }), {
      target: { value: 'missing title' },
    })
    expect(screen.getByText('No games match these filters.')).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    expect(screen.getByRole('button', { name: 'View Library game 1' })).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Manage library' }))
    expect(screen.getByText('Library management')).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Cover size' })).toBeNull()
  })
  it('removes the year chip after every dated game disappears', () => {
    const ctx = context(mode)
    ctx.games = [game(1, { firstReleaseYear: 2015 }), game(2)]
    const mounted = mount(ctx, AvalonLibrary)
    fireEvent.click(screen.getByRole('button', { name: 'Filters' }))
    fireEvent.change(screen.getByRole('textbox', { name: 'From this year' }), { target: { value: '2000' } })
    fireEvent.click(
      screen.getByRole('button', { name: mode === 'fullscreen' ? 'Apply filters' : 'Close filters' }),
    )
    expect(screen.queryByRole('button', { name: 'View Library game 2' })).toBeNull()
    ctx.games = [game(2)]
    mounted.update(ctx)
    expect(screen.getByText('No games match these filters.')).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Remove release year filter' }))
    expect(screen.getByRole('button', { name: 'View Library game 2' })).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Remove release year filter' })).toBeNull()
  })
  it('keeps an unknown saved facet visible and removes its individual chip', () => {
    const state = renderHook(() => useViewState<LibraryFilter>(`avalon:library:${mode}:rules`, {}))
    act(() => state.result.current[1]({ genreIds: [999], installed: true }))
    const ctx = context(mode)
    mount(ctx, AvalonLibrary)
    expect(screen.getByText('No games match these filters.')).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Remove Unavailable saved filter (999) filter' }))
    expect(state.result.current[0]).toEqual({ genreIds: [], installed: true })
    expect(screen.getByRole('button', { name: 'View Library game 1' })).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Remove Unavailable saved filter (999) filter' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Remove installation filter' })).toBeDefined()
  })
  it('adds imported Xbox stores and applies their residual-count filter after refresh', () => {
    const ctx = context(mode)
    const mounted = mount(ctx, AvalonLibrary)
    const imported = game(2)
    imported.entries[0].store = 'plugin:xbox'
    ctx.games = [...ctx.games, imported]
    mounted.update(ctx)
    fireEvent.click(screen.getByRole('button', { name: 'Filters' }))
    fireEvent.click(screen.getByText('Stores', { selector: 'summary' }))
    const xbox = screen.getByRole('checkbox', { name: 'Xbox, 1 matching title' })
    expect(xbox.hasAttribute('disabled')).toBe(false)
    fireEvent.click(xbox)
    if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    else fireEvent.click(screen.getByRole('button', { name: 'Close filters' }))
    expect(screen.queryByRole('button', { name: 'View Library game 1' })).toBeNull()
    expect(screen.getByRole('button', { name: 'View Library game 2' })).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Filters' }))
    expect(screen.getByRole('checkbox', { name: 'Xbox, 1 matching title' })).toBeDefined()
    expect(screen.getByRole('checkbox', { name: 'Steam, 1 matching title' })).toBeDefined()
  })
})

it('validates the theme and defaults without replacing host feature screens', () => {
  expect(validateThemeDefinition(avalon)).toBe(avalon)
  const profile = selectThemeProfile(DEFAULT_PROFILE, 'avalon', avalon)
  expect(resolvedThemeColors(profile).background).toBe('#0F1C1E')
  expect(profile.appearance.accent).toBe('#4DE8C2')
  expect(avalon.Details).toBeUndefined()
  expect(avalon.Journal).toBeUndefined()
  expect(avalon.Settings).toBeUndefined()
})

it('uses separate fullscreen shelf navigation and retains the column across shelves', () => {
  const ctx = context('fullscreen')
  ctx.games = [game(1), game(2), game(3)]
  ctx.feed!.shelves[0].items.push({
    ownershipId: 2,
    releaseId: 2,
    title: 'Library game 2',
    reason: 'Second choice.',
  })
  ctx.feed!.shelves.push({
    id: 'other',
    title: 'Another shelf',
    blurb: '',
    supportsFeedback: false,
    reserve: [],
    items: [{ ownershipId: 3, releaseId: 3, title: 'Library game 3', reason: 'Third choice.' }],
  })
  mount(ctx, AvalonDiscover)
  fireEvent.keyDown(screen.getByRole('button', { name: 'View Library game 1' }), { key: 'ArrowRight' })
  expect(document.querySelector('[data-selected="true"]')?.getAttribute('data-avalon-game')).toBe('2')
  fireEvent.keyDown(screen.getByRole('button', { name: 'View Library game 2' }), { key: 'ArrowDown' })
  expect(screen.queryByRole('button', { name: 'View Library game 2' })).toBeNull()
  expect(document.querySelector('[data-selected="true"]')?.getAttribute('data-avalon-game')).toBe('3')
  expect(screen.getByRole('button', { name: 'Next shelf' }).hasAttribute('disabled')).toBe(true)
})

it('navigates the complete ten-card fullscreen shelf with directional keys', () => {
  const ctx = context('fullscreen')
  ctx.games = Array.from({ length: 10 }, (_, index) => game(index + 1))
  const items = ctx.games.map((game) => ({
    ownershipId: game.workId,
    releaseId: game.workId,
    title: game.title,
    reason: 'A reason to return.',
  }))
  ctx.feed!.shelves[0].items = items.slice(0, 6)
  ctx.feed!.shelves[0].reserve = items.slice(6)
  mount(ctx, AvalonDiscover)
  const first = screen.getByRole('button', { name: 'View Library game 1' })
  first.focus()
  for (let id = 1; id < 10; id++)
    fireEvent.keyDown(screen.getByRole('button', { name: `View Library game ${id}` }), { key: 'ArrowRight' })
  expect(document.querySelectorAll('[data-avalon-game]')).toHaveLength(10)
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'View Library game 10' }))
})

it('appending deferred shelves retains the current cover element and keyboard focus', () => {
  const ctx = context('fullscreen'),
    client = new QueryClient()
  const tree = () => (
    <QueryClientProvider client={client}>
      <AvalonDiscover {...ctx} />
    </QueryClientProvider>
  )
  const mounted = render(tree())
  const first = screen.getByRole('button', { name: 'View Library game 1' })
  first.focus()
  ctx.feed = {
    ...ctx.feed!,
    shelves: [...ctx.feed!.shelves, { ...ctx.feed!.shelves[0], id: 'deferred', title: 'Deferred shelf' }],
  }
  mounted.rerender(tree())
  expect(screen.getByRole('button', { name: 'View Library game 1' })).toBe(first)
  expect(document.activeElement).toBe(first)
  fireEvent.keyDown(first, { key: 'ArrowDown' })
  expect(screen.getByRole('button', { name: 'Show Deferred shelf' }).getAttribute('aria-current')).toBe(
    'true',
  )
})

it('uses current data after hidden shelves are inserted or the feed is replaced', () => {
  const ctx = context('fullscreen'),
    client = new QueryClient()
  ctx.games = [game(1), game(2), game(3)]
  const tree = () => (
    <QueryClientProvider client={client}>
      <AvalonDiscover {...ctx} />
    </QueryClientProvider>
  )
  const mounted = render(tree())
  screen.getByRole('button', { name: 'View Library game 1' }).focus()
  ctx.feed = {
    ...ctx.feed!,
    shelves: [
      ctx.feed!.shelves[0],
      {
        id: 'inserted',
        title: 'Inserted shelf',
        blurb: '',
        supportsFeedback: false,
        reserve: [],
        items: [{ ownershipId: 2, releaseId: 2, title: 'Library game 2', reason: 'Inserted reason.' }],
      },
    ],
  }
  mounted.rerender(tree())
  fireEvent.keyDown(screen.getByRole('button', { name: 'View Library game 1' }), { key: 'ArrowDown' })
  expect(screen.getByRole('button', { name: 'View Library game 2' })).toBeDefined()
  ctx.feed = {
    ...ctx.feed,
    shelves: [
      {
        ...ctx.feed.shelves[1],
        id: 'replacement',
        title: 'Replacement shelf',
        items: [{ ownershipId: 3, releaseId: 3, title: 'Library game 3', reason: 'Replacement reason.' }],
      },
    ],
  }
  mounted.rerender(tree())
  expect(screen.queryByRole('button', { name: 'View Library game 2' })).toBeNull()
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'View Library game 3' }))
  expect(document.querySelectorAll('.avalon-home-row')).toHaveLength(1)
})

it('keeps desktop search separate from fullscreen search', () => {
  const desktop = mount(context('desktop'), AvalonLibrary)
  fireEvent.change(screen.getByRole('textbox', { name: 'Search games' }), {
    target: { value: 'desktop only' },
  })
  desktop.unmount()
  mount(context('fullscreen'), AvalonLibrary)
  expect((screen.getByRole('textbox', { name: 'Search games' }) as HTMLInputElement).value).toBe('')
  expect(screen.queryByRole('slider', { name: 'Cover size' })).toBeNull()
})

it('does not take focus from another control when Home data is replaced', () => {
  const ctx = context('fullscreen'), client = new QueryClient()
  ctx.games = [game(1), game(2)]
  const tree = () => <QueryClientProvider client={client}><button>Another control</button><AvalonDiscover {...ctx} /></QueryClientProvider>
  const mounted = render(tree())
  screen.getByRole('button', { name: 'View Library game 1' }).focus()
  const other = screen.getByRole('button', { name: 'Another control' })
  other.focus()
  ctx.feed = { ...ctx.feed!, shelves: [{ ...ctx.feed!.shelves[0], items: [{ ownershipId: 2, releaseId: 2, title: 'Library game 2', reason: 'Updated reason.' }] }] }
  mounted.rerender(tree())
  expect(screen.getByRole('button', { name: 'View Library game 2' })).toBeDefined()
  expect(document.activeElement).toBe(other)
})

it.each([true, false])(
  'Ctrl-click toggles games and context click preserves selection in grid=%s',
  (grid) => {
    const ctx = context('desktop')
    ctx.games = [game(1), game(2), game(3)]
    mount(ctx, AvalonLibrary)
    if (!grid) fireEvent.click(screen.getByRole('button', { name: 'List view' }))
    const first = screen.getByRole('button', { name: 'View Library game 1' }),
      second = screen.getByRole('button', { name: 'View Library game 2' })
    fireEvent.click(first, { ctrlKey: true })
    fireEvent.click(second, { ctrlKey: true })
    expect(screen.getByText('2 selected')).toBeDefined()
    expect(ctx.openGame).not.toHaveBeenCalled()
    fireEvent.contextMenu(first)
    expect(screen.getByText('2 selected')).toBeDefined()
    fireEvent.click(first, { ctrlKey: true })
    expect(screen.getByText('1 selected')).toBeDefined()
    fireEvent.contextMenu(screen.getByRole('button', { name: 'View Library game 3' }))
    const selectedThird = document.querySelector('[data-avalon-game="3"]')
    expect(selectedThird?.getAttribute(grid ? 'data-selected' : 'aria-pressed')).toBe('true')
    expect(screen.getByText('1 selected')).toBeDefined()
  },
)

it.each([true, false])(
  'preserves selection across refresh sort and view changes then prunes filtered games in grid=%s',
  (grid) => {
    const ctx = context('desktop')
    ctx.games = [game(1), game(2), game(3)]
    const mounted = mount(ctx, AvalonLibrary)
    if (!grid) fireEvent.click(screen.getByRole('button', { name: 'List view' }))
    fireEvent.click(screen.getByRole('button', { name: 'View Library game 1' }), { ctrlKey: true })
    fireEvent.click(screen.getByRole('button', { name: 'View Library game 2' }), { ctrlKey: true })
    ctx.games = ctx.games.map((game) => ({ ...game, summary: 'Refreshed metadata' }))
    mounted.update(ctx)
    expect(screen.getByText('2 selected')).toBeDefined()
    fireEvent.change(screen.getByRole('combobox', { name: 'Sort' }), { target: { value: 'title-desc' } })
    fireEvent.click(screen.getByRole('button', { name: grid ? 'List view' : 'Grid view' }))
    expect(screen.getByText('2 selected')).toBeDefined()
    fireEvent.change(screen.getByRole('textbox', { name: 'Search games' }), {
      target: { value: 'Library game 2' },
    })
    expect(screen.getByText('1 selected')).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Clear search' }))
    expect(screen.getByText('1 selected')).toBeDefined()
  },
)

it('applies Derelict exemptions only to the captured selected games', async () => {
  const ctx = context('desktop')
  ctx.games = [
    game(1, { bucket: 'derelict' }),
    game(2, { bucket: 'derelict' }),
    game(3, { bucket: 'derelict' }),
  ]
  const request = vi.fn().mockResolvedValue({ ok: true, status: 200, data: {} })
  Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
  mount(ctx, AvalonLibrary)
  fireEvent.click(screen.getByRole('button', { name: 'View Library game 1' }), { ctrlKey: true })
  fireEvent.click(screen.getByRole('button', { name: 'View Library game 2' }), { ctrlKey: true })
  fireEvent.click(screen.getByRole('button', { name: 'Remove from Derelict' }))
  await waitFor(() =>
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({ route: 'library.derelict-exemptions', body: { workIds: [1, 2] } }),
    ),
  )
})

it('marks linked releases as read without swallowing pushes newer than the displayed tile', async () => {
  const ctx = context('desktop')
  ctx.games = [game(1, { bucket: 'active', playtimeMinutes: 60 })]
  fixtures.workspace = {
    buckets: [
      {
        resolvedWorkId: 1,
        releaseId: 1,
        majorUpdateAt: '2026-08-01T00:00:00Z',
        game: { unreadUpdateCount: 1 },
      },
    ],
  }
  const request = vi.fn(async ({ route }: { route: string }) => ({
    ok: true,
    status: 200,
    data:
      route === 'game.details'
        ? {
            events: [
              { id: 10, releaseId: 1, kind: 'build_push', occurredAt: '2026-08-01T00:00:00Z' },
              { id: 11, releaseId: 1, kind: 'announcement', occurredAt: '2026-08-02T00:00:00Z' },
              { id: 12, releaseId: 1, kind: 'build_push', occurredAt: '2026-09-01T00:00:00Z' },
            ],
          }
        : { result: 'Stored' },
  }))
  Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
  mount(ctx, AvalonLibrary)
  fireEvent.click(screen.getByRole('button', { name: 'View Library game 1, patched since you played' }), {
    ctrlKey: true,
  })
  fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }))
  await waitFor(() =>
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        route: 'updates.acknowledge',
        params: { releaseId: 1 },
        body: { observedEventIds: [10, 11] },
      }),
    ),
  )
})
