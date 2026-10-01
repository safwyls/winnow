// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AvalonLibrary, AvalonShell, avalon } from '../src/renderer/themes/avalon'
import { useLibrary } from '../src/renderer/api/hooks'
import { request } from '../src/renderer/api/client'
import { useAvalonLists, filterFingerprint } from '../src/renderer/themes/avalon-list-state'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeContext } from '../src/shared/theme'
import type { ApiRequest, WinnowBridge } from '../src/shared/bridge'
import type { LibraryFilter, LibraryResponse, Mode } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'
import { selectCollection } from './library-collections'
import { libraryRole, returnToLibrary } from './library-controls'

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
const saved: LibraryFilter = { stores: ['steam'], genreIds: [7], yearFrom: 2000 }
let snapshot: LibraryResponse
let workspace: Record<string, unknown>
let clients: QueryClient[] = []
function clear() {
  for (const mode of ['desktop', 'fullscreen'])
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
}
beforeEach(() => {
  clear()
  snapshot = {
    games: [1, 2].map((id) => ({
      workId: id,
      title: `Game ${id}`,
      bucket: 'never_played',
      playtimeMinutes: 0,
      firstReleaseYear: id === 1 ? 2010 : null,
      entries: [
        {
          workId: id,
          ownershipId: id,
          releaseId: id,
          title: `Game ${id}`,
          store: 'steam',
          installed: false,
          playtimeMinutes: 0,
        },
      ],
    })),
    lists: [],
  }
  workspace = {
    works: [],
    externalIds: [],
    epicLaunchKeys: {},
    pluginActions: {},
    facets: [{ id: 7, kind: 'genre', slug: 'rpg', name: 'RPG' }],
    releaseFacets: [{ releaseId: 1, facetIds: [7], gameModes: [] }],
  }
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
    .mockReturnValue({ matches: false, addEventListener() {}, removeEventListener() {} })
  window.winnow = {
    request: vi.fn(async ({ route, body }: ApiRequest) => {
      if (route === 'list.filter') {
        snapshot = {
          ...snapshot,
          lists: [
            {
              ...snapshot.lists[0],
              filter: (body as { filter: LibraryFilter }).filter,
              revision: 'saved-again',
            },
          ],
        }
        return { ok: true, status: 200, data: snapshot.lists[0] }
      }
      return {
        ok: true,
        status: 200,
        data:
          route === 'library.get'
            ? structuredClone(snapshot)
            : route === 'library.workspace'
              ? structuredClone(workspace)
              : [],
      }
    }),
    artwork: vi.fn(async () => null),
  } as unknown as WinnowBridge
})
afterEach(() => {
  cleanup()
  clients.forEach((client) => client.clear())
  clients = []
  clear()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})
function Fixture({ mode }: { mode: Mode }) {
  const library = useLibrary()
  const state = useAvalonLists(mode, library.data?.lists ?? [], Boolean(library.data))
  const context: ThemeContext = {
    mode,
    page: 'library',
    games: library.data?.games ?? [],
    loading: library.isPending,
    selectedWorkId: null,
    profile: selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon),
    feed: undefined,
    children: null,
    setPage: vi.fn(),
    openGame: vi.fn(),
    toggleFullscreen: vi.fn(),
    renderScreen: () => null,
    actions: { launch: vi.fn() },
    components: {
      Artwork: () => null,
      Impression: ({ children }) => <>{children}</>,
      GameCard: () => null,
      GamePreview: ({ children }) => <>{children}</>,
      ArtworkEffects: ({ children }) => <>{children}</>,
    },
  }
  return (
    <>
      <output data-testid="current-rules">{filterFingerprint(state.filter)}</output>
      <output data-testid="dirty">{String(state.dirty)}</output>
      <AvalonShell {...context}>
        <AvalonLibrary {...context} />
      </AvalonShell>
    </>
  )
}
function mount(mode: Mode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  const view = render(
    <QueryClientProvider client={client}>
      <Fixture mode={mode} />
    </QueryClientProvider>,
  )
  return { ...view, client }
}
const titles = () =>
  [...document.querySelectorAll('[data-avalon-game]')].map((node) => node.getAttribute('data-work-id'))
async function openFilters(mode: Mode, group?: 'PLATFORM' | 'GENRE') {
  if (!screen.queryByRole(mode === 'desktop' ? 'region' : 'dialog', { name: 'Library filters' }))
    fireEvent.click(libraryRole('button', { name: mode === 'desktop' ? 'Filters' : 'Filter & sort' }))
  const panel = within(
    screen.getByRole(mode === 'desktop' ? 'region' : 'dialog', { name: 'Library filters' }),
  )
  if (group && mode === 'fullscreen')
    fireEvent.click(panel.getByRole('button', { name: new RegExp(`^${group} ·`) }))
  else if (group) {
    const summary = panel.getByText(new RegExp(`^${group === 'GENRE' ? 'Genres' : 'Stores'}`), {
      selector: 'summary',
    })
    if (!(summary.parentElement as HTMLDetailsElement).open) fireEvent.click(summary)
  }
  return panel
}
function apply(mode: Mode) {
  if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
  else fireEvent.click(screen.getByRole('button', { name: 'Close filters' }))
}

describe.each(['desktop', 'fullscreen'] as const)('%s source filter option lifecycle', (mode) => {
  it('PlayStation_filter_selects_imported_titles_on_both_surfaces', async () => {
    snapshot.games[1].entries[0].store = 'plugin:psn'
    mount(mode)
    await waitFor(() => expect(titles()).toEqual(['1', '2']))
    const panel = await openFilters(mode, 'PLATFORM')
    fireEvent.click(
      panel.getByRole(mode === 'desktop' ? 'checkbox' : 'button', {
        name: 'PlayStation, 1 matching title',
      }),
    )
    apply(mode)
    await waitFor(() => expect(titles()).toEqual(['2']))
    expect(screen.getByTestId('current-rules').textContent).toBe(
      filterFingerprint({ stores: ['plugin:psn'] }),
    )
    expect(document.querySelector('[data-avalon-game][data-work-id="2"]')?.textContent).toContain('Game 2')
    await openFilters(mode, 'PLATFORM')
    expect(
      screen.getByRole(mode === 'desktop' ? 'checkbox' : 'button', {
        name: 'PlayStation, 1 matching title',
      }),
    ).toBeDefined()
  })

  it('explains every built-in collection with the original description and Invested label', async () => {
    mount(mode)
    await waitFor(() => expect(titles()).toEqual(['1', '2']))
    if (mode === 'fullscreen') {
      await openFilters(mode)
      fireEvent.click(screen.getByRole('button', { name: /^Collection ·/ }))
    }
    const descriptions = [
      ['All games', 'Every title you own.'],
      ['Patched', 'Games with unread updates after a long break from playing.'],
      ['Never played', 'Games with no recorded playtime or last-played date.'],
      ['Started', "Games you've played beyond a brief trial."],
      ['Invested', "Games you've spent a lot of time playing."],
      ['Derelict', 'Games with evidence of closure, delisting or abandoned development.'],
    ]
    for (const [label, description] of descriptions) {
      const button =
        mode === 'fullscreen'
          ? within(screen.getByRole('dialog', { name: 'Library filters' })).getByRole('button', {
              name: label,
            })
          : document.querySelector<HTMLButtonElement>(`.avalon-rail button[title="${description}"]`)!
      expect(button).not.toBeNull()
      expect(button.title).toBe(description)
      expect(button.getAttribute('aria-description')).toBe(description)
      expect(button.textContent).toContain(label)
      if (mode === 'fullscreen') expect(within(button).getByText(description)).toBeDefined()
    }
  })

  it('shows each owned store for a title with Steam, Epic and GOG copies', async () => {
    snapshot.games[0].entries = ['steam', 'epic', 'gog'].map((store, index) => ({
      ...snapshot.games[0].entries[0],
      ownershipId: index + 10,
      store,
    }))
    mount(mode)
    await waitFor(() => expect(titles()).toEqual(['1', '2']))
    if (mode === 'desktop') {
      fireEvent.click(screen.getByRole('button', { name: 'List view' }))
      const chips = document.querySelectorAll('[data-work-id="1"] .avalon-record-stores .avalon-store-chip')
      expect([...chips].map((chip) => chip.textContent)).toEqual(['Steam', 'Epic', 'GOG'])
    } else {
      const game = document.querySelector('[data-work-id="1"]')!
      expect(game.getAttribute('aria-label')).toContain('Owned on Steam, Epic, GOG')
      expect(
        [...game.querySelectorAll('.avalon-store-initials > span')].map((node) => node.textContent),
      ).toEqual(['S', 'E', 'G'])
    }
  })

  it('adds Xbox only after its imported snapshot arrives and preserves both residual title counts', async () => {
    const view = mount(mode)
    await waitFor(() => expect(titles()).toEqual(['1', '2']))
    await openFilters(mode)
    expect(screen.queryByText('Stores', { selector: 'summary' })).toBeNull()
    expect(screen.queryByRole('button', { name: /^PLATFORM ·/ })).toBeNull()
    expect(
      screen.queryByRole(mode === 'desktop' ? 'checkbox' : 'button', { name: 'Xbox, 1 matching title' }),
    ).toBeNull()
    apply(mode)
    snapshot.games[1].entries[0].store = 'plugin:xbox'
    await act(() => view.client.invalidateQueries({ queryKey: ['api', 'library.get'] }))
    const panel = await openFilters(mode, 'PLATFORM')
    const xbox = panel.getByRole(mode === 'desktop' ? 'checkbox' : 'button', {
      name: 'Xbox, 1 matching title',
    }) as HTMLInputElement
    expect(xbox.disabled).toBe(false)
    fireEvent.click(xbox)
    apply(mode)
    expect(titles()).toEqual(['2'])
    expect(screen.getByTestId('current-rules').textContent).toBe(
      filterFingerprint({ stores: ['plugin:xbox'] }),
    )
    await openFilters(mode, 'PLATFORM')
    for (const name of ['Xbox, 1 matching title', 'Steam, 1 matching title'])
      expect(screen.getByRole(mode === 'desktop' ? 'checkbox' : 'button', { name })).toBeDefined()
  })

  it.each(['account', 'hide', 'remove', 'facets'] as const)(
    'keeps current and fresh saved rules restrictive after %s removes matching choices, then clears only RPG',
    async (change) => {
      snapshot.games[1].entries[0].store = 'gog'
      snapshot.lists = [
        {
          id: 10,
          name: 'Steam RPGs',
          isLive: true,
          revision: 'seen',
          releaseIds: [1],
          filter: structuredClone(saved),
        },
      ]
      let view = mount(mode)
      await waitFor(() => expect(titles()).toHaveLength(2))
      selectCollection(10)
      await waitFor(() => expect(titles()).toEqual(['1']))
      expect(screen.getByTestId('dirty').textContent).toBe('false')
      if (change === 'facets') workspace.releaseFacets = []
      else snapshot.games = snapshot.games.filter((game) => game.workId !== 1)
      snapshot.lists[0].releaseIds = []
      await act(async () => {
        await view.client.invalidateQueries({ queryKey: ['api', 'library.get'] })
        await view.client.invalidateQueries({ queryKey: ['api', 'library.workspace'] })
      })
      await waitFor(() => expect(titles()).toEqual([]))
      expect(screen.getByTestId('current-rules').textContent).toBe(filterFingerprint(saved))
      expect(screen.getByTestId('dirty').textContent).toBe('false')
      view.unmount()
      clear()
      view = mount(mode)
      await waitFor(() =>
        expect(
          document.querySelector('[data-avalon-list="10"]') ||
            screen.queryByRole('button', { name: 'My lists' }),
        ).not.toBeNull(),
      )
      selectCollection(10)
      await waitFor(() =>
        expect(screen.getByTestId('current-rules').textContent).toBe(filterFingerprint(saved)),
      )
      expect(titles()).toEqual([])
      expect(screen.getByTestId('dirty').textContent).toBe('false')
      await openFilters(mode, 'GENRE')
      const missing = screen.getByRole(mode === 'desktop' ? 'checkbox' : 'button', {
        name: 'RPG, 0 matching titles',
      }) as HTMLInputElement
      expect(missing.disabled).toBe(false)
      expect(mode === 'desktop' ? missing.checked : missing.getAttribute('aria-pressed') === 'true').toBe(
        true,
      )
      apply(mode)
      // The UI omits a no-op Update action; exercise the same unchanged API save as the source command.
      await request(
        'list.filter',
        { listId: 10 },
        { filter: saved, expectedRevision: snapshot.lists[0].revision },
      )
      expect(snapshot.lists[0].filter).toEqual(saved)
      expect(screen.getByTestId('dirty').textContent).toBe('false')
      await openFilters(mode, 'GENRE')
      fireEvent.click(
        screen.getByRole(mode === 'desktop' ? 'checkbox' : 'button', { name: 'RPG, 0 matching titles' }),
      )
      apply(mode)
      expect(screen.getByTestId('current-rules').textContent).toBe(
        filterFingerprint({ stores: ['steam'], yearFrom: 2000 }),
      )
      expect(screen.getByTestId('dirty').textContent).toBe('true')
      expect(titles()).toEqual(change === 'facets' ? ['1'] : [])
      expect(snapshot.lists[0].filter).toEqual(saved)
      returnToLibrary()
    },
  )
})
