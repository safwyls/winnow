// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useDetails } from '../src/renderer/api/hooks'
import type { GameDetails, LibraryGame, Mode, UpdateEvent } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'
import { UpdateSignals } from '../src/renderer/features/parity-details'
import { ActivityTimeline } from '../src/renderer/features/activity-timeline'
import { updateFlagState } from '../src/renderer/features/update-flags'
import { clearViewState } from '../src/renderer/viewState'

const now = Date.parse('2026-09-29T00:00:00Z')
const at = (days: number) => new Date(now + days * 86400000).toISOString()
const pair = (days: number, releaseId = 1, id = 1): UpdateEvent[] => [
  { id, releaseId, kind: 'build_push', occurredAt: at(days), title: `Build ${id}` },
  { id: id + 1, releaseId, kind: 'announcement', occurredAt: at(days + 1), title: `Notes ${id}` },
]
const game: LibraryGame = {
  workId: 1,
  title: 'Winnow flags',
  bucket: 'stale_but_patched',
  playtimeMinutes: 600,
  lastPlayedAt: at(-60),
  entries: [
    {
      ownershipId: 1,
      releaseId: 1,
      workId: 1,
      title: 'Winnow flags',
      store: 'steam',
      installed: true,
      playtimeMinutes: 600,
      lastPlayedAt: at(-60),
    },
  ],
}
const clients: QueryClient[] = []
function setup(
  mode: Mode,
  events = pair(-11),
  acknowledgements: Record<string, string> = {},
  available = true,
) {
  let saved = { ...acknowledgements }
  let currentEvents = events
  const fail = new Set<number>()
  let restoreResult = 'Stored'
  const intercept = { run: null as null | ((input: ApiRequest) => Promise<unknown>) }
  const facts = (): GameDetails => ({
    workId: 1,
    readAtUtc: at(0),
    events: currentEvents,
    acknowledgements: { ...saved },
    sessions: {},
    journalEntries: [],
    ratings: [],
    achievements: [],
  })
  const request = vi.fn(async (input: ApiRequest) => {
    const overridden = await intercept.run?.(input)
    if (overridden !== undefined) return overridden
    let data: unknown = facts()
    if (input.route === 'updates.acknowledge') {
      const releaseId = Number(input.params!.releaseId)
      if (fail.has(releaseId)) data = { result: 'NotStored' }
      else {
        const observed = (input.body as { observedEventIds: number[] }).observedEventIds
        const through = currentEvents
          .filter(
            (event) =>
              event.releaseId === releaseId && event.kind === 'build_push' && observed.includes(event.id),
          )
          .map((event) => event.occurredAt)
          .sort()
          .at(-1)!
        saved[releaseId] = through
        data = { result: 'Stored', acknowledgedThrough: through }
      }
    } else if (input.route === 'updates.restore') {
      if (restoreResult !== 'NotStored') delete saved[Number(input.params!.releaseId)]
      data = { result: restoreResult }
    }
    return { ok: true, status: 200, data }
  })
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request: available ? request : undefined },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  clients.push(client)
  client.setQueryData(['api', 'game.details', { workId: 1 }], facts())
  const reload = vi.spyOn(client, 'invalidateQueries')
  function Harness() {
    const details = useDetails(1).data!
    return (
      <div className={`mode-${mode}`}>
        <UpdateSignals details={details} game={game} />
        <ActivityTimeline details={details} game={game} mode={mode} />
      </div>
    )
  }
  const view = render(
    <QueryClientProvider client={client}>
      <Harness />
    </QueryClientProvider>,
  )
  return {
    ...view,
    request,
    reload,
    fail,
    intercept,
    setRestore: (value: string) => {
      restoreResult = value
    },
    publish: (next: UpdateEvent[], ack = saved) =>
      act(() => {
        currentEvents = next
        saved = ack
        client.setQueryData(['api', 'game.details', { workId: 1 }], facts())
      }),
  }
}
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  clearViewState('updates:1:sending')
  for (const mode of ['desktop', 'fullscreen'])
    for (const field of ['ownership', 'tracked']) clearViewState(`${mode}:timeline:1:${field}`)
})
const unread = (container: HTMLElement) => container.querySelectorAll('.update-row[data-unread="true"]')
const writes = (request: ReturnType<typeof setup>['request']) =>
  request.mock.calls.map(([input]) => input).filter((input) => input.route.startsWith('updates.'))
function expectSummaries(container: HTMLElement, gap: string, tracker: string) {
  const captions = container.querySelectorAll('.update-gap-caption')
  const summaries = container.querySelectorAll('.activity-tracker .activity-update-summary')
  expect(captions).toHaveLength(1)
  expect(captions[0]!.textContent).toBe(gap)
  expect(summaries).toHaveLength(1)
  expect(summaries[0]!.textContent).toBe(tracker)
}

describe.each<Mode>(['desktop', 'fullscreen'])('%s UpdateFlag source contracts', (mode) => {
  it('quiets dots at and below each watermark while keeping newer dots and timeline marks unread', () => {
    const view = setup(mode, [...pair(-5), ...pair(-20, 1, 3), ...pair(-40, 1, 5)], { 1: at(-20) })
    expect(unread(view.container)).toHaveLength(2)
    expectSummaries(view.container, '1 update landed while you were away.', '1 unread update')
    expect(view.container.querySelectorAll('.activity-timeline-update[data-unread="true"]')).toHaveLength(1)
    expect(view.container.querySelectorAll('.activity-timeline-update[data-unread="false"]')).toHaveLength(2)
  })
  it('quiets the later announcement with its dismissed push and preserves the newer pair', () => {
    const view = setup(mode, [...pair(-40), ...pair(-5, 1, 3)], { 1: at(-40) })
    expect(unread(view.container)).toHaveLength(2)
    expect(screen.getByRole('article', { name: /Notes 1/ }).getAttribute('data-unread')).toBe('false')
    expect(screen.getByRole('article', { name: /Notes 3/ }).getAttribute('data-unread')).toBe('true')
  })
  it('describes a fully read gap without claiming that nothing shipped', () => {
    const view = setup(mode, [...pair(-5), ...pair(-9, 1, 3)], { 1: at(-4) })
    expect(unread(view.container)).toHaveLength(0)
    expectSummaries(
      view.container,
      "2 updates landed while you were away. You've marked them read.",
      'No unread updates',
    )
    expect(view.container.querySelectorAll('.activity-timeline-update[data-unread="true"]')).toHaveLength(0)
  })
  it('preserves undismissed dots and counts patches rather than their two observations', () => {
    const view = setup(mode, [...pair(-5), ...pair(-9, 1, 3)])
    expect(unread(view.container)).toHaveLength(4)
    expectSummaries(view.container, '2 updates landed while you were away.', '2 unread updates')
  })
  it('dismisses the dots offers undo and refreshes the library once while preserving the timeline range', async () => {
    const view = setup(mode)
    fireEvent.click(screen.getByRole('button', { name: 'Tracked sessions' }))
    fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }))
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Show it again' }) as HTMLButtonElement).disabled).toBe(
        false,
      ),
    )
    expect(unread(view.container)).toHaveLength(0)
    expect(writes(view.request)).toEqual([
      { route: 'updates.acknowledge', params: { releaseId: 1 }, body: { observedEventIds: [2, 1] } },
    ])
    expect(view.reload).toHaveBeenCalledExactlyOnceWith({ queryKey: ['api'] })
    expect(screen.getByRole('button', { name: 'Tracked sessions' }).getAttribute('aria-pressed')).toBe('true')
    expectSummaries(
      view.container,
      "1 update landed while you were away. You've marked it read.",
      'No unread updates',
    )
  })
  it('leaves failed writes unread with no receipt no undo and no library refresh', async () => {
    const view = setup(mode)
    view.fail.add(1)
    fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }))
    await screen.findByText("Couldn't save that — nothing changed.")
    expect(unread(view.container)).toHaveLength(2)
    expect(screen.queryByRole('button', { name: 'Show it again' })).toBeNull()
    expect(screen.queryByText('These update flags are marked read.')).toBeNull()
    expect(view.reload).not.toHaveBeenCalled()
  })
  it.each(['Stored', 'NothingToDo'])(
    'restores a standing dismissal and raises the flag for %s',
    async (result) => {
      const view = setup(mode, pair(-11), { 1: at(-11) })
      view.setRestore(result)
      expect(screen.queryByRole('button', { name: 'Mark as read' })).toBeNull()
      fireEvent.click(screen.getByRole('button', { name: 'Show it again' }))
      await screen.findByRole('button', { name: 'Mark as read' })
      expect(unread(view.container)).toHaveLength(2)
      expect(view.reload).toHaveBeenCalledOnce()
    },
  )
  it('offers dismissal again when a newer push outranks the standing watermark', () => {
    const view = setup(mode, [...pair(-40), ...pair(-3, 1, 3)], { 1: at(-40) })
    expect(screen.getByRole('button', { name: 'Mark as read' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Show it again' })).toBeNull()
    expect(unread(view.container)).toHaveLength(2)
  })
  it('offers no write controls without the named service bridge', () => {
    setup(mode, pair(-11), { 1: at(-40) }, false)
    expect(screen.queryByRole('button', { name: 'Mark as read' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Show it again' })).toBeNull()
  })
  it('follows externally refreshed watermarks after a local save instead of retaining a stale override', async () => {
    const view = setup(mode)
    fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }))
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Show it again' }) as HTMLButtonElement).disabled).toBe(
        false,
      ),
    )
    view.publish(pair(-11), {})
    await screen.findByRole('button', { name: 'Mark as read' })
    expect(unread(view.container)).toHaveLength(2)
    expect(writes(view.request)).toHaveLength(1)
  })
  it('does not count marketing or notes for a push exactly at last play', () => {
    const view = setup(mode, [
      ...pair(-60),
      { id: 3, releaseId: 1, kind: 'announcement', occurredAt: at(-1), title: 'Sale' },
    ])
    expect(unread(view.container)).toHaveLength(0)
    expectSummaries(view.container, 'No updates recorded in that stretch.', 'No unread updates')
    expect(screen.queryByRole('button', { name: 'Mark as read' })).toBeNull()
  })
  it('does not extend the watermark through shared notes over a newer build', () => {
    const view = setup(mode, [...pair(-10).slice(0, 1), ...pair(-9, 1, 3)], { 1: at(-10) })
    expect(unread(view.container)).toHaveLength(2)
    expectSummaries(view.container, '1 update landed while you were away.', '1 unread update')
    expect(screen.getByRole('article', { name: /Notes 3/ }).getAttribute('data-unread')).toBe('true')
  })
  it('retains partial saves reports failure and retries only the remaining release', async () => {
    const view = setup(mode, [...pair(-40), ...pair(-39, 2, 3)])
    view.fail.add(2)
    fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }))
    await screen.findByText("Couldn't mark every patch read. Try again.")
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Mark as read' }) as HTMLButtonElement).disabled).toBe(
        false,
      ),
    )
    expect(unread(view.container)).toHaveLength(2)
    expect(screen.getByRole('article', { name: /Build 1/ }).getAttribute('data-unread')).toBe('false')
    expect(view.reload).toHaveBeenCalledOnce()
    view.fail.clear()
    fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }))
    await screen.findByRole('button', { name: 'Show it again' })
    expect(unread(view.container)).toHaveLength(0)
    expect(writes(view.request).map((input) => input.params!.releaseId)).toEqual([2, 1, 2])
    expect(view.reload).toHaveBeenCalledTimes(2)
  })
  it('keeps a failed undo standing without a success receipt or refresh', async () => {
    const view = setup(mode, pair(-11), { 1: at(-11) })
    view.setRestore('NotStored')
    fireEvent.click(screen.getByRole('button', { name: 'Show it again' }))
    await screen.findByText("Couldn't undo that just now.")
    expect(unread(view.container)).toHaveLength(0)
    expect(screen.getByRole('button', { name: 'Show it again' })).toBeTruthy()
    expect(view.reload).not.toHaveBeenCalled()
  })
  it('reconciles a lost response without falsely claiming the write changed nothing', async () => {
    const view = setup(mode)
    view.intercept.run = async (input) => {
      if (input.route.startsWith('updates.')) throw new Error('lost response')
    }
    fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }))
    await screen.findByText("Couldn't confirm that. Check the refreshed flags and try again.")
    expect(view.reload).toHaveBeenCalledOnce()
    expect(screen.queryByText("Couldn't save that — nothing changed.")).toBeNull()
  })
  it('does not take focus back when the user leaves a pending flag action', async () => {
    const view = setup(mode)
    let finish!: () => void
    view.intercept.run = async (input) => {
      if (input.route === 'updates.acknowledge')
        await new Promise<void>((resolve) => {
          finish = resolve
        })
    }
    const mark = screen.getByRole('button', { name: 'Mark as read' })
    mark.focus()
    fireEvent.click(mark)
    await waitFor(() => expect(finish).toBeTypeOf('function'))
    const moved = screen.getByRole('button', { name: 'Tracked sessions' })
    moved.focus()
    await act(async () => finish())
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Show it again' }) as HTMLButtonElement).disabled).toBe(
        false,
      ),
    )
    expect(document.activeElement).toBe(moved)
  })
})

it('counts the largest release patch group instead of adding duplicate edition reports', () => {
  const events = [...pair(-5), ...pair(-20, 1, 3), ...pair(-5, 2, 5)]
  expect(updateFlagState(events, {}, at(-60), 600).unread).toBe(2)
  expect(updateFlagState(events, { 1: at(-5) }, at(-60), 600).unread).toBe(1)
})
