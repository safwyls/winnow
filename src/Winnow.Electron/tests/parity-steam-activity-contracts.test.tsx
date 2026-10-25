// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SteamReportedActivity } from '../src/renderer/features/activity-steam'
import { Details } from '../src/renderer/features/Details'
import { Journal } from '../src/renderer/features/Journal'
import type { ApiRequest } from '../src/shared/bridge'
import type { LibraryGame, Mode } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'

vi.mock('../src/renderer/components/Artwork', () => ({ Artwork: () => null }))
vi.mock('../src/renderer/themes/avalon-backdrop', () => ({ AvalonBackdrop: () => null }))
const clients: QueryClient[] = []
const start = '2026-09-14T18:00:00Z',
  end = '2026-09-14T19:00:00Z'
const row = (id: number, ownershipId = 1) => ({
  id,
  ownershipId,
  accountRef: '123',
  windowStartedAt: start,
  windowEndedAt: end,
  steamDeltaMinutes: 31,
  unexplainedMinutes: 31 as number | null,
  matchedRecordedMinutes: 0,
  comparisonUnavailable: false,
})
const reports = [row(1), { ...row(2), comparisonUnavailable: true, unexplainedMinutes: null }]
const game: LibraryGame = {
  workId: 1,
  title: 'Dragonwilds',
  bucket: 'started',
  playtimeMinutes: 60,
  entries: [
    {
      ownershipId: 1,
      releaseId: 1,
      workId: 1,
      title: 'Dragonwilds',
      store: 'steam',
      installed: false,
      playtimeMinutes: 60,
    },
  ],
}
const now = new Date(),
  sessionStarted = new Date(now.getTime() - 7200000).toISOString()
const session = {
  id: 1,
  ownershipId: 1,
  startedAt: sessionStarted,
  endedAt: new Date(Date.parse(sessionStarted) + 600000).toISOString(),
  durationSeconds: 600,
  detectionMethod: 'process',
}
const ok = (data: unknown) => ({ ok: true, status: 200, data })
function fixture(
  games: LibraryGame[] = [game],
  steam: () => unknown = () => ok({ accountConfirmationRequired: false, activity: reports }),
) {
  const facts = {
    workId: 1,
    readAtUtc: now.toISOString(),
    events: [],
    sessions: { 1: [session] },
    journalEntries: [],
    ratings: [],
    achievements: [],
  }
  const request = vi.fn(async (input: ApiRequest) =>
    input.route === 'activity.steam'
      ? await steam()
      : ok(
          input.route === 'library.get'
            ? { games, lists: [] }
            : input.route === 'library.workspace'
              ? {
                  works: [{ id: 1, name: 'Dragonwilds' }],
                  externalIds: [],
                  epicLaunchKeys: {},
                  pluginActions: {},
                }
              : input.route === 'game.details'
                ? facts
                : input.route === 'activity.query'
                  ? { rows: [], next: null }
                  : input.route === 'activity.statistics'
                    ? { recordedSeconds: 600, gamesPlayedCount: 1, startedSessionCount: 1, periods: [] }
                    : input.route === 'journal.preferences.get'
                      ? { promptAfterPlay: false }
                      : input.route === 'metadata.get' || input.route === 'metadata.igdb'
                        ? { available: false }
                        : {},
        ),
  )
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, cancelRequest: vi.fn(async () => true), artwork: vi.fn(async () => null) },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  return {
    request,
    facts,
    client,
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  }
}
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of ['tab', 'section', 'reading', 'range', 'editing', 'editing-section']) {
      clearViewState(`${mode}:details:1:${key}`)
      clearViewState(`avalon:details:${mode}:1:${key}`)
    }
    for (const key of ['section', 'days', 'bounds', 'editing', 'panel', 'week', 'work', 'selected'])
      clearViewState(`${mode}:journal:${key}`)
  }
})
async function openHistory(mode: Mode, presentation: 'avalon' | 'shared') {
  await screen.findByRole('heading', { name: 'Dragonwilds', level: 1 })
  fireEvent.click(
    screen.getByRole(presentation === 'avalon' && mode === 'desktop' ? 'tab' : 'button', {
      name: presentation === 'shared' ? 'History' : mode === 'desktop' ? 'Activity' : 'Play history →',
    }),
  )
}

describe.each(['desktop', 'fullscreen'] as const)('%s reported activity exact source', (mode) => {
  it('Scope_filters_other_accounts_hidden_games_and_covered_increases (renderer request, error and confirmation boundary)', async () => {
    // Account123, other-account and covered rows are filtered by the API; the renderer also rejects out-of-scope ownerships.
    let response: unknown = ok({ accountConfirmationRequired: false, activity: [row(1), row(3, 99)] })
    const f = fixture([game], () => response)
    render(<SteamReportedActivity games={[game]} mode={mode} />, { wrapper: f.wrapper })
    await screen.findByText('About 31 min not matched to recorded sessions', { selector: 'strong' })
    expect(screen.getAllByRole('article')).toHaveLength(1)
    expect(f.request).toHaveBeenCalledWith(
      expect.objectContaining({ route: 'activity.steam', body: { ownershipIds: [1] } }),
    )
    response = { ok: false, status: 500 }
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    await screen.findByText("Couldn't read Steam-reported activity. Try again.")
    expect(screen.queryByRole('article')).toBeNull()
    response = ok({ accountConfirmationRequired: true, activity: [row(1)] })
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    await screen.findByText('Confirm your Steam account in Settings to see its reported activity.')
    expect(screen.queryByRole('article')).toBeNull()
  })
  it.each(['avalon', 'shared'] as const)(
    'Both_presentations_label_estimates_and_bounds_without_changing_sessions (%s)',
    async (presentation) => {
      let finish!: (value: unknown) => void
      const pending = new Promise((resolve) => {
        finish = resolve
      })
      const f = fixture([game], () => pending)
      const close = vi.fn()
      render(<Details workId={1} mode={mode} presentation={presentation} onClose={close} />, {
        wrapper: f.wrapper,
      })
      await openHistory(mode, presentation)
      await screen.findByRole('region', { name: 'Your play history' })
      const total = document.querySelector('.activity-total')!.textContent
      const before = structuredClone(f.facts.sessions)
      let entry: HTMLElement | undefined
      if (mode === 'fullscreen') {
        entry = screen.getByRole('button', { name: 'Steam-reported activity' })
        entry.focus()
        fireEvent.click(entry)
      }
      await act(async () => finish(ok({ accountConfirmationRequired: false, activity: reports })))
      const projection = await screen.findByRole('region', { name: 'Steam-reported activity' })
      const text = projection.textContent!
      for (const sentence of [
        'not exact sessions',
        'not added to recorded-session totals',
        'Observed between',
        'About 31 min not matched',
        'may overlap recorded sessions',
      ])
        expect(text).toContain(sentence)
      expect([...projection.querySelectorAll('time')].map((time) => time.dateTime)).toEqual([
        start,
        end,
        start,
        end,
      ])
      expect(f.facts.sessions).toEqual(before)
      expect(f.facts.sessions[1][0].durationSeconds).toBe(600)
      expect(document.querySelector('.activity-total')!.textContent).toBe(total)
      if (mode === 'fullscreen') {
        const action = within(projection).getByRole('button', {
          name: /Dragonwilds\. About 31 min not matched/,
        })
        action.focus()
        fireEvent.click(action)
        const reading = await screen.findByRole('region', { name: 'Steam activity observation' })
        const dialog = reading.closest('[role="dialog"]') as HTMLElement
        expect(reading.textContent).toContain('About 31 min not matched to recorded sessions')
        expect(reading.textContent).toContain('Observed between')
        expect(reading.textContent).toContain('not added to recorded-session totals')
        expect(
          within(dialog).getByRole('group', { name: 'Steam activity reading controls' }).textContent,
        ).toContain('Up/Down Read')
        const back = within(dialog).getByRole('button', { name: 'Back' })
        expect(document.activeElement).toBe(back)
        fireEvent.keyDown(back, { key: 'Escape' })
        await waitFor(() =>
          expect(screen.queryByRole('region', { name: 'Steam activity observation' })).toBeNull(),
        )
        expect(document.activeElement).toBe(action)
        expect(screen.getByRole('region', { name: 'Steam activity projection' })).toBeTruthy()
        fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
        await waitFor(() =>
          expect(screen.queryByRole('region', { name: 'Steam activity projection' })).toBeNull(),
        )
        expect(document.activeElement).toBe(entry)
        expect(close).not.toHaveBeenCalled()
      }
      expect(document.querySelector('.activity-total')!.textContent).toBe(total)
      expect(
        f.request.mock.calls.some(([input]) =>
          ['sessions.update', 'sessions.delete', 'journal.put'].includes(input.route),
        ),
      ).toBe(false)
    },
  )
  it.each(['epic', 'gog', 'mixed'])(
    'Non_Steam_games_hide_the_section_on_both_surfaces_but_mixed_games_keep_it (%s)',
    async (store) => {
      const g = {
        ...game,
        entries:
          store === 'mixed'
            ? [
                { ...game.entries[0], store: 'gog' },
                { ...game.entries[0], store: 'steam', ownershipId: 2, releaseId: 2 },
              ]
            : [{ ...game.entries[0], store }],
      }
      const f = fixture([g], () => ok({ accountConfirmationRequired: false, activity: [] }))
      render(<Details workId={1} mode={mode} presentation="avalon" />, { wrapper: f.wrapper })
      await openHistory(mode, 'avalon')
      if (store === 'mixed') {
        if (mode === 'fullscreen')
          fireEvent.click(screen.getByRole('button', { name: 'Steam-reported activity' }))
        expect(await screen.findByRole('region', { name: 'Steam-reported activity' })).toBeTruthy()
        await waitFor(() =>
          expect(f.request).toHaveBeenCalledWith(
            expect.objectContaining({ route: 'activity.steam', body: { ownershipIds: [2] } }),
          ),
        )
      } else {
        expect(screen.queryByRole('region', { name: 'Steam-reported activity' })).toBeNull()
        expect(screen.queryByRole('button', { name: 'Steam-reported activity' })).toBeNull()
        expect(f.request.mock.calls.some(([input]) => input.route === 'activity.steam')).toBe(false)
      }
    },
  )
})
it('Fullscreen_activity_and_per_game_history_offer_separate_entry_points (global Activity)', async () => {
  const f = fixture()
  render(<Journal mode="fullscreen" />, { wrapper: f.wrapper })
  const link = await screen.findByRole('button', { name: 'Steam-reported activity' })
  link.focus()
  fireEvent.click(link)
  expect(document.activeElement).toBe(link)
  fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
  expect(screen.queryByRole('region', { name: 'Steam-reported activity' })).toBeNull()
  expect(document.activeElement).toBe(
    within(screen.getByRole('navigation', { name: 'Activity pages' })).getByRole('button', {
      name: 'History',
    }),
  )
  fireEvent.click(link)
  const projection = await screen.findByRole('region', { name: 'Steam-reported activity' })
  await within(projection).findByText('About 31 min not matched to recorded sessions', { selector: 'strong' })
  expect(within(projection).getByRole('group', { name: 'Steam activity controls' }).textContent).toContain(
    'A Read activity',
  )
  fireEvent.keyDown(within(projection).getByRole('button', { name: 'Refresh' }), { key: 'Escape' })
  expect(screen.queryByRole('region', { name: 'Steam-reported activity' })).toBeNull()
  expect(document.activeElement).toBe(
    within(screen.getByRole('navigation', { name: 'Activity pages' })).getByRole('button', {
      name: 'History',
    }),
  )
})
