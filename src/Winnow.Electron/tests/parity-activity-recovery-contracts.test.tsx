// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useState, type ReactNode } from 'react'
import type { ApiRequest } from '../src/shared/bridge'
import type { ActivityPage, GameDetails, JournalResponse, Mode } from '../src/renderer/api/types'
import { DetailsJournal, Journal, JournalDraft, JournalEditor } from '../src/renderer/features/Journal'
import { useActivity, useApiQuery } from '../src/renderer/api/hooks'
import { refreshSnapshots } from '../src/renderer/refresh'
import { buildTimeline } from '../src/renderer/features/activity-timeline-model'
import { Details } from '../src/renderer/features/Details'
import { clearViewState } from '../src/renderer/viewState'

const ok = (data: unknown) => ({ ok: true, status: 200, data })
function deferred() {
  let resolve!: (value: unknown) => void
  const promise = new Promise<unknown>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
const games = [1, 2, 3, 4].map((id) => ({
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
      installed: true,
      playtimeMinutes: 0,
    },
  ],
}))
const clients: QueryClient[] = []
function setup(handler: (input: ApiRequest) => unknown = () => undefined) {
  const request = vi.fn(
    async (input: ApiRequest) =>
      handler(input) ??
      ok(
        input.route === 'library.get'
          ? { games, lists: [] }
          : input.route === 'library.workspace'
            ? {
                works: games.map((game) => ({ id: game.workId, name: game.title })),
                externalIds: [],
                pluginActions: {},
                epicLaunchKeys: {},
              }
            : input.route === 'journal.preferences.get'
              ? { promptAfterPlay: true }
              : input.route === 'activity.query'
                ? { rows: [], next: null }
                : input.route === 'journal.get'
                  ? { sessionId: input.params!.sessionId, revision: 'r1', note: null, rating: null }
                  : input.route === 'statistics.gameplay'
                    ? {
                        recordedSeconds: 0,
                        gamesPlayedCount: 0,
                        startedSessionCount: 0,
                        excludedSessionCount: 0,
                        overlappingSessionCount: 0,
                        periods: [],
                        topGames: [],
                      }
                    : input.route === 'artwork.backdrops'
                      ? { candidates: [], coverKey: null }
                      : {},
      ),
  )
  const cancelRequest = vi.fn().mockResolvedValue(undefined)
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, cancelRequest, artwork: vi.fn().mockResolvedValue(null) },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: Infinity, staleTime: Infinity } },
  })
  clients.push(client)
  const host = (child: ReactNode) =>
    render(<QueryClientProvider client={client}>{child}</QueryClientProvider>)
  return {
    request,
    cancelRequest,
    client,
    host,
    reads: () =>
      request.mock.calls.map(([input]) => input).filter((input) => input.route === 'activity.query'),
  }
}
const sourcePage = (id: number, from: string, next = false): ActivityPage => ({
  rows: [
    {
      ownershipId: 1,
      store: 'steam',
      atUtc: from,
      session: { id, ownershipId: 1, startedAt: from, durationSeconds: id * 60, detectionMethod: 'manual' },
    },
  ],
  next: next ? { atUtc: from, id } : null,
})

it('projects the same recovered sitting from unknown duration to exactly one sixth of an hour at eleven minutes', () => {
  const started = Date.parse('2026-09-10T12:00:00Z')
  const checkpoint = {
    id: 1,
    ownershipId: 1,
    startedAt: new Date(started).toISOString(),
    detectionMethod: 'process_watch',
    endedAt: null,
    durationSeconds: null,
  }
  const open = buildTimeline([], [checkpoint], null, null, started + 3 * 60_000, true)
  expect(open.bars).toEqual([])
  const sessions = [
    { ...checkpoint, endedAt: new Date(started + 10 * 60_000).toISOString(), durationSeconds: 600 },
  ]
  const completed = buildTimeline([], sessions, null, null, started + 11 * 60_000, true)
  expect(sessions.map((session) => session.id)).toEqual([checkpoint.id])
  expect(completed.bars).toHaveLength(1)
  expect(completed.bars[0]).toMatchObject({ start: started, end: started + 10 * 60_000, tracked: true })
  expect(completed.bars[0]!.hours).toBe(1 / 6)
})
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  vi.restoreAllMocks()
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of ['section', 'days', 'bounds', 'editing', 'panel', 'week', 'work', 'selected'])
      clearViewState(`${mode}:journal:${key}`)
    for (const key of ['editing', 'editing-section', 'journal-inline'])
      clearViewState(`${mode}:details:1:${key}`)
    clearViewState(`${mode}:source-journal:journal-inline`)
  }
  for (const id of [1, 2, 7, 42]) clearViewState(`draft:journal:${id}`)
})

describe('original saved journal and library-reload fixtures', () => {
  it.each(['desktop', 'fullscreen'] as const)(
    'edits and deletes saved session7 / ownership3 with the original note and rating in %s',
    async (mode) => {
      let saved: JournalResponse = { sessionId: 7, note: 'First route.', rating: 2, revision: 'r1' }
      const facts = {
        workId: 3,
        sessions: {},
        journalEntries: [{ ...saved, ownershipId: 3, sessionAt: '2026-08-22T21:00:00Z' }],
      }
      const f = setup((input) =>
        input.route === 'game.details'
          ? ok(facts)
          : input.route === 'journal.get'
            ? ok(saved)
            : input.route === 'journal.put'
              ? ok((saved = { ...saved, ...(input.body as object), revision: 'r2' }))
              : input.route === 'journal.delete'
                ? ok(null)
                : undefined,
      )
      function Fixture() {
        const query = useApiQuery<GameDetails>('game.details', { workId: 3 })
        const [editing, setEditing] = useState(false)
        return (
          <>
            {query.data && (
              <DetailsJournal
                notes={query.data.journalEntries}
                mode={mode}
                scopeKey={`${mode}:source-journal`}
                onEdit={() => setEditing(true)}
                promptAfterPlay
              />
            )}
            {editing && <JournalEditor sessionId={7} mode={mode} onClose={() => setEditing(false)} />}
          </>
        )
      }
      f.host(<Fixture />)
      fireEvent.click(await screen.findByRole('button', { name: 'Edit note' }))
      fireEvent.change(await screen.findByRole('textbox', { name: 'Journal note' }), {
        target: { value: 'Found the shortcut.' },
      })
      if (mode === 'desktop') fireEvent.click(screen.getByRole('button', { name: '4 out of 5' }))
      else fireEvent.change(screen.getByLabelText('Your rating'), { target: { value: '4' } })
      fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
      await screen.findByText('Found the shortcut.')
      expect(screen.queryByRole('textbox', { name: 'Journal note' })).toBeNull()
      expect(await screen.findByText('4 / 5')).toBeTruthy()
      expect(screen.getByText('Found the shortcut.')).toBeTruthy()
      expect(f.request.mock.calls.find(([input]) => input.route === 'journal.put')![0]).toMatchObject({
        params: { sessionId: 7 },
        body: { note: 'Found the shortcut.', rating: 4 },
      })
      if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Edit note' }))
      fireEvent.click(await screen.findByRole('button', { name: 'Delete note' }))
      await screen.findByRole('region', { name: 'Delete this note?' })
      fireEvent.click(screen.getByRole('button', { name: 'Yes, delete note' }))
      await screen.findByText('No notes yet. After you play, Winnow will ask how it went.')
      expect(
        f.request.mock.calls
          .filter(([input]) => input.route === 'journal.delete')
          .map(([input]) => input.params!.sessionId),
      ).toEqual([7])
    },
  )

  for (const mode of ['desktop', 'fullscreen'] as const)
    it.each([true, false])(`${mode} names whether empty-journal prompts are off (enabled=%s)`, (enabled) => {
      const f = setup()
      f.host(
        <DetailsJournal
          notes={[]}
          mode={mode}
          scopeKey={`${mode}:source-journal`}
          onEdit={vi.fn()}
          promptAfterPlay={enabled}
        />,
      )
      expect(screen.getByText(enabled ? /No notes yet/ : /prompts are off/)).toBeTruthy()
    })

  it.each(['desktop', 'fullscreen'] as const)(
    'refreshes the selected source session2 note and removes its hidden game in %s',
    async (mode) => {
      const at = new Date().toISOString(),
        previous = new Date(Date.now() - 60_000).toISOString()
      let note: string | null = null,
        hidden = false
      const f = setup((input) =>
        input.route === 'activity.query'
          ? ok({
              rows: [
                sourcePage(1, at).rows[0],
                {
                  ...sourcePage(2, previous).rows[0],
                  ownershipId: 2,
                  session: { ...sourcePage(2, previous).rows[0]!.session!, ownershipId: 2 },
                  note: note ? { sessionId: 2, note } : null,
                },
              ],
              next: null,
            })
          : input.route === 'library.get'
            ? ok({ games: games.filter((game) => !hidden || game.workId !== 2), lists: [] })
            : undefined,
      )
      f.host(<Journal mode={mode} />)
      await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(2))
      fireEvent.focus(screen.getAllByRole('article')[1]!)
      note = 'A newly saved note'
      await act(async () => refreshSnapshots(f.client))
      const selected = screen
        .getAllByRole('article')
        .find((row) => row.getAttribute('aria-current') === 'true')!
      expect(within(selected).getByRole('button', { name: 'Game 2' })).toBeTruthy()
      expect(within(selected).getByText('A newly saved note')).toBeTruthy()
      hidden = true
      await act(async () => refreshSnapshots(f.client))
      await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(1))
      expect(screen.getByRole('article').getAttribute('aria-current')).toBe('true')
      expect(screen.queryByText('A newly saved note')).toBeNull()
      expect(screen.queryByRole('button', { name: 'Game 2' })).toBeNull()
    },
  )
})

describe('original activity paging and recovery contracts', () => {
  it('coalesces two obsolete weeks into one latest read even when the first reader ignores cancellation', async () => {
    const first = deferred()
    let count = 0
    const f = setup((input) =>
      input.route === 'activity.query'
        ? ++count === 1
          ? first.promise
          : ok(sourcePage(2, (input.body as { fromUtc: string }).fromUtc))
        : undefined,
    )
    f.host(<Journal mode="fullscreen" />)
    await waitFor(() => expect(f.reads()).toHaveLength(1))
    const page = document.querySelector('.journal-page')!
    fireEvent.keyDown(page, { key: 'ArrowLeft' })
    fireEvent.keyDown(page, { key: 'ArrowLeft' })
    expect(f.reads()).toHaveLength(1)
    const initial = f.reads()[0]!.body as { fromUtc: string }
    await act(async () => first.resolve(ok(sourcePage(1, initial.fromUtc))))
    await screen.findByRole('article')
    expect(f.reads()).toHaveLength(2)
    const final = f.reads()[1]!.body as { fromUtc: string; untilUtc: string }
    const expected = new Date(initial.fromUtc)
    expected.setDate(expected.getDate() - 14)
    expect(final.fromUtc).toBe(expected.toISOString())
    const until = new Date(final.fromUtc)
    until.setDate(until.getDate() + 7)
    expect(final.untilUtc).toBe(until.toISOString())
    expect(screen.getByRole('article').getAttribute('aria-current')).toBe('true')
    expect(
      f.client.getQueryData<{ pages: ActivityPage[] }>([
        'api',
        'activity.query',
        final.fromUtc,
        final.untilUtc,
        0,
        undefined,
      ])!.pages[0]!.rows[0]!.session!.id,
    ).toBe(2)
    expect(
      f.client.getQueryData([
        'api',
        'activity.query',
        initial.fromUtc,
        (f.reads()[0]!.body as { untilUtc: string }).untilUtc,
        0,
        undefined,
      ]),
    ).toBeUndefined()
  })

  it.each(['desktop', 'fullscreen'] as const)(
    'loads the next page only on request and aborts disposal without late cache publication in %s',
    async (mode) => {
      const pending = deferred()
      const f = setup((input) =>
        input.route === 'activity.query'
          ? (input.body as { after: unknown }).after
            ? pending.promise
            : ok(sourcePage(1, (input.body as { fromUtc: string }).fromUtc, true))
          : undefined,
      )
      const view = f.host(<Journal mode={mode} />)
      const more = await screen.findByRole('button', { name: 'Load more' })
      expect(f.reads()).toHaveLength(1)
      expect(screen.getByRole('article').getAttribute('aria-current')).toBe('true')
      fireEvent.click(more)
      await waitFor(() => expect(f.reads()).toHaveLength(2))
      const append = f.reads()[1]!
      view.unmount()
      await waitFor(() => expect(f.cancelRequest).toHaveBeenCalledWith(append.requestId))
      await act(async () => pending.resolve(ok(sourcePage(2, (append.body as { fromUtc: string }).fromUtc))))
      const cached = f.client.getQueriesData<{ pages: ActivityPage[] }>({
        queryKey: ['api', 'activity.query'],
      })
      expect(
        cached.flatMap(
          ([, value]) => value?.pages.flatMap((page) => page.rows.map((row) => row.session?.id)) ?? [],
        ),
      ).toEqual([1])
    },
  )

  it.each(['desktop', 'fullscreen'] as const)(
    'retains the focused Updates tab when a pending read completes in %s',
    async (mode) => {
      const pending = deferred()
      const f = setup((input) => (input.route === 'activity.query' ? pending.promise : undefined))
      f.host(<Journal mode={mode} />)
      await waitFor(() => expect(f.reads()).toHaveLength(1))
      const updates = screen.getByRole('button', { name: 'Updates' })
      updates.focus()
      await act(async () =>
        pending.resolve(ok(sourcePage(1, (f.reads()[0]!.body as { fromUtc: string }).fromUtc))),
      )
      await screen.findByRole('article')
      expect(document.activeElement).toBe(updates)
      expect(updates.getAttribute('aria-pressed')).toBe('false')
    },
  )

  it.each([false, true])(
    'retries the identical failed cursor and retains earlier selection (append=%s)',
    async (append) => {
      let failed = false
      const f = setup((input) => {
        if (input.route !== 'activity.query') return
        const body = input.body as { after: unknown; fromUtc: string }
        if (!failed && Boolean(body.after) === append) {
          failed = true
          return { ok: false, status: 500 }
        }
        return ok(sourcePage(body.after ? 2 : 1, body.fromUtc, append && !body.after))
      })
      f.host(<Journal mode="fullscreen" />)
      if (append) fireEvent.click(await screen.findByRole('button', { name: 'Load more' }))
      await screen.findByText("Couldn't read your activity. Try again.")
      if (append) expect(screen.getByRole('article').getAttribute('aria-current')).toBe('true')
      fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
      await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(append ? 2 : 1))
      const reads = f.reads()
      expect(reads.at(-1)!.body).toEqual(reads.at(-2)!.body)
      expect(screen.queryByText("Couldn't read your activity. Try again.")).toBeNull()
    },
  )

  it.each(['desktop', 'fullscreen'] as const)(
    'saves the source older session2 without rereading page one or losing selection in %s',
    async (mode) => {
      const f = setup((input) =>
        input.route === 'activity.query'
          ? ok(
              sourcePage(
                (input.body as { after: unknown }).after ? 2 : 1,
                (input.body as { fromUtc: string }).fromUtc,
                !(input.body as { after: unknown }).after,
              ),
            )
          : input.route === 'journal.put'
            ? ok({ sessionId: 2, note: 'Continue from the old session.', rating: null, revision: 'r2' })
            : undefined,
      )
      f.host(<Journal mode={mode} />)
      fireEvent.click(await screen.findByRole('button', { name: 'Load more' }))
      await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(2))
      const older = screen.getAllByRole('article')[1]!
      fireEvent.focus(older)
      fireEvent.keyDown(older, { key: 'x' })
      fireEvent.change(await screen.findByRole('textbox', { name: 'Journal note' }), {
        target: { value: 'Continue from the old session.' },
      })
      fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      expect(f.reads()).toHaveLength(2)
      expect(older.getAttribute('aria-current')).toBe('true')
      expect(within(older).getByText('Continue from the old session.')).toBeTruthy()
      expect(within(older).getByText('Journal entry')).toBeTruthy()
      expect(
        within(screen.getByRole('complementary', { name: 'Selected activity' })).getByText(
          'Continue from the old session.',
        ),
      ).toBeTruthy()
    },
  )

  it('separates empty Sessions and Journal and cycles local sections without changing the week', async () => {
    const f = setup()
    f.host(<Journal mode="fullscreen" />)
    await screen.findByText('No sessions this week')
    const page = document.querySelector('.journal-page')!
    fireEvent.keyDown(page, { key: 'ArrowLeft' })
    await waitFor(() => expect(f.reads()).toHaveLength(2))
    fireEvent.keyDown(page, { key: 'ArrowRight' })
    fireEvent.keyDown(page, { key: 'ArrowRight' })
    await screen.findByText('No sessions this week')
    const tabs = screen.getByRole('navigation', { name: 'Activity type' })
    expect(page.querySelector('[data-section-trigger]')).toBeNull()
    ;(page as HTMLElement).focus()
    fireEvent.keyDown(page, { key: 'ArrowUp' })
    expect(document.activeElement).toBe(within(tabs).getByRole('button', { name: 'Sessions' }))
    const journal = within(tabs).getByRole('button', { name: 'Journal' })
    journal.focus()
    fireEvent.keyDown(journal, { key: 'ArrowRight' })
    expect((screen.getByRole('button', { name: 'Next week' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(journal)
    await screen.findByText('No journal entries this week')
    expect(screen.queryByText('No sessions this week')).toBeNull()
    fireEvent.keyDown(tabs, { key: 'PageDown' })
    await screen.findByText('No sessions this week')
    expect(document.activeElement).toBe(within(tabs).getByRole('button', { name: 'Sessions' }))
    fireEvent.keyDown(tabs, { key: 'PageUp' })
    await screen.findByText('No journal entries this week')
    expect((screen.getByRole('button', { name: 'Next week' }) as HTMLButtonElement).disabled).toBe(true)
  })
})

describe('shared activity reader lifetimes', () => {
  const from = '2026-09-07T07:00:00Z',
    until = '2026-09-14T07:00:00Z'
  function Reader({ id }: { id: number }) {
    const query = useActivity(from, until, 0)
    return (
      <output aria-label={`Reader ${id}`}>
        {query.data?.pages.flatMap((page) => page.rows.map((row) => row.session?.id)).join(',') ?? 'Waiting'}
      </output>
    )
  }
  function Readers() {
    const [visible, setVisible] = useState([1, 2])
    return (
      <>
        {visible.map((id) => (
          <div key={id}>
            <button onClick={() => setVisible((previous) => previous.filter((value) => value !== id))}>
              Remove {id}
            </button>
            <Reader id={id} />
          </div>
        ))}
      </>
    )
  }
  it.each([1, 2])('keeps a shared in-flight read alive when observer%s leaves first', async (removed) => {
    const pending = deferred(),
      f = setup((input) => (input.route === 'activity.query' ? pending.promise : undefined))
    f.host(<Readers />)
    await waitFor(() => expect(f.reads()).toHaveLength(1))
    fireEvent.click(screen.getByRole('button', { name: `Remove ${removed}` }))
    await act(async () => {
      await Promise.resolve()
    })
    expect(f.cancelRequest).not.toHaveBeenCalled()
    await act(async () => pending.resolve(ok(sourcePage(2, from))))
    await waitFor(() => expect(screen.getByLabelText(`Reader ${3 - removed}`).textContent).toBe('2'))
    expect(f.reads()).toHaveLength(1)
  })
  it.each([1, 2])(
    'aborts at the final shared observer departure and protects a fresh mount (first departure=%s)',
    async (removed) => {
      const old = deferred(),
        fresh = deferred()
      let reads = 0
      const f = setup((input) =>
        input.route === 'activity.query' ? (++reads === 1 ? old.promise : fresh.promise) : undefined,
      )
      const oldView = f.host(<Readers />)
      await waitFor(() => expect(f.reads()).toHaveLength(1))
      const firstRequest = f.reads()[0]!
      fireEvent.click(screen.getByRole('button', { name: `Remove ${removed}` }))
      await act(async () => {
        await Promise.resolve()
      })
      expect(f.cancelRequest).not.toHaveBeenCalled()
      fireEvent.click(screen.getByRole('button', { name: `Remove ${3 - removed}` }))
      await waitFor(() => expect(f.cancelRequest).toHaveBeenCalledWith(firstRequest.requestId))
      oldView.unmount()
      f.host(<Reader id={3} />)
      await waitFor(() => expect(f.reads()).toHaveLength(2))
      const freshRequest = f.reads()[1]!
      expect(freshRequest.requestId).not.toBe(firstRequest.requestId)
      await act(async () => old.resolve(ok(sourcePage(1, from))))
      expect(screen.getByLabelText('Reader 3').textContent).toBe('Waiting')
      expect(f.cancelRequest).not.toHaveBeenCalledWith(freshRequest.requestId)
      await act(async () => fresh.resolve(ok(sourcePage(2, from))))
      await waitFor(() => expect(screen.getByLabelText('Reader 3').textContent).toBe('2'))
    },
  )
})

describe('original session-note validation and recovery matrix', () => {
  const original = (existing: boolean): JournalResponse => ({
    sessionId: 7,
    note: existing ? 'Original note' : null,
    rating: existing ? 4 : null,
    revision: 'r1',
  })
  for (const surface of ['desktop', 'details', 'activity'] as const) {
    it.each([false, true])(
      `${surface} rejects an empty note/rating then trims the source successful note (existing=%s)`,
      async (existing) => {
        const close = vi.fn()
        const f = setup((input) =>
          input.route === 'journal.get'
            ? ok(original(existing))
            : input.route === 'journal.put'
              ? ok({ ...original(existing), note: 'Remember the other route.', rating: 5, revision: 'r2' })
              : undefined,
        )
        f.host(
          surface === 'desktop' ? (
            <JournalDraft initial={original(existing)} onClose={close} />
          ) : (
            <JournalEditor sessionId={7} mode="fullscreen" onClose={close} />
          ),
        )
        const field = await screen.findByRole('textbox', { name: 'Journal note' })
        const rating = screen.getByLabelText('Your rating') as HTMLSelectElement
        expect(rating.value).toBe(existing ? '4' : '0')
        fireEvent.change(field, { target: { value: '  \r\n ' } })
        fireEvent.change(rating, { target: { value: '0' } })
        fireEvent.submit(field.closest('form')!)
        await screen.findByText('Add a note or rating, or delete this entry.')
        expect(f.request.mock.calls.filter(([input]) => input.route === 'journal.put')).toHaveLength(0)
        expect(close).not.toHaveBeenCalled()
        fireEvent.change(field, { target: { value: '  Remember the other route. \r\n ' } })
        fireEvent.change(rating, { target: { value: '5' } })
        fireEvent.submit(field.closest('form')!)
        await waitFor(() => expect(close).toHaveBeenCalledOnce())
        expect(f.request.mock.calls.find(([input]) => input.route === 'journal.put')![0]).toMatchObject({
          params: { sessionId: 7 },
          body: { note: 'Remember the other route.', rating: 5, expectedRevision: 'r1' },
        })
      },
    )
    it(`${surface} retains the original draft after failure and blocks every conflicting action during retry`, async () => {
      const close = vi.fn(),
        gate = deferred()
      let attempts = 0
      const f = setup((input) =>
        input.route === 'journal.get'
          ? ok(original(true))
          : input.route === 'journal.put'
            ? ++attempts === 1
              ? { ok: false, status: 500 }
              : gate.promise
            : undefined,
      )
      f.host(
        surface === 'desktop' ? (
          <JournalDraft initial={original(true)} onClose={close} />
        ) : (
          <JournalEditor sessionId={7} mode="fullscreen" onClose={close} />
        ),
      )
      const field = (await screen.findByRole('textbox', { name: 'Journal note' })) as HTMLTextAreaElement
      fireEvent.change(field, { target: { value: '  Keep my draft.  ' } })
      fireEvent.submit(field.closest('form')!)
      await screen.findByText("Couldn't save that. Your changes are still here — try again.")
      await waitFor(() =>
        expect((screen.getByRole('button', { name: 'Save note' }) as HTMLButtonElement).disabled).toBe(false),
      )
      expect(field.value).toBe('  Keep my draft.  ')
      expect(close).not.toHaveBeenCalled()
      fireEvent.submit(field.closest('form')!)
      await waitFor(() => expect(attempts).toBe(2))
      const scope = field.closest('[role="dialog"]') ?? field.closest('form')!
      for (const button of within(scope as HTMLElement).getAllByRole('button'))
        expect((button as HTMLButtonElement).disabled).toBe(true)
      expect(field.disabled).toBe(true)
      fireEvent.change(screen.getByLabelText('Your rating'), { target: { value: '2' } })
      fireEvent.keyDown(field, { key: 'Escape' })
      fireEvent.submit(field.closest('form')!)
      expect(attempts).toBe(2)
      expect(close).not.toHaveBeenCalled()
      expect((screen.getByLabelText('Your rating') as HTMLSelectElement).value).toBe('4')
      await act(async () => gate.resolve(ok({ ...original(true), note: 'Keep my draft.', revision: 'r2' })))
      await waitFor(() => expect(close).toHaveBeenCalledOnce())
      expect(
        f.request.mock.calls.filter(([input]) => input.route === 'journal.put').at(-1)![0].body,
      ).toMatchObject({ note: 'Keep my draft.', rating: 4 })
    })
  }
})

it('edits the original Bluebird42 note inline through Library and back, then deletes only after confirmation', async () => {
  let saved: JournalResponse = { sessionId: 42, note: 'Looking for the key.', rating: 3, revision: 'r1' }
  const facts: GameDetails = {
    workId: 1,
    readAtUtc: '2026-08-23T12:00:00Z',
    sessions: {
      '1': [{ id: 42, ownershipId: 1, startedAt: '2026-08-22T12:00:00Z', detectionMethod: 'manual' }],
    },
    journalEntries: [{ ...saved, ownershipId: 1, sessionAt: '2026-08-22T12:00:00Z' }],
    events: [],
    ratings: [],
    achievements: [],
    images: [],
  }
  const f = setup((input) =>
    input.route === 'library.get'
      ? ok({
          games: [{ ...games[0]!, title: 'Bluebird', bucket: 'bounced', playtimeMinutes: 120 }],
          lists: [],
        })
      : input.route === 'game.details'
        ? ok(facts)
        : input.route === 'journal.get'
          ? ok(saved)
          : input.route === 'journal.put'
            ? ok((saved = { ...saved, ...(input.body as object), revision: 'r2' }))
            : input.route === 'journal.delete'
              ? ok(null)
              : undefined,
  )
  f.host(<Details workId={1} mode="desktop" presentation="avalon" />)
  await screen.findByRole('heading', { name: 'Bluebird' })
  fireEvent.click(screen.getByRole('tab', { name: 'Journal' }))
  fireEvent.click(screen.getByRole('button', { name: 'Edit note' }))
  fireEvent.change(await screen.findByRole('textbox', { name: 'Journal note' }), {
    target: { value: 'Found the key behind the waterfall.' },
  })
  const body = document.querySelector('.avalon-details-reading')
  fireEvent.click(screen.getByRole('tab', { name: 'Library' }))
  fireEvent.click(screen.getByRole('tab', { name: 'Journal' }))
  expect(((await screen.findByRole('textbox', { name: 'Journal note' })) as HTMLTextAreaElement).value).toBe(
    'Found the key behind the waterfall.',
  )
  expect(document.querySelector('.avalon-details-reading')).toBe(body)
  fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
  await screen.findByText('Found the key behind the waterfall.')
  expect(f.request.mock.calls.find(([input]) => input.route === 'journal.put')![0].body).toMatchObject({
    note: 'Found the key behind the waterfall.',
    rating: 3,
  })
  fireEvent.click(screen.getByRole('button', { name: 'Delete note' }))
  await screen.findByRole('region', { name: 'Delete this note?' })
  expect(f.request.mock.calls.some(([input]) => input.route === 'journal.delete')).toBe(false)
  fireEvent.click(screen.getByRole('button', { name: 'Yes, delete note' }))
  await screen.findByText('No notes yet. After you play, Winnow will ask how it went.')
  expect(
    f.request.mock.calls
      .filter(([input]) => input.route === 'journal.delete')
      .map(([input]) => input.params!.sessionId),
  ).toEqual([42])
})

describe('Details dismissal during a pending journal retry', () => {
  for (const presentation of ['avalon', 'shared'] as const)
    it.each(['desktop', 'fullscreen'] as const)(
      `${presentation} %s keeps the outer Details and journal draft through Escape, close and outside input`,
      async (mode) => {
        const gate = deferred(),
          close = vi.fn()
        const saved: JournalResponse = { sessionId: 7, note: 'Original note', rating: 4, revision: 'r1' }
        const facts: GameDetails = {
          workId: 1,
          readAtUtc: '2026-09-10T12:00:00Z',
          sessions: {},
          journalEntries: [{ ...saved, ownershipId: 1, sessionAt: '2026-09-10T12:00:00Z' }],
          events: [],
          ratings: [],
          achievements: [],
          images: [],
        }
        let writes = 0
        const f = setup((input) =>
          input.route === 'game.details'
            ? ok(facts)
            : input.route === 'journal.get'
              ? ok(saved)
              : input.route === 'journal.put'
                ? ++writes === 1
                  ? { ok: false, status: 500 }
                  : gate.promise
                : undefined,
        )
        function Surface() {
          const [open, setOpen] = useState(true)
          return open ? (
            <Details
              workId={1}
              mode={mode}
              presentation={presentation}
              onClose={() => {
                close()
                setOpen(false)
              }}
            />
          ) : (
            <p>Details closed</p>
          )
        }
        f.host(<Surface />)
        await screen.findByRole('heading', { name: 'Game 1' })
        fireEvent.click(screen.getByRole(presentation === 'avalon' ? 'tab' : 'button', { name: 'Journal' }))
        fireEvent.click(screen.getByRole('button', { name: 'Edit note' }))
        const field = (await screen.findByRole('textbox', { name: 'Journal note' })) as HTMLTextAreaElement
        fireEvent.change(field, { target: { value: '  Keep my draft.  ' } })
        fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
        await screen.findByText("Couldn't save that. Your changes are still here — try again.")
        await waitFor(() =>
          expect((screen.getByRole('button', { name: 'Save note' }) as HTMLButtonElement).disabled).toBe(
            false,
          ),
        )
        fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
        await waitFor(() => expect(writes).toBe(2))
        expect(field.disabled).toBe(true)
        fireEvent.keyDown(field, { key: 'Escape' })
        const outer = document.querySelector<HTMLElement>('.avalon-details, .details-page')!
        fireEvent.keyDown(outer, { key: 'Escape' })
        const closeButton = screen.queryByRole('button', {
          name: presentation === 'avalon' ? 'Close game details' : 'Back to your library',
          hidden: true,
        }) as HTMLButtonElement | null
        if (closeButton) {
          expect(closeButton.disabled).toBe(true)
          fireEvent.click(closeButton)
        }
        const overlay = document.querySelector('.avalon-details-scrim')
        if (overlay) {
          fireEvent.pointerDown(overlay, { button: 0, pointerType: 'mouse', ctrlKey: false })
          fireEvent.focusIn(overlay)
        }
        fireEvent.submit(field.closest('form')!)
        expect(close).not.toHaveBeenCalled()
        expect(screen.queryByText('Details closed')).toBeNull()
        expect(field.isConnected).toBe(true)
        expect(field.value).toBe('  Keep my draft.  ')
        expect(writes).toBe(2)
        expect(
          f.request.mock.calls.filter(([input]) => input.route === 'journal.put').at(-1)![0].body,
        ).toMatchObject({ note: 'Keep my draft.', rating: 4 })
        await act(async () => gate.resolve(ok({ ...saved, note: 'Keep my draft.', revision: 'r2' })))
        await waitFor(() => expect(screen.queryByRole('textbox', { name: 'Journal note' })).toBeNull())
        expect(close).not.toHaveBeenCalled()
        expect(await screen.findByText('Keep my draft.')).toBeTruthy()
        if (closeButton) {
          await waitFor(() => expect(closeButton.disabled).toBe(false))
          fireEvent.click(closeButton)
          expect(close).toHaveBeenCalledOnce()
        }
      },
    )
})
