// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { ActivityTimeline, ActivityTracker, TimelinePlot } from '../src/renderer/features/activity-timeline'
import * as timeline from '../src/renderer/features/activity-timeline-model'
import type { GameDetails, LibraryGame, Mode, Session } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'

const day = 86_400_000
const now = Date.parse('2026-09-08T00:00:00Z')
const date = (month: number, dayOfMonth = 1) => Date.UTC(2026, month - 1, dayOfMonth)
const iso = (at: number) => new Date(at).toISOString()
const session = (start: number, seconds: number, id: number): Session => ({
  id,
  ownershipId: 1,
  startedAt: iso(start),
  endedAt: iso(start + seconds * 1000),
  durationSeconds: seconds,
  detectionMethod: 'process',
})
const snapshots = [360, 600, 780].map((playtimeMinutes, index) => ({
  ownershipId: 1,
  playtimeMinutes,
  observedAt: iso(Date.UTC(2022, index + 3, 1) - 1000),
}))
const sessions = [session(now - 20 * day, 3600, 1), session(now - 15 * day, 7200, 2)]
const rangeKeys = new Set<string>()
function tracker(mode: Mode, overrides: Partial<ComponentProps<typeof ActivityTracker>> = {}) {
  const rangeKey = `${mode}:source-tracker`
  rangeKeys.add(rangeKey)
  return {
    mode,
    now,
    snapshots,
    sessions,
    acquiredAt: '2020-09-08T00:00:00.000Z',
    lastPlayedAt: iso(now - 15 * day),
    totalMinutes: 960,
    updates: [],
    scopeKey: '1',
    rangeKey,
    ...overrides,
  } satisfies ComponentProps<typeof ActivityTracker>
}
const series = (bars: timeline.TimelineBar[] = []): timeline.TimelineSeries => ({
  start: date(1),
  end: now,
  bars,
  coverage: [],
  lastPlayed: null,
  summary: 'Recorded activity',
  coverageNote: 'No monthly record',
  periodLabel: 'Hours per month',
})
function plotWidth(width: number) {
  const observers: ResizeObserverCallback[] = []
  vi.stubGlobal(
    'ResizeObserver',
    class {
      constructor(callback: ResizeObserverCallback) {
        observers.push(callback)
      }
      observe() {}
      disconnect() {}
    },
  )
  return () =>
    act(() => {
      for (const callback of observers)
        callback([{ contentRect: { width } } as ResizeObserverEntry], {} as ResizeObserver)
    })
}
afterEach(() => {
  cleanup()
  for (const key of rangeKeys) clearViewState(key)
  for (const mode of ['desktop', 'fullscreen']) {
    clearViewState(`${mode}:timeline:1:ownership`)
    clearViewState(`${mode}:timeline:1:tracked`)
  }
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe.each(['desktop', 'fullscreen'] as const)('%s exact activity tracker contracts', (mode) => {
  it('refreshes the source single announcement after acknowledgement without changing the empty tracked range', () => {
    const update: timeline.TimelineUpdate = {
      id: 1,
      releaseId: 1,
      kind: 'announcement',
      occurredAt: iso(now - 2 * day),
      title: 'Exploration update',
      unread: true,
    }
    const props = tracker(mode, {
      snapshots: [],
      sessions: [],
      acquiredAt: '2024-09-08T00:00:00.000Z',
      lastPlayedAt: iso(now - 20 * day),
      totalMinutes: 600,
      updates: [update],
    })
    const view = render(<ActivityTracker {...props} />)
    expect(screen.getByText('1 unread update')).toBeTruthy()
    expect(screen.getByRole('button', { name: /Exploration update/ }).getAttribute('data-unread')).toBe(
      'true',
    )
    fireEvent.click(screen.getByRole('button', { name: 'Tracked sessions' }))
    expect(screen.getByRole('status').textContent).toBe('No completed sessions recorded yet')
    update.unread = false
    const refreshed = [...props.updates]
    expect(refreshed).not.toBe(props.updates)
    view.rerender(<ActivityTracker {...props} updates={refreshed} />)
    expect(screen.getByRole('button', { name: 'Tracked sessions' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByText('No unread updates')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Lifetime' }))
    expect(screen.getByRole('button', { name: /Exploration update/ }).getAttribute('data-unread')).toBe(
      'false',
    )
  })

  it('preserves the exact STEAM copy sparse fixture without inventing a plot or records', () => {
    const view = render(
      <ActivityTracker
        {...tracker(mode, {
          snapshots: [],
          sessions: [],
          acquiredAt: null,
          lastPlayedAt: null,
          totalMinutes: 600,
          scopeLabel: 'STEAM copy',
        })}
      />,
    )
    const total = screen.getByText('10h')
    expect(total.parentElement?.textContent).toBe('10h played · STEAM copy')
    expect(screen.getByText('Last-played date unavailable')).toBeTruthy()
    expect(view.container.querySelector('.activity-timeline-plot')).toBeNull()
    expect(screen.queryByText('Recorded hours')).toBeNull()
  })

  it.each([620, 360])('keeps the exact 2022 monthly and 2026 session records reachable at %ipx', (width) => {
    const resize = plotWidth(width)
    const view = render(
      <div style={{ width }}>
        <ActivityTracker {...tracker(mode)} />
      </div>,
    )
    resize()
    expect(screen.getByText(/Hours per month/)).toBeTruthy()
    const bars = () => [...view.container.querySelectorAll<HTMLButtonElement>('.activity-timeline-bar')]
    expect(bars()).toHaveLength(3)
    expect(bars().map((bar) => bar.getAttribute('aria-label'))).toEqual([
      'Apr 2022 · 4h · monthly history',
      'May 2022 · 3h · monthly history',
      'Aug 2026 · 3h · Winnow sessions',
    ])
    const tracked = screen.getByRole('button', { name: 'Tracked sessions' })
    fireEvent.click(tracked)
    expect(tracked.getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByText(/Hours per session/)).toBeTruthy()
    expect(bars()).toHaveLength(2)
    const labels = bars().map((bar) => bar.getAttribute('aria-label'))
    expect(labels[0]).toContain('Aug 19, 2026')
    expect(labels[0]).toContain('1h · Winnow session')
    expect(labels[1]).toContain('Aug 24, 2026')
    expect(labels[1]).toContain('2h · Winnow session')
    fireEvent.click(tracked)
    expect(tracked.getAttribute('aria-pressed')).toBe('true')
    const lifetime = screen.getByRole('button', { name: 'Lifetime' })
    fireEvent.click(lifetime)
    fireEvent.click(lifetime)
    expect(lifetime.getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(screen.getByText('Recorded hours'))
    const disclosure = screen.getByText('Recorded hours').parentElement!
    expect(disclosure.hasAttribute('open')).toBe(true)
    expect(
      within(disclosure)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual([
      'Apr 2022 · 4h · monthly history',
      'May 2022 · 3h · monthly history',
      'Aug 2026 · 3h · Winnow sessions',
    ])
  })

  it('refreshes actual API patch acknowledgements without changing selected range or ownership', () => {
    const game: LibraryGame = {
      workId: 1,
      title: 'Game 1',
      bucket: 'bounced',
      playtimeMinutes: 600,
      entries: [
        {
          workId: 1,
          ownershipId: 1,
          releaseId: 1,
          title: 'Game 1',
          store: 'steam',
          installed: false,
          playtimeMinutes: 600,
          lastPlayedAt: iso(now - 20 * day),
        },
      ],
    }
    const details: GameDetails = {
      workId: 1,
      readAtUtc: iso(now),
      sessions: { 1: sessions },
      history: { 1: snapshots },
      ownerships: [{ id: 1, acquiredAt: '2024-09-08T00:00:00.000Z' }],
      events: [
        { id: 1, releaseId: 1, kind: 'build_push', occurredAt: iso(now - 2 * day) },
        {
          id: 2,
          releaseId: 1,
          kind: 'announcement',
          occurredAt: iso(now - 2 * day),
          title: 'Exploration update',
        },
      ],
      acknowledgements: {},
      journalEntries: [],
      ratings: [],
      achievements: [],
    }
    const view = render(<ActivityTimeline game={game} details={details} mode={mode} />)
    expect(screen.getByText('1 unread update')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Tracked sessions' }))
    view.rerender(
      <ActivityTimeline
        game={game}
        details={{ ...details, acknowledgements: { 1: iso(now) } }}
        mode={mode}
      />,
    )
    expect(screen.getByRole('button', { name: 'Tracked sessions' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByText('No unread updates')).toBeTruthy()
    expect(screen.getByText('10h').parentElement?.textContent).toBe('10h played · Steam copy')
    expect(screen.getByRole('button', { name: /Exploration update/ }).getAttribute('data-unread')).toBe(
      'false',
    )
  })

  it('keeps measured projection work out of focus, range and acknowledgement-only updates', () => {
    const project = vi.spyOn(timeline, 'buildTimeline')
    const props = tracker(mode)
    const view = render(<ActivityTracker {...props} />)
    expect(project).toHaveBeenCalledTimes(2)
    fireEvent.focus(
      within(view.container.querySelector<HTMLElement>('.activity-timeline-plot')!).getByRole('button', {
        name: 'Apr 2022 · 4h · monthly history',
      }),
    )
    expect(screen.getByRole('status').textContent).toBe('Apr 2022 · 4h · monthly history')
    fireEvent.click(screen.getByRole('button', { name: 'Tracked sessions' }))
    view.rerender(
      <ActivityTracker
        {...props}
        updates={[
          {
            id: 1,
            releaseId: 1,
            kind: 'announcement',
            occurredAt: iso(now - 2 * day),
            title: 'Patch',
            unread: false,
          },
        ]}
      />,
    )
    expect(project).toHaveBeenCalledTimes(2)
    view.rerender(<ActivityTracker {...props} sessions={[...sessions, session(now - day, 1800, 3)]} />)
    expect(project).toHaveBeenCalledTimes(4)
    expect(screen.getByRole('status').textContent).toContain('3 observed sessions')
  })
})

describe('exact standalone plot data contracts', () => {
  it.each([
    ['2026-01-01T00:00:00.000Z', 'Jan 1, 2026', 'Dec 31, 2025'],
    ['2024-09-08T00:00:00.000Z', 'Sep 8, 2024', 'Sep 7, 2024'],
  ])('keeps the UTC axis aligned with the model at midnight %s', (instant, expected, pacific) => {
    const start = Date.parse(instant)
    expect(
      new Intl.DateTimeFormat(undefined, {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
        timeZone: 'America/Los_Angeles',
      }).format(start),
    ).toBe(pacific)
    const projected = timeline.buildTimeline([], [], instant, null, now)
    expect(projected.summary).toContain(expected)
    const view = render(
      <TimelinePlot series={projected} updates={[]} tracked={false} onSelect={vi.fn()} onTracked={vi.fn()} />,
    )
    expect(view.container.querySelector('.activity-timeline-dates > span')?.textContent).toBe(expected)
  })

  it('uses the source January–September domain and literal labels for equal-width months and ten-pixel colliding sessions', () => {
    const lifetime = series([
      { start: date(2), end: date(3), hours: 2, tracked: false, label: 'February · 2h · monthly history' },
      { start: date(7), end: date(8), hours: 4, tracked: true, label: 'July · 4h · Winnow sessions' },
    ])
    const months = timeline.timelineMarks(lifetime, [], 700, false).bars
    expect(months).toHaveLength(2)
    expect(months[0]!.width).toBeCloseTo(months[1]!.width, 3)
    expect(months[0]!.width).toBeGreaterThan(10)
    const tracked = series([
      { start: date(6), end: date(6) + 2 * 3600000, hours: 2, tracked: true, label: 'First session' },
      {
        start: date(6) + 3 * 3600000,
        end: date(6) + 4 * 3600000,
        hours: 1,
        tracked: true,
        label: 'Second session',
      },
      { start: date(8), end: date(8) + 4 * 3600000, hours: 4, tracked: true, label: 'Third session' },
    ])
    const bars = timeline.timelineMarks(tracked, [], 700, true).bars
    expect(bars).toHaveLength(2)
    expect(bars.map((bar) => bar.width)).toEqual([10, 10])
    expect(bars[0]!.label).toContain('2 observed sessions')
    expect(bars[0]!.label).toContain('3h')
    expect(bars[1]!.label).toBe('Third session')
  })

  it('preserves all thirty identical Patch observations and redraws their acknowledgement without a count limit', () => {
    const resize = plotWidth(700)
    const updates = Array.from({ length: 30 }, (_, index) => ({
      id: index + 1,
      releaseId: 1,
      kind: 'announcement',
      title: 'Patch',
      occurredAt: iso(date(8) + index * 60000),
      unread: true,
    }))
    const select = vi.fn(),
      tracked = vi.fn()
    const props = { series: series(), updates, tracked: false, onSelect: select, onTracked: tracked }
    const view = render(<TimelinePlot {...props} />)
    resize()
    const mark = screen.getByRole('button', { name: /30 updates · 30 unread/ })
    expect(screen.getAllByRole('button')).toHaveLength(1)
    fireEvent.click(mark)
    expect(tracked).toHaveBeenCalledOnce()
    const selected = select.mock.calls.at(-1)![0] as string
    expect(selected).toContain('Aug 1, 2026')
    expect(selected.split('Patch')).toHaveLength(31)
    for (const update of updates) update.unread = false
    view.rerender(<TimelinePlot {...props} updates={[...updates]} />)
    expect(screen.getByRole('button', { name: /30 updates · 0 unread/ }).getAttribute('data-unread')).toBe(
      'false',
    )
  })

  it('preserves locale formatting and all distinct records at the exact large-history ownership volume', () => {
    const observed = [
      ...Array.from({ length: 60 }, (_, index) => session(now - (index + 1) * 10 * day, 3600, index + 1)),
      ...Array.from({ length: 5000 }, (_, index) => session(now - (index + 1) * day, 3600, index + 61)),
    ]
    const readings = Array.from({ length: 14600 }, (_, index) => ({
      ownershipId: 1,
      playtimeMinutes: (index + 1) * 10,
      observedAt: iso(now - (14600 - index - 1) * 6 * 3600000),
    }))
    const result = timeline.buildTimeline(readings, observed, null, null, now, true)
    // The 60 ten-day observations duplicate already recovered daily sittings.
    expect(result.bars).toHaveLength(5000)
    expect(result.bars.reduce((sum, bar) => sum + bar.hours, 0)).toBe(5000)
    expect(result.bars[0]!.start).toBe(now - 5000 * day)
    expect(result.bars.at(-1)!.start).toBe(now - day)
    for (const bar of [result.bars[0]!, result.bars[2499]!, result.bars.at(-1)!]) {
      const date = new Date(bar.start)
      expect(bar.label).toBe(
        `${date.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })} ${date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', timeZone: 'UTC' })} UTC · 1h · Winnow session`,
      )
    }
  })
})
