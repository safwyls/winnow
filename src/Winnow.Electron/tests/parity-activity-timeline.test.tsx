// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import {
  buildTimeline,
  timelineMarks,
  timelineUpdates,
  type PlaytimeSnapshot,
  type TimelineSeries,
} from '../src/renderer/features/activity-timeline-model'
import { ActivityTimeline, TimelinePlot } from '../src/renderer/features/activity-timeline'
import type { GameDetails, LibraryGame, Session, UpdateEvent } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'

const now = Date.parse('2026-09-09T00:00:00Z')
const utc = (month: number, day = 1) => Date.UTC(2026, month - 1, day)
const snap = (month: number, playtimeMinutes: number, ownershipId = 1): PlaytimeSnapshot => ({
  ownershipId,
  playtimeMinutes,
  observedAt: new Date(Date.UTC(2026, month, 1) - 1000).toISOString(),
})
const session = (start: number, seconds: number, id = 1): Session => ({
  id,
  ownershipId: 1,
  startedAt: new Date(start).toISOString(),
  endedAt: new Date(start + seconds * 1000).toISOString(),
  durationSeconds: seconds,
  detectionMethod: 'process',
})
const build = (snapshots: PlaytimeSnapshot[], sessions: Session[] = [], tracked = false) =>
  buildTimeline(snapshots, sessions, null, null, now, tracked)
afterEach(() => {
  cleanup()
  for (const mode of ['desktop', 'fullscreen'])
    for (const field of ['ownership', 'tracked']) clearViewState(`${mode}:timeline:1:${field}`)
})

describe('activity timeline projection parity', () => {
  it('preserves known zero between consecutive month ends and leaves missing months unknown', () => {
    const result = build([snap(1, 60), snap(2, 60), snap(4, 180)])
    expect(result.bars).toHaveLength(1)
    expect(result.bars[0]).toMatchObject({ start: utc(2), hours: 0 })
    expect(result.coverage).toHaveLength(1)
  })
  it('prefers imported monthly history while keeping individual sessions available', () => {
    const snapshots = [snap(1, 0), snap(2, 600)],
      sessions = [session(utc(2, 3), 3600)]
    expect(build(snapshots, sessions).bars).toMatchObject([{ hours: 10, tracked: false }])
    expect(build(snapshots, sessions, true).bars).toMatchObject([{ hours: 1, tracked: true }])
    expect(build(snapshots, sessions, true).coverage).toEqual([])
  })
  it('splits sessions over UTC month boundaries without losing duration', () => {
    const result = build([], [session(utc(1, 31) + 23 * 3600000, 7200)])
    expect(result.bars).toMatchObject([
      { start: utc(1), hours: 1 },
      { start: utc(2), hours: 1 },
    ])
    expect(result.coverage).toEqual([])
  })
  it('treats counter resets and conflicting endpoints as unknown rather than zero', () => {
    expect(build([snap(1, 600), snap(2, 60)]).bars).toEqual([])
    expect(build([snap(1, 0), snap(2, 60), snap(2, 120)]).bars).toEqual([])
    expect(
      build([
        snap(1, 120),
        snap(2, 240),
        { ownershipId: 1, observedAt: new Date(utc(2, 10)).toISOString(), playtimeMinutes: 0 },
      ]).bars,
    ).toEqual([])
  })
  it('never combines independent ownership counters', () => {
    expect(build([snap(1, 0), snap(2, 60, 2)]).bars).toEqual([])
  })
  it('omits future, incomplete, negative and impossible sessions', () => {
    expect(
      build(
        [snap(10, 0), snap(11, 60)],
        [
          session(now + 86400000, 3600),
          { ...session(now - 86400000, 3600), endedAt: null },
          { ...session(now - 86400000, 3600), durationSeconds: -1 },
          { ...session(now - 86400000, 3600), durationSeconds: 7200 },
        ],
      ).bars,
    ).toEqual([])
  })
  it('does not invent a release date when temporal evidence is absent', () => {
    expect(build([])).toMatchObject({ start: now, end: now, bars: [] })
  })
  it('keeps play earlier than acquisition and ignores a future last-played date', () => {
    expect(
      buildTimeline(
        [],
        [session(utc(1), 3600)],
        new Date(utc(6)).toISOString(),
        new Date(now + 86400000).toISOString(),
        now,
      ),
    ).toMatchObject({ start: utc(1), lastPlayed: null })
  })
  it('retains older sessions while giving recent sessions thirty days of space', () => {
    expect(build([], [session(now - 86400000, 3600)], true).start).toBe(now - 30 * 86400000)
    expect(build([], [session(utc(1), 3600)], true).start).toBe(utc(1))
  })
  it('describes tracked sessions instead of acquisition and keeps an honest empty state', () => {
    expect(build([], [], true).summary).toBe('No completed sessions recorded yet')
    const result = build([], [session(utc(1), 3600), session(utc(2), 3600, 2)], true)
    expect(result.summary).toMatch(/^2 observed sessions · /)
    expect(result.summary).not.toContain('Acquired')
  })
  it('dates the baseline counter without inventing when play happened', () => {
    expect(build([snap(1, 600), snap(3, 660)]).summary).toContain('10h recorded by')
    for (const snapshots of [[snap(1, 600), snap(1, 660)], [snap(1, -1)], [snap(1, 600), snap(2, 660, 2)]])
      expect(build(snapshots).summary).not.toContain('recorded by')
  })
  it('deduplicates recovered session records before adding their duration', () => {
    const recovered = session(utc(2), 3600)
    expect(build([], [recovered, { ...recovered, id: 999 }], true).bars).toHaveLength(1)
    expect(build([], [recovered, { ...recovered, id: 999 }]).bars[0]!.hours).toBe(1)
  })
  it('derives unread marks from correlated pushes and their own release watermarks', () => {
    const events: UpdateEvent[] = [
      { id: 1, releaseId: 1, kind: 'build_push', occurredAt: new Date(utc(8, 1)).toISOString() },
      {
        id: 2,
        releaseId: 1,
        kind: 'announcement',
        occurredAt: new Date(utc(8, 2)).toISOString(),
        title: 'Patch',
      },
      {
        id: 3,
        releaseId: 2,
        kind: 'announcement',
        occurredAt: new Date(utc(8, 2)).toISOString(),
        title: 'Unrelated news',
      },
    ]
    expect(
      timelineUpdates(events, {}, new Date(utc(1)).toISOString(), 60).map((item) => item.unread),
    ).toEqual([true, true, false])
    expect(
      timelineUpdates(
        events,
        { 1: new Date(utc(8, 1)).toISOString() },
        new Date(utc(1)).toISOString(),
        60,
      ).map((item) => item.unread),
    ).toEqual([false, false, false])
    expect(timelineUpdates(events, {}, null, 0).every((item) => !item.unread)).toBe(true)
  })
})

describe('activity timeline interaction parity', () => {
  const series: TimelineSeries = {
    start: utc(1),
    end: now,
    bars: [],
    coverage: [],
    lastPlayed: null,
    summary: 'Recorded activity',
    coverageNote: 'No monthly record',
    periodLabel: 'Hours per month',
  }
  const game: LibraryGame = {
    workId: 1,
    title: 'Dragonwilds',
    bucket: 'Started',
    playtimeMinutes: 300,
    entries: [
      {
        workId: 1,
        releaseId: 1,
        ownershipId: 1,
        store: 'steam',
        title: 'Dragonwilds',
        installed: true,
        playtimeMinutes: 300,
        lastPlayedAt: new Date(utc(7)).toISOString(),
      },
    ],
  }
  const details = (sessions: Session[] = []): GameDetails => ({
    workId: 1,
    readAtUtc: new Date(now).toISOString(),
    sessions: { 1: sessions },
    history: { 1: [snap(1, 0), snap(2, 600)] },
    ownerships: [{ id: 1, acquiredAt: '2020-01-01T00:00:00Z' }],
    events: [],
    journalEntries: [],
    ratings: [],
    achievements: [],
  })
  it('gives lifetime months equal width and accessible selectable labels', () => {
    const data = {
      ...series,
      bars: [
        { start: utc(2), end: utc(3), hours: 2, tracked: false, label: 'February · 2h · monthly history' },
        { start: utc(7), end: utc(8), hours: 4, tracked: true, label: 'July · 4h · Winnow sessions' },
      ],
    }
    const marks = timelineMarks(data, [], 700, false)
    expect(marks.bars[0]!.width).toBe(marks.bars[1]!.width)
    expect(marks.bars[0]!.width).toBeGreaterThan(10)
    const select = vi.fn()
    render(<TimelinePlot series={data} updates={[]} tracked={false} onSelect={select} onTracked={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'February · 2h · monthly history' }))
    expect(select).toHaveBeenLastCalledWith('February · 2h · monthly history')
    fireEvent.focus(screen.getByRole('button', { name: 'July · 4h · Winnow sessions' }))
    expect(select).toHaveBeenLastCalledWith('July · 4h · Winnow sessions')
    expect(screen.getByRole('button', { name: 'July · 4h · Winnow sessions' }).title).toBe(
      'July · 4h · Winnow sessions',
    )
  })
  it('uses ten-pixel session bars and retains every colliding observation', () => {
    const data = build(
      [],
      [session(utc(6), 7200), session(utc(6) + 3 * 3600000, 3600, 2), session(utc(8), 14400, 3)],
      true,
    )
    const marks = timelineMarks(data, [], 700, true)
    expect(marks.bars).toHaveLength(2)
    expect(marks.bars.every((mark) => mark.width === 10)).toBe(true)
    expect(marks.bars[0]!.label).toContain('2 observed sessions · 3h total')
    expect(marks.bars[0]!.label).toContain('longest 2h')
  })
  it('clusters unlimited update marks and requests tracked history on activation', () => {
    const updates = Array.from({ length: 30 }, (_, i) => ({
      id: i + 1,
      releaseId: 1,
      kind: 'announcement',
      title: `Patch ${i + 1}`,
      occurredAt: new Date(utc(8) + i * 60000).toISOString(),
      unread: true,
    }))
    const marks = timelineMarks(series, updates, 700, false)
    expect(marks.updates).toHaveLength(1)
    expect(marks.updates[0]!.label).toContain('30 updates · 30 unread')
    const select = vi.fn(),
      tracked = vi.fn()
    const view = render(
      <TimelinePlot
        series={series}
        updates={updates}
        tracked={false}
        onSelect={select}
        onTracked={tracked}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /30 updates · 30 unread/ }))
    expect(tracked).toHaveBeenCalledOnce()
    expect(select.mock.calls.at(-1)![0]).toContain('Patch 30')
    view.rerender(
      <TimelinePlot
        series={series}
        updates={updates.map((value) => ({ ...value, unread: false }))}
        tracked={false}
        onSelect={select}
        onTracked={tracked}
      />,
    )
    expect(screen.getByRole('button', { name: /30 updates · 0 unread/ }).getAttribute('data-unread')).toBe(
      'false',
    )
  })
  it('keeps narrow plot marks within its measured width', () => {
    const updates = [
      { id: 1, releaseId: 1, kind: 'announcement', occurredAt: new Date(utc(1)).toISOString(), unread: true },
      { id: 2, releaseId: 1, kind: 'announcement', occurredAt: new Date(now).toISOString(), unread: true },
    ]
    const marks = timelineMarks(series, updates, 180, false)
    expect(marks.updates.every((mark) => mark.left >= 0 && mark.left + 24 <= 180)).toBe(true)
  })
  it.each(['desktop', 'fullscreen'] as const)(
    'keeps monthly and individual session records reachable in %s',
    (mode) => {
      render(
        <ActivityTimeline
          game={game}
          details={details([session(utc(8), 3600), session(utc(8, 2), 7200, 2)])}
          mode={mode}
        />,
      )
      expect(screen.getByText(/Hours per month/)).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Tracked sessions' }))
      expect(screen.getByText(/Hours per session/)).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Tracked sessions' }))
      expect(screen.getByRole('button', { name: 'Tracked sessions' }).getAttribute('aria-pressed')).toBe(
        'true',
      )
      fireEvent.click(screen.getByRole('button', { name: 'Lifetime' }))
      fireEvent.click(screen.getByText('Recorded hours'))
      expect(screen.getByText('Recorded hours').parentElement!.hasAttribute('open')).toBe(true)
      expect(screen.getAllByRole('button', { name: /monthly history/ })).toHaveLength(2)
    },
  )
  it('keeps grouped updates reachable when there are no sessions', () => {
    const data = details()
    data.history = {}
    data.events = Array.from({ length: 3 }, (_, i) => ({
      id: i + 1,
      releaseId: 1,
      kind: 'announcement',
      occurredAt: new Date(now - (i + 1) * 86400000).toISOString(),
      title: `Update ${i + 1}`,
    }))
    render(<ActivityTimeline game={game} details={data} />)
    fireEvent.click(screen.getByRole('button', { name: /3 updates/ }))
    expect(screen.getByRole('button', { name: 'Lifetime' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('status').textContent).toContain('Update 1')
    expect(screen.getByRole('status').textContent).toContain('Update 3')
  })
  it('shows honest sparse history without inventing monthly or session records', () => {
    render(
      <ActivityTimeline
        game={{ ...game, entries: [{ ...game.entries[0]!, lastPlayedAt: null }] }}
        details={{ ...details(), history: {}, ownerships: [] }}
      />,
    )
    expect(screen.getByText('Last-played date unavailable')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Tracked sessions' }))
    expect(screen.getByRole('status').textContent).toBe('No completed sessions recorded yet')
    expect(screen.queryByText('Recorded hours')).toBeNull()
  })
  it('keeps ownership counters separate when switching editions', () => {
    const twoGames = {
      ...game,
      entries: [...game.entries, { ...game.entries[0]!, ownershipId: 2, store: 'gog', releaseId: 2 }],
    }
    const data = {
      ...details(),
      history: { 1: [snap(1, 0), snap(2, 600)], 2: [snap(1, 600, 2), snap(2, 660, 2)] },
    }
    render(<ActivityTimeline game={twoGames} details={data} />)
    fireEvent.click(screen.getByText('Recorded hours'))
    expect(screen.getAllByRole('button', { name: /10h · monthly history/ })).toHaveLength(2)
    fireEvent.change(screen.getByLabelText('Edition history'), { target: { value: '2' } })
    expect(screen.getAllByRole('button', { name: /1h · monthly history/ })).toHaveLength(2)
    expect(screen.queryByRole('button', { name: /10h · monthly history/ })).toBeNull()
  })
})
