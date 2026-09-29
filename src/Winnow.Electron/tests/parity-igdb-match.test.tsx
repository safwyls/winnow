// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { IgdbMatch } from '../src/renderer/features/igdb-match'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'

const candidate = {
  igdbId: 5678,
  name: 'Prey 2017',
  coverUrl: 'https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg',
  firstReleaseYear: 2017,
  platforms: ['PC', 'PlayStation 4'],
}
const holder = {
  workId: 77,
  title: 'Existing Prey',
  coverUrl: 'https://images.igdb.com/igdb/image/upload/t_cover_big/coholder.jpg',
  firstReleaseYear: 2017,
}
const clients: QueryClient[] = []
const ok = (data: unknown) => ({ ok: true, status: 200, data })
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  for (const key of [
    'draft:igdb:42',
    ...['holder', 'offer-revision', 'sending', 'operation', 'message', 'error'].map(
      (name) => `igdb:42:${name}`,
    ),
  ])
    clearViewState(key)
})
function mount(
  options: {
    title?: string
    pinned?: boolean
    results?: (typeof candidate)[]
    outcome?: string
    claimant?: typeof holder | null
    clear?: boolean
    handler?: (input: ApiRequest) => unknown
  } = {},
) {
  let pin = options.pinned ? { igdbId: 5678 } : null
  let revision = 'match-1'
  const onChanged = vi.fn()
  const request = vi.fn(async (input: ApiRequest) => {
    const custom = await options.handler?.(input)
    if (custom !== undefined) return custom
    switch (input.route) {
      case 'metadata.igdb':
        return ok({ workId: 42, mappingRevision: 1, revision, pin })
      case 'metadata.search':
        return ok(options.results ?? [candidate])
      case 'metadata.candidate':
        return ok(candidate)
      case 'metadata.assign': {
        const outcome = options.outcome ?? 'Assigned'
        if (outcome === 'Assigned') {
          pin = { igdbId: 5678 }
          revision = 'match-2'
        }
        return ok({ outcome })
      }
      case 'metadata.claiming':
        return options.claimant === null ? { ok: false, status: 404 } : ok(options.claimant ?? holder)
      case 'identity.get':
        return ok({ revision: 'review-1' })
      case 'identity.link':
        return ok({ revision: 'review-2', actId: 31 })
      case 'metadata.clear': {
        if (options.clear !== false) {
          pin = null
          revision = 'match-2'
        }
        return ok(options.clear !== false)
      }
      default:
        throw new Error(`Unexpected route ${input.route}`)
    }
  })
  const artwork = vi.fn().mockResolvedValue('data:image/png;base64,aA==')
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, artwork } })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } },
  })
  clients.push(client)
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  const node = (
    <QueryClientProvider client={client}>
      <IgdbMatch workId={42} title={options.title ?? 'Library title'} onChanged={onChanged} />
    </QueryClientProvider>
  )
  return { ...render(node), node, request, artwork, onChanged, client, invalidate }
}
async function search(query?: string) {
  const button = screen.getByRole('button', { name: 'Search IGDB' })
  if (query !== undefined)
    fireEvent.change(screen.getByLabelText('Game title or IGDB ID'), { target: { value: query } })
  await waitFor(() => expect((button as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(button)
  await waitFor(() => expect(screen.queryByText('Searching IGDB…')).toBeNull())
}
async function offer() {
  await search()
  fireEvent.click(screen.getByRole('button', { name: 'Use this match' }))
  await screen.findByText('Is this the same game as Existing Prey?')
}
describe('original IGDB match contracts', () => {
  it('starts with the library title and refuses blank searches without sending a request', async () => {
    const { request } = mount()
    expect((screen.getByLabelText('Game title or IGDB ID') as HTMLInputElement).value).toBe('Library title')
    fireEvent.change(screen.getByLabelText('Game title or IGDB ID'), { target: { value: '  ' } })
    expect((screen.getByRole('button', { name: 'Search IGDB' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.submit(screen.getByLabelText('Game title or IGDB ID').closest('form')!)
    expect(request.mock.calls.every(([input]) => input.route === 'metadata.igdb')).toBe(true)
  })
  it('shows cover year and platforms with a tooltip and no empty detail line for sparse rows', async () => {
    const { artwork } = mount({
      results: [candidate, { igdbId: 12, name: 'Sparse game', platforms: [] } as unknown as typeof candidate],
    })
    await search()
    const rich = screen.getByRole('heading', { name: 'Prey 2017' }).closest('article')!
    expect(within(rich).getByText('2017 · PC, PlayStation 4').title).toBe('PC, PlayStation 4')
    await waitFor(() => expect(artwork).toHaveBeenCalledWith('igdb', 'co2abc', 100))
    expect(
      screen.getByRole('heading', { name: 'Sparse game' }).closest('article')!.querySelector('p'),
    ).toBeNull()
    expect(within(rich).getByRole('button', { name: 'Use this match' }).title).toBe('Use Prey 2017')
  })
  it('announces a pending search in words and removes its status when an empty result arrives', async () => {
    let finish!: (value: unknown) => void
    const pending = new Promise((resolve) => {
      finish = resolve
    })
    mount({ handler: (input) => (input.route === 'metadata.search' ? pending : undefined) })
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Search IGDB' }) as HTMLButtonElement).disabled).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Search IGDB' }))
    expect(screen.getByRole('button', { name: 'Searching IGDB…' })).toBeTruthy()
    await act(async () => finish(ok([])))
    expect(await screen.findByText('No matching games. Try a different title or an IGDB ID.')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.queryByText('Searching IGDB…')).toBeNull()
  })
  it.each(['5678', '005678'])(
    'prepends one marked ID hit for %s while retaining title results and their details',
    async (query) => {
      const { request } = mount({ results: [candidate, { ...candidate, igdbId: 1234, name: 'Prey 2006' }] })
      await search(query)
      expect(request).toHaveBeenCalledWith({
        route: 'metadata.candidate',
        params: { igdbId: 5678 },
        body: undefined,
      })
      const rows = [...document.querySelectorAll('.igdb-candidates article')]
      expect(rows).toHaveLength(2)
      expect(rows[0].textContent).toContain('ID MATCH')
      expect(rows[0].textContent).toContain('Prey 2017')
      expect(rows[1].textContent).not.toContain('ID MATCH')
      expect(within(rows[0] as HTMLElement).getByText('2017 · PC, PlayStation 4')).toBeTruthy()
    },
  )
  it.each(['1979 Revolution', '7 Days to Die', '0', '-1', '9007199254740992', '１２'])(
    'uses only title lookup for %s',
    async (title) => {
      const { request } = mount({ title })
      await search()
      expect(request.mock.calls.some(([input]) => input.route === 'metadata.candidate')).toBe(false)
      expect(screen.queryByText('ID MATCH')).toBeNull()
    },
  )
  it('reports an ID miss beside the surviving title results without presenting a failure', async () => {
    mount({
      handler: (input) => (input.route === 'metadata.candidate' ? { ok: false, status: 404 } : undefined),
    })
    await search('999999')
    expect(screen.getByText('No IGDB entry uses that ID. Title matches are shown below.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Use this match' })).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
  })
  it('pins a chosen entry with the searched revision and carries its title through a library and artwork refresh', async () => {
    const { request, onChanged, invalidate } = mount()
    await search()
    fireEvent.click(screen.getByRole('button', { name: 'Use this match' }))
    await waitFor(() => expect(onChanged).toHaveBeenCalledWith('Now using Prey 2017.'))
    expect(request).toHaveBeenCalledWith({
      route: 'metadata.assign',
      params: { workId: 42 },
      body: { igdbId: 5678, expectedRevision: 'match-1' },
    })
    expect(screen.getByRole('button', { name: 'Return to automatic matching' })).toBeTruthy()
    expect(invalidate.mock.calls.map(([value]) => value?.queryKey)).toEqual([
      ['api'],
      ['artwork'],
      ['artwork-image'],
    ])
    expect(screen.queryByRole('alert')).toBeNull()
  })
  const refusalCopy = {
    WorkNotFound: 'That game is no longer in your library.',
    MetadataUnavailable: 'IGDB has no metadata for that entry. Nothing changed.',
    IgdbIdClaimedByAnotherWork: 'Another game in your library already uses that IGDB entry.',
    MappingChanged:
      "This game's match changed while the metadata loaded. Reopen the match picker to choose again.",
    IdentifierHistoryUnavailable:
      'The old ID has no recorded origin. Keep this entry, or add the corrected game separately in Library settings.',
    StorefrontObservation:
      'A store entry uses the old ID. Keep this entry, or add the corrected game separately in Library settings.',
    Failed: "Couldn't save that. Nothing changed.",
  }
  it.each(Object.entries(refusalCopy))(
    'keeps %s as its own refusal without pinning or refreshing the library',
    async (outcome, copy) => {
      const { onChanged, invalidate } = mount({ outcome, claimant: null })
      await search()
      fireEvent.click(screen.getByRole('button', { name: 'Use this match' }))
      expect((await screen.findByRole('alert')).textContent).toBe(copy)
      await waitFor(() =>
        expect((screen.getByRole('button', { name: 'Use this match' }) as HTMLButtonElement).disabled).toBe(
          false,
        ),
      )
      expect(screen.queryByRole('button', { name: 'Return to automatic matching' })).toBeNull()
      expect(onChanged).not.toHaveBeenCalled()
      expect(invalidate).not.toHaveBeenCalled()
    },
  )
  it('offers a named holder with cover and year and clears the collision alert', async () => {
    const { artwork } = mount({ outcome: 'IgdbIdClaimedByAnotherWork' })
    await offer()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByText('2017', { exact: true })).toBeTruthy()
    await waitFor(() => expect(artwork).toHaveBeenCalledWith('igdb', 'coholder', 100))
    expect(screen.getByRole('button', { name: 'Yes, group these editions' }).title).toBe(
      'Link as the same game as Existing Prey',
    )
  })
  it('links under the holder using the review revision without pinning the child', async () => {
    const { request, onChanged } = mount({ outcome: 'IgdbIdClaimedByAnotherWork' })
    await offer()
    fireEvent.click(screen.getByRole('button', { name: 'Yes, group these editions' }))
    await waitFor(() => expect(onChanged).toHaveBeenCalledWith('Linked with Existing Prey.'))
    expect(request).toHaveBeenCalledWith({
      route: 'identity.link',
      params: undefined,
      body: {
        expectedRevision: 'review-1',
        parentWorkId: 77,
        childWorkIds: [42],
        kind: 'same_game',
        relationLabel: null,
        rejectedCandidateIds: [],
        refusedPairs: [],
      },
    })
    expect(request.mock.calls.filter(([input]) => input.route === 'metadata.assign')).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Return to automatic matching' })).toBeNull()
    expect(screen.queryByText('Is this the same game as Existing Prey?')).toBeNull()
  })
  it('declines without a write and restores the refusal while keeping candidates', async () => {
    const { request, onChanged } = mount({ outcome: 'IgdbIdClaimedByAnotherWork' })
    await offer()
    const count = request.mock.calls.length
    fireEvent.click(screen.getByRole('button', { name: 'Keep them separate' }))
    expect(screen.getByRole('alert').textContent).toBe(refusalCopy.IgdbIdClaimedByAnotherWork)
    expect(screen.getByRole('button', { name: 'Use this match' })).toBeTruthy()
    expect(request.mock.calls).toHaveLength(count)
    expect(onChanged).not.toHaveBeenCalled()
  })
  it('gives the claim its own accessible copy and announces linking until the write settles', async () => {
    let finish!: (value: unknown) => void
    const pending = new Promise((resolve) => {
      finish = resolve
    })
    const { onChanged } = mount({
      outcome: 'IgdbIdClaimedByAnotherWork',
      handler: (input) => (input.route === 'identity.link' ? pending : undefined),
    })
    await offer()
    const accept = screen.getByRole('button', { name: 'Yes, group these editions' })
    const labels = [
      screen.getByText('Is this the same game as Existing Prey?').textContent,
      accept.textContent,
      accept.title,
      screen.getByRole('button', { name: 'Keep them separate' }).textContent,
    ]
    expect(new Set(labels).size).toBe(4)
    expect(labels.every((label) => Boolean(label?.trim()))).toBe(true)
    fireEvent.click(accept)
    expect(screen.getByRole('status').textContent).toBe('Linking…')
    expect((accept as HTMLButtonElement).disabled).toBe(true)
    await act(async () => finish(ok({ revision: 'review-2', actId: 2 })))
    await waitFor(() => expect(onChanged).toHaveBeenCalledWith('Linked with Existing Prey.'))
    expect(screen.queryByText('Linking…')).toBeNull()
  })
  it('refuses a holder that is the same work without offering an invalid link', async () => {
    mount({ outcome: 'IgdbIdClaimedByAnotherWork', claimant: { ...holder, workId: 42 } })
    await search()
    fireEvent.click(screen.getByRole('button', { name: 'Use this match' }))
    await screen.findByRole('alert')
    expect(screen.queryByRole('button', { name: 'Yes, group these editions' })).toBeNull()
  })
  it('keeps the collision refusal when the backend has no identity review capability', async () => {
    mount({
      outcome: 'IgdbIdClaimedByAnotherWork',
      handler: (input) => (input.route === 'identity.get' ? { ok: false, status: 404 } : undefined),
    })
    await search()
    fireEvent.click(screen.getByRole('button', { name: 'Use this match' }))
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Use this match' }) as HTMLButtonElement).disabled).toBe(
        false,
      ),
    )
    expect(screen.getByRole('alert').textContent).toBe(refusalCopy.IgdbIdClaimedByAnotherWork)
    expect(screen.queryByRole('button', { name: 'Yes, group these editions' })).toBeNull()
  })
  it('preserves candidates after a revision conflict and requires explicit refresh before retrying', async () => {
    let changed = false
    const { request, onChanged } = mount({
      handler: (input) => {
        if (input.route === 'metadata.assign' && !changed) {
          changed = true
          return { ok: false, status: 409, message: 'The saved match changed.' }
        }
        if (input.route === 'metadata.igdb' && changed) return ok({ revision: 'external-2', pin: null })
      },
    })
    await search()
    fireEvent.click(screen.getByRole('button', { name: 'Use this match' }))
    await screen.findByRole('button', { name: 'Refresh saved match' })
    expect((screen.getByRole('button', { name: 'Use this match' }) as HTMLButtonElement).disabled).toBe(true)
    expect(onChanged).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Refresh saved match' }))
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Use this match' }) as HTMLButtonElement).disabled).toBe(
        false,
      ),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Use this match' }))
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1))
    expect(
      request.mock.calls.filter(([input]) => input.route === 'metadata.assign')[1][0].body,
    ).toMatchObject({ expectedRevision: 'external-2' })
  })
  it('blocks duplicate writes and clearing while a pin assignment is pending', async () => {
    let finish!: (value: unknown) => void
    const pending = new Promise((resolve) => {
      finish = resolve
    })
    const { request } = mount({
      pinned: true,
      handler: (input) => (input.route === 'metadata.assign' ? pending : undefined),
    })
    await search()
    const assign = screen.getByRole('button', { name: 'Use this match' })
    fireEvent.click(assign)
    expect(screen.getByRole('status').textContent).toBe('Saving…')
    expect(
      (screen.getByRole('button', { name: 'Return to automatic matching' }) as HTMLButtonElement).disabled,
    ).toBe(true)
    fireEvent.click(assign)
    expect(request.mock.calls.filter(([input]) => input.route === 'metadata.assign')).toHaveLength(1)
    await act(async () => finish(ok({ outcome: 'Failed' })))
    await screen.findByRole('alert')
    expect(screen.queryByText('Saving…')).toBeNull()
  })
  it('keeps a refused link offer for retry and reports the failure with no success callback', async () => {
    const { onChanged } = mount({
      outcome: 'IgdbIdClaimedByAnotherWork',
      handler: (input) =>
        input.route === 'identity.link'
          ? { ok: false, status: 400, message: "Couldn't link those. Nothing changed." }
          : undefined,
    })
    await offer()
    fireEvent.click(screen.getByRole('button', { name: 'Yes, group these editions' }))
    expect((await screen.findByRole('alert')).textContent).toContain("Couldn't link those")
    expect(screen.getByRole('button', { name: 'Yes, group these editions' })).toBeTruthy()
    expect(onChanged).not.toHaveBeenCalled()
  })
  it('preserves the query candidates and claim when the existing editor is entered again', async () => {
    const { unmount, node, request } = mount({ outcome: 'IgdbIdClaimedByAnotherWork' })
    await offer()
    unmount()
    render(node)
    expect(screen.getByText('Is this the same game as Existing Prey?')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Use this match' })).toBeTruthy()
    expect((screen.getByLabelText('Game title or IGDB ID') as HTMLInputElement).value).toBe('Library title')
    expect(request.mock.calls.filter(([input]) => input.route === 'metadata.search')).toHaveLength(1)
  })
  it('offers no clear control for automatic matching', async () => {
    mount()
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Search IGDB' }) as HTMLButtonElement).disabled).toBe(false),
    )
    expect(screen.queryByRole('button', { name: 'Return to automatic matching' })).toBeNull()
  })
  it('returns a pin to automatic matching and refreshes its artwork with a carried confirmation', async () => {
    const { onChanged, request } = mount({ pinned: true })
    fireEvent.click(await screen.findByRole('button', { name: 'Return to automatic matching' }))
    await waitFor(() => expect(onChanged).toHaveBeenCalledWith('Returned to automatic metadata matching.'))
    expect(screen.queryByRole('button', { name: 'Return to automatic matching' })).toBeNull()
    expect(request).toHaveBeenCalledWith({
      route: 'metadata.clear',
      params: { workId: 42 },
      body: { expectedRevision: 'match-1' },
    })
  })
  it('keeps a refused clear pinned and available with an alert and no refresh', async () => {
    const { onChanged, invalidate } = mount({ pinned: true, clear: false })
    fireEvent.click(await screen.findByRole('button', { name: 'Return to automatic matching' }))
    expect((await screen.findByRole('alert')).textContent).toBe("Couldn't clear that. Nothing changed.")
    expect(screen.getByRole('button', { name: 'Return to automatic matching' })).toBeTruthy()
    expect(onChanged).not.toHaveBeenCalled()
    expect(invalidate).not.toHaveBeenCalled()
  })
})
