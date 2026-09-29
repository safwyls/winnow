// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { feedSchema, useFeed } from '../src/renderer/api/hooks'
import { RefreshQueue, refreshSnapshots } from '../src/renderer/refresh'

const shelf = (id: string) => ({
  id,
  title: id,
  blurb: 'Pitch.',
  items: [{ ownershipId: 1, releaseId: 1, title: 'Game', reason: 'Reason.' }],
  reserve: [],
  supportsFeedback: true,
})
const primary = { shelves: [shelf('Primary')], candidateCount: 1, confidence: 0, failed: false }
function probe(handler: (route: string) => Promise<unknown>) {
  const calls: { route: string; requestId?: string }[] = []
  const cancel = vi.fn(async (_id: string) => true)
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      cancelRequest: cancel,
      request: async (input: { route: string; requestId?: string }) => {
        calls.push(input)
        return {
          ok: true,
          status: 200,
          data: await handler(input.route),
        }
      },
    },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  function Probe() {
    const feed = useFeed()
    return (
      <div>
        {feed.data?.shelves.map((shelf) => (
          <span key={shelf.id}>{shelf.title}</span>
        ))}
      </div>
    )
  }
  const view = render(
    <QueryClientProvider client={client}>
      <Probe />
    </QueryClientProvider>,
  )
  return { client, calls, cancel, ...view }
}
afterEach(() => cleanup())

it('publishes primary recommendations without waiting for an optional provider', async () => {
  let finish!: (value: unknown) => void
  probe(async (route) =>
    route === 'feed.get'
      ? primary
      : new Promise((resolve) => {
          finish = resolve
        }),
  )
  await screen.findByText('Primary')
  expect(screen.queryByText('Optional')).toBeNull()
  await waitFor(() => expect(finish).toBeDefined())
  await act(async () => finish({ shelves: [shelf('Optional')], candidateCount: 1 }))
  expect(await screen.findByText('Optional')).toBeDefined()
})

it('does not append the optional result of a primary pass superseded by a reload', async () => {
  const finish: ((value: unknown) => void)[] = []
  let generation = 0
  const { client } = probe(async (route) =>
    route === 'feed.get'
      ? { ...primary, shelves: [shelf(`Primary ${++generation}`)] }
      : new Promise((resolve) => finish.push(resolve)),
  )
  await screen.findByText('Primary 1')
  await waitFor(() => expect(finish).toHaveLength(1))
  await act(async () => {
    await client.invalidateQueries({ queryKey: ['api', 'feed.get'] })
  })
  await screen.findByText('Primary 2')
  await waitFor(() => expect(finish).toHaveLength(2))
  await act(async () => finish[0]({ shelves: [shelf('Stale optional')], candidateCount: 1 }))
  expect(screen.queryByText('Stale optional')).toBeNull()
  await act(async () => finish[1]({ shelves: [shelf('Current optional')], candidateCount: 1 }))
  expect(await screen.findByText('Current optional')).toBeDefined()
})

it.each(['reject', 'malformed'] as const)(
  'keeps primary recommendations after a %s optional response',
  async (kind) => {
    const optional = vi.fn(async () => {
      if (kind === 'reject') throw Error('Provider offline')
      return { shelves: [{ id: 'Bad' }], candidateCount: 1 }
    })
    const { client } = probe(async (route) => (route === 'feed.get' ? primary : optional()))
    expect(await screen.findByText('Primary')).toBeDefined()
    await waitFor(() =>
      expect(
        client
          .getQueryCache()
          .getAll()
          .some((query) => query.queryKey[1] === 'feed.supplement' && query.state.status === 'error'),
      ).toBe(true),
    )
    expect(screen.getByText('Primary')).toBeDefined()
    expect(screen.queryByText('Bad')).toBeNull()
  },
)

it('validates reserve cards with the same contract as visible cards', () => {
  expect(
    feedSchema.safeParse({ ...primary, shelves: [{ ...shelf('Primary'), reserve: [{}] }] }).success,
  ).toBe(false)
  expect(feedSchema.parse(primary)).toEqual(primary)
})

it.each(['feed.get', 'feed.supplement'])(
  'cancels an unfinished %s on disposal and ignores its late result',
  async (route) => {
    let finish!: (value: unknown) => void
    const view = probe(async (current) =>
      current === route
        ? new Promise((resolve) => {
            finish = resolve
          })
        : primary,
    )
    await waitFor(() => expect(finish).toBeDefined())
    const call = view.calls.find((call) => call.route === route)!
    expect(call.requestId).toMatch(/^[0-9a-f]{32}$/)
    view.unmount()
    expect(view.cancel).toHaveBeenCalledWith(call.requestId)
    await act(async () => finish(primary))
    expect(
      view.client
        .getQueryCache()
        .getAll()
        .find((query) => query.queryKey[1] === route)?.state.data,
    ).toBeUndefined()
    expect(screen.queryByText('Primary')).toBeNull()
    view.client.clear()
  },
)

it('replays a burst of library changes once with the final feed and never waits for an optional provider', async () => {
  const completions: ((value: unknown) => void)[] = []
  let primaryCalls = 0
  const view = probe(async (route) => {
    if (route === 'feed.supplement') return new Promise((resolve) => completions.push(resolve))
    primaryCalls++
    return primaryCalls === 1 ? primary : { ...primary, shelves: [shelf('Refreshed')] }
  })
  await screen.findByText('Primary')
  await waitFor(() => expect(completions).toHaveLength(1))
  const queue = new RefreshQueue(() => refreshSnapshots(view.client))
  await act(async () => {
    queue.request()
    queue.request()
    queue.request()
  })
  await screen.findByText('Refreshed')
  await waitFor(() => expect(primaryCalls).toBe(3))
  expect(screen.queryByText('Primary')).toBeNull()
  await act(async () => completions[0]({ shelves: [shelf('Obsolete optional')], candidateCount: 1 }))
  expect(screen.queryByText('Obsolete optional')).toBeNull()
  queue.dispose()
  view.unmount()
  view.client.clear()
})

it('keeps completed primary and optional cards on screen while a replacement primary pass is in flight', async () => {
  let calls = 0,
    finish!: (value: unknown) => void
  const view = probe(async (route) => {
    if (route === 'feed.supplement') return { shelves: [shelf('Optional')], candidateCount: 1 }
    return ++calls === 1
      ? primary
      : new Promise((resolve) => {
          finish = resolve
        })
  })
  const optional = await screen.findByText('Optional'),
    first = screen.getByText('Primary')
  let loading!: Promise<void>
  await act(async () => {
    loading = view.client.invalidateQueries({ queryKey: ['api', 'feed.get'] })
  })
  await waitFor(() => expect(finish).toBeDefined())
  expect(screen.getByText('Primary')).toBe(first)
  expect(screen.getByText('Optional')).toBe(optional)
  await act(async () => {
    finish({ ...primary, shelves: [shelf('Replacement')] })
    await loading
  })
  expect(await screen.findByText('Replacement')).toBeDefined()
  view.unmount()
  view.client.clear()
})
