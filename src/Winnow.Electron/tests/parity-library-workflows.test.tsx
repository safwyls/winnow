// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AvalonDiscover, AvalonLibrary, AvalonShell, avalon } from '../src/renderer/themes/avalon'
import { useAvalonLists } from '../src/renderer/themes/avalon-list-state'
import { useLibrary } from '../src/renderer/api/hooks'
import { Details } from '../src/renderer/features/Details'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeContext } from '../src/shared/theme'
import type { ApiRequest } from '../src/shared/bridge'
import type { GameList, LibraryFilter, LibraryGame, LibraryResponse, Mode } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'

const preferences = vi.hoisted(() => ({ values: { DefaultSort: 'NameAscending' } as Record<string, string> }))
vi.mock('../src/renderer/features/SettingsPreferences', () => ({
  usePresentationPreferences: () => preferences,
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
const games: LibraryGame[] = [1, 2, 3].map((id) => ({
  workId: id,
  title: ['Alpha', 'Bravo', 'Charlie'][id - 1]!,
  bucket: id === 2 ? 'derelict' : 'never_played',
  playtimeMinutes: 0,
  entries: [
    {
      workId: id,
      ownershipId: id,
      releaseId: id * 100,
      title: 'Edition',
      store: id === 3 ? 'gog' : 'steam',
      installed: id === 1,
      playtimeMinutes: 0,
    },
    {
      workId: id,
      ownershipId: id + 10,
      releaseId: id * 100 + 1,
      title: 'Another edition',
      store: 'manual',
      installed: false,
      playtimeMinutes: 0,
    },
  ],
}))
const lists: GameList[] = [
  { id: 10, name: 'Handpicked', isLive: false, revision: 'm1', releaseIds: [300, 100, 200] },
  {
    id: 11,
    name: 'Steam evenings',
    isLive: true,
    revision: 'l1',
    releaseIds: [100, 200],
    filter: { stores: ['steam'] },
  },
  {
    id: 12,
    name: 'Installed now',
    isLive: true,
    revision: 'i1',
    releaseIds: [100],
    filter: { installed: true },
  },
]
const ok = (data: unknown) => ({ ok: true, status: 200, data })
let current: LibraryResponse, handler: (input: ApiRequest) => unknown
beforeEach(() => {
  current = structuredClone({ games, lists })
  handler = () => undefined
  preferences.values.DefaultSort = 'NameAscending'
  delete preferences.values.GroupExpansions
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
    ])
      clearViewState(`avalon:library:${mode}:${key}`)
    for (const key of ['tab', 'editing']) clearViewState(`${mode}:details:1:${key}`)
    for (const key of ['shelf', 'column']) clearViewState(`avalon:home:${mode}:${key}`)
    clearViewState(`draft:list:filters:${mode}`)
  }
  for (const origin of ['library', 'details', 'feed', 'test'])
    for (const ids of ['100', '100,200', '300']) clearViewState(`draft:add-list:${origin}:${ids}`)
  clearViewState('draft:list:new')
})
function Fixture({ mode, origin = 'library' }: { mode: Mode; origin?: string }) {
  const library = useLibrary()
  const ctx: ThemeContext = {
    mode,
    page: origin === 'feed' ? 'discover' : 'library',
    games: library.data?.games ?? [],
    loading: false,
    selectedWorkId: null,
    profile: selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon),
    feed: {
      candidateCount: 1,
      confidence: 1,
      failed: false,
      shelves: [
        {
          id: 'first',
          title: 'For you',
          blurb: '',
          supportsFeedback: false,
          reserve: [],
          items: [{ ownershipId: 1, releaseId: 100, title: 'Alpha', reason: 'Ready to play.' }],
        },
      ],
    },
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
      {origin === 'details' ? (
        <Details mode={mode} workId={1} />
      ) : origin === 'feed' ? (
        <AvalonDiscover {...ctx} />
      ) : (
        <AvalonLibrary {...ctx} />
      )}
    </AvalonShell>
  )
}
function setup(mode: Mode, origin = 'library') {
  const request = vi.fn(
    async (input: ApiRequest) =>
      (await handler(input)) ??
      ok(
        input.route === 'library.get'
          ? current
          : input.route === 'game.details'
            ? {
                workId: 1,
                readAtUtc: '2026-09-28T20:00:00Z',
                events: [],
                sessions: {},
                journalEntries: [],
                ratings: [],
                achievements: [],
              }
            : input.route === 'library.workspace'
              ? { works: [], externalIds: [], epicLaunchKeys: {}, pluginActions: {} }
              : {},
      ),
  )
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  client.setQueryData(['api', 'library.get'], current)
  const view = render(
    <QueryClientProvider client={client}>
      <Fixture mode={mode} origin={origin} />
    </QueryClientProvider>,
  )
  return { ...view, request, client }
}
const cards = () =>
  [...document.querySelectorAll<HTMLButtonElement>('.avalon-library [data-avalon-game]')].map((element) =>
    Number(element.dataset.avalonGame),
  )
const openList = (id: number | 'all') =>
  fireEvent.change(screen.getByLabelText('My lists'), { target: { value: String(id) } })
const selectedCards = () =>
  [...document.querySelectorAll<HTMLButtonElement>('.avalon-library [data-avalon-game]')]
    .filter(
      (card) => card.getAttribute('data-selected') === 'true' || card.getAttribute('aria-pressed') === 'true',
    )
    .map((card) => Number(card.dataset.avalonGame))

function chromeLibrary() {
  current.games = current.games.map((game, index) => ({
    ...game,
    bucket: index === 2 ? 'never_played' : 'bounced',
    playtimeMinutes: [300, 60, 0][index],
    lastPlayedAt: ['2024-01-01T00:00:00Z', '2026-09-01T00:00:00Z', null][index],
  }))
  current.lists[1] = {
    ...current.lists[1],
    name: 'RPGs',
    filter: { genreIds: [1], buckets: ['bounced'] },
    releaseIds: [100],
  }
  const workspace = {
    works: [],
    externalIds: [],
    epicLaunchKeys: {},
    pluginActions: {},
    facets: ['RPG', 'Action', 'Strategy'].map((name, index) => ({
      id: index + 1,
      kind: 'genre',
      slug: name.toLowerCase(),
      name,
    })),
    releaseFacets: current.games.map((game, index) => ({
      releaseId: game.entries[0].releaseId,
      facetIds: [index + 1],
      gameModes: [],
    })),
  }
  handler = (input) => (input.route === 'library.workspace' ? ok(workspace) : undefined)
  return workspace
}
async function genre(mode: Mode, name: string) {
  fireEvent.click(screen.getByRole('button', { name: 'Filters' }))
  fireEvent.click(await screen.findByRole('checkbox', { name: new RegExp(`^${name},`) }))
  fireEvent.click(
    screen.getByRole('button', { name: mode === 'fullscreen' ? 'Apply filters' : 'Close filters' }),
  )
}

describe('Library column and density controls', () => {
  it('toggles all three column headers through the shared menu state with one active direction', () => {
    chromeLibrary()
    setup('desktop')
    fireEvent.click(screen.getByRole('button', { name: 'List view' }))
    for (const [column, sort, direction, order] of [
      ['playtime', 'time', 'descending', [1, 2, 3]],
      ['playtime', 'time-low', 'ascending', [3, 2, 1]],
      ['title', 'title', 'ascending', [1, 2, 3]],
      ['title', 'title-desc', 'descending', [3, 2, 1]],
      ['idle', 'dormant', 'descending', [3, 1, 2]],
      ['idle', 'recent', 'ascending', [2, 1, 3]],
    ] as const) {
      fireEvent.click(screen.getByRole('button', { name: `Sort by ${column}` }))
      expect((screen.getByRole('combobox', { name: 'Sort' }) as HTMLSelectElement).value).toBe(sort)
      expect(cards()).toEqual(order)
      expect(document.querySelectorAll('.avalon-record-header [data-sort-direction]')).toHaveLength(1)
      expect(
        screen.getByRole('button', { name: `Sort by ${column}` }).getAttribute('data-sort-direction'),
      ).toBe(direction)
    }
    fireEvent.change(screen.getByRole('combobox', { name: 'Sort' }), { target: { value: 'title' } })
    expect(screen.getByRole('button', { name: 'Sort by title' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('button', { name: 'Sort by idle' }).getAttribute('aria-pressed')).toBe('false')
  })
  it('mirrors density across the declared 108 to 200 range and adds grid columns toward the right', () => {
    setup('desktop')
    const slider = screen.getByRole('slider', { name: 'Density' }) as HTMLInputElement
    expect([slider.min, slider.max, slider.value]).toEqual(['108', '200', '160'])
    fireEvent.change(slider, { target: { value: '108' } })
    expect(document.querySelector<HTMLElement>('.avalon-grid-row')!.style.gridTemplateColumns).toContain(
      'repeat(4,',
    )
    fireEvent.change(slider, { target: { value: '200' } })
    expect(document.querySelector<HTMLElement>('.avalon-grid-row')!.style.gridTemplateColumns).toContain(
      'repeat(7,',
    )
    fireEvent.click(screen.getByRole('button', { name: 'List view' }))
    fireEvent.click(screen.getByRole('button', { name: 'Grid view' }))
    expect(slider.value).toBe('200')
  })
})

describe.each(['desktop', 'fullscreen'] as const)('Library cut bar in %s', (mode) => {
  it('explains an empty saved facet cut and restores matching games by dropping one rule', async () => {
    chromeLibrary()
    current.lists[1] = { ...current.lists[1], filter: { genreIds: [1], stores: ['gog'] }, releaseIds: [] }
    setup(mode)
    openList(11)
    await screen.findByRole('button', { name: 'Remove RPG filter' })
    expect(cards()).toEqual([])
    expect(screen.getByText('No titles match these filters. Drop one to widen the cut.')).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Remove RPG filter' }))
    expect(cards()).toEqual([3])
  })
  it('states total and remaining games and removes every bucket and genre rule independently', async () => {
    chromeLibrary()
    setup(mode)
    expect(screen.queryByRole('region', { name: 'Current library filters' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Started2' }))
    await genre(mode, 'RPG')
    const bar = screen.getByRole('region', { name: 'Current library filters' })
    expect(within(bar).getByLabelText('3 → 1')).toBeDefined()
    const chips = [...bar.querySelectorAll('[data-filter-origin]')]
    expect(chips.map((chip) => chip.textContent)).toEqual(['Started', 'RPG'])
    expect(chips.map((chip) => chip.getAttribute('aria-description'))).toEqual([
      'BUCKET: Started',
      'GENRE: RPG',
    ])
    fireEvent.click(within(bar).getByRole('button', { name: 'Remove RPG filter' }))
    expect(cards()).toEqual([1, 2])
    fireEvent.click(within(bar).getByRole('button', { name: 'Remove Started filter' }))
    expect(cards()).toEqual([1, 2, 3])
    expect(screen.queryByRole('region', { name: 'Current library filters' })).toBeNull()
  })
  it('marks saved live-list rules neutrally and turns unsaved additions into saved rules after Update', async () => {
    chromeLibrary()
    const old = handler
    handler = (input) => {
      if (input.route === 'list.filter') {
        const body = input.body as { filter: LibraryFilter; expectedRevision: string }
        expect(body.expectedRevision).toBe('l1')
        current.lists[1] = {
          ...current.lists[1],
          filter: body.filter,
          revision: 'l2',
          releaseIds: [100, 200],
        }
        return ok(current.lists[1])
      }
      return old(input)
    }
    setup(mode)
    openList(11)
    const brought = await screen.findByRole('button', { name: 'Remove RPG filter' })
    expect(brought.getAttribute('aria-description')).toBe('GENRE: RPG — from this live list')
    expect(screen.getByRole('button', { name: 'Leave this list' }).textContent).toContain('LIVE LISTRPGs')
    expect(screen.getByRole('button', { name: 'Leave this list' }).getAttribute('data-filter-origin')).toBe(
      'context',
    )
    expect((screen.getByRole('combobox', { name: 'My lists' }) as HTMLSelectElement).value).toBe('11')
    expect(document.querySelector('.avalon-cut-chips')!.firstElementChild).toBe(
      screen.getByRole('button', { name: 'Leave this list' }),
    )
    const rail = screen.getByRole('group', { name: 'Library collections' })
    expect(
      within(rail)
        .getAllByRole('button')
        .filter((button) => button.getAttribute('aria-pressed') === 'true'),
    ).toHaveLength(0)
    expect(within(rail).getByRole('button', { name: 'Started2' }).getAttribute('data-filter-rule')).toBe(
      'true',
    )
    await genre(mode, 'Action')
    const added = screen.getByRole('button', { name: 'Remove Action filter' })
    expect(added.getAttribute('aria-description')).toBe('GENRE: Action — yours, not saved to this list')
    expect(added.getAttribute('data-filter-origin')).toBe('unsaved')
    expect(cards()).toEqual([1, 2])
    fireEvent.click(screen.getByRole('button', { name: 'Update RPGs' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Update RPGs' })).toBeNull())
    for (const chip of document.querySelectorAll(
      '.avalon-cut-chips [data-filter-origin]:not([data-filter-origin="context"])',
    ))
      expect(chip.getAttribute('data-filter-origin')).toBe('list')
    fireEvent.click(screen.getByRole('button', { name: 'Leave this list' }))
    expect(cards()).toEqual([1, 2, 3])
    expect(screen.queryByRole('region', { name: 'Current library filters' })).toBeNull()
  })
  it('keeps user rules when leaving a manual context and clears search using its own chip', async () => {
    const workspace = chromeLibrary()
    current.lists[0] = { ...current.lists[0], releaseIds: [100, 200] }
    workspace.releaseFacets = workspace.releaseFacets!.map((row) =>
      row.releaseId === 300 ? { ...row, facetIds: [1] } : row,
    )
    setup(mode)
    openList(10)
    await genre(mode, 'RPG')
    expect(screen.getByRole('button', { name: 'Leave this list' }).textContent).toContain('LISTHandpicked')
    expect(screen.getByRole('button', { name: 'Leave this list' }).getAttribute('data-filter-origin')).toBe(
      'context',
    )
    expect(screen.getByRole('button', { name: 'Remove RPG filter' }).getAttribute('data-filter-origin')).toBe(
      'user',
    )
    expect(cards()).toEqual([1])
    fireEvent.click(screen.getByRole('button', { name: 'Leave this list' }))
    expect(cards()).toEqual([1, 3])
    fireEvent.change(screen.getByRole('textbox', { name: 'Search games' }), { target: { value: 'no match' } })
    expect(cards()).toEqual([])
    fireEvent.click(screen.getByRole('button', { name: 'Remove search filter' }))
    expect(cards()).toEqual([1, 3])
  })
  it('toggles the current bucket off outside a live list and clears only panel rules from the panel', async () => {
    chromeLibrary()
    setup(mode)
    const started = screen.getByRole('button', { name: 'Started2' })
    fireEvent.click(started)
    fireEvent.click(started)
    expect(cards()).toEqual([1, 2, 3])
    fireEvent.click(started)
    await genre(mode, 'RPG')
    fireEvent.click(screen.getByRole('button', { name: 'Filters' }))
    const panel = screen.getByRole(mode === 'fullscreen' ? 'dialog' : 'region', { name: 'Library filters' })
    fireEvent.click(within(panel).getByRole('button', { name: 'Clear filters' }))
    fireEvent.click(
      within(panel).getByRole('button', { name: mode === 'fullscreen' ? 'Apply filters' : 'Close filters' }),
    )
    expect(cards()).toEqual([1, 2])
    expect(screen.getByRole('button', { name: 'Remove Started filter' })).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Remove RPG filter' })).toBeNull()
    if (mode === 'fullscreen') {
      expect(screen.queryByRole('slider', { name: 'Density' })).toBeNull()
      expect(screen.queryByRole('group', { name: 'Library columns' })).toBeNull()
    }
  })
})

describe.each(['desktop', 'fullscreen'] as const)('library selection in %s', (mode) => {
  it('retains selected identities after reload and sort while keyboard navigation leaves one selected game', async () => {
    const view = setup(mode)
    const card = (id: number) => document.querySelector<HTMLButtonElement>(`[data-avalon-game="${id}"]`)!
    fireEvent.click(card(1), { ctrlKey: true })
    fireEvent.click(card(2), { ctrlKey: true })
    act(() => card(3).focus())
    expect(selectedCards()).toEqual([1, 2])
    act(() =>
      view.client.setQueryData(['api', 'library.get'], {
        ...current,
        games: current.games.map((game) => ({ ...game, summary: 'Fresh summary' })),
      }),
    )
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'title-desc' } })
    expect(selectedCards()).toEqual([2, 1])
    act(() => card(3).focus())
    fireEvent.keyDown(card(3), { key: 'ArrowRight' })
    await waitFor(() => expect(selectedCards()).toEqual([2]))
    expect(screen.getByRole('group', { name: 'Selected games' }).textContent).toContain('1 selected')
  })
  it('prunes hidden multi-selection without bringing it back when the filter is cleared', () => {
    setup(mode)
    fireEvent.click(document.querySelector('[data-avalon-game="1"]')!, { ctrlKey: true })
    fireEvent.click(document.querySelector('[data-avalon-game="2"]')!, { ctrlKey: true })
    fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'Bravo' } })
    expect(selectedCards()).toEqual([2])
    fireEvent.click(screen.getByRole('button', { name: 'Clear search' }))
    expect(selectedCards()).toEqual([2])
  })
})

it.each(['grid', 'list'] as const)(
  'clears filtered and explicitly cleared primary selection in the desktop %s',
  (kind) => {
    setup('desktop')
    if (kind === 'list') fireEvent.click(screen.getByRole('button', { name: 'List view' }))
    act(() => document.querySelector<HTMLButtonElement>('[data-avalon-game="2"]')!.focus())
    expect(selectedCards()).toEqual([2])
    fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'Alpha' } })
    expect(selectedCards()).toEqual([])
    fireEvent.click(screen.getByRole('button', { name: 'Clear search' }))
    expect(selectedCards()).toEqual([])
    fireEvent.click(document.querySelector('[data-avalon-game="1"]')!, { ctrlKey: true })
    expect(selectedCards()).toEqual([1])
    fireEvent.click(document.querySelector('[data-avalon-game="1"]')!, { ctrlKey: true })
    expect(selectedCards()).toEqual([])
    expect(document.querySelector<HTMLElement>('.avalon-selection-actions')!.style.visibility).toBe('hidden')
    fireEvent.click(document.querySelector('[data-avalon-game="2"]')!, { ctrlKey: true })
    fireEvent.click(screen.getByRole('button', { name: 'Clear selection' }))
    expect(selectedCards()).toEqual([])
  },
)

describe.each(['desktop', 'fullscreen'] as const)('list browsing in %s', (mode) => {
  it('counts grouped games once in the rail and per store and keeps a multi-store game in either store cut', () => {
    current.games = current.games.map((game, index) => ({
      ...game,
      bucket: ['bounced', 'active', 'never_played'][index]!,
      entries:
        index === 0
          ? [
              { ...game.entries[0]!, store: 'steam' },
              { ...game.entries[1]!, store: 'epic' },
            ]
          : [{ ...game.entries[0]!, store: index === 1 ? 'steam' : 'gog' }],
    }))
    setup(mode)
    expect(cards()).toHaveLength(3)
    for (const label of ['All games3', 'Started1', 'In rotation1', 'Never played1'])
      expect(screen.getByRole('button', { name: label })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Filters' }))
    const panel = screen.getByRole(mode === 'fullscreen' ? 'dialog' : 'region', { name: 'Library filters' })
    fireEvent.click(within(panel).getByText('Stores', { exact: true }))
    expect(within(panel).getByRole('checkbox', { name: 'Steam, 2 matching titles' })).toBeTruthy()
    expect(within(panel).getByRole('checkbox', { name: 'GOG, 1 matching title' })).toBeTruthy()
    fireEvent.click(within(panel).getByRole('checkbox', { name: 'Epic Games, 1 matching title' }))
    if (mode === 'fullscreen') fireEvent.click(within(panel).getByRole('button', { name: 'Apply filters' }))
    else fireEvent.click(within(panel).getByRole('button', { name: 'Close filters' }))
    expect(cards()).toEqual([1])
    expect(screen.getByRole('button', { name: 'All games3' })).toBeTruthy()
  })
  it.each([0, 45])(
    'folds expansions with %s minutes before rail list and search cuts and restores their own tiles when grouping is disabled',
    async (minutes) => {
      preferences.values.GroupExpansions = ' True '
      current.games[0] = { ...current.games[0]!, playtimeMinutes: 12000, bucket: 'active' }
      current.games[1] = {
        ...current.games[1]!,
        playtimeMinutes: minutes,
        bucket: minutes ? 'active' : 'never_played',
      }
      current.lists[0] = { ...current.lists[0]!, releaseIds: [200] }
      handler = (input) =>
        input.route === 'library.workspace'
          ? ok({
              works: [],
              externalIds: [],
              epicLaunchKeys: {},
              pluginActions: {},
              identityLinks: [{ parentWorkId: 1, childWorkId: 2, kind: 'expansion_of' }],
            })
          : undefined
      const view = setup(mode)
      await waitFor(() => expect(cards()).toEqual([1, 3]))
      const name = `View Alpha. Includes 1 expansion${minutes ? '' : ', one of them never played'}.`
      expect(screen.getByRole('button', { name })).toBeTruthy()
      expect(document.querySelector('[data-avalon-game="1"] .avalon-expansion-mark')?.textContent).toBe('+1')
      expect(screen.getByRole('button', { name: 'All games2' })).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Never played1' })).toBeTruthy()
      if (mode === 'desktop') {
        expect(document.querySelector('.avalon-library-total strong')?.textContent).toBe('2')
        fireEvent.click(screen.getByRole('button', { name: 'List view' }))
        expect(screen.getByRole('button', { name }).textContent).toContain('+1')
      }
      openList(10)
      expect(cards()).toEqual([])
      openList('all')
      fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'Bravo' } })
      expect(cards()).toEqual([])
      preferences.values.GroupExpansions = 'false'
      view.rerender(
        <QueryClientProvider client={view.client}>
          <Fixture mode={mode} />
        </QueryClientProvider>,
      )
      expect(cards()).toEqual([2])
      fireEvent.click(screen.getByRole('button', { name: 'Clear search' }))
      expect(cards()).toEqual([1, 2, 3])
      expect(document.querySelector('.avalon-expansion-mark')).toBeNull()
      expect(current.games[0]!.playtimeMinutes).toBe(12000)
      expect(current.games[1]!.playtimeMinutes).toBe(minutes)
    },
  )
  it.each([
    ['DormantLongest', 'dormant', [1, 3, 2]],
    ['RecentlyPlayed', 'recent', [2, 3, 1]],
    ['PlaytimeHighToLow', 'time', [2, 3, 1]],
    ['PlaytimeLowToHigh', 'time-low', [1, 3, 2]],
    ['NameAscending', 'title', [1, 2, 3]],
    ['NameDescending', 'title-desc', [3, 2, 1]],
  ] as const)(
    'applies changed default %s over a temporary sort without persisting browsing choices',
    (saved, sort, order) => {
      preferences.values.DefaultSort = saved === 'NameAscending' ? 'NameDescending' : 'NameAscending'
      current.games = current.games.map((game, index) => ({
        ...game,
        playtimeMinutes: [0, 120, 60][index]!,
        lastPlayedAt: [null, '2026-09-01T00:00:00Z', '2025-09-01T00:00:00Z'][index],
      }))
      const view = setup(mode)
      fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'title-desc' } })
      preferences.values.DefaultSort = saved
      view.rerender(
        <QueryClientProvider client={view.client}>
          <Fixture mode={mode} />
        </QueryClientProvider>,
      )
      expect((screen.getByLabelText('Sort') as HTMLSelectElement).value).toBe(sort)
      expect(cards()).toEqual(order)
      fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'title-desc' } })
      expect(preferences.values.DefaultSort).toBe(saved)
      expect(view.request.mock.calls.some(([input]) => input.route === 'preferences.presentation.put')).toBe(
        false,
      )
      view.unmount()
      setup(mode)
      expect((screen.getByLabelText('Sort') as HTMLSelectElement).value).toBe('title-desc')
      expect(preferences.values.DefaultSort).toBe(saved)
    },
  )
  it('applies a saved default on return while retaining a manual list until it closes', () => {
    const view = setup(mode)
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'title-desc' } })
    openList(10)
    expect(cards()).toEqual([3, 1, 2])
    view.unmount()
    preferences.values.DefaultSort = 'PlaytimeHighToLow'
    current.games[1]!.playtimeMinutes = 120
    current.games[2]!.playtimeMinutes = 60
    setup(mode)
    expect(cards()).toEqual([3, 1, 2])
    expect((screen.getByLabelText('Sort') as HTMLSelectElement).value).toBe('list-order')
    openList('all')
    expect((screen.getByLabelText('Sort') as HTMLSelectElement).value).toBe('time')
    expect(cards()).toEqual([2, 3, 1])
  })
  it('updates the order of a live list without changing its rules or writing its revision', () => {
    const view = setup(mode)
    openList(11)
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'time-low' } })
    preferences.values.DefaultSort = 'NameDescending'
    view.rerender(
      <QueryClientProvider client={view.client}>
        <Fixture mode={mode} />
      </QueryClientProvider>,
    )
    expect(cards()).toEqual([2, 1])
    expect((screen.getByLabelText('My lists') as HTMLSelectElement).value).toBe('11')
    expect(screen.queryByRole('button', { name: 'Update Steam evenings' })).toBeNull()
    expect(view.request.mock.calls.some(([input]) => input.route === 'list.filter')).toBe(false)
  })
  it('clears a deleted live list without hiding its former games', async () => {
    const view = setup(mode)
    openList(11)
    expect(cards()).toEqual([1, 2])
    act(() =>
      view.client.setQueryData(['api', 'library.get'], {
        ...current,
        lists: current.lists.filter((list) => list.id !== 11),
      }),
    )
    await waitFor(() => expect(cards()).toEqual([1, 2, 3]))
    expect((screen.getByLabelText('My lists') as HTMLSelectElement).value).toBe('all')
  })
  it('moves and removes handpicked games with boundary guards and keeps the list open after a failed write', async () => {
    let fail = true
    handler = (input) => {
      if (!['list.order', 'list.member.remove'].includes(input.route)) return undefined
      if (fail) return { ok: false, status: 400, message: 'Database busy' }
      const ids = (input.body as { releaseIds: number[] }).releaseIds
      current.lists[0] = {
        ...current.lists[0]!,
        releaseIds:
          input.route === 'list.order' ? ids : current.lists[0]!.releaseIds.filter((id) => !ids.includes(id)),
        revision: `m${current.lists[0]!.revision.length}`,
      }
      return ok(current.lists[0])
    }
    const view = setup(mode)
    openList(10)
    act(() => document.querySelector<HTMLButtonElement>('[data-avalon-game="3"]')!.focus())
    expect((screen.getByRole('button', { name: 'Move earlier' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Remove from Handpicked' }))
    await screen.findByText("Couldn't save list changes. Try again.")
    expect(cards()).toEqual([3, 1, 2])
    expect((screen.getByLabelText('My lists') as HTMLSelectElement).value).toBe('10')
    fail = false
    fireEvent.click(screen.getByRole('button', { name: 'Move later' }))
    await waitFor(() => expect(cards()).toEqual([1, 3, 2]))
    fireEvent.click(screen.getByRole('button', { name: 'Remove from Handpicked' }))
    await waitFor(() => expect(cards()).toEqual([1, 2]))
    act(() => document.querySelector<HTMLButtonElement>('[data-avalon-game="2"]')!.focus())
    expect((screen.getByRole('button', { name: 'Move later' }) as HTMLButtonElement).disabled).toBe(true)
    expect(view.request.mock.calls.find(([input]) => input.route === 'list.order')![0].body).toEqual({
      releaseIds: [100, 300, 200],
      expectedRevision: 'm1',
    })
  })
  it('creates an empty handpicked list from the collections footer despite the current multi-selection', async () => {
    handler = (input) =>
      input.route === 'list.create'
        ? ok({ id: 95, name: 'Empty list', isLive: false, releaseIds: [], revision: 'new' })
        : undefined
    const view = setup(mode)
    fireEvent.click(document.querySelector('[data-avalon-game="1"]')!, { ctrlKey: true })
    fireEvent.click(document.querySelector('[data-avalon-game="2"]')!, { ctrlKey: true })
    fireEvent.click(screen.getByRole('button', { name: 'New list…' }))
    fireEvent.change(screen.getByLabelText('List name'), { target: { value: 'Empty list' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create list' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(view.request.mock.calls.find(([input]) => input.route === 'list.create')![0].body).toEqual({
      name: 'Empty list',
      releaseIds: [],
    })
    expect(screen.getByText('2 selected')).toBeTruthy()
  })
  it('opens manual lists in member order, leaves predefined buckets, and restores the prior/default sort on exit', async () => {
    const view = setup(mode)
    fireEvent.click(screen.getByRole('button', { name: /^Derelict/ }))
    expect(cards()).toEqual([2])
    openList(10)
    expect(cards()).toEqual([3, 1, 2])
    expect((screen.getByLabelText('Sort') as HTMLSelectElement).value).toBe('list-order')
    preferences.values.DefaultSort = 'NameDescending'
    view.rerender(
      <QueryClientProvider client={view.client}>
        <Fixture mode={mode} />
      </QueryClientProvider>,
    )
    expect(cards()).toEqual([3, 1, 2])
    fireEvent.click(screen.getByRole('button', { name: 'Close list' }))
    expect(cards()).toEqual([3, 2, 1])
    expect((screen.getByLabelText('Sort') as HTMLSelectElement).value).toBe('title-desc')
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'title' } })
    openList(10)
    openList('all')
    expect(cards()).toEqual([1, 2, 3])
  })
  it('reopens saved live rules without inherited filters, includes newly matching titles, and clears rules when leaving', async () => {
    const view = setup(mode)
    fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'Charlie' } })
    fireEvent.click(screen.getByRole('button', { name: /^Derelict/ }))
    openList(11)
    expect(cards()).toEqual([1, 2])
    expect((screen.getByLabelText('Search games') as HTMLInputElement).value).toBe('')
    expect(screen.getByLabelText('Live list rules').textContent).toContain('Rules for Steam evenings')
    const incoming = {
      ...games[0]!,
      workId: 4,
      title: 'Delta',
      entries: [{ ...games[0]!.entries[0]!, releaseId: 400, workId: 4 }],
    }
    act(() =>
      view.client.setQueryData(['api', 'library.get'], { ...current, games: [...current.games, incoming] }),
    )
    await waitFor(() => expect(cards()).toEqual([1, 2, 4]))
    openList(12)
    expect(cards()).toEqual([1, 4])
    fireEvent.click(screen.getByRole('button', { name: /^Derelict/ }))
    expect(cards()).toEqual([2])
    expect(screen.queryByLabelText('Live list rules')).toBeNull()
    openList(11)
    fireEvent.click(screen.getByRole('button', { name: 'Close list' }))
    expect(cards()).toEqual([1, 2, 3, 4])
  })
  it('updates and reverts named live rules using the observed revision while preserving drafts after conflicts', async () => {
    let conflict = true
    handler = (input) => {
      if (input.route !== 'list.filter') return undefined
      if (conflict) {
        current.lists[1] = { ...current.lists[1]!, revision: 'other-window' }
        return { ok: false, status: 409, message: 'List changed' }
      }
      const saved = {
        ...current.lists[1]!,
        filter: (input.body as { filter: LibraryFilter }).filter,
        revision: 'l3',
      }
      current.lists[1] = saved
      return ok(saved)
    }
    const view = setup(mode)
    openList(11)
    fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'Bravo' } })
    expect(cards()).toEqual([2])
    fireEvent.click(screen.getByRole('button', { name: 'Revert Steam evenings' }))
    expect(cards()).toEqual([1, 2])
    fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'Alpha' } })
    fireEvent.click(screen.getByRole('button', { name: 'Update Steam evenings' }))
    await screen.findByRole('button', { name: 'Keep these rules and use latest revision' })
    expect((screen.getByLabelText('Search games') as HTMLInputElement).value).toBe('Alpha')
    expect(
      (screen.getByRole('button', { name: 'Update Steam evenings' }) as HTMLButtonElement).disabled,
    ).toBe(true)
    conflict = false
    fireEvent.click(screen.getByRole('button', { name: 'Keep these rules and use latest revision' }))
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Update Steam evenings' }) as HTMLButtonElement).disabled,
      ).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Update Steam evenings' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Update Steam evenings' })).toBeNull())
    const writes = view.request.mock.calls.filter(([input]) => input.route === 'list.filter')
    expect(writes.map(([input]) => (input.body as { expectedRevision: string }).expectedRevision)).toEqual([
      'l1',
      'other-window',
    ])
    expect(current.lists[1]!.filter).toEqual({ stores: ['steam'], search: 'Alpha' })
  })
  it('keeps filters changed inside a manual list and saves the complete current cut as a new live list', async () => {
    const view = setup(mode)
    openList(10)
    fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'Alpha' } })
    openList('all')
    expect(cards()).toEqual([1])
    expect((screen.getByLabelText('Search games') as HTMLInputElement).value).toBe('Alpha')
    handler = (input) =>
      input.route === 'list.live'
        ? ok({
            id: 50,
            name: 'My cut',
            isLive: true,
            releaseIds: [],
            filter: (input.body as { filter: object }).filter,
            revision: 'new',
          })
        : undefined
    fireEvent.click(screen.getByRole('button', { name: 'Save filters as a live list…' }))
    fireEvent.change(screen.getByLabelText('List name'), { target: { value: 'My cut' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create live list' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(view.request.mock.calls.find(([input]) => input.route === 'list.live')![0].body).toEqual({
      name: 'My cut',
      filter: { search: 'Alpha' },
    })
  })
})

describe.each(['desktop', 'fullscreen'] as const)('list prompts in %s', (mode) => {
  it.each(['library', 'details', 'feed'])(
    'preserves existing choices and new-list text after failure from %s',
    async (origin) => {
      let fails = true
      handler = (input) =>
        input.route === 'list.create'
          ? fails
            ? { ok: false, status: 400, message: 'Cannot save this list' }
            : ok({ id: 90, name: 'Rainy evenings', isLive: false, releaseIds: [100], revision: 'new' })
          : undefined
      const view = setup(mode, origin)
      if (origin === 'library')
        act(() => document.querySelector<HTMLButtonElement>('[data-avalon-game="1"]')!.focus())
      fireEvent.click(
        await screen.findByRole('button', {
          name: origin === 'feed' && mode === 'desktop' ? 'Add Alpha to list…' : 'Add to list…',
        }),
      )
      const dialog = screen.getByRole('dialog')
      expect(within(dialog).getByRole('button', { name: 'Handpicked' })).toBeTruthy()
      expect(within(dialog).queryByText('Steam evenings')).toBeNull()
      fireEvent.change(within(dialog).getByLabelText('New list name'), {
        target: { value: 'Rainy evenings' },
      })
      fireEvent.click(within(dialog).getByRole('button', { name: 'New list' }))
      await within(dialog).findByRole('alert')
      expect((within(dialog).getByLabelText('New list name') as HTMLInputElement).value).toBe(
        'Rainy evenings',
      )
      expect((within(dialog).getByRole('button', { name: 'Handpicked' }) as HTMLButtonElement).disabled).toBe(
        false,
      )
      fails = false
      fireEvent.click(within(dialog).getByRole('button', { name: 'New list' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      expect(
        view.request.mock.calls
          .filter(([input]) => input.route === 'list.create')
          .map(([input]) => input.body),
      ).toEqual([
        { name: 'Rainy evenings', releaseIds: [100] },
        { name: 'Rainy evenings', releaseIds: [100] },
      ])
    },
  )
  it('keeps multi-selection and browsing context while adding primary editions to an existing list', async () => {
    handler = (input) =>
      input.route === 'list.member.add'
        ? ok({ ...lists[0], releaseIds: [300, 100, 200], revision: 'm2' })
        : undefined
    const view = setup(mode)
    fireEvent.click(document.querySelector('[data-avalon-game="1"]')!, { ctrlKey: true })
    fireEvent.click(document.querySelector('[data-avalon-game="2"]')!, { ctrlKey: true })
    fireEvent.click(screen.getByRole('button', { name: 'Add 2 to list…' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Handpicked' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(screen.getByText('2 selected')).toBeTruthy()
    expect(cards()).toEqual([1, 2, 3])
    expect(view.request.mock.calls.find(([input]) => input.route === 'list.member.add')![0].body).toEqual({
      releaseIds: [100, 200],
      expectedRevision: 'm1',
    })
  })
})

it.each([false, true])(
  'disables conflicting prompt actions while pending and avoids navigating disposed content: %s',
  async (dispose) => {
    let finish!: (value: unknown) => void
    handler = (input) =>
      input.route === 'list.create'
        ? new Promise((resolve) => {
            finish = resolve
          })
        : undefined
    const view = setup('fullscreen', 'feed')
    fireEvent.click(screen.getByRole('button', { name: 'Add to list…' }))
    fireEvent.change(screen.getByLabelText('New list name'), { target: { value: 'Held save' } })
    fireEvent.click(screen.getByRole('button', { name: 'New list' }))
    const dialog = screen.getByRole('dialog')
    for (const button of within(dialog).getAllByRole('button'))
      expect((button as HTMLButtonElement).disabled).toBe(true)
    fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(screen.getByRole('dialog')).toBeTruthy()
    if (dispose) {
      view.unmount()
      render(<p>Another screen</p>)
    }
    await act(async () =>
      finish(ok({ id: 90, name: 'Held save', isLive: false, releaseIds: [100], revision: 'new' })),
    )
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    if (dispose) expect(screen.getByText('Another screen')).toBeTruthy()
    expect(view.request.mock.calls.filter(([input]) => input.route === 'list.create')).toHaveLength(1)
  },
)

it('checks an uncertain creation before allowing a retry and can use the saved list without duplicating it', async () => {
  handler = (input) =>
    input.route === 'list.create' ? { ok: false, status: 503, message: 'Disconnected' } : undefined
  const view = setup('desktop', 'details')
  fireEvent.click(await screen.findByRole('button', { name: 'Add to list…' }))
  fireEvent.change(screen.getByLabelText('New list name'), { target: { value: 'Already saved' } })
  fireEvent.click(screen.getByRole('button', { name: 'New list' }))
  await screen.findByRole('button', { name: 'Check saved lists' })
  expect((screen.getByRole('button', { name: 'New list' }) as HTMLButtonElement).disabled).toBe(true)
  current.lists.push({ id: 90, name: 'Already saved', isLive: false, releaseIds: [100], revision: 'new' })
  fireEvent.click(screen.getByRole('button', { name: 'Check saved lists' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Use saved list: Already saved' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  expect(view.request.mock.calls.filter(([input]) => input.route === 'list.create')).toHaveLength(1)
})

it('clicking the current live list again leaves its rules and preserves explicit unknown saved rules', () => {
  const value = { ...lists[1]!, filter: { stores: ['plugin:missing'], genreIds: [999] } }
  function State() {
    const state = useAvalonLists('desktop', [value])
    return (
      <>
        <button onClick={() => state.selectList('11')}>Toggle list</button>
        <output>{JSON.stringify({ id: state.listId, filter: state.filter })}</output>
      </>
    )
  }
  render(<State />)
  fireEvent.click(screen.getByText('Toggle list'))
  expect(screen.getByRole('status').textContent).toContain('plugin:missing')
  fireEvent.click(screen.getByText('Toggle list'))
  expect(screen.getByRole('status').textContent).toBe('{"id":"all","filter":{"search":null}}')
})
