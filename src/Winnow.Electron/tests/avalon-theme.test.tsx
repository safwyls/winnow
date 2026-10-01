// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, renderHook, screen, waitFor, within } from '@testing-library/react'
import {
  libraryRole,
  libraryLabel,
  returnToLibrary,
  chooseFilterSelect,
  openFilterGroup,
} from './library-controls'
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

it.each(['desktop', 'fullscreen'] as const)(
  'Patched navigation announces its twelve games and meaning in %s',
  (mode) => {
    const ctx = context(mode)
    ctx.page = 'library'
    ctx.games = Array.from({ length: 12 }, (_, index) =>
      game(index + 1, {
        bucket: 'stale_but_patched',
        playtimeMinutes: 600,
      }),
    )
    ctx.children = <AvalonLibrary {...ctx} />
    mount(ctx, AvalonShell)
    expect(libraryRole('button', { name: 'Patched, 12 games with unread updates' })).toBeDefined()
  },
)
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
      'rows',
      'viewport',
      'selected',
      'selection',
      'rules',
      'filter-order',
      'tools',
    ])
      clearViewState(`avalon:library:${mode}:${key}`)
    for (const key of ['shelf', 'column', 'positions']) clearViewState(`avalon:home:${mode}:${key}`)
  }
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe.each(['desktop', 'fullscreen'] as const)('Avalon in %s', (mode) => {
  it.each([21, 22])('routes linked release %s from Home to its grouped game identity', (releaseId) => {
    const ctx = context(mode)
    const prey = game(1, { title: 'Prey', playtimeMinutes: 390 })
    prey.entries = [
      { ...prey.entries[0], ownershipId: 11, releaseId: 21, title: 'Prey', store: 'steam' },
      { ...prey.entries[0], ownershipId: 12, releaseId: 22, workId: 2, title: 'Prey Deluxe', store: 'epic' },
    ]
    ctx.games = [prey]
    ctx.feed = {
      ...ctx.feed!,
      shelves: [
        {
          ...ctx.feed!.shelves[0],
          items: [
            { ownershipId: releaseId - 10, releaseId, title: 'Prey Deluxe', reason: 'Worth another visit.' },
          ],
        },
      ],
    }
    mount(ctx, AvalonDiscover)
    const cover = libraryRole('button', { name: 'View Prey. Owned on Steam, Epic' })
    expect(document.querySelectorAll('.avalon-cover')).toHaveLength(1)
    fireEvent.click(cover)
    expect(ctx.openGame).toHaveBeenCalledExactlyOnceWith(1)
    expect(ctx.actions.launch).not.toHaveBeenCalled()
  })
  it('renders each unlinked source entry separately with its own title total and store', () => {
    const ctx = context(mode)
    ctx.games = [
      game(1, { title: 'Prey', playtimeMinutes: 300, bucket: 'bounced' }),
      game(2, { title: 'Prey', playtimeMinutes: 90, bucket: 'active' }),
      game(3, { title: 'Dishonored' }),
      game(4, { title: 'Hades', playtimeMinutes: 8000, bucket: 'active' }),
    ]
    ctx.games.forEach((item, index) => {
      item.entries[0] = {
        ...item.entries[0],
        title: item.title,
        playtimeMinutes: item.playtimeMinutes,
        store: ['steam', 'epic', 'steam', 'gog'][index],
      }
    })
    mount(ctx, AvalonLibrary)
    expect(document.querySelectorAll('[data-avalon-game]')).toHaveLength(4)
    for (const [index, text, store] of [
      [1, '5h played', 'STEAM'],
      [2, '1h played', 'EPIC'],
      [3, '— played', 'STEAM'],
      [4, '133h played', 'GOG'],
    ] as const) {
      const tile = document.querySelector(`[data-avalon-game="${index}"]`)!
      expect(tile.textContent).toContain(
        mode === 'desktop' ? (text === '— played' ? 'never opened' : text.replace(' played', '')) : text,
      )
      expect(tile.querySelector('.avalon-store-chips')?.textContent).toBe(store)
      expect(tile.querySelector('.avalon-store-initials')).toBeNull()
    }
  })
  it('carries the grouped 340-minute headline and summed bucket through Library cuts', () => {
    const ctx = context(mode)
    const date = new Date(Date.now() - 10 * 86400000).toISOString()
    const prey = game(1, { title: 'Prey', playtimeMinutes: 340, lastPlayedAt: date, bucket: 'bounced' })
    prey.entries = [
      {
        ...prey.entries[0],
        store: 'steam',
        playtimeMinutes: 300,
        lastPlayedAt: new Date(Date.now() - 400 * 86400000).toISOString(),
      },
      {
        ...prey.entries[0],
        store: 'epic',
        workId: 2,
        releaseId: 2,
        ownershipId: 2,
        playtimeMinutes: 40,
        lastPlayedAt: date,
      },
    ]
    ctx.games = [prey]
    ctx.page = 'library'
    mount(ctx, (value) => (
      <AvalonShell {...value}>
        <AvalonLibrary {...value} />
      </AvalonShell>
    ))
    expect(libraryRole('button', { name: 'View Prey. Owned on Steam, Epic' }).textContent).toContain(
      mode === 'desktop' ? '5h · idle 10d' : '5h played',
    )
    if (mode === 'desktop') fireEvent.click(libraryRole('button', { name: 'Started1' }))
    else {
      fireEvent.click(libraryRole('button', { name: 'Filters' }))
      const panel = within(libraryRole('dialog', { name: 'Library filters' }))
      chooseFilterSelect('Collection', 'bounced')
      fireEvent.click(panel.getByRole('button', { name: 'Apply filters' }))
    }
    expect(document.querySelectorAll('[data-avalon-game]')).toHaveLength(1)
    fireEvent.click(libraryRole('button', { name: 'Never played0' }))
    expect(document.querySelectorAll('[data-avalon-game]')).toHaveLength(0)
  })
  it('keeps linked copies in one tile with exactly their distinct store words and resting initials', () => {
    const ctx = context(mode)
    const prey = game(1, { title: 'Prey', playtimeMinutes: 390, bucket: 'bounced' })
    prey.entries = [
      { ...prey.entries[0], store: 'steam', title: 'Prey', playtimeMinutes: 300 },
      {
        ...prey.entries[0],
        workId: 2,
        releaseId: 2,
        ownershipId: 2,
        store: 'epic',
        title: 'Prey Deluxe',
        playtimeMinutes: 90,
      },
    ]
    ctx.games = [prey, game(3, { title: 'Dishonored' })]
    mount(ctx, AvalonLibrary)
    expect(document.querySelectorAll('[data-avalon-game]')).toHaveLength(2)
    const grouped = libraryRole('button', { name: 'View Prey. Owned on Steam, Epic' })
    expect(
      [...grouped.querySelectorAll('.avalon-store-chips > span')].map((mark) => mark.textContent),
    ).toEqual(['STEAM', 'EPIC'])
    expect(
      [...grouped.querySelectorAll('.avalon-store-initials > span')].map((mark) => mark.textContent),
    ).toEqual(['S', 'E'])
    expect(grouped.querySelector('.avalon-store-initials')?.getAttribute('aria-hidden')).toBe('true')
    expect(grouped.textContent).toContain(mode === 'desktop' ? '6h' : '6h played')
    const single = libraryRole('button', { name: 'View Dishonored' })
    expect(single.querySelector('.avalon-store-initials')).toBeNull()
    expect(single.querySelector('.avalon-store-chips')?.textContent).toBe('STEAM')
    if (mode === 'desktop') {
      fireEvent.click(libraryRole('button', { name: 'List view' }))
      const row = libraryRole('button', { name: 'View Prey. Owned on Steam, Epic' })
      expect(row.className).toBe('avalon-record')
      expect([...row.querySelectorAll('.avalon-store-chip')].map((chip) => chip.textContent)).toEqual([
        'Steam',
        'Epic',
      ])
      expect(document.querySelectorAll('.avalon-record')).toHaveLength(2)
    }
  })
  it('deduplicates repeated licences case insensitively without dropping any entries', () => {
    const ctx = context(mode),
      prey = game(1, { title: 'Prey' })
    prey.entries = ['Steam', 'STEAM', 'gog'].map((store, index) => ({
      ...prey.entries[0],
      store,
      ownershipId: index + 1,
      releaseId: index + 1,
    }))
    render(<AvalonCover context={ctx} game={prey} />)
    const cover = libraryRole('button', { name: 'View Prey. Owned on Steam, GOG' })
    expect([...cover.querySelectorAll('.avalon-store-chips > span')].map((mark) => mark.textContent)).toEqual(
      ['STEAM', 'GOG'],
    )
    expect(
      [...cover.querySelectorAll('.avalon-store-initials > span')].map((mark) => mark.textContent),
    ).toEqual(['S', 'G'])
    expect(prey.entries.map((entry) => entry.ownershipId)).toEqual([1, 2, 3])
  })
  it('uses the grouped total and latest date for headline and dormancy while leaving copy facts intact', () => {
    const ctx = context(mode)
    const now = Date.now(),
      day = 86400000
    const primary = game(1, {
      title: 'Prey',
      playtimeMinutes: 300,
      lastPlayedAt: new Date(now - 1460 * day).toISOString(),
    })
    primary.entries[0] = { ...primary.entries[0], playtimeMinutes: 300, lastPlayedAt: primary.lastPlayedAt }
    const grouped = {
      ...primary,
      workId: 2,
      playtimeMinutes: 310,
      lastPlayedAt: new Date(now - 2 * day).toISOString(),
      entries: [
        primary.entries[0],
        {
          ...primary.entries[0],
          workId: 2,
          ownershipId: 2,
          releaseId: 2,
          store: 'epic',
          playtimeMinutes: 10,
          lastPlayedAt: new Date(now - 2 * day).toISOString(),
        },
      ],
    }
    render(
      <>
        <AvalonCover context={ctx} game={primary} />
        <AvalonCover context={ctx} game={grouped} />
      </>,
    )
    const old = libraryRole('button', { name: 'View Prey' })
    const recent = libraryRole('button', { name: 'View Prey. Owned on Steam, Epic' })
    const brightness = (element: HTMLElement) =>
      Number(/brightness\(([\d.]+)\)/.exec(element.style.getPropertyValue('--avalon-dormancy'))![1])
    expect(brightness(recent)).toBeGreaterThan(brightness(old))
    expect(recent.textContent).toContain(mode === 'desktop' ? '5h · idle 2d' : '5h played')
    expect(grouped.entries.map((entry) => [entry.playtimeMinutes, entry.lastPlayedAt])).toEqual([
      [300, primary.lastPlayedAt],
      [10, grouped.lastPlayedAt],
    ])
  })
  it('retains navigation, library operations and full screen controls', () => {
    const ctx = context(mode)
    mount(ctx, AvalonShell)
    fireEvent.click(libraryRole('button', { name: 'Activity' }))
    expect(ctx.setPage).toHaveBeenCalledWith('journal')
    fireEvent.click(libraryRole('button', { name: 'Settings' }))
    expect(ctx.setPage).toHaveBeenCalledWith('settings')
    fireEvent.click(
      libraryRole('button', { name: mode === 'fullscreen' ? 'Leave fullscreen' : 'Enter fullscreen' }),
    )
    expect(ctx.toggleFullscreen).toHaveBeenCalled()
    expect(libraryRole('main').textContent).toContain('Active screen')
    expect(document.querySelector('.avalon-rail') !== null).toBe(mode === 'desktop')
  })
  it('opens a game without launching it and exposes its unread state', () => {
    const ctx = context(mode),
      item = game(1, { bucket: 'stale_but_patched', playtimeMinutes: 60 })
    render(<AvalonCover context={ctx} game={item} />)
    fireEvent.click(libraryRole('button', { name: `View ${item.title}, patched since you played` }))
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
    ctx.feed!.shelves[0].supportsFeedback = true
    mount(ctx, AvalonDiscover)
    const cover = libraryRole('button', { name: 'View Library game 1' })
    expect(screen.getAllByText('Still waiting for your first visit.').length).toBeGreaterThan(0)
    fireEvent.click(cover)
    expect(ctx.openGame).toHaveBeenCalledWith(1)
    expect(document.querySelectorAll('[data-impression]')).toHaveLength(1)
  })
  it('does not record feed exposures for an unscored supplemental shelf', () => {
    mount(context(mode), AvalonDiscover)
    expect(document.querySelectorAll('[data-impression]')).toHaveLength(0)
  })
  it('exposes empty and failed recommendation states with a recovery route', () => {
    const ctx = context(mode)
    ctx.feed = { candidateCount: 0, confidence: 0, failed: true, shelves: [] }
    mount(ctx, AvalonDiscover)
    expect(
      screen.getByText('Recommendations could not be loaded. Your library is still available.'),
    ).toBeDefined()
    fireEvent.click(libraryRole('button', { name: 'Browse library' }))
    expect(ctx.setPage).toHaveBeenCalledWith('library')
  })
  it('searches, clears filters and keeps management available', () => {
    const ctx = context(mode)
    mount(ctx, AvalonLibrary)
    expect(libraryRole('button', { name: 'View Library game 1' })).toBeDefined()
    fireEvent.change(libraryRole('textbox', { name: 'Search games' }), {
      target: { value: 'missing title' },
    })
    expect(screen.getByText('No games match these filters.')).toBeDefined()
    fireEvent.click(libraryRole('button', { name: 'Clear filters' }))
    expect(libraryRole('button', { name: 'View Library game 1' })).toBeDefined()
    fireEvent.click(libraryRole('button', { name: 'Manage library' }))
    expect(screen.getByText('Library management')).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Cover size' })).toBeNull()
  })
  it('removes the year chip after every dated game disappears', () => {
    const ctx = context(mode)
    ctx.games = [game(1, { firstReleaseYear: 2015 }), game(2)]
    const mounted = mount(ctx, AvalonLibrary)
    fireEvent.click(libraryRole('button', { name: 'Filters' }))
    fireEvent.change(libraryRole('textbox', { name: 'From this year' }), { target: { value: '2000' } })
    fireEvent.click(
      libraryRole('button', { name: mode === 'fullscreen' ? 'Apply filters' : 'Close filters' }),
    )
    expect(screen.queryByRole('button', { name: 'View Library game 2' })).toBeNull()
    ctx.games = [game(2)]
    mounted.update(ctx)
    expect(screen.getByText('No games match these filters.')).toBeDefined()
    fireEvent.click(libraryRole('button', { name: 'Remove release year filter' }))
    expect(libraryRole('button', { name: 'View Library game 2' })).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Remove release year filter' })).toBeNull()
  })
  it('keeps an unknown saved facet visible and removes its individual chip', () => {
    const state = renderHook(() => useViewState<LibraryFilter>(`avalon:library:${mode}:rules`, {}))
    act(() => state.result.current[1]({ genreIds: [999], installed: true }))
    const ctx = context(mode)
    mount(ctx, AvalonLibrary)
    expect(screen.getByText('No games match these filters.')).toBeDefined()
    fireEvent.click(libraryRole('button', { name: 'Remove Unavailable saved filter (999) filter' }))
    expect(state.result.current[0]).toEqual({ genreIds: [], installed: true })
    expect(libraryRole('button', { name: 'View Library game 1' })).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Remove Unavailable saved filter (999) filter' })).toBeNull()
    expect(libraryRole('button', { name: 'Remove installation filter' })).toBeDefined()
  })
  it('adds imported Xbox stores and applies their residual-count filter after refresh', () => {
    const ctx = context(mode)
    const mounted = mount(ctx, AvalonLibrary)
    const imported = game(2)
    imported.entries[0].store = 'plugin:xbox'
    ctx.games = [...ctx.games, imported]
    mounted.update(ctx)
    fireEvent.click(libraryRole('button', { name: 'Filters' }))
    if (mode === 'fullscreen') openFilterGroup('PLATFORM')
    else fireEvent.click(screen.getByText('Stores', { selector: 'summary' }))
    const xbox = libraryRole(mode === 'fullscreen' ? 'button' : 'checkbox', {
      name: 'Xbox, 1 matching title',
    })
    expect(xbox.hasAttribute('disabled')).toBe(false)
    fireEvent.click(xbox)
    if (mode === 'fullscreen') fireEvent.click(libraryRole('button', { name: 'Apply filters' }))
    else fireEvent.click(libraryRole('button', { name: 'Close filters' }))
    expect(screen.queryByRole('button', { name: 'View Library game 1' })).toBeNull()
    expect(libraryRole('button', { name: 'View Library game 2' })).toBeDefined()
    fireEvent.click(libraryRole('button', { name: 'Filters' }))
    if (mode === 'fullscreen') openFilterGroup('PLATFORM')
    expect(
      libraryRole(mode === 'fullscreen' ? 'button' : 'checkbox', { name: 'Xbox, 1 matching title' }),
    ).toBeDefined()
    expect(
      libraryRole(mode === 'fullscreen' ? 'button' : 'checkbox', { name: 'Steam, 1 matching title' }),
    ).toBeDefined()
  })
})

it('validates the theme and keeps shared Activity and Settings screens', () => {
  expect(validateThemeDefinition(avalon)).toBe(avalon)
  const profile = selectThemeProfile(DEFAULT_PROFILE, 'avalon', avalon)
  expect(resolvedThemeColors(profile).background).toBe('#0F1C1E')
  expect(profile.appearance.accent).toBe('#4DE8C2')
  expect(avalon.Details).toBeDefined()
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
  fireEvent.keyDown(libraryRole('button', { name: 'View Library game 1' }), { key: 'ArrowRight' })
  expect(document.querySelector('[data-selected="true"]')?.getAttribute('data-avalon-game')).toBe('2')
  fireEvent.keyDown(libraryRole('button', { name: 'View Library game 2' }), { key: 'ArrowDown' })
  expect(screen.queryByRole('button', { name: 'View Library game 2' })).toBeNull()
  expect(document.querySelector('[data-selected="true"]')?.getAttribute('data-avalon-game')).toBe('3')
  expect(libraryRole('button', { name: 'Next shelf' }).hasAttribute('disabled')).toBe(true)
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
  const first = libraryRole('button', { name: 'View Library game 1' })
  first.focus()
  for (let id = 1; id < 10; id++)
    fireEvent.keyDown(libraryRole('button', { name: `View Library game ${id}` }), { key: 'ArrowRight' })
  expect(document.querySelectorAll('[data-avalon-game]')).toHaveLength(10)
  expect(document.activeElement).toBe(libraryRole('button', { name: 'View Library game 10' }))
})

it.each(['desktop', 'fullscreen'] as const)(
  'carries the current column on revisits and clamps short %s shelves',
  (mode) => {
    const ctx = context(mode)
    ctx.games = Array.from({ length: 10 }, (_, index) => game(index + 1))
    ctx.feed!.shelves = [
      [1, 2, 3, 4],
      [5, 6, 7, 8],
      [9, 10],
    ].map((ids, index) => ({
      id: `shelf-${index}`,
      title: `Shelf ${index}`,
      blurb: '',
      supportsFeedback: false,
      reserve: [],
      items: ids.map((id) => ({
        ownershipId: id,
        releaseId: id,
        title: `Library game ${id}`,
        reason: 'Waiting.',
      })),
    }))
    mount(ctx, AvalonDiscover)
    const move = (id: number, key: string, target: number) => {
      const cover = libraryRole('button', { name: `View Library game ${id}` })
      act(() => cover.focus())
      fireEvent.keyDown(cover, { key })
      expect(document.activeElement).toBe(libraryRole('button', { name: `View Library game ${target}` }))
    }
    move(4, 'ArrowDown', 8)
    move(6, 'ArrowUp', 2)
    move(4, 'ArrowDown', 8)
    move(8, 'ArrowDown', 10)
    move(10, 'ArrowUp', 6)
  },
)

it('appending deferred shelves retains the current cover element and keyboard focus', () => {
  const ctx = context('fullscreen'),
    client = new QueryClient()
  const tree = () => (
    <QueryClientProvider client={client}>
      <AvalonDiscover {...ctx} />
    </QueryClientProvider>
  )
  const mounted = render(tree())
  const first = libraryRole('button', { name: 'View Library game 1' })
  first.focus()
  ctx.feed = {
    ...ctx.feed!,
    shelves: [...ctx.feed!.shelves, { ...ctx.feed!.shelves[0], id: 'deferred', title: 'Deferred shelf' }],
  }
  mounted.rerender(tree())
  expect(libraryRole('button', { name: 'View Library game 1' })).toBe(first)
  expect(document.activeElement).toBe(first)
  fireEvent.keyDown(first, { key: 'ArrowDown' })
  expect(libraryRole('button', { name: 'Show Deferred shelf' }).getAttribute('aria-current')).toBe('true')
})

it('retains each Home overflow page while carrying only the current visible column', () => {
  const ctx = context('fullscreen')
  ctx.games = Array.from({ length: 50 }, (_, index) => game(index + 1))
  ctx.feed!.shelves = [
    [1, 24],
    [25, 48],
    [49, 50],
  ].map(([first, last], index) => ({
    id: `pages-${index}`,
    title: `Paged shelf ${index}`,
    blurb: '',
    supportsFeedback: false,
    reserve: [],
    items: Array.from({ length: last - first + 1 }, (_, offset) => ({
      ownershipId: first + offset,
      releaseId: first + offset,
      title: `Library game ${first + offset}`,
      reason: 'Waiting.',
    })),
  }))
  mount(ctx, AvalonDiscover)
  const cover = (id: number) => libraryRole('button', { name: `View Library game ${id}` })
  const press = (key: string) => fireEvent.keyDown(document.activeElement!, { key })
  act(() => cover(9).focus())
  press('ArrowRight')
  press('ArrowRight')
  expect(document.activeElement).toBe(cover(11))
  expect(screen.queryByRole('button', { name: 'View Library game 1' })).toBeNull()
  expect(
    document.querySelector('[data-row-active="true"] .avalon-home-row')?.getAttribute('data-home-page'),
  ).toBe('1')
  press('ArrowDown')
  expect(document.activeElement).toBe(cover(25))
  for (let index = 0; index < 3; index++) press('ArrowRight')
  press('ArrowUp')
  expect(document.activeElement).toBe(cover(14))
  press('ArrowDown')
  press('ArrowDown')
  expect(document.activeElement).toBe(cover(50))
  press('ArrowUp')
  expect(document.activeElement).toBe(cover(26))
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
  libraryRole('button', { name: 'View Library game 1' }).focus()
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
      {
        id: 'second-inserted',
        title: 'Second inserted shelf',
        blurb: '',
        supportsFeedback: false,
        reserve: [],
        items: [{ ownershipId: 3, releaseId: 3, title: 'Library game 3', reason: 'Another reason.' }],
      },
    ],
  }
  mounted.rerender(tree())
  fireEvent.keyDown(libraryRole('button', { name: 'View Library game 1' }), { key: 'ArrowDown' })
  expect(libraryRole('button', { name: 'View Library game 2' })).toBeDefined()
  fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' })
  expect(document.activeElement).toBe(libraryRole('button', { name: 'View Library game 3' }))
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
  expect(document.activeElement).toBe(libraryRole('button', { name: 'View Library game 3' }))
  expect(document.querySelectorAll('.avalon-home-row')).toHaveLength(1)
})

it('keeps desktop search separate from fullscreen search', () => {
  const desktop = mount(context('desktop'), AvalonLibrary)
  fireEvent.change(libraryRole('textbox', { name: 'Search games' }), {
    target: { value: 'desktop only' },
  })
  desktop.unmount()
  mount(context('fullscreen'), AvalonLibrary)
  expect((libraryRole('textbox', { name: 'Search games' }) as HTMLInputElement).value).toBe('')
  expect(screen.queryByRole('slider', { name: 'Cover size' })).toBeNull()
})

it('does not take focus from another control when Home data is replaced', () => {
  const ctx = context('fullscreen'),
    client = new QueryClient()
  ctx.games = [game(1), game(2)]
  const tree = () => (
    <QueryClientProvider client={client}>
      <button>Another control</button>
      <AvalonDiscover {...ctx} />
    </QueryClientProvider>
  )
  const mounted = render(tree())
  libraryRole('button', { name: 'View Library game 1' }).focus()
  const other = libraryRole('button', { name: 'Another control' })
  other.focus()
  ctx.feed = {
    ...ctx.feed!,
    shelves: [
      {
        ...ctx.feed!.shelves[0],
        items: [{ ownershipId: 2, releaseId: 2, title: 'Library game 2', reason: 'Updated reason.' }],
      },
    ],
  }
  mounted.rerender(tree())
  expect(libraryRole('button', { name: 'View Library game 2' })).toBeDefined()
  expect(document.activeElement).toBe(other)
})

it.each([true, false])(
  'Ctrl-click toggles games and context click preserves selection in grid=%s',
  (grid) => {
    const ctx = context('desktop')
    ctx.games = [game(1), game(2), game(3)]
    mount(ctx, AvalonLibrary)
    if (!grid) fireEvent.click(libraryRole('button', { name: 'List view' }))
    const first = libraryRole('button', { name: 'View Library game 1' }),
      second = libraryRole('button', { name: 'View Library game 2' })
    fireEvent.click(first, { ctrlKey: true })
    fireEvent.click(second, { ctrlKey: true })
    expect(screen.getByText('2 selected')).toBeDefined()
    expect(ctx.openGame).not.toHaveBeenCalled()
    fireEvent.contextMenu(first)
    expect(screen.getByText('2 selected')).toBeDefined()
    fireEvent.click(first, { ctrlKey: true })
    expect(screen.getByText('1 selected')).toBeDefined()
    fireEvent.contextMenu(libraryRole('button', { name: 'View Library game 3' }))
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
    if (!grid) fireEvent.click(libraryRole('button', { name: 'List view' }))
    fireEvent.click(libraryRole('button', { name: 'View Library game 1' }), { ctrlKey: true })
    fireEvent.click(libraryRole('button', { name: 'View Library game 2' }), { ctrlKey: true })
    ctx.games = ctx.games.map((game) => ({ ...game, summary: 'Refreshed metadata' }))
    mounted.update(ctx)
    expect(screen.getByText('2 selected')).toBeDefined()
    fireEvent.click(libraryRole('button', { name: /^Sort ·/ }))
    fireEvent.click(screen.getByRole('menuitemradio', { name: 'Name Z–A' }))
    fireEvent.click(libraryRole('button', { name: grid ? 'List view' : 'Grid view' }))
    expect(screen.getByText('2 selected')).toBeDefined()
    fireEvent.change(libraryRole('textbox', { name: 'Search games' }), {
      target: { value: 'Library game 2' },
    })
    expect(screen.getByText('1 selected')).toBeDefined()
    fireEvent.click(libraryRole('button', { name: 'Clear search' }))
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
  ctx.children = <AvalonLibrary {...ctx} />
  mount(ctx, AvalonShell)
  fireEvent.click(libraryRole('button', { name: /^Derelict/ }))
  fireEvent.click(libraryRole('button', { name: 'View Library game 1' }), { ctrlKey: true })
  fireEvent.click(libraryRole('button', { name: 'View Library game 2' }), { ctrlKey: true })
  fireEvent.click(libraryRole('button', { name: 'Remove from Derelict' }))
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
    works: [],
    externalIds: [],
    pluginActions: {},
    epicLaunchKeys: {},
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
  fireEvent.click(
    libraryRole('button', { name: 'View Library game 1, patched since you played: 1 update' }),
    {
      ctrlKey: true,
    },
  )
  fireEvent.click(libraryRole('button', { name: 'Mark as read' }))
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
