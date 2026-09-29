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

const preferences = vi.hoisted(() => ({ values: { DefaultSort: 'NameAscending' } }))
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
      'sort-before-list',
      'list-base',
      'view',
      'density',
      'selected',
      'selection',
      'rules',
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

describe.each(['desktop', 'fullscreen'] as const)('list browsing in %s', (mode) => {
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
