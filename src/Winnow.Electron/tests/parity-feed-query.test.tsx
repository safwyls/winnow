// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { feedSchema, useFeed } from '../src/renderer/api/hooks'
import { invalidateFeed } from '../src/renderer/api/feed-refresh'
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
  function Probe({ enabled = true }: { enabled?: boolean }) {
    const feed = useFeed(enabled)
    return (
      <div>
        {feed.isPending && <span role="status">Building the feed…</span>}
        {feed.data?.shelves.map((shelf) => (
          <span key={shelf.id}>{shelf.title}</span>
        ))}
      </div>
    )
  }
  const view = render(
    <QueryClientProvider client={client}>
      <Probe key="owner" />
    </QueryClientProvider>,
  )
  return { client, calls, cancel, Probe, ...view }
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

it.each([1, 3])(
  'replays %d invalidations during the original pending pass once with the final library state',
  async (changes) => {
    let finish!: () => void
    const gate = new Promise<void>((resolve) => {
      finish = resolve
    })
    let next = primary,
      count = 0
    const view = probe(async (route) => {
      if (route === 'feed.supplement') return { shelves: [], candidateCount: 0 }
      count++
      const snapshot = next
      await gate
      return snapshot
    })
    await waitFor(() => expect(count).toBe(1))
    next = {
      ...primary,
      shelves: [
        {
          ...shelf('ready_to_play'),
          title: 'Installed and waiting',
          items: [
            {
              ownershipId: 99,
              releaseId: 99,
              title: 'Deep Rock Galactic 99',
              reason: 'The final library state.',
            },
          ],
        },
      ],
    }
    const queue = new RefreshQueue(() => refreshSnapshots(view.client))
    await act(async () => {
      for (let index = 0; index < changes; index++) queue.request()
    })
    await act(async () => {
      finish()
    })
    await screen.findByText('Installed and waiting')
    await waitFor(() => expect(view.client.isFetching({ queryKey: ['api', 'feed.get'] })).toBe(0))
    expect(count).toBe(2)
    expect(view.client.getQueryData(['api', 'feed.get'])).toMatchObject({
      shelves: [{ items: [{ releaseId: 99 }] }],
    })
    queue.dispose()
    view.unmount()
    view.client.clear()
  },
)

it('replays an invalidation after the original scoring pass fails and exposes a failed replay as a retryable query error', async () => {
  let reject!: (error: Error) => void
  let count = 0
  const view = probe(async (route) => {
    if (route === 'feed.supplement') return { shelves: [], candidateCount: 0 }
    count++
    if (count === 1)
      return new Promise((_resolve, fail) => {
        reject = fail
      })
    throw Error('Scoring offline')
  })
  await waitFor(() => expect(count).toBe(1))
  act(() => invalidateFeed(view.client))
  await act(async () => reject(Error('First pass failed')))
  await waitFor(() => expect(view.client.getQueryState(['api', 'feed.get'])?.status).toBe('error'))
  expect(count).toBe(2)
  expect(screen.queryByText('Primary')).toBeNull()
  expect(view.client.getQueryState(['api', 'feed.get'])?.error?.message).toBe('Scoring offline')
  view.unmount()
  view.client.clear()
})

it('a disabled feed with cached primary data does not start an optional provider', async () => {
  let finish!: (value: unknown) => void
  const view = probe(
    async () =>
      new Promise((resolve) => {
        finish = resolve
      }),
  )
  await waitFor(() => expect(finish).toBeDefined())
  view.rerender(
    <QueryClientProvider client={view.client}>
      <view.Probe key="owner" enabled={false} />
    </QueryClientProvider>,
  )
  await act(async () => finish(primary))
  await act(async () => {
    await Promise.resolve()
  })
  expect(view.calls.map((call) => call.route)).toEqual(['feed.get'])
  expect(view.client.getQueryData(['api', 'feed.get'])).toEqual(primary)
  view.unmount()
  view.client.clear()
})

it('shares one pending scoring pass and replay across readers when the original reader leaves', async () => {
  const finish: ((value: unknown) => void)[] = []
  const view = probe(async (route) =>
    route === 'feed.get'
      ? new Promise((resolve) => finish.push(resolve))
      : { shelves: [], candidateCount: 0 },
  )
  await waitFor(() => expect(finish).toHaveLength(1))
  const { Probe } = view
  view.rerender(
    <QueryClientProvider client={view.client}>
      <Probe key="owner" />
      <Probe key="survivor" />
    </QueryClientProvider>,
  )
  act(() => {
    invalidateFeed(view.client)
    invalidateFeed(view.client)
    invalidateFeed(view.client)
  })
  view.rerender(
    <QueryClientProvider client={view.client}>
      <Probe key="survivor" />
    </QueryClientProvider>,
  )
  expect(view.cancel).not.toHaveBeenCalled()
  await act(async () => finish[0](primary))
  await waitFor(() => expect(finish).toHaveLength(2))
  expect(screen.queryByText('Primary')).toBeNull()
  expect(screen.getByRole('status').textContent).toBe('Building the feed…')
  await act(async () => finish[1]({ ...primary, shelves: [shelf('Final shared pass')] }))
  expect(await screen.findByText('Final shared pass')).toBeDefined()
  expect(view.calls.filter((call) => call.route === 'feed.get')).toHaveLength(2)
  view.unmount()
  view.client.clear()
})

it('the final reader cancels its real request and queued replay, and a new reader starts a clean pass', async () => {
  const finish: ((value: unknown) => void)[] = []
  const view = probe(async (route) =>
    route === 'feed.get'
      ? new Promise((resolve) => finish.push(resolve))
      : { shelves: [], candidateCount: 0 },
  )
  await waitFor(() => expect(finish).toHaveLength(1))
  const { Probe } = view
  view.rerender(
    <QueryClientProvider client={view.client}>
      <Probe key="owner" />
      <Probe key="survivor" />
    </QueryClientProvider>,
  )
  act(() => invalidateFeed(view.client))
  view.rerender(
    <QueryClientProvider client={view.client}>
      <Probe key="survivor" />
    </QueryClientProvider>,
  )
  expect(view.cancel).not.toHaveBeenCalled()
  view.unmount()
  await waitFor(() => expect(view.cancel).toHaveBeenCalledWith(view.calls[0].requestId))
  await act(async () => finish[0](primary))
  expect(finish).toHaveLength(1)
  expect(view.client.getQueryData(['api', 'feed.get'])).toBeUndefined()
  const next = render(
    <QueryClientProvider client={view.client}>
      <Probe />
    </QueryClientProvider>,
  )
  await waitFor(() => expect(finish).toHaveLength(2))
  await act(async () => finish[1]({ ...primary, shelves: [shelf('New lifetime')] }))
  expect(await screen.findByText('New lifetime')).toBeDefined()
  expect(view.calls.filter((call) => call.route === 'feed.get')).toHaveLength(2)
  next.unmount()
  view.client.clear()
})
