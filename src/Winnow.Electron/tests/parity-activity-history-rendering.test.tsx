// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Details } from '../src/renderer/features/Details'
import { ActivityTracker } from '../src/renderer/features/activity-timeline'
import { SessionRows } from '../src/renderer/features/Journal'
import * as format from '../src/renderer/features/activity-format'
import type { GameDetails, LibraryGame, Session } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'
import { clearViewState } from '../src/renderer/viewState'

const now = Date.parse('2026-09-08T00:00:00Z')
const sessions = (count: number): Session[] =>
  Array.from({ length: count }, (_, index) => ({
    id: index + 1,
    ownershipId: 1,
    startedAt: new Date(now - (index + 1) * 86400000).toISOString(),
    endedAt: new Date(now - (index + 1) * 86400000 + 3600000).toISOString(),
    durationSeconds: 3600,
    detectionMethod: 'process_watch',
  }))
const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  for (const client of clients.splice(0)) client.clear()
  vi.restoreAllMocks()
  for (const mode of ['desktop', 'fullscreen']) clearViewState(`rendering:${mode}`)
})

describe.each(['desktop', 'fullscreen'] as const)('%s large history rendering', (mode) => {
  it('does not format unvisited journal rows and retains every note when Journal is opened', () => {
    const values = sessions(200)
    const game: LibraryGame = {
      workId: 1,
      title: 'Game 1',
      bucket: 'never_played',
      playtimeMinutes: 0,
      entries: [
        {
          workId: 1,
          releaseId: 1,
          ownershipId: 1,
          title: 'Game 1',
          store: 'steam',
          installed: false,
          playtimeMinutes: 0,
        },
      ],
    }
    const facts: GameDetails = {
      workId: 1,
      readAtUtc: new Date(now).toISOString(),
      events: [],
      sessions: {},
      ratings: [],
      achievements: [],
      journalEntries: values.map((session) => ({
        sessionId: session.id,
        ownershipId: 1,
        sessionAt: session.startedAt,
        note: `Preserved note ${session.id}`,
        rating: 4,
      })),
    }
    Object.defineProperty(window, 'winnow', {
      configurable: true,
      value: {
        request: vi.fn(async (input: ApiRequest) => ({
          ok: true,
          status: 200,
          data:
            input.route === 'artwork.backdrops'
              ? { candidates: [], coverKey: null }
              : input.route === 'artwork.sources'
                ? []
                : {},
        })),
        artwork: vi.fn().mockResolvedValue(null),
      },
    })
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
    clients.push(client)
    client.setQueryData(['api', 'library.get'], { games: [game], lists: [] })
    client.setQueryData(['api', 'library.workspace', undefined], {
      works: [{ id: 1, name: 'Game 1' }],
      externalIds: [],
      pluginActions: {},
      epicLaunchKeys: {},
    })
    client.setQueryData(['api', 'game.details', { workId: 1 }], facts)
    const dates = vi.spyOn(format, 'activityDateLabel')
    const view = render(
      <QueryClientProvider client={client}>
        <Details workId={1} mode={mode} presentation="avalon" />
      </QueryClientProvider>,
    )
    expect(screen.getByRole('heading', { name: 'Game 1', level: 1 })).toBeTruthy()
    expect(dates).not.toHaveBeenCalled()
    expect(view.container.querySelectorAll('article.timeline-entry')).toHaveLength(0)
    fireEvent.click(screen.getByRole('tab', { name: 'Journal' }))
    const body = document.querySelector<HTMLElement>('.avalon-details-reading')!
    expect(body.querySelectorAll('article.timeline-entry')).toHaveLength(200)
    expect(within(body).getAllByRole('button', { name: 'Edit note' })).toHaveLength(200)
    expect(dates).toHaveBeenCalledTimes(200)
    expect(body.textContent).toContain('Preserved note 1')
    expect(body.textContent).toContain('Preserved note 200')
  })

  it('keeps five thousand records out of a closed disclosure and reveals every exact observation on demand', () => {
    const observed = sessions(5000)
    const view = render(
      <ActivityTracker
        snapshots={[]}
        sessions={observed}
        acquiredAt={null}
        lastPlayedAt={null}
        totalMinutes={300000}
        now={now}
        scopeKey="1"
        rangeKey={`rendering:${mode}`}
        updates={[]}
        mode={mode}
      />,
    )
    expect(view.container.querySelector('.activity-records')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Tracked sessions' }))
    expect(screen.getByRole('status').textContent).toContain('5000 observed sessions')
    expect(view.container.querySelector('.activity-records')).toBeNull()
    const disclosure = screen.getByText('Recorded hours')
    fireEvent.click(disclosure)
    const records = [...view.container.querySelectorAll<HTMLButtonElement>('.activity-records button')]
    expect(records).toHaveLength(5000)
    expect(records[0]!.textContent).toContain(
      new Date(observed.at(-1)!.startedAt).toLocaleDateString(undefined, {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
        timeZone: 'UTC',
      }),
    )
    expect(records.at(-1)!.textContent).toContain(
      new Date(observed[0]!.startedAt).toLocaleDateString(undefined, {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
        timeZone: 'UTC',
      }),
    )
    fireEvent.click(records.at(-1)!)
    expect(screen.getByRole('status').textContent).toBe(records.at(-1)!.textContent)
    fireEvent.click(disclosure)
    expect(view.container.querySelector('.activity-records')).toBeNull()
    expect(screen.getByRole('button', { name: 'Tracked sessions' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(disclosure)
    expect(view.container.querySelectorAll('.activity-records button')).toHaveLength(5000)
  })
})

it('retains session row DOM for unchanged data while new observations and editing remain reachable', () => {
  const observed = sessions(2),
    edit = vi.fn(),
    dates = vi.spyOn(format, 'activityDateLabel')
  const view = render(<SessionRows sessions={observed} onEdit={edit} />)
  const original = view.container.querySelector('article')
  expect(dates).toHaveBeenCalledTimes(2)
  view.rerender(<SessionRows sessions={observed} onEdit={edit} />)
  expect(dates).toHaveBeenCalledTimes(2)
  expect(view.container.querySelector('article')).toBe(original)
  view.rerender(<SessionRows sessions={[...observed, { ...sessions(3)[2]!, id: 3 }]} onEdit={edit} />)
  expect(dates).toHaveBeenCalledTimes(5)
  expect(view.container.querySelector('article')).toBe(original)
  fireEvent.click(screen.getAllByRole('button', { name: 'Journal' })[2]!)
  expect(edit).toHaveBeenCalledWith(3)
})

it('retains the local date and hour formatting used by existing session rows', () => {
  for (const value of ['2026-01-01T00:00:00.000Z', '2026-09-08T12:34:00.000Z', 'not-a-date', ''])
    expect(format.activityDateLabel(value)).toBe(
      new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }),
    )
  for (const minutes of [-1, 0, 1.4, 59.8, 60, 95, 125000, NaN, Infinity, -Infinity])
    expect(format.activityHours(minutes)).toBe(
      minutes < 60
        ? `${Math.round(minutes)} min`
        : `${(minutes / 60).toLocaleString(undefined, { maximumFractionDigits: 1 })} hr`,
    )
})
