// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { Journal, JournalDraft } from '../src/renderer/features/Journal'
import { Details } from '../src/renderer/features/Details'
import { GameplayDashboard } from '../src/renderer/features/activity-gameplay'
import { SteamReportedActivity } from '../src/renderer/features/activity-steam'
import { gameplayRange, mondayWeek, patchJournalCaches } from '../src/renderer/features/activity-model'
import { clearViewState } from '../src/renderer/viewState'
import type { ActivityPage, LibraryGame } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'

const game: LibraryGame = {
  workId: 1,
  title: 'Dragonwilds',
  bucket: 'Started',
  playtimeMinutes: 300,
  entries: [
    {
      ownershipId: 11,
      releaseId: 21,
      workId: 1,
      title: 'Dragonwilds',
      store: 'steam',
      installed: true,
      playtimeMinutes: 300,
    },
  ],
}
const row = (id: number, day = '25') => ({
  ownershipId: 11,
  store: 'steam',
  atUtc: `2026-09-${day}T16:00:00Z`,
  session: {
    id,
    ownershipId: 11,
    startedAt: `2026-09-${day}T16:00:00Z`,
    endedAt: `2026-09-${day}T17:00:00Z`,
    durationSeconds: 3600,
    detectionMethod: 'process',
  },
})
const stats = {
  recordedSeconds: 7200,
  gamesPlayedCount: 1,
  startedSessionCount: 2,
  medianSessionSeconds: 3600,
  excludedSessionCount: 1,
  overlappingSessionCount: 2,
  periods: [{ fromUtc: '2026-09-01T00:00:00Z', untilUtc: '2026-09-08T00:00:00Z', recordedSeconds: 7200 }],
  topGames: [{ resolvedWorkId: 1, recordedSeconds: 7200 }],
  sessionLengths: [
    { minimumSeconds: 0, maximumSeconds: 1800, count: 0 },
    { minimumSeconds: 1800, maximumSeconds: 3600, count: 2 },
  ],
}
const ok = (data: unknown) => ({ ok: true, status: 200, data })
function bridge(handler?: (input: ApiRequest) => unknown) {
  const request = vi.fn(
    async (input: ApiRequest) =>
      handler?.(input) ??
      ok(
        input.route === 'library.get'
          ? { games: [game], lists: [] }
          : input.route === 'activity.query'
            ? { rows: [row(101)], next: null }
            : input.route === 'journal.get'
              ? { sessionId: input.params!.sessionId, revision: 'r1' }
              : input.route === 'journal.preferences.get'
                ? { promptAfterPlay: false }
                : input.route === 'artworkState'
                  ? { current: null, revision: 'r1' }
                  : stats,
      ),
  )
  Object.defineProperty(window, 'winnow', { value: { request, openExternal: vi.fn() }, configurable: true })
  return request
}
function host(
  content: ReactNode,
  client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } }),
) {
  return { ...render(<QueryClientProvider client={client}>{content}</QueryClientProvider>), client }
}
afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.unstubAllEnvs()
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of ['section', 'days', 'bounds', 'editing', 'panel', 'week', 'work', 'selected'])
      clearViewState(`${mode}:journal:${key}`)
    for (const key of ['section', 'period', 'store', 'from', 'until', 'applied'])
      clearViewState(`${mode}:stats:${key}`)
    for (const key of ['tab', 'editing']) clearViewState(`${mode}:details:1:${key}`)
  }
  for (const id of [101, 102, 103, 104, 105]) clearViewState(`draft:journal:${id}`)
})

describe('journal editing parity', () => {
  it('rejects empty entries and trims notes while preserving ratings', async () => {
    const request = bridge(() => ok({ sessionId: 101, revision: 'r2' }))
    const close = vi.fn()
    host(<JournalDraft initial={{ sessionId: 101, note: '', revision: 'r1' }} onClose={close} />)
    fireEvent.change(screen.getByLabelText('Your note'), { target: { value: '  ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    expect(await screen.findByText('Add a note or rating, or delete this entry.')).toBeTruthy()
    expect(request).not.toHaveBeenCalled()
    fireEvent.change(screen.getByLabelText('Your note'), { target: { value: '  Found the tower  ' } })
    fireEvent.change(screen.getByLabelText('Your rating'), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    await waitFor(() => expect(close).toHaveBeenCalledOnce())
    expect(request.mock.calls[0]![0].body).toEqual({
      note: 'Found the tower',
      rating: 5,
      expectedRevision: 'r1',
    })
  })
  it('allows a rating-only entry and requires explicit confirmation before deletion', async () => {
    const request = bridge(() => ok({ sessionId: 102, note: null, rating: 3, revision: 'r2' }))
    const view = host(<JournalDraft initial={{ sessionId: 102, revision: 'r1' }} onClose={vi.fn()} />)
    fireEvent.change(screen.getByLabelText('Your rating'), { target: { value: '3' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    await waitFor(() => expect(request).toHaveBeenCalledOnce())
    view.unmount()
    host(
      <JournalDraft
        initial={{ sessionId: 103, note: 'Keep this', rating: 3, revision: 'r9' }}
        onClose={vi.fn()}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Delete note' }))
    expect(screen.getByRole('region', { name: 'Delete this note?' })).toBeTruthy()
    expect(request).toHaveBeenCalledOnce()
    fireEvent.click(screen.getByRole('button', { name: 'Keep note' }))
    expect((screen.getByLabelText('Your note') as HTMLTextAreaElement).value).toBe('Keep this')
    fireEvent.click(screen.getByRole('button', { name: 'Delete note' }))
    fireEvent.click(screen.getByRole('button', { name: 'Yes, delete note' }))
    await waitFor(() => expect(request).toHaveBeenCalledTimes(2))
    expect(request.mock.calls[1]![0]).toMatchObject({
      route: 'journal.delete',
      body: { expectedRevision: 'r9' },
    })
  })
  it('does not navigate when a save completes after the editor is disposed', async () => {
    let finish!: (value: unknown) => void
    bridge(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    const close = vi.fn()
    const view = host(
      <JournalDraft initial={{ sessionId: 104, note: 'A note', revision: 'r1' }} onClose={close} />,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    view.unmount()
    await act(async () => finish(ok({ sessionId: 104, revision: 'r2' })))
    expect(close).not.toHaveBeenCalled()
  })
  it('retains failed drafts and blocks uncertain retries until the saved note can be checked', async () => {
    const request = bridge(() => ({ ok: false, status: 503, message: 'Disconnected' }))
    host(
      <JournalDraft
        initial={{ sessionId: 105, note: 'My draft', rating: 4, revision: 'r1' }}
        onClose={vi.fn()}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    await screen.findByRole('button', { name: 'Read saved note' })
    expect((screen.getByLabelText('Your note') as HTMLTextAreaElement).value).toBe('My draft')
    expect((screen.getByRole('button', { name: 'Save note' }) as HTMLButtonElement).disabled).toBe(true)
    request.mockResolvedValue(ok({ sessionId: 105, note: 'Old saved note', rating: 2, revision: 'r2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Read saved note' }))
    await screen.findByText('Old saved note')
    fireEvent.click(screen.getByRole('button', { name: 'Keep my draft for the next save' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    await waitFor(() => expect(request).toHaveBeenCalledTimes(4))
    expect(request.mock.calls[3]![0].body).toMatchObject({
      note: 'My draft',
      rating: 4,
      expectedRevision: 'r2',
    })
  })
  it('patches loaded session and journal pages without losing page cursors or details history', () => {
    const client = new QueryClient()
    const pages = [
      { rows: [row(101)], next: { atUtc: row(101).atUtc, id: 101 } },
      { rows: [row(102, '20')], next: null },
    ]
    const key = ['api', 'activity.query', '2026-09-01T00:00:00Z', '2026-10-01T00:00:00Z', 0, undefined]
    const journalKey = [...key]
    journalKey[4] = 2
    client.setQueryData(key, { pages, pageParams: [null, pages[0]!.next] })
    client.setQueryData(journalKey, { pages: [{ rows: [], next: null }], pageParams: [null] })
    client.setQueryData(['api', 'game.details', { workId: 1 }], {
      sessions: { 11: [row(102, '20').session] },
      journalEntries: [],
    })
    patchJournalCaches(client, 102, { sessionId: 102, note: 'New note', rating: 4, revision: 'r2' })
    const changed = client.getQueryData<{ pages: ActivityPage[]; pageParams: unknown[] }>(key)!
    expect(changed.pages).toHaveLength(2)
    expect(changed.pageParams).toEqual([null, pages[0]!.next])
    expect(changed.pages[1]!.rows[0]!.note).toMatchObject({ sessionId: 102, note: 'New note', rating: 4 })
    expect(client.getQueryData<{ pages: ActivityPage[] }>(journalKey)!.pages[0]!.rows).toHaveLength(1)
    patchJournalCaches(client, 102, null)
    expect(client.getQueryData<{ pages: ActivityPage[] }>(journalKey)!.pages[0]!.rows).toHaveLength(0)
    expect(client.getQueryData<{ pages: ActivityPage[] }>(key)!.pages[1]!.rows).toHaveLength(1)
  })
})

describe('activity browsing parity', () => {
  it.each(['desktop', 'fullscreen'] as const)(
    'opens the same grouped game from either member ownership in %s Activity',
    async (mode) => {
      const prey = {
        ...game,
        title: 'Prey',
        playtimeMinutes: 390,
        entries: [
          { ...game.entries[0], title: 'Prey' },
          {
            ...game.entries[0],
            title: 'Prey Deluxe',
            workId: 2,
            ownershipId: 12,
            releaseId: 22,
            store: 'epic',
            playtimeMinutes: 90,
          },
        ],
      }
      const secondary = {
        ...row(102),
        ownershipId: 12,
        store: 'epic',
        session: { ...row(102).session, ownershipId: 12 },
      }
      bridge((input) =>
        input.route === 'library.get'
          ? ok({ games: [prey], lists: [] })
          : input.route === 'activity.query'
            ? ok({ rows: [row(101), secondary], next: null })
            : undefined,
      )
      const open = vi.fn()
      host(<Journal mode={mode} onOpenGame={open} />)
      const articles = await screen.findAllByRole('article')
      expect(articles).toHaveLength(2)
      for (const article of articles) fireEvent.click(within(article).getByRole('button', { name: 'Prey' }))
      expect(open.mock.calls).toEqual([[1], [1]])
    },
  )
  it('uses Monday local weeks through DST and never selects a future week', () => {
    vi.stubEnv('TZ', 'America/Los_Angeles')
    expect(mondayWeek(0, new Date('2026-11-01T20:00:00Z'))).toEqual({
      fromUtc: '2026-10-26T07:00:00.000Z',
      untilUtc: '2026-11-02T08:00:00.000Z',
    })
    expect(mondayWeek(3, new Date('2026-11-01T20:00:00Z'))).toEqual(
      mondayWeek(0, new Date('2026-11-01T20:00:00Z')),
    )
  })
  it.each(['desktop', 'fullscreen'] as const)(
    'keeps loaded pages and selection while editing an older note in %s',
    async (mode) => {
      const request = bridge((input) =>
        input.route === 'activity.query'
          ? ok(
              (input.body as { after?: unknown }).after
                ? { rows: [row(102, '20')], next: null }
                : { rows: [row(101)], next: { atUtc: row(101).atUtc, id: 101 } },
            )
          : input.route === 'journal.put'
            ? ok({ sessionId: 102, note: 'Older session memory', rating: 4, revision: 'r2' })
            : undefined,
      )
      host(<Journal mode={mode} />)
      fireEvent.click(await screen.findByRole('button', { name: 'Earlier activity' }))
      await waitFor(() => expect(screen.getAllByRole('button', { name: 'Add note' })).toHaveLength(2))
      fireEvent.click(screen.getAllByRole('button', { name: 'Add note' })[1]!)
      fireEvent.change(await screen.findByLabelText('Your note'), {
        target: { value: 'Older session memory' },
      })
      fireEvent.change(screen.getByLabelText('Your rating'), { target: { value: '4' } })
      fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      expect(
        within(screen.getByRole('complementary', { name: 'Selected activity' })).getByText(
          'Older session memory',
        ),
      ).toBeTruthy()
      expect(screen.getAllByRole('article')).toHaveLength(2)
      expect(request.mock.calls.filter(([input]) => input.route === 'activity.query')).toHaveLength(2)
    },
  )
  it('retries only a failed next page and retains visible rows', async () => {
    let fail = true
    const request = bridge((input) =>
      input.route === 'activity.query'
        ? (input.body as { after?: unknown }).after
          ? fail
            ? { ok: false, status: 500 }
            : ok({ rows: [row(101), row(102)], next: null })
          : ok({ rows: [row(101)], next: { atUtc: row(101).atUtc, id: 101 } })
        : undefined,
    )
    host(<Journal />)
    fireEvent.click(await screen.findByRole('button', { name: 'Earlier activity' }))
    await screen.findByText("Couldn't read your activity. Try again.")
    expect(screen.getAllByRole('article')).toHaveLength(1)
    fail = false
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(2))
    const calls = request.mock.calls.filter(([input]) => input.route === 'activity.query')
    expect(calls).toHaveLength(3)
    expect(calls[1]![0].body).toEqual(calls[2]![0].body)
  })
  it('discards late week reads and preserves a section chosen while waiting', async () => {
    const pending: ((value: unknown) => void)[] = []
    bridge((input) =>
      input.route === 'activity.query' ? new Promise((resolve) => pending.push(resolve)) : undefined,
    )
    host(<Journal mode="fullscreen" />)
    await waitFor(() => expect(pending).toHaveLength(1))
    fireEvent.click(screen.getByRole('button', { name: 'Previous week' }))
    await waitFor(() => expect(pending).toHaveLength(2))
    fireEvent.click(screen.getByRole('button', { name: 'Updates' }))
    await waitFor(() => expect(pending).toHaveLength(3))
    await act(async () =>
      pending[2]!(
        ok({
          rows: [
            {
              ownershipId: 11,
              store: 'steam',
              atUtc: row(101).atUtc,
              update: {
                id: 30,
                releaseId: 21,
                kind: 'patch',
                occurredAt: row(101).atUtc,
                title: 'Current patch',
              },
            },
          ],
          next: null,
        }),
      ),
    )
    await act(async () => {
      pending[0]!(ok({ rows: [row(101)], next: null }))
      pending[1]!(ok({ rows: [row(102)], next: null }))
    })
    expect(screen.getByRole('button', { name: 'Updates' }).getAttribute('aria-pressed')).toBe('true')
    await waitFor(() => expect(screen.getAllByText('Current patch')).toHaveLength(2))
    expect(screen.queryByRole('button', { name: 'Add note' })).toBeNull()
  })
  it('distinguishes unavailable activity from empty history and names prompt-off journal state', async () => {
    const request = bridge((input) =>
      input.route === 'activity.query' ? { ok: false, status: 503 } : undefined,
    )
    host(<Journal />)
    await screen.findByText('Your activity history is unavailable.')
    expect(screen.queryByText(/No recorded sessions in this period/)).toBeNull()
    request.mockImplementation(async (input) =>
      ok(input.route === 'activity.query' ? { rows: [], next: null } : { promptAfterPlay: false }),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Journal' }))
    await screen.findByText('Journal prompts are off. Turn them on in Display preferences after a game.')
  })
  it('keeps unsaved text through a library refresh and removes hidden games from history', async () => {
    bridge()
    const view = host(<Journal mode="fullscreen" />)
    fireEvent.click(await screen.findByRole('button', { name: 'Add note' }))
    fireEvent.change(await screen.findByLabelText('Your note'), {
      target: { value: 'Do not lose this memory' },
    })
    act(() => view.client.setQueryData(['api', 'library.get'], { games: [], lists: [] }))
    await waitFor(() => expect(screen.queryByRole('article')).toBeNull())
    expect((screen.getByLabelText('Your note') as HTMLTextAreaElement).value).toBe('Do not lose this memory')
  })
  it('changes Monday weeks outside section tabs, clamps the future and offers selected-note reading and editing', async () => {
    const request = bridge((input) =>
      input.route === 'activity.query'
        ? ok({
            rows: [{ ...row(101), note: { sessionId: 101, note: 'Read this next time', rating: 4 } }],
            next: null,
          })
        : undefined,
    )
    host(<Journal mode="fullscreen" />)
    const events = await screen.findByRole('region', { name: 'Activity events' })
    fireEvent.keyDown(events, { key: 'ArrowLeft' })
    await waitFor(() =>
      expect(request.mock.calls.filter(([input]) => input.route === 'activity.query')).toHaveLength(2),
    )
    fireEvent.keyDown(screen.getByRole('button', { name: 'Updates' }), { key: 'ArrowLeft' })
    expect(request.mock.calls.filter(([input]) => input.route === 'activity.query')).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: 'This week' }))
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Next week' }) as HTMLButtonElement).disabled).toBe(true),
    )
    await screen.findByRole('button', { name: 'Read note' })
    fireEvent.keyDown(screen.getByRole('region', { name: 'Activity events' }), { key: 'y' })
    expect(within(await screen.findByRole('dialog')).getByText('Read this next time')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Back' }))
    const edit = screen.getByRole('button', { name: 'Edit selected note' })
    expect(edit.hasAttribute('data-controller-play')).toBe(true)
    fireEvent.click(edit)
    await screen.findByLabelText('Your note')
  })
})

describe('details history and journal parity', () => {
  const facts = {
    workId: 1,
    readAtUtc: '2026-09-28T20:00:00Z',
    events: [],
    sessions: { 11: [row(101).session] },
    journalEntries: [
      { sessionId: 101, ownershipId: 11, sessionAt: row(101).atUtc, note: 'Looking for the key.', rating: 3 },
    ],
    ratings: [],
    achievements: [],
  }
  const workspace = {
    preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'AdultsOnly' },
    externalIds: [],
    works: [],
    epicLaunchKeys: {},
    pluginActions: {},
  }
  it.each(['desktop', 'fullscreen'] as const)(
    'edits, retains across navigation and explicitly deletes a details journal note in %s',
    async (mode) => {
      let saved = { sessionId: 101, note: 'Looking for the key.', rating: 3, revision: 'r1' }
      const request = bridge((input) =>
        input.route === 'game.details'
          ? ok(facts)
          : input.route === 'library.workspace'
            ? ok(workspace)
            : input.route === 'journal.get'
              ? ok(saved)
              : input.route === 'journal.put'
                ? ((saved = { ...saved, note: (input.body as { note: string }).note, revision: 'r2' }),
                  ok(saved))
                : input.route === 'journal.delete'
                  ? ok(null)
                  : undefined,
      )
      const first = host(<Details mode={mode} workId={1} />)
      await screen.findByRole('heading', { name: 'Dragonwilds', level: 1 })
      fireEvent.click(screen.getByRole('button', { name: 'Journal' }))
      fireEvent.click(await screen.findByRole('button', { name: 'Edit note' }))
      fireEvent.change(await screen.findByLabelText('Your note'), {
        target: { value: 'Found the key behind the waterfall.' },
      })
      first.unmount()
      host(<Details mode={mode} workId={1} />, first.client)
      expect(((await screen.findByLabelText('Your note')) as HTMLTextAreaElement).value).toBe(
        'Found the key behind the waterfall.',
      )
      fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      expect(screen.getByText('Found the key behind the waterfall.')).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Edit note' }))
      fireEvent.click(await screen.findByRole('button', { name: 'Delete note' }))
      expect(request.mock.calls.some(([input]) => input.route === 'journal.delete')).toBe(false)
      fireEvent.click(screen.getByRole('button', { name: 'Yes, delete note' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      expect(screen.queryByText('Found the key behind the waterfall.')).toBeNull()
      expect(
        screen.getByText('Journal prompts are off. Turn them on in Display preferences after a game.'),
      ).toBeTruthy()
    },
  )
  it.each(['desktop', 'fullscreen'] as const)(
    'exposes separate Steam history and a prompt-off empty journal in details in %s',
    async (mode) => {
      bridge((input) =>
        input.route === 'game.details'
          ? ok({ ...facts, journalEntries: [] })
          : input.route === 'library.workspace'
            ? ok(workspace)
            : input.route === 'activity.steam'
              ? ok({ accountConfirmationRequired: false, activity: [] })
              : undefined,
      )
      host(<Details mode={mode} workId={1} />)
      await screen.findByRole('heading', { name: 'Dragonwilds', level: 1 })
      fireEvent.click(screen.getByRole('button', { name: 'History' }))
      expect(await screen.findByRole('region', { name: 'Steam-reported activity' })).toBeTruthy()
      expect(screen.getByRole('region', { name: 'Your play history' })).toBeTruthy()
      fireEvent.click(
        within(screen.getByRole('navigation', { name: 'Game information' })).getByRole('button', {
          name: 'Journal',
        }),
      )
      await screen.findByText('Journal prompts are off. Turn them on in Display preferences after a game.')
    },
  )
})

describe('Steam reported activity parity', () => {
  const reports = Array.from({ length: 21 }, (_, index) => ({
    id: index + 1,
    ownershipId: 11,
    windowStartedAt: '2026-09-20T12:00:00Z',
    windowEndedAt: '2026-09-20T13:00:00Z',
    steamDeltaMinutes: 31,
    unexplainedMinutes: index ? 31 : null,
    comparisonUnavailable: index === 0,
  }))
  it.each(['desktop', 'fullscreen'] as const)(
    'keeps estimates separate and pages twenty observations in %s',
    async (mode) => {
      const request = bridge(() => ok({ accountConfirmationRequired: false, activity: reports }))
      host(<SteamReportedActivity games={[game]} mode={mode} />)
      await screen.findByText('Page 1 of 2')
      expect(screen.getAllByRole('article')).toHaveLength(20)
      expect(screen.getByText(/not exact sessions and are not added to recorded-session totals/)).toBeTruthy()
      expect(screen.getByText(/may overlap recorded sessions/)).toBeTruthy()
      expect(screen.getAllByText(/About 31 min not matched/)).toHaveLength(19)
      fireEvent.click(screen.getByRole('button', { name: 'Next' }))
      expect(screen.getAllByRole('article')).toHaveLength(1)
      fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
      await screen.findByText('Page 1 of 2')
      expect(request.mock.calls.every(([input]) => input.route === 'activity.steam')).toBe(true)
      expect(request.mock.calls[0]![0].body).toEqual({ ownershipIds: [11] })
    },
  )
  it('hides non-Steam scope and discards pending observations after a scope change', async () => {
    let finish!: (value: unknown) => void
    const request = bridge(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    const view = host(<SteamReportedActivity games={[game]} />)
    await waitFor(() => expect(request).toHaveBeenCalledOnce())
    view.rerender(
      <QueryClientProvider client={view.client}>
        <SteamReportedActivity games={[{ ...game, entries: [{ ...game.entries[0]!, store: 'gog' }] }]} />
      </QueryClientProvider>,
    )
    await act(async () => finish(ok({ accountConfirmationRequired: false, activity: reports })))
    expect(screen.queryByRole('region', { name: 'Steam-reported activity' })).toBeNull()
    expect(screen.queryByRole('article')).toBeNull()
  })
  it('retries failed reads and distinguishes missing account confirmation from an empty result', async () => {
    const request = bridge(() => ({ ok: false, status: 500 }))
    host(<SteamReportedActivity games={[game]} />)
    await screen.findByText("Couldn't read Steam-reported activity. Try again.")
    request.mockResolvedValue(ok({ accountConfirmationRequired: true, activity: reports }))
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    await screen.findByText('Confirm your Steam account in Settings to see its reported activity.')
    expect(screen.queryByRole('article')).toBeNull()
  })
})

describe('recorded gameplay statistics parity', () => {
  it('uses inclusive local dates and contiguous bins with honest validation', () => {
    vi.stubEnv('TZ', 'America/Los_Angeles')
    const range = gameplayRange('custom', '2026-10-26', '2026-11-02', new Date('2026-11-03T20:00:00Z'))
    expect(range.fromUtc).toBe('2026-10-26T07:00:00.000Z')
    expect(range.untilUtc).toBe('2026-11-03T08:00:00.000Z')
    expect(range.timeBins[0]!.untilUtc).toBe(range.timeBins[1]!.fromUtc)
    expect(range.timeBins[1]!.untilUtc).toBe(range.untilUtc)
    expect(() => gameplayRange('custom', '2026-02-30', '2026-03-01')).toThrow('YYYY-MM-DD')
    expect(() => gameplayRange('custom', '2026-09-10', '2026-09-01')).toThrow('start date')
    expect(() => gameplayRange('custom', '2026-09-01', '2099-09-01')).toThrow('no later than today')
    expect(() => gameplayRange('custom', '1899-09-01', '1900-09-01')).toThrow('1900 or later')
  })
  it.each(['desktop', 'fullscreen'] as const)(
    'renders four selectable charts, scopes dates and stores, and retains state across spending in %s',
    async (mode) => {
      const request = bridge()
      const open = vi.fn()
      host(<GameplayDashboard mode={mode} onOpenGame={open} />)
      await screen.findByRole('region', { name: 'Recorded hours over time' })
      for (const title of ['Games you spent time with', 'Session lengths', 'Your library today'])
        expect(screen.getByRole('region', { name: title })).toBeTruthy()
      fireEvent.click(
        within(screen.getByRole('region', { name: 'Games you spent time with' })).getByRole('button', {
          name: /Dragonwilds/,
        }),
      )
      fireEvent.click(screen.getByRole('button', { name: 'Open game' }))
      expect(open).toHaveBeenCalledWith(1)
      fireEvent.change(screen.getByLabelText('Store'), { target: { value: 'steam' } })
      await waitFor(() =>
        expect(
          request.mock.calls.some(
            ([input]) =>
              input.route === 'statistics.gameplay' && (input.body as { store?: string }).store === 'steam',
          ),
        ).toBe(true),
      )
      fireEvent.change(screen.getByLabelText('Gameplay period'), { target: { value: 'custom' } })
      fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-09-01' } })
      fireEvent.change(screen.getByLabelText('Through'), { target: { value: '2026-09-10' } })
      fireEvent.click(screen.getByRole('button', { name: 'Apply dates' }))
      await waitFor(() =>
        expect(
          request.mock.calls.some(
            ([input]) =>
              input.route === 'statistics.gameplay' &&
              (input.body as { fromUtc: string }).fromUtc.startsWith('2026-09-01'),
          ),
        ).toBe(true),
      )
      fireEvent.click(screen.getByRole('button', { name: 'Spending' }))
      fireEvent.click(screen.getByRole('button', { name: 'Gameplay' }))
      expect((screen.getByLabelText('Gameplay period') as HTMLSelectElement).value).toBe('custom')
      expect((screen.getByLabelText('Store') as HTMLSelectElement).value).toBe('steam')
      expect((screen.getByLabelText('From') as HTMLInputElement).value).toBe('2026-09-01')
    },
  )
  it('clears obsolete figures for invalid custom dates and recovers after correction', async () => {
    bridge()
    host(<GameplayDashboard />)
    await screen.findByRole('region', { name: 'Recorded hours over time' })
    fireEvent.change(screen.getByLabelText('Gameplay period'), { target: { value: 'custom' } })
    fireEvent.change(screen.getByLabelText('From'), { target: { value: 'not a date' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply dates' }))
    await screen.findByText('Enter both dates as YYYY-MM-DD.')
    expect(screen.queryByRole('region', { name: 'Recorded hours over time' })).toBeNull()
    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-09-01' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply dates' }))
    await screen.findByRole('region', { name: 'Recorded hours over time' })
  })
  it('retries, cancels and resumes without publishing the canceled result', async () => {
    let response: unknown = { ok: false, status: 500 }
    const request = bridge((input) => (input.route === 'statistics.gameplay' ? response : undefined))
    host(<GameplayDashboard mode="fullscreen" />)
    await screen.findByText("Couldn't read gameplay statistics. Try again.")
    let finish!: (value: unknown) => void
    response = new Promise((resolve) => {
      finish = resolve
    })
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    await waitFor(() =>
      expect(request.mock.calls.filter(([input]) => input.route === 'statistics.gameplay')).toHaveLength(2),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await screen.findByText('Reading stopped. Choose Try again to resume.')
    response = ok(stats)
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    await screen.findByRole('region', { name: 'Recorded hours over time' })
    await act(async () => finish(ok({ ...stats, recordedSeconds: 9999999 })))
    expect(screen.queryByText('2,777.8 hr')).toBeNull()
    expect(screen.getByRole('region', { name: 'Recorded hours over time' })).toBeTruthy()
  })
  it('refreshes imported store choices and counts resolved games once', async () => {
    bridge()
    const view = host(<GameplayDashboard />)
    await screen.findByRole('region', { name: 'Your library today' })
    expect(screen.queryByRole('option', { name: /xbox/i })).toBeNull()
    act(() =>
      view.client.setQueryData(['api', 'library.get'], {
        games: [
          {
            ...game,
            entries: [...game.entries, { ...game.entries[0], ownershipId: 12, store: 'plugin:xbox' }],
          },
        ],
        lists: [],
      }),
    )
    expect(await screen.findByRole('option', { name: /xbox/i })).toBeTruthy()
    expect(
      within(screen.getByRole('region', { name: 'Your library today' })).getByText('1 games'),
    ).toBeTruthy()
  })
})
