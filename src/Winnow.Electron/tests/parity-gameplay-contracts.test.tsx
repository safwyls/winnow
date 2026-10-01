// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useLayoutEffect, useState, type ReactNode } from 'react'
import { GameplayDashboard } from '../src/renderer/features/activity-gameplay'
import { gameplayRange } from '../src/renderer/features/activity-model'
import { useLibrary } from '../src/renderer/api/hooks'
import { avalonFilter } from '../src/renderer/themes/avalon-data'
import { clearViewState, useViewState } from '../src/renderer/viewState'
import type { LibraryGame, Mode } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'
const now = new Date('2026-12-01T12:00:00Z')
const ok = (data: unknown) => ({ ok: true, status: 200, data })
const facts = (recordedSeconds = 0) => ({
  recordedSeconds,
  gamesPlayedCount: 0,
  overlappingSessionCount: 0,
  startedSessionCount: 0,
  medianSessionSeconds: null,
  excludedSessionCount: 0,
  periods: [],
  topGames: [],
  sessionLengths: [],
})
// Exact LibraryReadFixtures.Seed(2) IDs, titles and unplayed ownerships.
function game(id: number, store = 'steam'): LibraryGame {
  return {
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
        store,
        installed: false,
        playtimeMinutes: 0,
      },
    ],
  }
}
// PreviewLibrary.Snapshot: nine ownerships resolve to eight games; the GOG Witcher is work30 under3.
const preview = [
  [1, 'Hollow Knight', 'steam', 'stale_but_patched', 1540],
  [2, 'Disco Elysium', 'steam', 'never_played', 0],
  [3, 'The Witcher 3: Wild Hunt', 'steam', 'retired', 8200],
  [4, 'Stardew Valley', 'gog', 'stale_but_patched', 340],
  [5, 'Celeste', 'epic', 'active', 45],
  [6, "Baldur's Gate 3", 'steam', 'active', 90],
  [7, 'Slay the Spire', 'steam', 'never_played', 0],
  [8, 'Portal 2', 'steam', 'retired', 6500],
].map(([workId, title, store, bucket, playtimeMinutes]) => ({
  workId: Number(workId),
  title: String(title),
  bucket: String(bucket),
  playtimeMinutes: Number(playtimeMinutes),
  entries: [
    {
      ownershipId: 200 + Number(workId),
      releaseId: 100 + Number(workId),
      workId: Number(workId),
      title: String(title),
      store: String(store),
      installed: [1, 3, 4, 6].includes(Number(workId)),
      playtimeMinutes: Number(playtimeMinutes),
    },
  ],
})) satisfies LibraryGame[]
preview[2]!.entries[0]!.playtimeMinutes = 7200
preview[2]!.entries.push({
  ownershipId: 230,
  releaseId: 130,
  workId: 30,
  title: 'The Witcher 3: Wild Hunt',
  store: 'gog',
  installed: false,
  playtimeMinutes: 1000,
})
function previewStats(input: ApiRequest) {
  const body = input.body as ReturnType<typeof gameplayRange>
  const topGames = [1, 2, 3, 4, 5, 6].map((resolvedWorkId, index) => ({
    resolvedWorkId,
    recordedSeconds: (6 - index) * 5400,
  }))
  const total = 113400,
    count = body.timeBins.length
  return ok({
    recordedSeconds: total,
    gamesPlayedCount: 6,
    overlappingSessionCount: 24,
    startedSessionCount: 23,
    medianSessionSeconds: 2700,
    excludedSessionCount: 1,
    topGames,
    periods: body.timeBins.map((bin, index) => ({
      ...bin,
      recordedSeconds: (total * (index + 1)) / ((count * (count + 1)) / 2),
    })),
    sessionLengths: [
      { minimumSeconds: 0, maximumSeconds: 1800, count: 5 },
      { minimumSeconds: 1800, maximumSeconds: 3600, count: 9 },
      { minimumSeconds: 3600, maximumSeconds: 7200, count: 6 },
      { minimumSeconds: 7200, maximumSeconds: null, count: 3 },
    ],
  })
}
const spending = {
  source: 'steam',
  hasAnything: true,
  isSingleCurrency: true,
  currencySymbol: '$',
  currencyGroups: [],
  transactionCount: 1,
  licenseCount: 0,
  knownAccountCount: 0,
  unknownAccountFactCount: 0,
  transactionsWithoutCurrency: 0,
  grossProductSpendCents: 2000,
  refundedProductSpendCents: 0,
  netProductSpendCents: 2000,
  grossProductTransactionCount: 1,
  refundedProductTransactionCount: 0,
  netProductTransactionCount: 1,
  purchases: { count: 1, cents: 2000 },
  giftPurchases: { count: 0, cents: 0 },
  inGamePurchases: { count: 0, cents: 0 },
  bundlePurchases: { count: 0, cents: 0 },
  refundTransactions: { count: 0, cents: 0 },
  walletCreditPurchases: { count: 0, cents: 0 },
  walletCreditRedemptions: { count: 0, cents: 0 },
  discountedPurchases: { count: 0, cents: 0 },
  discountedPurchaseListCents: 0,
  spendByYear: [{ year: 2026, transactionCount: 1, cents: 2000 }],
  undatedNetSpendCents: 0,
  undatedNetTransactionCount: 0,
  licenseAcquisitions: [],
}
function deferred() {
  let resolve!: (value: ReturnType<typeof ok>) => void
  const promise = new Promise<ReturnType<typeof ok>>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
const clients: QueryClient[] = []
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(now)
  vi.stubEnv('TZ', 'UTC')
})
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  for (const mode of ['desktop', 'fullscreen'])
    for (const key of ['section', 'period', 'store', 'from', 'until', 'applied', 'currency'])
      clearViewState(`${mode}:stats:${key}`)
  vi.useRealTimers()
  vi.unstubAllEnvs()
})
function setup(
  mode: Mode,
  initial = [game(1), game(2, 'gog')],
  read: (input: ApiRequest) => unknown = () => ok(facts()),
) {
  const request = vi.fn(async (input: ApiRequest) =>
    input.route === 'library.get'
      ? ok({ games: initial, lists: [] })
      : input.route === 'library.workspace'
        ? ok({ works: [], identityLinks: [], preferredHeaderStores: {} })
        : input.route === 'statistics.account'
          ? ok(spending)
          : read(input),
  )
  const cancelRequest = vi.fn(async () => undefined),
    exportAcquisitions = vi.fn(async () => true)
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, cancelRequest, exportAcquisitions },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } })
  clients.push(client)
  const wrap = (child: ReactNode) => <QueryClientProvider client={client}>{child}</QueryClientProvider>
  const view = render(wrap(<GameplayDashboard mode={mode} />))
  const reads = () =>
    request.mock.calls.map(([input]) => input).filter((input) => input.route === 'statistics.gameplay')
  const publish = (games: LibraryGame[]) =>
    act(() => {
      client.setQueryData(['api', 'library.get'], { games, lists: [] })
    })
  return {
    ...view,
    client,
    request,
    cancelRequest,
    exportAcquisitions,
    reads,
    publish,
    show: (child: ReactNode = <GameplayDashboard mode={mode} />) => view.rerender(wrap(child)),
  }
}
function choose(mode: Mode, label: 'Store' | 'Gameplay period', value: string, text: string) {
  if (mode === 'desktop')
    fireEvent.change(screen.getByRole('combobox', { name: label }), { target: { value } })
  else fireEvent.click(within(screen.getByRole('group', { name: label })).getByRole('button', { name: text }))
}
function selectedStore(mode: Mode, text: string, value: string) {
  if (mode === 'desktop')
    expect((screen.getByRole('combobox', { name: 'Store' }) as HTMLSelectElement).value).toBe(value)
  else
    expect(
      within(screen.getByRole('group', { name: 'Store' }))
        .getByRole('button', { name: text })
        .getAttribute('aria-pressed'),
    ).toBe('true')
}
const loaded = () => screen.findByRole('region', { name: 'Your library today' })
function SeedDates({ mode, from, until }: { mode: Mode; from: string; until: string }) {
  const [, period] = useViewState(`${mode}:stats:period`, '30'),
    [, setFrom] = useViewState(`${mode}:stats:from`, ''),
    [, setUntil] = useViewState(`${mode}:stats:until`, '')
  const [, applied] = useViewState(`${mode}:stats:applied`, { from, until })
  useLayoutEffect(() => {
    period('custom')
    setFrom(from)
    setUntil(until)
    applied({ from, until })
  }, [period, setFrom, setUntil, applied, from, until])
  return null
}
function SearchContext({ mode }: { mode: Mode }) {
  const library = useLibrary(),
    [query, setQuery] = useState('')
  const visible = avalonFilter(library.data?.games ?? [], [], {
    query,
    bucket: 'all',
    store: 'all',
    listId: 'all',
    sort: 'title',
  })
  return (
    <>
      <label>
        Library search
        <input value={query} onChange={(event) => setQuery(event.target.value)} />
      </label>
      <output aria-label="Search results">{visible.length}</output>
      <GameplayDashboard mode={mode} />
    </>
  )
}
describe.each<Mode>(['desktop', 'fullscreen'])('%s gameplay source contracts', (mode) => {
  it('renders the original eight-game preview figures, four charts and current store composition independent of period', async () => {
    const view = setup(mode, preview, previewStats)
    await loaded()
    expect(screen.getByText('31.5 h')).toBeTruthy()
    expect(screen.getByText('45 min')).toBeTruthy()
    expect(screen.getByText('Recorded hours')).toBeTruthy()
    expect(
      screen.getByText(
        'Completed Winnow sessions on this library, not lifetime playtime or active attention. Overlapping games count independently. Account filters select games, not who played.',
      ),
    ).toBeTruthy()
    const lengths = within(screen.getByRole('region', { name: 'Session lengths' }))
    for (const count of [5, 9, 6, 3]) expect(lengths.getByText(`${count} sessions`)).toBeTruthy()
    expect(
      lengths.getByText(
        '23 completed sessions started in this period; median and bands use their full lengths. Hours include the portions of 24 sessions within these dates. 1 unfinished or invalid records excluded.',
      ),
    ).toBeTruthy()
    const hours = within(screen.getByRole('region', { name: 'Recorded hours over time' }))
    expect(hours.getAllByRole('button')[0]!.textContent).toContain('Nov 2, 2026 – Nov 8, 2026')
    expect(
      within(screen.getByRole('region', { name: 'Games you spent time with' })).getAllByRole('button'),
    ).toHaveLength(6)
    const storeGroup = within(screen.getByRole('group', { name: 'Store entries' }))
    expect(storeGroup.getByText('Steam').parentElement?.textContent).toBe('Steam6')
    expect(storeGroup.getByText('GOG').parentElement?.textContent).toBe('GOG2')
    expect(storeGroup.getByText('Epic Games').parentElement?.textContent).toBe('Epic Games1')
    const counts = [
      ...screen.getByRole('region', { name: 'Your library today' }).querySelectorAll(':scope > ol strong'),
    ].map((node) => node.textContent)
    expect(counts).toEqual(['2 games', '2 games', '2 games', '2 games'])
    choose(mode, 'Gameplay period', '90', '90 days')
    await waitFor(() =>
      expect(view.reads().at(-1)?.body).toMatchObject({
        fromUtc: '2026-09-03T00:00:00.000Z',
        untilUtc: '2026-12-02T00:00:00.000Z',
      }),
    )
    await loaded()
    expect(
      [
        ...screen.getByRole('region', { name: 'Your library today' }).querySelectorAll(':scope > ol strong'),
      ].map((node) => node.textContent),
    ).toEqual(counts)
  })

  it('hidden scope reload cancels and rejects an obsolete cancellation-ignoring result', async () => {
    const old = deferred(),
      current = deferred()
    let index = 0
    const view = setup(mode, undefined, () => (++index === 1 ? old.promise : current.promise))
    await waitFor(() => expect(view.reads()).toHaveLength(1))
    view.publish([game(1)])
    await waitFor(() => expect(view.reads()).toHaveLength(2))
    expect(view.cancelRequest).toHaveBeenCalledWith(view.reads()[0]!.requestId)
    await act(async () => current.resolve(ok(facts(3600))))
    await loaded()
    const committed = screen.getByText('1 h')
    await act(async () => old.resolve(ok(facts(999999))))
    expect(screen.getByText('1 h')).toBe(committed)
    expect(screen.queryByText('277.8 h')).toBeNull()
    expect(
      within(screen.getByRole('region', { name: 'Your library today' })).getByText('1 games'),
    ).toBeTruthy()
  })
  it.each([
    ['gog', 'GOG'],
    ['plugin:xbox', 'Xbox'],
  ])(
    'identity reload retains %s, then removal permanently resets its saved selection',
    async (store, label) => {
      const view = setup(mode, [game(1), game(2, store)])
      await loaded()
      choose(mode, 'Store', store, label)
      await waitFor(() => expect(view.reads().at(-1)?.body).toMatchObject({ store }))
      await loaded()
      const before = view.reads().length
      view.publish([{ ...game(1), entries: [game(1).entries[0]!, game(2, store).entries[0]!] }])
      await waitFor(() => expect(view.reads().length).toBe(before + 1))
      expect(view.reads().at(-1)?.body).toMatchObject({ store })
      await loaded()
      expect(
        within(screen.getByRole('region', { name: 'Your library today' })).getByText('1 games'),
      ).toBeTruthy()
      selectedStore(mode, label, store)
      view.publish([game(1)])
      await waitFor(() => expect(view.reads().at(-1)?.body).toMatchObject({ store: null }))
      selectedStore(mode, 'All stores', '')
      await loaded()
      view.publish([game(1), game(2, store)])
      await loaded()
      selectedStore(mode, 'All stores', '')
      expect(view.reads().at(-1)?.body).toMatchObject({ store: null })
    },
  )
  it('search does not filter statistics and deactivation aborts pending data', async () => {
    const held = deferred(),
      view = setup(mode, undefined, () => held.promise)
    view.show(<SearchContext mode={mode} />)
    await waitFor(() => expect(view.reads().length).toBeGreaterThan(0))
    fireEvent.change(screen.getByRole('textbox', { name: 'Library search' }), {
      target: { value: 'no matching game' },
    })
    expect(screen.getByLabelText('Search results').textContent).toBe('0')
    const pending = view.reads().at(-1)!,
      count = view.reads().length
    expect(pending.body).toMatchObject({ store: null })
    expect(pending.params).toBeUndefined()
    view.show(null)
    expect(view.cancelRequest).toHaveBeenCalledWith(pending.requestId)
    await act(async () => held.resolve(ok(facts(36000))))
    expect(view.reads()).toHaveLength(count)
    expect(
      view.client
        .getQueriesData({ queryKey: ['api', 'statistics.gameplay.dashboard'] })
        .every(([, value]) => value === undefined),
    ).toBe(true)
    expect(screen.queryByText('10 h')).toBeNull()
  })
  it.each([
    ['bad', '2026-01-01', 'YYYY-MM-DD'],
    ['2026-01-02', '2026-01-01', 'start date'],
    ['2027-01-01', '2027-01-01', 'no later than today'],
    ['1899-12-31', '1900-01-01', '1900 or later'],
    ['2000-01-01', '2026-01-01', 'ten years'],
  ])('invalid dates %s through %s issue no read or obsolete publication', async (from, until, error) => {
    const old = deferred(),
      view = setup(mode, undefined, () => old.promise)
    await waitFor(() => expect(view.reads()).toHaveLength(1))
    view.show(<SeedDates mode={mode} from={from} until={until} />)
    expect(view.cancelRequest).toHaveBeenCalledWith(view.reads()[0]!.requestId)
    view.show()
    expect((await screen.findByRole('alert')).textContent).toContain(error)
    expect(view.reads()).toHaveLength(1)
    await act(async () => old.resolve(ok(facts(999999))))
    expect(screen.queryByRole('region', { name: 'Recorded hours over time' })).toBeNull()
    expect(screen.queryByText('Reading gameplay statistics…')).toBeNull()
    expect(view.reads()).toHaveLength(1)
  })
  it('invalid Apply cancels the current physical read and correction remains actionable', async () => {
    const held = deferred(),
      view = setup(mode, undefined, () => held.promise)
    await waitFor(() => expect(view.reads()).toHaveLength(1))
    choose(mode, 'Gameplay period', 'custom', 'Custom')
    await waitFor(() => expect(view.reads()).toHaveLength(2))
    fireEvent.change(screen.getByRole('textbox', { name: 'From' }), { target: { value: 'not a date' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply dates' }))
    expect(view.cancelRequest).toHaveBeenCalledWith(view.reads()[1]!.requestId)
    await screen.findByText('Enter both dates as YYYY-MM-DD.')
    await act(async () => held.resolve(ok(facts(999999))))
    expect(screen.queryByRole('region', { name: 'Your library today' })).toBeNull()
    expect(view.reads()).toHaveLength(2)
    fireEvent.change(screen.getByRole('textbox', { name: 'From' }), { target: { value: '2026-09-01' } })
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    await loaded()
    expect(view.reads()).toHaveLength(3)
  })
  it('Xbox import refreshes ownership counts, store-prefixed dates and the empty median', async () => {
    const view = setup(mode, [game(1), game(2)])
    await loaded()
    expect(
      mode === 'desktop'
        ? screen.queryByRole('option', { name: 'Xbox' })
        : screen.queryByRole('button', { name: 'Xbox' }),
    ).toBeNull()
    view.publish([game(1), game(2, 'plugin:xbox')])
    await waitFor(() => expect(view.reads()).toHaveLength(2))
    await screen.findByRole('group', { name: 'Store entries' })
    const stores = within(screen.getByRole('group', { name: 'Store entries' }))
    expect(stores.getByText('Steam').parentElement?.textContent).toBe('Steam1')
    expect(stores.getByText('Xbox').parentElement?.textContent).toBe('Xbox1')
    choose(mode, 'Store', 'plugin:xbox', 'Xbox')
    await screen.findByText(/^Xbox ·/)
    expect(screen.getByText('0 h')).toBeTruthy()
    expect(screen.getByText('No completed sessions')).toBeTruthy()
    const selected = within(screen.getByRole('group', { name: 'Store entries' }))
    expect(selected.queryByText('Steam')).toBeNull()
    expect(selected.getByText('Xbox').parentElement?.textContent).toBe('Xbox1')
    expect(
      within(screen.getByRole('region', { name: 'Your library today' })).getByText('1 games'),
    ).toBeTruthy()
  })
  it('failed refresh clears earlier figures and Try again publishes only the new successful read', async () => {
    let response: unknown = ok(facts(3600))
    const view = setup(mode, undefined, () => response)
    await loaded()
    expect(screen.getByText('1 h')).toBeTruthy()
    response = { ok: false, status: 500, message: 'Injected failure' }
    await act(async () => {
      await view.client.refetchQueries({ queryKey: ['api', 'statistics.gameplay.dashboard'] })
    })
    await screen.findByText("Couldn't read gameplay statistics. Try again.")
    expect(screen.queryByRole('region', { name: 'Your library today' })).toBeNull()
    expect(screen.queryByText('1 h')).toBeNull()
    response = ok(facts(7200))
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    await screen.findByText('2 h')
    expect(screen.queryByRole('alert')).toBeNull()
    expect(view.reads()).toHaveLength(3)
  })
  it('invalid applied dates survive Spending and reactivation without querying the previous valid range', async () => {
    const view = setup(mode)
    await loaded()
    choose(mode, 'Gameplay period', 'custom', 'Custom')
    await waitFor(() => expect(view.reads()).toHaveLength(2))
    fireEvent.change(screen.getByRole('textbox', { name: 'From' }), { target: { value: '2026-09-01' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply dates' }))
    await waitFor(() => expect(view.reads()).toHaveLength(3))
    await loaded()
    fireEvent.change(screen.getByRole('textbox', { name: 'From' }), { target: { value: 'bad' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply dates' }))
    await screen.findByText('Enter both dates as YYYY-MM-DD.')
    fireEvent.click(screen.getByRole('button', { name: 'Spending' }))
    await screen.findByRole('button', { name: 'Refresh Steam spending' })
    fireEvent.click(screen.getByRole('button', { name: 'Gameplay' }))
    expect(screen.getByRole('textbox', { name: 'From' })).toHaveProperty('value', 'bad')
    await screen.findByText('Enter both dates as YYYY-MM-DD.')
    expect(screen.queryByRole('region', { name: 'Your library today' })).toBeNull()
    expect(view.reads()).toHaveLength(3)
  })

  it('section controls retain focus and custom scope across the compact Spending toolbar', async () => {
    const view = setup(mode)
    await loaded()
    choose(mode, 'Store', 'gog', 'GOG')
    choose(mode, 'Gameplay period', 'custom', 'Custom')
    fireEvent.change(screen.getByRole('textbox', { name: 'From' }), { target: { value: '2026-09-01' } })
    fireEvent.change(screen.getByRole('textbox', { name: 'Through' }), { target: { value: '2026-09-10' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply dates' }))
    await screen.findByText(/^GOG · 1 Sept? 2026 – 10 Sept? 2026 · local dates$/)
    const spendingButton = screen.getByRole('button', { name: 'Spending' })
    spendingButton.focus()
    if (mode === 'fullscreen')
      fireEvent.keyDown(screen.getByRole('navigation', { name: 'Library summary section' }), {
        key: 'PageDown',
      })
    else fireEvent.click(spendingButton)
    await screen.findByRole('button', { name: 'Refresh Steam spending' })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Spending' }))
    expect(screen.queryByRole('heading', { name: 'What you brought home' })).toBeNull()
    expect(
      screen.getByText('Source: Steam account pages. Spending imports for other stores are not available.'),
    ).toBeTruthy()
    expect(
      screen.getByText('Totals of the Steam account pages that were read, not of the whole account.'),
    ).toBeTruthy()
    const figures = document.querySelector('.account-spending-figures')!
    expect(within(figures as HTMLElement).getAllByText('$20.00')).toHaveLength(2)
    expect(
      figures.compareDocumentPosition(screen.getByText('Refunded share of product purchases')) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Export acquisitions' }))
    await waitFor(() => expect(view.exportAcquisitions).toHaveBeenCalledOnce())
    const gameplayButton = screen.getByRole('button', { name: 'Gameplay' })
    gameplayButton.focus()
    if (mode === 'fullscreen')
      fireEvent.keyDown(screen.getByRole('navigation', { name: 'Library summary section' }), {
        key: 'PageUp',
      })
    else fireEvent.click(gameplayButton)
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Gameplay' }))
    selectedStore(mode, 'GOG', 'gog')
    expect(screen.getByRole('textbox', { name: 'From' })).toHaveProperty('value', '2026-09-01')
    expect(screen.getByRole('textbox', { name: 'Through' })).toHaveProperty('value', '2026-09-10')
    await loaded()
  })
})
describe('source inclusive calendar boundaries', () => {
  it.each([
    ['2026-03-08', 23],
    ['2026-11-01', 25],
  ] as const)('Los Angeles %s spans exactly %d hours in one UTC bin', (date, hours) => {
    vi.stubEnv('TZ', 'America/Los_Angeles')
    const range = gameplayRange('custom', date, date, now)
    expect(Date.parse(range.untilUtc) - Date.parse(range.fromUtc)).toBe(hours * 3600000)
    expect(range.timeBins).toEqual([{ fromUtc: range.fromUtc, untilUtc: range.untilUtc }])
    expect(range.fromUtc.endsWith('Z') && range.untilUtc.endsWith('Z')).toBe(true)
  })
  it('accepts exactly 3660 elapsed calendar days plus the inclusive final date', () => {
    const end = new Date('2026-12-01T00:00:00Z'),
      start = new Date(end)
    start.setUTCDate(start.getUTCDate() - 3660)
    const range = gameplayRange('custom', start.toISOString().slice(0, 10), '2026-12-01', now)
    expect(Date.parse(range.untilUtc) - Date.parse(range.fromUtc)).toBe(3661 * 86400000)
    start.setUTCDate(start.getUTCDate() - 1)
    expect(() => gameplayRange('custom', start.toISOString().slice(0, 10), '2026-12-01', now)).toThrow(
      'ten years',
    )
  })
})
