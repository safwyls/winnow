// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Journal } from '../src/renderer/features/Journal'
import { journalPeriod } from '../src/renderer/api/journalPeriod'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.unstubAllEnvs()
  for (const mode of ['desktop', 'fullscreen'])
    for (const field of ['days', 'bounds', 'section', 'editing']) clearViewState(`${mode}:journal:${field}`)
})

describe('Journal whole-second periods', () => {
  it('removes fractional seconds without moving the end forward and preserves local-day semantics over DST', () => {
    vi.stubEnv('TZ', 'America/Los_Angeles')
    const now = new Date('2026-11-03T20:30:15.987Z')
    expect(journalPeriod(7, now)).toEqual({
      fromUtc: '2026-10-27T19:30:15.000Z',
      untilUtc: '2026-11-03T20:30:15.000Z',
    })
    expect(now.toISOString()).toBe('2026-11-03T20:30:15.987Z')
    expect(() => journalPeriod(0, now)).toThrow(RangeError)
    expect(() => journalPeriod(-7, now)).toThrow(RangeError)
  })

  it.each(['desktop', 'fullscreen'] as const)(
    'sends matching, positive whole-second activity and statistics ranges in %s',
    async (mode) => {
      vi.useFakeTimers({ toFake: ['Date'] })
      vi.setSystemTime(new Date('2026-09-26T20:30:15.987Z'))
      const request = vi.fn(async ({ route }: ApiRequest) => ({
        ok: true,
        status: 200,
        data:
          route === 'library.get'
            ? { games: [], lists: [] }
            : route === 'activity.query'
              ? { rows: [], next: null }
              : {
                  recordedSeconds: 0,
                  gamesPlayedCount: 0,
                  startedSessionCount: 0,
                  medianSessionSeconds: null,
                },
      }))
      Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      render(
        <QueryClientProvider client={client}>
          <Journal mode={mode} />
        </QueryClientProvider>,
      )
      await waitFor(() =>
        expect(request.mock.calls.some(([input]) => input.route === 'statistics.gameplay')).toBe(true),
      )
      fireEvent.change(screen.getByLabelText('Time period'), { target: { value: '7' } })
      await waitFor(() =>
        expect(request.mock.calls.filter(([input]) => input.route === 'statistics.gameplay')).toHaveLength(2),
      )
      const statistics = request.mock.calls
        .filter(([input]) => input.route === 'statistics.gameplay')
        .map(
          ([input]) =>
            input.body as {
              fromUtc: string
              untilUtc: string
              asOfUtc: string
              timeBins: { fromUtc: string; untilUtc: string }[]
            },
        )
      for (const period of statistics) {
        expect(Date.parse(period.fromUtc) % 1000).toBe(0)
        expect(Date.parse(period.untilUtc) % 1000).toBe(0)
        expect(Date.parse(period.untilUtc)).toBeGreaterThan(Date.parse(period.fromUtc))
        expect(period.untilUtc).toBe('2026-09-26T20:30:15.000Z')
        expect(period.asOfUtc).toBe(period.untilUtc)
        expect(period.timeBins).toEqual([{ fromUtc: period.fromUtc, untilUtc: period.untilUtc }])
        expect(
          request.mock.calls.some(
            ([input]) =>
              input.route === 'activity.query' &&
              (input.body as { fromUtc: string }).fromUtc === period.fromUtc &&
              (input.body as { untilUtc: string }).untilUtc === period.untilUtc,
          ),
        ).toBe(true)
      }
    },
  )
})
