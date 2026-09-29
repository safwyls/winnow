// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import { feedSchema, useFeed } from '../src/renderer/api/hooks'

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
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      request: async ({ route }: { route: string }) => ({
        ok: true,
        status: 200,
        data: await handler(route),
      }),
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
  return { client, ...view }
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
