// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useEffect } from 'react'
import type { ApiRequest } from '../src/shared/bridge'
import type { GameList, LibraryGame, LibraryResponse, Mode } from '../src/renderer/api/types'
import { useLibrary } from '../src/renderer/api/hooks'
import { Details } from '../src/renderer/features/Details'
import { AddToListDialog } from '../src/renderer/features/parity-list-prompt'
import { ListEditor } from '../src/renderer/features/LibraryTools'
import { useAvalonLists } from '../src/renderer/themes/avalon-list-state'
import { AvalonLibrary, AvalonShell, avalon } from '../src/renderer/themes/avalon'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeContext } from '../src/shared/theme'
import { useViewState } from '../src/renderer/viewState'
import { libraryRole, returnToLibrary } from './library-controls'
import { clearViewState } from '../src/renderer/viewState'

// ListWriteParityTests/ListPromptParityTests at cf45d9f1127243a987d3cf6e664a32fc767ecb67.
// These IDs and ordered membership match LibraryReadFixtures.Seed rather than demo data.
const games: LibraryGame[] = [1, 2, 3].map((id) => ({
  workId: id,
  title: `Game ${id}`,
  bucket: 'never_played',
  playtimeMinutes: 0,
  entries: [
    {
      ownershipId: id,
      releaseId: id,
      workId: id,
      title: `Game ${id}`,
      store: 'steam',
      installed: false,
      playtimeMinutes: 0,
    },
  ],
}))
const initial: GameList = { id: 1, name: 'Try next', isLive: false, releaseIds: [3, 1], revision: 'r1' }
const ok = (data: unknown) => ({ ok: true, status: 200, data: structuredClone(data) })
const clients: QueryClient[] = []
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
function setup(respond: (input: ApiRequest) => unknown, initialList = initial) {
  const request = vi.fn(
    async (input: ApiRequest) =>
      (await respond(input)) ??
      ok(
        input.route === 'library.get'
          ? { games, lists: [initialList] }
          : input.route === 'game.details'
            ? {
                workId: 2,
                readAtUtc: '2026-09-28T20:00:00Z',
                events: [],
                sessions: {},
                journalEntries: [],
                ratings: [],
                achievements: [],
              }
            : input.route === 'library.workspace'
              ? { works: [], externalIds: [], epicLaunchKeys: {}, pluginActions: {} }
              : input.route === 'preferences.presentation.get'
                ? []
                : {},
      ),
  )
  Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  client.setQueryData(['api', 'library.get'], { games, lists: [structuredClone(initialList)] })
  clients.push(client)
  return { client, request }
}
function Membership({ mode }: { mode: Mode }) {
  const library = useLibrary()
  return (
    <>
      <button onClick={() => void library.refetch()}>Refresh library</button>
      <Details mode={mode} workId={2} />
    </>
  )
}
function OpenListEditor({
  mode,
  observe,
}: {
  mode: Mode
  observe(state: ReturnType<typeof useAvalonLists>): void
}) {
  const library = useLibrary()
  const state = useAvalonLists(mode, library.data?.lists ?? [])
  useEffect(() => observe(state))
  return (
    <>
      <button onClick={() => state.selectList('1')}>Open fixture list</button>
      {state.list && <ListEditor list={state.list} />}
    </>
  )
}
function SelectionFixture({ mode, list, selected }: { mode: Mode; list: string; selected: boolean }) {
  const library = useLibrary()
  const prefix = `avalon:library:${mode}`
  const [, setList] = useViewState(`${prefix}:list`, 'all')
  const [, setSelected] = useViewState<number | null>(`${prefix}:selected`, null)
  const [, setSelection] = useViewState<number[]>(`${prefix}:selection`, [])
  useEffect(() => {
    setList(list)
    setSelected(selected ? 1 : null)
    setSelection(selected ? [1] : [])
  }, [list, selected])
  const context: ThemeContext = {
    mode,
    page: 'library',
    games: library.data?.games ?? [],
    loading: false,
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
      Artwork: () => <span />,
      Impression: ({ children }) => <>{children}</>,
      GameCard: () => null,
      GamePreview: () => null,
      ArtworkEffects: ({ children }) => <>{children}</>,
    },
  }
  return (
    <AvalonShell {...context}>
      <AvalonLibrary {...context} />
    </AvalonShell>
  )
}
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of ['tab', 'editing']) clearViewState(`${mode}:details:2:${key}`)
    clearViewState(`draft:add-list:pending-${mode}:1`)
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
  for (const key of ['draft:list:1', 'draft:list-filter:1', 'list-members:1:sending']) clearViewState(key)
  vi.unstubAllGlobals()
})

it('waits for both independent membership writers before publishing a library refresh', async () => {
  const second: GameList = {
    id: 2,
    name: 'Second list',
    isLive: false,
    releaseIds: [],
    revision: 'second-r1',
  }
  const stored = [structuredClone(initial), second]
  const release = new Map<number, () => void>()
  let reads = 0
  const { client } = setup(async (input) => {
    if (input.route === 'library.get') {
      reads++
      return ok({ games, lists: stored })
    }
    if (input.route === 'list.member.add') {
      const id = Number(input.params!.listId)
      await new Promise<void>((resolve) => release.set(id, resolve))
      const index = stored.findIndex((list) => list.id === id)
      stored[index] = {
        ...stored[index],
        releaseIds: [...stored[index].releaseIds, 2],
        revision: `saved-${id}`,
      }
      return ok(stored[index])
    }
  })
  client.setQueryData(['api', 'library.get'], { games, lists: stored })
  render(
    <QueryClientProvider client={client}>
      <Membership mode="desktop" />
    </QueryClientProvider>,
  )
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Add to Try next' }))
  fireEvent.click(screen.getByRole('checkbox', { name: 'Add to Second list' }))
  let finished = false
  const refresh = client.refetchQueries({ queryKey: ['api', 'library.get'] }).then(() => {
    finished = true
  })
  await act(async () => {
    release.get(1)!()
    await new Promise((resolve) => setTimeout(resolve, 20))
  })
  expect(finished).toBe(false)
  expect(reads).toBe(0)
  await act(async () => {
    release.get(2)!()
    await refresh
  })
  expect(finished).toBe(true)
  expect(reads).toBe(1)
  expect(
    client.getQueryData<LibraryResponse>(['api', 'library.get'])!.lists.map((list) => list.releaseIds),
  ).toEqual([[3, 1, 2], [2]])
})

it('cancels a queued refresh on disposal while the write completes and later reads still run', async () => {
  let release!: () => void
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  let stored = structuredClone(initial),
    reads = 0
  const { client } = setup(async (input) => {
    if (input.route === 'library.get') {
      reads++
      return ok({ games, lists: [stored] })
    }
    if (input.route === 'list.member.add') {
      await held
      stored = { ...stored, releaseIds: [3, 1, 2], revision: 'r2' }
      return ok(stored)
    }
  })
  const view = render(
    <QueryClientProvider client={client}>
      <Membership mode="desktop" />
    </QueryClientProvider>,
  )
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Add to Try next' }))
  const refresh = client.refetchQueries({ queryKey: ['api', 'library.get'] })
  view.unmount()
  await refresh
  expect(reads).toBe(0)
  await act(async () => release())
  await waitFor(() =>
    expect(client.getQueryData<LibraryResponse>(['api', 'library.get'])!.lists[0].revision).toBe('r2'),
  )
  await client.refetchQueries({ queryKey: ['api', 'library.get'] })
  expect(reads).toBe(1)
})

it('releases the refresh barrier when both a write and its saved-state recovery fail', async () => {
  let release!: () => void
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  let offline = true,
    reads = 0
  const { client } = setup(async (input) => {
    if (input.route === 'list.member.add') await held
    if (input.route === 'library.get') reads++
    if (offline && ['list.member.add', 'library.get'].includes(input.route))
      return { ok: false, status: 503, message: 'Disconnected' }
  })
  render(
    <QueryClientProvider client={client}>
      <Membership mode="desktop" />
    </QueryClientProvider>,
  )
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Add to Try next' }))
  const refresh = client.refetchQueries({ queryKey: ['api', 'library.get'] })
  await act(async () => {
    release()
    await refresh
  })
  expect(reads).toBe(2) // The writer's recovery and the queued refresh both settle.
  expect((screen.getByRole('checkbox') as HTMLInputElement).disabled).toBe(true)
  offline = false
  fireEvent.click(screen.getByRole('button', { name: 'Check saved list' }))
  await waitFor(() => expect((screen.getByRole('checkbox') as HTMLInputElement).disabled).toBe(false))
  await client.refetchQueries({ queryKey: ['api', 'library.get'] })
  expect(reads).toBe(4)
})

describe.each(['desktop', 'fullscreen'] as const)('source list transactions in %s', (mode) => {
  it.each(['rename', 'filter', 'delete', 'reorder'] as const)(
    'failed %s retains the exact committed model object and open context',
    async (action) => {
      const list: GameList =
        action === 'filter'
          ? {
              id: 1,
              name: 'Saved rules',
              isLive: true,
              filter: { yearFrom: 2000 },
              releaseIds: [],
              revision: 'r1',
            }
          : { ...initial, releaseIds: [4, 1] }
      const game4 = {
        ...games[0],
        workId: 4,
        title: 'Game 4',
        entries: [{ ...games[0].entries[0], ownershipId: 4, workId: 4, releaseId: 4, title: 'Game 4' }],
      }
      const { client, request } = setup(
        (input) =>
          input.route.startsWith('list.')
            ? { ok: false, status: 500, message: "Couldn't save list changes. Try again." }
            : input.route === 'library.get'
              ? ok({ games: [...games, game4], lists: [list] })
              : undefined,
        list,
      )
      client.setQueryData(['api', 'library.get'], { games: [...games, game4], lists: [list] })
      let state!: ReturnType<typeof useAvalonLists>
      render(
        <QueryClientProvider client={client}>
          <OpenListEditor
            mode={mode}
            observe={(value) => {
              state = value
            }}
          />
        </QueryClientProvider>,
      )
      fireEvent.click(screen.getByRole('button', { name: 'Open fixture list' }))
      const open = state.list!,
        filter = open.filter,
        order = [...open.releaseIds]
      if (action === 'rename') {
        fireEvent.click(screen.getByRole('button', { name: 'Edit' }))
        fireEvent.change(screen.getByLabelText('List name'), { target: { value: 'Rejected' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save list' }))
      } else if (action === 'filter') {
        fireEvent.click(screen.getByRole('button', { name: 'Edit live filters' }))
        fireEvent.change(screen.getByLabelText('Released from'), { target: { value: '2020' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save live filters' }))
      } else if (action === 'delete') {
        fireEvent.click(screen.getByRole('button', { name: 'Delete list…' }))
        fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete list' }))
      } else {
        document.querySelector('details')!.open = true
        fireEvent.click(screen.getByRole('button', { name: 'Move Game 4 later' }))
      }
      await screen.findByRole('alert')
      await waitFor(() => expect(client.isFetching()).toBe(0))
      expect(state.list).toBe(open)
      expect(state.lists).toContain(open)
      expect(open.name).toBe(list.name)
      expect(open.filter).toBe(filter)
      expect(open.releaseIds).toEqual(order)
      expect(client.getQueryData<LibraryResponse>(['api', 'library.get'])!.lists[0]).toBe(open)
      expect(screen.getByRole('alert').textContent).toContain("Couldn't save list changes. Try again.")
      const sent = request.mock.calls.find(([input]) => input.route.startsWith('list.'))![0]
      expect(sent.params).toEqual({ listId: 1 })
      expect((sent.body as { expectedRevision: string }).expectedRevision).toBe('r1')
      if (action === 'rename') {
        expect((screen.getByLabelText('List name') as HTMLInputElement).value).toBe('Rejected')
        expect((screen.getByRole('button', { name: 'Save list' }) as HTMLButtonElement).disabled).toBe(false)
      } else if (action === 'filter') {
        expect((screen.getByLabelText('Released from') as HTMLInputElement).value).toBe('2020')
        expect(
          (screen.getByRole('button', { name: 'Save live filters' }) as HTMLButtonElement).disabled,
        ).toBe(false)
      } else if (action === 'delete')
        expect((screen.getByRole('button', { name: 'Delete list' }) as HTMLButtonElement).disabled).toBe(
          false,
        )
      else
        expect(
          (screen.getByRole('button', { name: 'Move Game 4 later' }) as HTMLButtonElement).disabled,
        ).toBe(false)
    },
  )

  it('offers contextual removal only for a static list with a selected game', async () => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        disconnect() {}
      },
    )
    const staticList = { ...initial, name: 'Static', releaseIds: [1] }
    const liveList: GameList = {
      id: 2,
      name: 'Live',
      isLive: true,
      filter: {},
      releaseIds: [1],
      revision: 'l1',
    }
    const { client, request } = setup((input) =>
      input.route === 'list.member.remove'
        ? ok({ ...staticList, releaseIds: [], revision: 'r2' })
        : input.route === 'library.get'
          ? ok({ games, lists: [staticList, liveList] })
          : undefined,
    )
    client.setQueryData(['api', 'library.get'], { games, lists: [staticList, liveList] })
    const tree = (list: string, selected: boolean) => (
      <QueryClientProvider client={client}>
        <SelectionFixture mode={mode} list={list} selected={selected} />
      </QueryClientProvider>
    )
    const view = render(tree('all', true))
    const remove = () => screen.queryByRole('button', { name: 'Remove from Static' })
    if (mode === 'fullscreen') fireEvent.click(libraryRole('button', { name: 'More' }))
    expect(remove()).toBeNull()
    returnToLibrary()
    view.rerender(tree('2', true))
    if (mode === 'fullscreen') fireEvent.click(libraryRole('button', { name: 'More' }))
    expect(remove()).toBeNull()
    returnToLibrary()
    view.rerender(tree('1', true))
    const action = libraryRole('button', { name: 'Remove from Static' })
    expect((action as HTMLButtonElement).disabled).toBe(false)
    view.rerender(tree('1', false))
    if (mode === 'desktop') expect(remove()).toBeNull()
    else {
      // The fullscreen grid always selects its focused tile when it has games.
      expect(document.querySelector('[data-avalon-game="1"]')?.getAttribute('data-selected')).toBe('true')
      expect((remove() as HTMLButtonElement).disabled).toBe(false)
    }
    expect(client.getQueryData<LibraryResponse>(['api', 'library.get'])!.lists[0].releaseIds).toEqual([1])
    view.rerender(tree('1', true))
    fireEvent.click(libraryRole('button', { name: 'Remove from Static' }))
    await waitFor(() =>
      expect(request.mock.calls.some(([input]) => input.route === 'list.member.remove')).toBe(true),
    )
    expect(request.mock.calls.find(([input]) => input.route === 'list.member.remove')![0]).toMatchObject({
      params: { listId: 1 },
      body: { releaseIds: [1], expectedRevision: 'r1' },
    })
    await waitFor(() => expect(screen.queryByText('Saving list changes…')).toBeNull())
  })

  it.each(['latest', 'failure', 'refresh', 'compensation_failure'] as const)(
    'holds Game 2 membership intent, refresh, committed row identity and retry after %s',
    async (scenario) => {
      let release!: () => void
      const held = new Promise<void>((resolve) => {
        release = resolve
      })
      let stored = structuredClone(initial),
        writes = 0,
        failed = false,
        reads = 0
      const { client, request } = setup(async (input) => {
        if (input.route === 'library.get') {
          reads++
          return ok({ games, lists: [stored] })
        }
        if (!input.route.startsWith('list.member.')) return undefined
        writes++
        if (writes === 1) await held
        const add = input.route === 'list.member.add'
        if (!failed && ((scenario === 'failure' && add) || (scenario === 'compensation_failure' && !add))) {
          failed = true
          return { ok: false, status: 400, message: 'Injected list write failure' }
        }
        stored = {
          ...stored,
          releaseIds: add ? [...stored.releaseIds, 2] : stored.releaseIds.filter((id) => id !== 2),
          revision: `r${writes + 1}`,
        }
        return ok(stored)
      })
      render(
        <QueryClientProvider client={client}>
          <Membership mode={mode} />
        </QueryClientProvider>,
      )
      const row = (await screen.findByRole('checkbox', { name: 'Add to Try next' })) as HTMLInputElement
      expect(screen.getAllByRole('checkbox')).toEqual([row])
      fireEvent.click(row)
      await screen.findByText('Saving list changes…')
      expect(row.disabled).toBe(false)
      expect(stored.releaseIds).toEqual([3, 1])
      let refreshed = false,
        refresh: Promise<unknown> | undefined
      if (scenario === 'refresh') {
        act(() => {
          refresh = client.refetchQueries({ queryKey: ['api', 'library.get'] }).then(() => {
            refreshed = true
          })
        })
        // Let an ungated bridge read settle: the source LoadCommand must still await the held write.
        await act(async () => {
          await new Promise((resolve) => setTimeout(resolve, 20))
        })
        expect(refreshed).toBe(false)
        expect(reads).toBe(0)
      }
      if (scenario === 'latest' || scenario === 'compensation_failure') fireEvent.click(row)
      await act(async () => {
        release()
        await refresh
      })
      await waitFor(() => expect(screen.queryByText('Saving list changes…')).toBeNull())
      const committed = scenario === 'refresh' || scenario === 'compensation_failure'
      expect(screen.getByRole('checkbox')).toBe(row)
      expect(row.checked).toBe(committed)
      expect(stored.releaseIds).toEqual(committed ? [3, 1, 2] : [3, 1])
      expect(client.getQueryData<LibraryResponse>(['api', 'library.get'])!.lists[0].releaseIds).toEqual(
        stored.releaseIds,
      )
      const writesSent = request.mock.calls
        .filter(([input]) => input.route.startsWith('list.member.'))
        .map(([input]) => input)
      expect(writesSent[0].body).toEqual({ releaseIds: [2], expectedRevision: 'r1' })
      if (scenario === 'latest' || scenario === 'compensation_failure')
        expect(writesSent[1].body).toEqual({ releaseIds: [2], expectedRevision: 'r2' })
      if (scenario === 'refresh') {
        expect(refreshed).toBe(true)
        expect(reads).toBe(1)
      }
      if (scenario.includes('failure')) {
        expect(screen.getByRole('alert').textContent).toBe("Couldn't save list changes. Try again.")
        fireEvent.click(row)
        await waitFor(() => expect(screen.queryByText('Saving list changes…')).toBeNull())
        expect(screen.queryByRole('alert')).toBeNull()
        expect(row.checked).toBe(!committed)
        expect(stored.releaseIds.includes(2)).toBe(!committed)
        expect(
          client.getQueryData<LibraryResponse>(['api', 'library.get'])!.lists[0].releaseIds.includes(2),
        ).toBe(!committed)
      } else expect(screen.queryByRole('alert')).toBeNull()
    },
  )

  it.each([false, true])(
    'locks every pending prompt action, ignores duplicate submission, and closes only a live page (disposed=%s)',
    async (disposed) => {
      let release!: (value: unknown) => void
      const { client, request } = setup((input) =>
        input.route === 'list.create'
          ? new Promise((resolve) => {
              release = resolve
            })
          : undefined,
      )
      const close = vi.fn()
      const view = render(
        <QueryClientProvider client={client}>
          <AddToListDialog
            mode={mode}
            games={[games[0]]}
            origin={`pending-${mode}`}
            onClose={close}
            restoreFocus={vi.fn()}
          />
        </QueryClientProvider>,
      )
      const dialog = screen.getByRole('dialog')
      fireEvent.change(within(dialog).getByLabelText('New list name'), { target: { value: 'Later' } })
      fireEvent.click(within(dialog).getByRole('button', { name: 'New list' }))
      await within(dialog).findByText('Saving list changes…')
      expect(
        within(dialog)
          .getAllByRole('button')
          .every((button) => (button as HTMLButtonElement).disabled),
      ).toBe(true)
      expect((within(dialog).getByLabelText('New list name') as HTMLInputElement).disabled).toBe(true)
      fireEvent.keyDown(dialog, { key: 'Escape' })
      fireEvent.submit(dialog.querySelector('form')!)
      expect(close).not.toHaveBeenCalled()
      expect(request.mock.calls.filter(([input]) => input.route === 'list.create')).toHaveLength(1)
      if (disposed) view.unmount()
      await act(async () =>
        release(ok({ id: 2, name: 'Later', isLive: false, releaseIds: [1], revision: 'new' })),
      )
      await waitFor(() =>
        expect(
          client.getQueryData<LibraryResponse>(['api', 'library.get'])!.lists.find((list) => list.id === 2)
            ?.releaseIds,
        ).toEqual([1]),
      )
      expect(close).toHaveBeenCalledTimes(disposed ? 0 : 1)
      expect(request.mock.calls.filter(([input]) => input.route === 'list.create')).toHaveLength(1)
      if (!disposed)
        expect((within(dialog).getByLabelText('New list name') as HTMLInputElement).value).toBe('')
    },
  )
})
