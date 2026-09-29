// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { useLibrary, useWorkspace, useDetails } from '../src/renderer/api/hooks'
import { refreshSnapshots } from '../src/renderer/refresh'
import type { ApiRequest } from '../src/shared/bridge'

const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
})
const game = (id: number, summary = 'Old fact') => ({
  workId: id,
  title: `Game ${id}`,
  summary,
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
})
const workspace = { works: [], externalIds: [], epicLaunchKeys: {}, pluginActions: {} }
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
function Fixture({ workId = 1 }: { workId?: number }) {
  const library = useLibrary(),
    workspace = useWorkspace(),
    details = useDetails(workId)
  return (
    <>
      <button onClick={() => void library.refetch()}>Reload library</button>
      <output aria-label="Count">{library.data?.games.length ?? 0}</output>
      <output aria-label="Games">
        {library.data?.games.map((game) => `${game.workId}:${game.summary}`).join(',')}
      </output>
      <output aria-label="Workspace">{JSON.stringify(workspace.data)}</output>
      <output aria-label="Details">{JSON.stringify(details.data)}</output>
    </>
  )
}
function mount(handler: (input: ApiRequest) => unknown) {
  const cancelRequest = vi.fn(async () => undefined)
  const request = vi.fn(async (input: ApiRequest) => ({ ok: true, status: 200, data: await handler(input) }))
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, cancelRequest } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  const view = render(
    <QueryClientProvider client={client}>
      <Fixture />
    </QueryClientProvider>,
  )
  return { client, request, cancelRequest, ...view }
}

it('a slow manual read cannot overwrite a newer committed visibility and metadata snapshot', async () => {
  const old = deferred<unknown>()
  let reads = 0
  const { client, request, cancelRequest } = mount((input) =>
    input.route === 'library.get'
      ? ++reads === 1
        ? { games: [game(1), game(2)], lists: [] }
        : reads === 2
          ? old.promise
          : { games: [game(1, 'Current summary')], lists: [] }
      : input.route === 'library.workspace'
        ? workspace
        : { workId: 1 },
  )
  await waitFor(() => expect(screen.getByLabelText('Count').textContent).toBe('2'))
  fireEvent.click(screen.getByRole('button', { name: 'Reload library' }))
  await waitFor(() => expect(reads).toBe(2))
  await act(async () => {
    await client.invalidateQueries({ queryKey: ['api', 'library.get'] })
  })
  await waitFor(() => expect(screen.getByLabelText('Count').textContent).toBe('1'))
  expect(screen.getByLabelText('Games').textContent).toBe('1:Current summary')
  const retired = request.mock.calls.filter(([input]) => input.route === 'library.get')[1][0].requestId
  expect(retired).toMatch(/^[a-f0-9]{32}$/)
  expect(cancelRequest).toHaveBeenCalledWith(retired)
  await act(async () => {
    old.resolve({ games: [game(1), game(2)], lists: [] })
    await old.promise
  })
  expect(screen.getByLabelText('Count').textContent).toBe('1')
  expect(screen.getByLabelText('Games').textContent).toBe('1:Current summary')
})

it.each(['cancel', 'unmount'])(
  'a reader that ignores %s cannot publish its late library, workspace or details',
  async (action) => {
    const pending = new Map<string, ReturnType<typeof deferred<unknown>>>()
    const { client, unmount, request, cancelRequest } = mount((input) => {
      const read = deferred<unknown>()
      pending.set(input.route, read)
      return read.promise
    })
    await waitFor(() => expect(pending.size).toBe(3))
    if (action === 'cancel') await act(async () => client.cancelQueries())
    else unmount()
    for (const [input] of request.mock.calls) expect(cancelRequest).toHaveBeenCalledWith(input.requestId)
    await act(async () => {
      pending.get('library.get')!.resolve({ games: [game(1)], lists: [] })
      pending.get('library.workspace')!.resolve(workspace)
      pending.get('game.details')!.resolve({ workId: 1 })
      await Promise.all([...pending.values()].map((read) => read.promise))
    })
    for (const query of client.getQueryCache().getAll()) expect(query.state.data).toBeUndefined()
  },
)

it('a required refresh starts immediately and retires an unfinished first read', async () => {
  const old = deferred<unknown>()
  let reads = 0
  const { client } = mount((input) =>
    input.route === 'library.get'
      ? ++reads === 1
        ? old.promise
        : { games: [game(1, 'Fresh')], lists: [] }
      : input.route === 'library.workspace'
        ? workspace
        : { workId: 1 },
  )
  await waitFor(() => expect(reads).toBe(1))
  await act(async () => refreshSnapshots(client))
  expect(reads).toBe(2)
  expect(
    client.getQueryData<{ games: { summary: string }[] }>(['api', 'library.get'])?.games[0].summary,
  ).toBe('Fresh')
  // React Query batches observer notifications after the completed cache publication.
  await waitFor(() => expect(screen.getByLabelText('Games').textContent).toBe('1:Fresh'))
  await act(async () => {
    old.resolve({ games: [game(2)], lists: [] })
    await old.promise
  })
  expect(screen.getByLabelText('Games').textContent).toBe('1:Fresh')
})
