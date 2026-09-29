// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AvalonDiscover, AvalonLibrary, AvalonShell, avalon } from '../src/renderer/themes/avalon'
import { filterFingerprint, useAvalonLists } from '../src/renderer/themes/avalon-list-state'
import { useLibrary } from '../src/renderer/api/hooks'
import { Details } from '../src/renderer/features/Details'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeContext } from '../src/shared/theme'
import type { ApiRequest } from '../src/shared/bridge'
import type { GameList, LibraryFilter, LibraryGame, LibraryResponse, Mode } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'
import { selectCollection as openList, selectedCollection, collectionNames } from './library-collections'

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
      'filters-open',
    ])
      clearViewState(`avalon:library:${mode}:${key}`)
    for (const key of ['tab', 'editing']) clearViewState(`${mode}:details:1:${key}`)
    for (const key of ['shelf', 'column']) clearViewState(`avalon:home:${mode}:${key}`)
    clearViewState(`draft:list:filters:${mode}`)
    clearViewState(`${mode}:library-tools:tab`)
  }
  for (const origin of ['library', 'details', 'feed', 'test'])
    for (const ids of ['100', '100,200', '300']) clearViewState(`draft:add-list:${origin}:${ids}`)
  clearViewState('avalon:collections:manual-expanded')
  clearViewState('avalon:collections:live-expanded')
  clearViewState('draft:list:new')
  clearViewState('draft:list:rail:manual')
  clearViewState('draft:list:rail:live')
  for (const id of [10, 11, 12, 20]) clearViewState(`draft:list:${id}`)
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
          items: [
            {
              ownershipId: 1,
              releaseId: 100,
              title: library.data?.games[0]?.title ?? 'Alpha',
              reason: 'Ready to play.',
            },
          ],
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
const selectedCards = () =>
  [...document.querySelectorAll<HTMLButtonElement>('.avalon-library [data-avalon-game]')]
    .filter(
      (card) => card.getAttribute('data-selected') === 'true' || card.getAttribute('aria-pressed') === 'true',
    )
    .map((card) => Number(card.dataset.avalonGame))

function chooseBucket(mode: Mode, key: string, label: string) {
  if (mode === 'desktop') {
    fireEvent.click(screen.getByRole('button', { name: new RegExp(`^${label}\\d`) }))
    return
  }
  const leave = screen.queryByRole('button', { name: 'Leave this list' })
  if (leave) fireEvent.click(leave)
  fireEvent.click(screen.getByRole('button', { name: 'Filters' }))
  const panel = within(screen.getByRole('dialog', { name: 'Library filters' }))
  fireEvent.change(panel.getByLabelText('Collection'), { target: { value: key } })
  fireEvent.click(panel.getByRole('button', { name: 'Apply filters' }))
}

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
  const filters = screen.getByRole('button', { name: 'Filters' })
  if (filters.getAttribute('aria-expanded') !== 'true') fireEvent.click(filters)
  fireEvent.click(await screen.findByRole('checkbox', { name: new RegExp(`^${name},`) }))
  fireEvent.click(
    screen.getByRole('button', { name: mode === 'fullscreen' ? 'Apply filters' : 'Close filters' }),
  )
}

describe('Library column and density controls', () => {
  it('keeps exactly one view and one primary selection through the grid and list toggle', () => {
    setup('desktop')
    expect(document.querySelector('.avalon-grid-row')).not.toBeNull()
    expect(document.querySelector('.avalon-record')).toBeNull()
    act(() => screen.getByRole('button', { name: 'View Alpha. Owned on Steam, Manual' }).focus())
    expect(selectedCards()).toEqual([1])
    fireEvent.click(screen.getByRole('button', { name: 'List view' }))
    expect(document.querySelector('.avalon-grid-row')).toBeNull()
    expect(document.querySelector('.avalon-record')).not.toBeNull()
    expect(selectedCards()).toEqual([1])
    act(() => screen.getByRole('button', { name: 'View Bravo. Owned on Steam, Manual' }).focus())
    expect(selectedCards()).toEqual([2])
    fireEvent.click(screen.getByRole('button', { name: 'Grid view' }))
    expect(document.querySelector('.avalon-record')).toBeNull()
    expect(selectedCards()).toEqual([2])
  })
  it.each(['grid', 'list'])(
    'an empty search replaces the %s and its headers until the search is cleared',
    (view) => {
      setup('desktop')
      if (view === 'list') fireEvent.click(screen.getByRole('button', { name: 'List view' }))
      fireEvent.change(screen.getByRole('textbox', { name: 'Search games' }), {
        target: { value: 'nothing matches this' },
      })
      expect(cards()).toEqual([])
      expect(document.querySelector('.avalon-record-header')).toBeNull()
      expect(document.querySelector('.avalon-record-row')).toBeNull()
      expect(document.querySelector('.avalon-grid-row')).toBeNull()
      expect(screen.getByText('No titles match “nothing matches this”.')).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Clear search' }))
      expect(cards()).toHaveLength(3)
      expect(!!document.querySelector('.avalon-record-header')).toBe(view === 'list')
    },
  )
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
    chooseBucket(mode, 'bounced', 'Started')
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
    expect(selectedCollection()).toBe('11')
    expect(document.querySelector('.avalon-cut-chips')!.firstElementChild).toBe(
      screen.getByRole('button', { name: 'Leave this list' }),
    )
    const rail = screen.getByRole('group', { name: 'Library collections' })
    expect(
      within(rail)
        .getAllByRole('button')
        .filter((button) => button.getAttribute('aria-pressed') === 'true'),
    ).toHaveLength(0)
    if (mode === 'desktop')
      expect(within(rail).getByRole('button', { name: 'Started2' }).getAttribute('data-filter-rule')).toBe(
        'true',
      )
    else
      expect(
        screen.getByRole('button', { name: 'Remove Started filter' }).getAttribute('data-filter-origin'),
      ).toBe('list')
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
  it('clears the current bucket and panel rules through the controls for each surface', async () => {
    chromeLibrary()
    setup(mode)
    chooseBucket(mode, 'bounced', 'Started')
    if (mode === 'desktop') chooseBucket(mode, 'bounced', 'Started')
    else fireEvent.click(screen.getByRole('button', { name: 'Remove Started filter' }))
    expect(cards()).toEqual([1, 2, 3])
    chooseBucket(mode, 'bounced', 'Started')
    await genre(mode, 'RPG')
    fireEvent.click(screen.getByRole('button', { name: 'Filters' }))
    const panel = screen.getByRole(mode === 'fullscreen' ? 'dialog' : 'region', { name: 'Library filters' })
    fireEvent.click(within(panel).getByRole('button', { name: 'Clear filters' }))
    fireEvent.click(
      within(panel).getByRole('button', { name: mode === 'fullscreen' ? 'Apply filters' : 'Close filters' }),
    )
    expect(cards()).toEqual(mode === 'desktop' ? [1, 2] : [1, 2, 3])
    if (mode === 'desktop')
      expect(screen.getByRole('button', { name: 'Remove Started filter' })).toBeDefined()
    else expect(screen.queryByRole('button', { name: 'Remove Started filter' })).toBeNull()
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
  it('returns from identity review to the collection grid through the collection or Close tools action', async () => {
    handler = (input) =>
      input.route === 'identity.get'
        ? ok({
            revision: 'review-1',
            hasCompletedSweep: true,
            candidates: [],
            history: [],
            expansions: [],
            workspace: {
              works: [],
              releases: [],
              ownerships: [],
              externalIds: [],
              buckets: [],
              identityLinks: [],
            },
          })
        : undefined
    setup(mode)
    fireEvent.click(screen.getByRole('button', { name: 'Manage library' }))
    fireEvent.click(screen.getByRole('button', { name: 'Identity review' }))
    await screen.findByRole('heading', { name: 'Are these the same game?' })
    expect(cards()).toEqual([])
    fireEvent.click(screen.getByRole('button', { name: mode === 'desktop' ? 'All games3' : 'Close tools' }))
    expect(screen.queryByRole('heading', { name: 'Are these the same game?' })).toBeNull()
    expect(cards()).toEqual([1, 2, 3])
    expect(screen.getByRole('button', { name: 'All games3' }).getAttribute('aria-pressed')).toBe('true')
  })
  it('defaults to longest dormancy with never-opened first and reverses for recent play', () => {
    delete preferences.values.DefaultSort
    current.games = current.games.map((game, index) => ({
      ...game,
      title: ['Recent', 'Ancient', 'Untouched'][index]!,
      lastPlayedAt: ['2026-09-26T00:00:00Z', '2022-09-29T00:00:00Z', null][index],
      playtimeMinutes: index === 2 ? 0 : 600,
    }))
    setup(mode)
    expect((screen.getByLabelText('Sort') as HTMLSelectElement).value).toBe('dormant')
    expect(cards()).toEqual([3, 2, 1])
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'recent' } })
    expect(cards()).toEqual([1, 2, 3])
  })
  it('sorts unequal playtimes both ways and names with mixed case independently', () => {
    current.games = current.games.map((game, index) => ({
      ...game,
      title: ['banjo', 'Anvil', 'cobalt'][index]!,
      playtimeMinutes: [600, 30000, 12][index]!,
    }))
    setup(mode)
    for (const [sort, order] of [
      ['time', [2, 1, 3]],
      ['time-low', [3, 1, 2]],
      ['title', [2, 1, 3]],
      ['title-desc', [3, 1, 2]],
    ] as const) {
      fireEvent.change(screen.getByLabelText('Sort'), { target: { value: sort } })
      expect(cards()).toEqual(order)
    }
  })
  it('All games preserves desktop search, clears fullscreen search and keeps the sort and repeated selection', () => {
    current.games = [
      ...current.games,
      {
        ...current.games[0]!,
        workId: 4,
        entries: [{ ...current.games[0]!.entries[0]!, workId: 4, releaseId: 400, ownershipId: 40 }],
      },
    ].map((game, index) => ({
      ...game,
      title: ['Zero Alpha', 'Zero Beta', 'Zero Gamma', 'Played Zero Delta'][index]!,
      bucket: index === 3 ? 'active' : 'never_played',
      playtimeMinutes: index === 3 ? 5000 : 0,
    }))
    setup(mode)
    const all = screen.getByRole('button', { name: 'All games4' })
    const rail = screen.getByRole('group', { name: 'Library collections' })
    const selected = () =>
      within(rail)
        .getAllByRole('button')
        .filter((button) => button.getAttribute('aria-pressed') === 'true')
    expect(selected()).toEqual([all])
    expect(cards()).toHaveLength(4)
    const never = screen.getByRole('button', { name: 'Never played3' })
    fireEvent.click(never)
    expect(selected()).toEqual([never])
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'title-desc' } })
    fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'alpha' } })
    expect(cards()).toEqual([1])
    fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'zero' } })
    expect(cards()).toEqual([3, 2, 1])
    for (let click = 0; click < 2; click++) {
      fireEvent.click(all)
      expect(selected()).toEqual([all])
      expect(cards()).toEqual([3, 2, 1, 4])
      expect((screen.getByLabelText('Search games') as HTMLInputElement).value).toBe(
        mode === 'desktop' ? 'zero' : '',
      )
      expect((screen.getByLabelText('Sort') as HTMLSelectElement).value).toBe('title-desc')
    }
  })
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
    for (const label of mode === 'desktop'
      ? ['All games3', 'Started1', 'In rotation1', 'Never played1']
      : ['All games3', 'Installed1', 'Never played1', 'Patched0'])
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
      const name = `View Alpha. Owned on Steam, Manual. Includes 1 expansion${minutes ? '' : ', one of them never played'}.`
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
    expect(selectedCollection()).toBe('11')
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
    expect(selectedCollection()).toBe('all')
  })
  it('counts a list holding both linked releases once and moves a grouped tile by its secondary member', async () => {
    const prey = {
      ...current.games[0],
      title: 'Prey',
      playtimeMinutes: 390,
      entries: [
        { ...current.games[0].entries[0], title: 'Prey', store: 'steam', playtimeMinutes: 300 },
        { ...current.games[0].entries[1], title: 'Prey Deluxe', store: 'epic', playtimeMinutes: 90 },
      ],
    }
    const other = { ...current.games[2], title: 'Dishonored', entries: [current.games[2].entries[0]] }
    current.games = [prey, other]
    current.lists[0] = { ...current.lists[0], name: 'Co-op night', releaseIds: [100, 101] }
    handler = (input) => {
      if (input.route !== 'list.order') return undefined
      current.lists[0] = {
        ...current.lists[0],
        releaseIds: (input.body as { releaseIds: number[] }).releaseIds,
        revision: 'm2',
      }
      return ok(current.lists[0])
    }
    const view = setup(mode)
    openList(10)
    expect(cards()).toEqual([1])
    expect(screen.getByRole('button', { name: 'View Prey. Owned on Steam, Epic' })).toBeTruthy()
    expect(screen.getByText('1 game')).toBeTruthy()
    current.lists[0] = { ...current.lists[0], releaseIds: [101, 300] }
    act(() => view.client.setQueryData(['api', 'library.get'], structuredClone(current)))
    await waitFor(() => expect(cards()).toEqual([1, 3]))
    expect((screen.getByLabelText('Sort') as HTMLSelectElement).value).toBe('list-order')
    act(() => document.querySelector<HTMLButtonElement>('[data-avalon-game="1"]')!.focus())
    expect((screen.getByRole('button', { name: 'Move earlier' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Move later' }) as HTMLButtonElement).disabled).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Move later' }))
    await waitFor(() => expect(cards()).toEqual([3, 1]))
    expect(view.request.mock.calls.find(([input]) => input.route === 'list.order')?.[0].body).toEqual({
      releaseIds: [300, 101],
      expectedRevision: 'm1',
    })
    expect(selectedCollection()).toBe('10')
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
    expect(selectedCollection()).toBe('10')
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
    fireEvent.click(screen.getByRole('button', { name: mode === 'desktop' ? 'New list' : 'New list…' }))
    if (mode === 'desktop') fireEvent.click(screen.getByRole('menuitem', { name: 'Static list' }))
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
    chooseBucket(mode, 'derelict', 'Derelict')
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
    fireEvent.click(screen.getByRole('button', { name: 'Leave this list' }))
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
    chooseBucket(mode, 'derelict', 'Derelict')
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
    chooseBucket(mode, 'derelict', 'Derelict')
    expect(cards()).toEqual([2])
    expect(screen.queryByLabelText('Live list rules')).toBeNull()
    openList(11)
    fireEvent.click(screen.getByRole('button', { name: 'Leave this list' }))
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
    const body = view.request.mock.calls.find(([input]) => input.route === 'list.live')![0].body as {
      name: string
      filter: LibraryFilter
    }
    expect(body.name).toBe('My cut')
    expect(filterFingerprint(body.filter)).toBe('{"search":"Alpha"}')
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

describe.each(['desktop', 'fullscreen'] as const)('original manual list contracts in %s', (mode) => {
  function source() {
    current.games = current.games.map((game, index) => ({
      ...game,
      title: ['Hades', 'Celeste', 'Tunic'][index],
      entries: [{ ...game.entries[0], title: ['Hades', 'Celeste', 'Tunic'][index] }],
    }))
    current.lists = [
      { id: 10, name: 'Friday night', isLive: false, releaseIds: [100, 200, 300], revision: 'm1' },
    ]
  }
  const focus = (id: number) =>
    act(() => document.querySelector<HTMLButtonElement>(`[data-avalon-game="${id}"]`)!.focus())

  it('explains the original empty collection and offers both static and live creation', () => {
    source()
    current.lists = []
    setup(mode)
    expect(
      screen.getByText('No lists yet. Choose New list below to create a static or live list.'),
    ).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: mode === 'desktop' ? 'New list' : 'New list…' }))
    if (mode === 'desktop') {
      const menu = screen.getByRole('menu', { name: 'New list' })
      expect(within(menu).getByRole('menuitem', { name: 'Static list' }).title).toBe(
        'Choose the games yourself. Add or remove titles whenever you like.',
      )
      expect(within(menu).getByRole('menuitem', { name: 'Live list' }).title).toBe(
        'Save the current library filters. Matching games update automatically.',
      )
      fireEvent.click(within(menu).getByRole('menuitem', { name: 'Static list' }))
      expect(screen.getByRole('dialog', { name: 'Name this list' })).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Create list' })).toBeTruthy()
      return
    }
    const dialog = screen.getByRole('dialog')
    expect(
      within(dialog).getByText('Choose games yourself, or let a live list follow your filters.'),
    ).toBeTruthy()
    expect(within(dialog).getByRole('button', { name: 'Create list' })).toBeTruthy()
    fireEvent.click(within(dialog).getByLabelText('Keep this list up to date with filters'))
    expect(within(dialog).getByRole('button', { name: 'Create live list' })).toBeTruthy()
  })

  it('explains empty Details membership when the only list is live', async () => {
    source()
    current.lists = [{ ...current.lists[0], name: 'Unplayed', isLive: true, filter: {}, releaseIds: [] }]
    setup(mode, 'details')
    expect(await screen.findByText('Create a list in Library tools.')).toBeTruthy()
    expect(screen.queryAllByRole('checkbox')).toEqual([])
    expect(screen.getByRole('button', { name: 'Add to list…' })).toBeTruthy()
  })

  it.each(['feed', 'details'])(
    'adds only Hades from %s to existing and new lists while preserving Celeste selection and cancellation',
    async (origin) => {
      source()
      current.lists = [
        { ...current.lists[0], name: 'Friday', releaseIds: [] },
        { id: 11, name: 'Automatic', isLive: true, releaseIds: [], revision: 'l1', filter: {} },
      ]
      handler = (input) => {
        if (input.route === 'list.member.add') {
          current.lists[0] = { ...current.lists[0], releaseIds: [100], revision: 'm2' }
          return ok(current.lists[0])
        }
        if (input.route === 'list.create') {
          const created = { id: 20, name: 'Next up', isLive: false, releaseIds: [100], revision: 'n1' }
          current.lists.push(created)
          return ok(created)
        }
        return undefined
      }
      const view = setup(mode)
      focus(2)
      expect(selectedCards()).toEqual([2])
      view.rerender(
        <QueryClientProvider client={view.client}>
          <Fixture mode={mode} origin={origin} />
        </QueryClientProvider>,
      )
      const add = () =>
        fireEvent.click(
          screen.getByRole('button', {
            name: origin === 'feed' && mode === 'desktop' ? 'Add Hades to list…' : 'Add to list…',
          }),
        )
      add()
      expect(screen.getByRole('heading', { name: 'Add Hades to a list' })).toBeTruthy()
      expect(
        within(screen.getByRole('group', { name: 'Existing lists' }))
          .getAllByRole('button')
          .map((button) => button.textContent),
      ).toEqual(['Friday'])
      fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Friday' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      add()
      fireEvent.change(screen.getByLabelText('New list name'), { target: { value: 'Next up' } })
      fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'New list' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      if (origin === 'details')
        expect(
          (screen.getByRole('checkbox', { name: 'Remove from Next up' }) as HTMLInputElement).checked,
        ).toBe(true)
      else expect(screen.getAllByText('Ready to play.').length).toBeGreaterThan(0)
      add()
      fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }))
      expect(screen.queryByRole('dialog')).toBeNull()
      expect(
        view.request.mock.calls
          .filter(([input]) => input.route.startsWith('list.'))
          .map(([input]) => [input.route, input.body]),
      ).toEqual([
        ['list.member.add', { releaseIds: [100], expectedRevision: 'm1' }],
        ['list.create', { name: 'Next up', releaseIds: [100] }],
      ])
      view.rerender(
        <QueryClientProvider client={view.client}>
          <Fixture mode={mode} />
        </QueryClientProvider>,
      )
      expect(selectedCards()).toEqual([2])
      expect(selectedCollection()).toBe('all')
    },
  )

  it.each([false, true])(
    'keeps the source collection and both selected games after saving to a destination: create=%s',
    async (createNew) => {
      source()
      current.lists[0].releaseIds = [100, 200]
      current.lists.push({ id: 11, name: 'Later', isLive: false, releaseIds: [], revision: 'r1' })
      handler = (input) =>
        input.route === (createNew ? 'list.create' : 'list.member.add')
          ? ok({
              id: createNew ? 20 : 11,
              name: createNew ? 'New destination' : 'Later',
              isLive: false,
              releaseIds: [100, 200],
              revision: 'r2',
            })
          : undefined
      const view = setup(mode)
      openList(10)
      fireEvent.click(document.querySelector('[data-avalon-game="1"]')!, { ctrlKey: true })
      fireEvent.click(document.querySelector('[data-avalon-game="2"]')!, { ctrlKey: true })
      const elements = [...document.querySelectorAll('.avalon-library [data-avalon-game]')]
      fireEvent.click(screen.getByRole('button', { name: 'Add 2 to list…' }))
      if (createNew) {
        fireEvent.change(screen.getByLabelText('New list name'), { target: { value: 'New destination' } })
        fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'New list' }))
      } else fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Later' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      expect(selectedCollection()).toBe('10')
      expect(cards()).toEqual([1, 2])
      expect(selectedCards()).toEqual([1, 2])
      for (const [index, element] of elements.entries())
        expect(document.querySelectorAll('.avalon-library [data-avalon-game]')[index]).toBe(element)
      expect(
        view.client
          .getQueryData<LibraryResponse>(['api', 'library.get'])!
          .lists.find((list) => list.id === (createNew ? 20 : 11))?.releaseIds,
      ).toEqual([100, 200])
    },
  )

  it('opens the original Hades Celeste Tunic order and disables each move only at its corresponding boundary', () => {
    source()
    const view = setup(mode)
    expect(cards()).toEqual([2, 1, 3])
    openList(10)
    expect(cards()).toEqual([1, 2, 3])
    expect((screen.getByLabelText('Sort') as HTMLSelectElement).value).toBe('list-order')
    for (const id of [1, 2, 3]) {
      focus(id)
      const earlier = screen.getByRole('button', { name: 'Move earlier' }),
        later = screen.getByRole('button', { name: 'Move later' })
      expect((earlier as HTMLButtonElement).disabled).toBe(id === 1)
      expect((later as HTMLButtonElement).disabled).toBe(id === 3)
      if (id !== 2) fireEvent.click(id === 1 ? earlier : later)
    }
    expect(view.request.mock.calls.some(([input]) => input.route === 'list.order')).toBe(false)
  })

  it('renames with the saved revision, reorders the picker and requires confirmation before deleting only the list', async () => {
    source()
    current.lists = [
      { id: 11, name: 'Zebra', isLive: false, releaseIds: [100], revision: 'z1' },
      { ...current.lists[0], name: 'Middle', releaseIds: [100] },
    ]
    handler = (input) => {
      if (input.route === 'list.update') {
        current = {
          ...current,
          lists: current.lists.map((list) =>
            list.id === 10 ? { ...list, name: 'Aardvark', revision: 'm2' } : list,
          ),
        }
        return ok(current.lists.find((list) => list.id === 10))
      }
      if (input.route === 'list.delete') {
        current = { ...current, lists: current.lists.filter((list) => list.id !== 10) }
        return ok(null)
      }
      return undefined
    }
    const view = setup(mode)
    openList(10)
    fireEvent.click(screen.getByRole('button', { name: 'Manage library' }))
    const panel = screen.getByRole('heading', { name: 'Middle' }).closest('section')!
    fireEvent.click(within(panel).getByRole('button', { name: 'Edit' }))
    expect((within(panel).getByLabelText('List name') as HTMLInputElement).value).toBe('Middle')
    fireEvent.change(within(panel).getByLabelText('List name'), { target: { value: 'Aardvark' } })
    fireEvent.click(within(panel).getByRole('button', { name: 'Save list' }))
    await screen.findByRole('heading', { name: 'Aardvark' })
    fireEvent.click(within(panel).getByRole('button', { name: 'Delete list…' }))
    expect(screen.getByRole('dialog', { name: 'Delete Aardvark?' })).toBeTruthy()
    expect(screen.getByText('Its games will stay in your library.')).toBeTruthy()
    expect(view.request.mock.calls.some(([input]) => input.route === 'list.delete')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Keep list' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Close tools' }))
    expect(collectionNames()).toEqual(['All games', 'Aardvark', 'Zebra'])
    expect(selectedCollection()).toBe('10')
    fireEvent.click(screen.getByRole('button', { name: 'Manage library' }))
    const renamed = screen.getByRole('heading', { name: 'Aardvark' }).closest('section')!
    fireEvent.click(within(renamed).getByRole('button', { name: 'Delete list…' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete list' }))
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Aardvark' })).toBeNull())
    fireEvent.click(screen.getByRole('button', { name: 'Close tools' }))
    expect(cards()).toEqual([2, 1, 3])
    expect(selectedCollection()).toBe('all')
    expect(
      view.request.mock.calls
        .filter(([input]) => ['list.update', 'list.delete'].includes(input.route))
        .map(([input]) => input.body),
    ).toEqual([{ name: 'Aardvark', description: null, expectedRevision: 'm1' }, { expectedRevision: 'm2' }])
  })

  it('retains a manual list when another game arrives and counts only its currently visible member games', async () => {
    source()
    current.lists[0].releaseIds = [100, 900]
    const view = setup(mode)
    openList(10)
    expect(cards()).toEqual([1])
    expect(screen.getByText('1 game')).toBeTruthy()
    current.games.push({
      ...current.games[2],
      workId: 4,
      title: 'Dead Cells',
      entries: [
        { ...current.games[2].entries[0], ownershipId: 4, releaseId: 400, workId: 4, title: 'Dead Cells' },
      ],
    })
    act(() => view.client.setQueryData(['api', 'library.get'], structuredClone(current)))
    await waitFor(() => expect(screen.getByLabelText('4 → 1')).toBeTruthy())
    expect(cards()).toEqual([1])
    expect(screen.getByText('1 game')).toBeTruthy()
    expect(current.lists[0].releaseIds).toEqual([100, 900])
    expect(view.request.mock.calls.some(([input]) => input.route.startsWith('list.'))).toBe(false)
  })

  it('arms add-to-list through keyboard selection and names multiple selections exactly once', () => {
    source()
    setup(mode)
    expect(selectedCards()).toEqual(mode === 'fullscreen' ? [2] : [])
    const celeste = document.querySelector<HTMLButtonElement>('[data-avalon-game="2"]')!
    focus(2)
    fireEvent.keyDown(celeste, { key: 'ArrowRight' })
    expect(selectedCards()).toHaveLength(1)
    expect((screen.getByRole('button', { name: 'Add to list…' }) as HTMLButtonElement).disabled).toBe(false)
    fireEvent.keyDown(document.querySelector('[data-avalon-game="1"]')!, { key: ' ', ctrlKey: true })
    fireEvent.click(document.querySelector('[data-avalon-game="2"]')!, { ctrlKey: true })
    expect(selectedCards()).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: 'Add 2 to list…' })).toHaveLength(1)
  })

  it('shows the original checked and unchecked memberships while excluding live lists in Details', async () => {
    source()
    current.lists = [
      { ...current.lists[0], name: 'Finish these first', releaseIds: [100] },
      { id: 11, name: 'Couch co-op night', isLive: false, releaseIds: [], revision: 'r1' },
      { id: 12, name: 'Unplayed', isLive: true, releaseIds: [], filter: {}, revision: 'l1' },
    ]
    setup(mode, 'details')
    expect(
      ((await screen.findByRole('checkbox', { name: 'Remove from Finish these first' })) as HTMLInputElement)
        .checked,
    ).toBe(true)
    expect(
      (screen.getByRole('checkbox', { name: 'Add to Couch co-op night' }) as HTMLInputElement).checked,
    ).toBe(false)
    expect(screen.queryByRole('checkbox', { name: /Unplayed/ })).toBeNull()
    expect(screen.getAllByRole('checkbox')).toHaveLength(2)
  })

  it('ticks and unticks the open game with committed revisions and refreshes the Library count', async () => {
    source()
    current.lists[0].releaseIds = []
    handler = (input) => {
      if (!['list.member.add', 'list.member.remove'].includes(input.route)) return undefined
      current.lists[0] = {
        ...current.lists[0],
        releaseIds: input.route === 'list.member.add' ? [100] : [],
        revision: input.route === 'list.member.add' ? 'm2' : 'm3',
      }
      return ok(current.lists[0])
    }
    const view = setup(mode, 'details')
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Add to Friday night' }))
    await waitFor(() =>
      expect(
        (screen.getByRole('checkbox', { name: 'Remove from Friday night' }) as HTMLInputElement).checked,
      ).toBe(true),
    )
    expect(view.client.getQueryData<LibraryResponse>(['api', 'library.get'])!.lists[0].releaseIds).toEqual([
      100,
    ])
    fireEvent.click(screen.getByRole('checkbox', { name: 'Remove from Friday night' }))
    await waitFor(() =>
      expect(
        (screen.getByRole('checkbox', { name: 'Add to Friday night' }) as HTMLInputElement).checked,
      ).toBe(false),
    )
    expect(view.client.getQueryData<LibraryResponse>(['api', 'library.get'])!.lists[0].releaseIds).toEqual([])
    expect(
      view.request.mock.calls
        .filter(([input]) => input.route.startsWith('list.member.'))
        .map(([input]) => input.body),
    ).toEqual([
      { releaseIds: [100], expectedRevision: 'm1' },
      { releaseIds: [100], expectedRevision: 'm2' },
    ])
    view.rerender(
      <QueryClientProvider client={view.client}>
        <Fixture mode={mode} />
      </QueryClientProvider>,
    )
    openList(10)
    expect(cards()).toEqual([])
  })
})

describe.each(['desktop', 'fullscreen'] as const)('original live list contracts in %s', (mode) => {
  function source() {
    const workspace = chromeLibrary()
    current.games = current.games.map((game, index) => ({
      ...game,
      title: ['Disco Elysium', 'Hades', 'Tunic'][index],
      entries: [game.entries[0]],
    }))
    current.lists = [
      { id: 11, name: 'Every RPG', isLive: true, releaseIds: [], filter: { genreIds: [1] }, revision: 'r1' },
    ]
    return workspace
  }
  it('replaces prior buckets and list genres and leaves only the newly chosen bucket or All games', async () => {
    source()
    current.games[0].bucket = 'never_played'
    current.lists.push({
      id: 12,
      name: 'Action',
      isLive: true,
      releaseIds: [],
      filter: { genreIds: [2] },
      revision: 'a1',
    })
    setup(mode)
    chooseBucket(mode, 'bounced', 'Started')
    expect(cards()).toEqual([2])
    openList(11)
    await waitFor(() => expect(cards()).toEqual([1]))
    expect(screen.queryByRole('button', { name: 'Remove Started filter' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Update Every RPG' })).toBeNull()
    openList(12)
    expect(cards()).toEqual([2])
    expect(screen.queryByRole('button', { name: 'Remove RPG filter' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Remove Action filter' })).toBeTruthy()
    openList(11)
    fireEvent.change(screen.getByRole('textbox', { name: 'Search games' }), { target: { value: 'Disco' } })
    chooseBucket(mode, 'bounced', 'Started')
    expect(cards()).toEqual([2])
    expect(selectedCollection()).toBe('all')
    expect((screen.getByRole('textbox', { name: 'Search games' }) as HTMLInputElement).value).toBe('')
    expect([...document.querySelectorAll('[data-filter-origin]')].map((chip) => chip.textContent)).toEqual([
      'Started',
    ])
    if (mode === 'desktop') {
      expect(screen.getByRole('button', { name: 'Started1' }).getAttribute('aria-pressed')).toBe('true')
      expect(screen.getByRole('button', { name: 'Started1' }).getAttribute('data-filter-rule')).toBeNull()
    } else expect(document.querySelectorAll('.avalon-buckets [aria-pressed="true"]')).toHaveLength(0)
    openList(11)
    fireEvent.click(screen.getByRole('button', { name: 'All games3' }))
    expect(cards()).toEqual([1, 2, 3])
    expect(screen.queryByRole('region', { name: 'Current library filters' })).toBeNull()
    expect(screen.getByRole('button', { name: 'All games3' }).getAttribute('aria-pressed')).toBe('true')
    expect(document.querySelectorAll('.avalon-buckets [aria-pressed="true"]')).toHaveLength(1)
    expect(document.querySelectorAll('[data-filter-rule="true"]')).toHaveLength(0)
  })
  it('reverts and updates original genre rules by name and discovers a new Pillars of Eternity without writing list members', async () => {
    const workspace = source(),
      previous = handler
    handler = (input) => {
      if (input.route === 'list.filter') {
        current = {
          ...current,
          lists: [
            { ...current.lists[0], filter: (input.body as { filter: LibraryFilter }).filter, revision: 'r2' },
          ],
        }
        return ok(current.lists[0])
      }
      return previous(input)
    }
    const view = setup(mode)
    openList(11)
    await waitFor(() => expect(cards()).toEqual([1]))
    await genre(mode, 'Action')
    expect(cards()).toEqual([1, 2])
    expect(screen.getByRole('button', { name: 'Update Every RPG' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Revert Every RPG' }))
    expect(cards()).toEqual([1])
    expect(screen.queryByRole('button', { name: 'Update Every RPG' })).toBeNull()
    await genre(mode, 'Action')
    fireEvent.click(screen.getByRole('button', { name: 'Update Every RPG' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Update Every RPG' })).toBeNull())
    expect(
      view.client.getQueryData<LibraryResponse>(['api', 'library.get'])!.lists[0].filter?.genreIds,
    ).toEqual([1, 2])
    openList('all')
    openList(11)
    expect(cards()).toEqual([1, 2])
    const newGame = {
      ...current.games[0],
      workId: 4,
      title: 'Pillars of Eternity',
      entries: [
        {
          ...current.games[0].entries[0],
          ownershipId: 4,
          releaseId: 400,
          workId: 4,
          title: 'Pillars of Eternity',
        },
      ],
    }
    current = { ...current, games: [...current.games, newGame] }
    workspace.releaseFacets.push({ releaseId: 400, facetIds: [1], gameModes: [] })
    act(() => {
      view.client.setQueryData(['api', 'library.workspace', undefined], structuredClone(workspace))
      view.client.setQueryData(['api', 'library.get'], structuredClone(current))
    })
    await waitFor(() => expect(cards()).toEqual([1, 2, 4]))
    expect(screen.getByText('3 games')).toBeTruthy()
    expect(current.lists[0].releaseIds).toEqual([])
    expect(
      view.request.mock.calls
        .filter(([input]) => input.route.startsWith('list.'))
        .map(([input]) => input.route),
    ).toEqual(['list.filter'])
  })
  it('suggests the original Started RPG cut, opens its saved rules and restores them after leaving and reopening', async () => {
    source()
    const previous = handler
    handler = (input) => {
      if (input.route === 'list.live') {
        const body = input.body as { name: string; filter: LibraryFilter }
        const saved = {
          id: 20,
          name: body.name,
          isLive: true,
          releaseIds: [],
          filter: body.filter,
          revision: 'new',
        }
        current = { ...current, lists: [...current.lists, saved] }
        return ok(saved)
      }
      return previous(input)
    }
    const view = setup(mode)
    chooseBucket(mode, 'bounced', 'Started')
    await genre(mode, 'RPG')
    fireEvent.click(screen.getByRole('button', { name: 'Save filters as a live list…' }))
    expect(screen.getByRole('dialog', { name: 'Name this live list' })).toBeTruthy()
    expect((screen.getByLabelText('List name') as HTMLInputElement).value).toBe('Started · RPG')
    fireEvent.change(screen.getByLabelText('List name'), { target: { value: 'Unfinished RPGs' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create live list' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(selectedCollection()).toBe('20')
    expect(cards()).toEqual([1])
    expect(screen.queryByRole('button', { name: 'Update Unfinished RPGs' })).toBeNull()
    const body = view.request.mock.calls.find(([input]) => input.route === 'list.live')![0].body as {
      name: string
      filter: LibraryFilter
    }
    expect(body.name).toBe('Unfinished RPGs')
    expect(filterFingerprint(body.filter)).toBe('{"buckets":["bounced"],"genreIds":[1]}')
    openList('all')
    expect(cards()).toEqual([1, 2, 3])
    openList(20)
    expect(cards()).toEqual([1])
    expect(screen.getByRole('button', { name: 'Remove RPG filter' }).getAttribute('data-filter-origin')).toBe(
      'list',
    )
    if (mode === 'desktop')
      expect(screen.getByRole('button', { name: 'Started2' }).getAttribute('data-filter-rule')).toBe('true')
    else
      expect(
        screen.getByRole('button', { name: 'Remove Started filter' }).getAttribute('data-filter-origin'),
      ).toBe('list')
    expect(screen.queryByRole('button', { name: 'Update Unfinished RPGs' })).toBeNull()
  })
  it('retains a live cut behind Home without marking both Home and its Library collection selected', async () => {
    source()
    const view = setup(mode)
    openList(11)
    await waitFor(() => expect(cards()).toEqual([1]))
    view.rerender(
      <QueryClientProvider client={view.client}>
        <Fixture mode={mode} origin="feed" />
      </QueryClientProvider>,
    )
    expect(
      screen.getByRole('navigation', { name: 'Main navigation' }).querySelector('[aria-current="page"]')
        ?.textContent,
    ).toBe('For you')
    expect(selectedCollection()).toBe('all')
    view.rerender(
      <QueryClientProvider client={view.client}>
        <Fixture mode={mode} />
      </QueryClientProvider>,
    )
    expect(selectedCollection()).toBe('11')
    expect(cards()).toEqual([1])
    expect(
      screen.getByRole('navigation', { name: 'Main navigation' }).querySelector('[aria-current="page"]')
        ?.textContent,
    ).toContain('Library')
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

describe('desktop collection rows', () => {
  it.each(['Static list', 'Live list'])(
    'opens the original %s naming prompt and returns cancellation focus to New list',
    async (label) => {
      const view = setup('desktop')
      const trigger = screen.getByRole('button', { name: 'New list' })
      act(() => trigger.focus())
      fireEvent.click(trigger)
      const choice = screen.getByRole('menuitem', { name: label })
      expect(choice.getAttribute('aria-description')).toBe(choice.title)
      expect(choice.title.length).toBeGreaterThan(20)
      fireEvent.click(choice)
      expect(screen.queryByRole('menu')).toBeNull()
      const dialog = screen.getByRole('dialog', {
        name: label === 'Static list' ? 'Name this list' : 'Name this live list',
      })
      expect(document.activeElement).toBe(within(dialog).getByLabelText('List name'))
      expect(within(dialog).queryByRole('checkbox')).toBeNull()
      fireEvent.change(within(dialog).getByLabelText('List name'), { target: { value: 'Not saved' } })
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))
      await waitFor(() => expect(document.activeElement).toBe(trigger))
      expect(screen.queryByRole('dialog')).toBeNull()
      expect(view.request.mock.calls.filter(([input]) => input.route.startsWith('list.'))).toEqual([])
    },
  )

  it('navigates the creation menu with arrows and Escape returns focus without creating anything', () => {
    setup('desktop')
    const trigger = screen.getByRole('button', { name: 'New list' })
    fireEvent.click(trigger)
    const menu = screen.getByRole('menu')
    expect(document.activeElement).toBe(screen.getByRole('menuitem', { name: 'Static list' }))
    fireEvent.keyDown(menu, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(screen.getByRole('menuitem', { name: 'Live list' }))
    fireEvent.keyDown(menu, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(screen.getByRole('menuitem', { name: 'Static list' }))
    fireEvent.keyDown(menu, { key: 'End' })
    expect(document.activeElement).toBe(screen.getByRole('menuitem', { name: 'Live list' }))
    fireEvent.keyDown(menu, { key: 'Escape' })
    expect(screen.queryByRole('menu')).toBeNull()
    expect(document.activeElement).toBe(trigger)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('saves the complete current cut from the footer, suggests its first two rules and opens the committed live list', async () => {
    chromeLibrary()
    const previous = handler
    handler = (input) => {
      if (input.route === 'list.live') {
        const body = input.body as { name: string; filter: LibraryFilter }
        const saved = {
          id: 77,
          name: body.name,
          filter: body.filter,
          isLive: true,
          releaseIds: [],
          revision: 'new',
        }
        current = { ...current, lists: [...current.lists, saved] }
        return ok(saved)
      }
      return previous(input)
    }
    const view = setup('desktop')
    fireEvent.click(screen.getByRole('button', { name: 'Started2' }))
    await genre('desktop', 'RPG')
    fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'Alpha' } })
    fireEvent.click(screen.getByRole('button', { name: 'New list' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Live list' }))
    const dialog = screen.getByRole('dialog', { name: 'Name this live list' })
    expect((within(dialog).getByLabelText('List name') as HTMLInputElement).value).toBe('Started · RPG')
    fireEvent.change(within(dialog).getByLabelText('List name'), { target: { value: 'Tonight' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(selectedCollection()).toBe('77')
    expect(cards()).toEqual([1])
    expect(screen.getByRole('region', { name: 'Library filters' })).toBeTruthy()
    const body = view.request.mock.calls.find(([input]) => input.route === 'list.live')![0].body as {
      name: string
      filter: LibraryFilter
    }
    expect(body.name).toBe('Tonight')
    expect(filterFingerprint(body.filter)).toBe('{"buckets":["bounced"],"genreIds":[1],"search":"Alpha"}')
  })

  it('keeps a failed footer name, locks a pending retry and restores a usable creation menu after saving', async () => {
    let finish!: (value: unknown) => void
    let fail = true
    handler = (input) =>
      input.route === 'list.create'
        ? fail
          ? { ok: false, status: 400, message: 'Cannot create this list' }
          : new Promise((resolve) => {
              finish = resolve
            })
        : undefined
    const view = setup('desktop')
    fireEvent.click(screen.getByRole('button', { name: 'New list' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Static list' }))
    fireEvent.change(screen.getByLabelText('List name'), { target: { value: 'Weekend' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create list' }))
    await screen.findByText('Cannot create this list')
    expect((screen.getByLabelText('List name') as HTMLInputElement).value).toBe('Weekend')
    fail = false
    fireEvent.click(screen.getByRole('button', { name: 'Create list' }))
    const dialog = screen.getByRole('dialog')
    for (const button of within(dialog).getAllByRole('button'))
      expect((button as HTMLButtonElement).disabled).toBe(true)
    fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(screen.getByRole('dialog')).toBe(dialog)
    await waitFor(() => expect(typeof finish).toBe('function'))
    await act(async () =>
      finish(ok({ id: 78, name: 'Weekend', isLive: false, releaseIds: [], revision: 'new' })),
    )
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(view.request.mock.calls.filter(([input]) => input.route === 'list.create')).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: 'New list' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Static list' }))
    expect((screen.getByLabelText('List name') as HTMLInputElement).value).toBe('')
    expect((screen.getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('collapses manual and live sections independently and retains their state across Home navigation', () => {
    const view = setup('desktop')
    const manual = screen.getByRole('button', { name: 'LISTS' })
    const live = screen.getByRole('button', { name: 'LIVE LISTS' })
    const section = (button: HTMLElement) => document.getElementById(button.getAttribute('aria-controls')!)!
    expect(manual.getAttribute('aria-expanded')).toBe('true')
    expect(live.getAttribute('aria-expanded')).toBe('true')
    fireEvent.click(manual)
    expect(section(manual).hidden).toBe(true)
    expect(section(live).hidden).toBe(false)
    expect(manual.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(live)
    expect(section(live).hidden).toBe(true)
    fireEvent.click(manual)
    expect(section(manual).hidden).toBe(false)
    expect(section(live).hidden).toBe(true)
    for (const origin of ['feed', 'library']) {
      view.rerender(
        <QueryClientProvider client={view.client}>
          <Fixture mode="desktop" origin={origin} />
        </QueryClientProvider>,
      )
      expect(screen.getByRole('button', { name: 'LISTS' }).getAttribute('aria-expanded')).toBe('true')
      expect(screen.getByRole('button', { name: 'LIVE LISTS' }).getAttribute('aria-expanded')).toBe('false')
      expect(screen.queryByRole('button', { name: 'Steam evenings, 2 games' })).toBeNull()
      expect(screen.getByRole('button', { name: 'Handpicked, 3 games' })).toBeTruthy()
    }
  })

  it('counts projected games once, evaluates saved live rules and refreshes counts after publication', async () => {
    current.lists[0].releaseIds = [100, 101, 200, 99999]
    current.lists[1].releaseIds = [300]
    const view = setup('desktop')
    expect(screen.getByRole('button', { name: 'Handpicked, 2 games' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Steam evenings, 2 games' })).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'Alpha' } })
    expect(cards()).toEqual([1])
    expect(screen.getByRole('button', { name: 'Steam evenings, 2 games' })).toBeTruthy()
    current = { ...current, games: current.games.filter((game) => game.workId !== 2) }
    act(() => view.client.setQueryData(['api', 'library.get'], current))
    await screen.findByRole('button', { name: 'Handpicked, 1 game' })
    expect(screen.getByRole('button', { name: 'Steam evenings, 1 game' })).toBeTruthy()
  })

  it('opens live filters inline and toggles the same actual rail row off while leaving the panel open', () => {
    setup('desktop')
    const live = screen.getByRole('button', { name: 'Steam evenings, 2 games' })
    expect(screen.queryByRole('region', { name: 'Library filters' })).toBeNull()
    fireEvent.click(live)
    expect(screen.getByRole('region', { name: 'Library filters' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Filters' }).getAttribute('aria-expanded')).toBe('true')
    expect(live.getAttribute('aria-pressed')).toBe('true')
    expect(
      document.querySelectorAll(
        '.avalon-buckets [aria-pressed="true"], [data-avalon-list][aria-pressed="true"]',
      ),
    ).toHaveLength(1)
    expect(cards()).toEqual([1, 2])
    fireEvent.click(live)
    expect(live.getAttribute('aria-pressed')).toBe('false')
    expect(selectedCollection()).toBe('all')
    expect(cards()).toEqual([1, 2, 3])
    expect(screen.queryByRole('button', { name: 'Remove Steam filter' })).toBeNull()
    expect(screen.getByRole('region', { name: 'Library filters' })).toBeTruthy()
    expect(
      document.querySelectorAll(
        '.avalon-buckets [aria-pressed="true"], [data-avalon-list][aria-pressed="true"]',
      ),
    ).toHaveLength(1)
  })

  it('does not open filters for a manual list and retains open live filters when switching to a manual list', () => {
    setup('desktop')
    openList(10)
    expect(screen.queryByRole('region', { name: 'Library filters' })).toBeNull()
    openList(11)
    expect(screen.getByRole('region', { name: 'Library filters' })).toBeTruthy()
    openList(10)
    expect(screen.getByRole('region', { name: 'Library filters' })).toBeTruthy()
    expect(cards()).toEqual([3, 1, 2])
    expect(selectedCollection()).toBe('10')
  })
})

it('fullscreen returns to its Browse results after selecting a live list without opening the filter page', () => {
  setup('fullscreen')
  openList(11)
  expect(cards()).toEqual([1, 2])
  expect(screen.getByRole('button', { name: 'Filters' }).getAttribute('aria-expanded')).toBe('false')
  expect(screen.queryByRole('dialog', { name: 'Library filters' })).toBeNull()
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
