// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import {
  acquisitionFacts,
  durationText,
  playtimeRecordLine,
  receptionFigures,
  refetchStatus,
  updatePageUrl,
} from '../src/renderer/features/details-facts'
import { MetadataRefresh, ReceptionLine } from '../src/renderer/features/details-presentation'
import { AcquisitionSummary, Screenshots, UpdateSignals } from '../src/renderer/features/parity-details'
import type { GameDetails } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'

const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  clearViewState('details:1:refetch')
})
const rating = (source: string, score: number | null, ratingCount: number | null, label?: string) => ({
  source,
  score,
  ratingCount,
  label,
  hasFigure: score != null && Boolean(ratingCount),
  observedAt: '2026-09-01T00:00:00Z',
})
const facts = (extra: Partial<GameDetails> = {}): GameDetails => ({
  workId: 1,
  readAtUtc: '2026-09-29T00:00:00Z',
  events: [],
  ratings: [],
  sessions: {},
  journalEntries: [],
  achievements: [],
  ...extra,
})
function mount(ui: React.ReactNode, result: unknown = { outcome: 1 }) {
  const request = vi.fn().mockResolvedValue({ ok: true, status: 200, data: result })
  const artwork = vi.fn().mockResolvedValue('data:image/png;base64,eA==')
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, artwork, cancelRequest: vi.fn().mockResolvedValue(undefined), openExternal: vi.fn() },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  const view = render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
  return { ...view, request, artwork, client }
}

describe('original details fact contracts', () => {
  it('omits the acquisition block when neither date nor recognized licence is held', () => {
    for (const rows of [
      null,
      [],
      [{ acquiredAt: null, licenseType: null }],
      [{ licenseType: 'unknown_private_token' }],
    ])
      expect(acquisitionFacts(rows)).toBeNull()
  })
  it('uses the earliest acquisition date and does not expose unknown licence values', () => {
    expect(
      acquisitionFacts([
        { acquiredAt: '2019-12-21T00:00:00Z', licenseType: 'steam_store' },
        { acquiredAt: '2016-11-04T00:00:00Z', licenseType: 'gift' },
      ])?.acquiredAt,
    ).toBe('2016-11-04T00:00:00Z')
    expect(
      acquisitionFacts([{ acquiredAt: '2019-12-21T00:00:00Z', licenseType: 'something_new' }])?.licenseText,
    ).toBe('')
  })
  it.each([
    ['steam_store', 'Steam Store'],
    ['complimentary', 'Complimentary'],
    ['gift', 'Gift or guest pass'],
    ['retail', 'Retail key'],
  ])('names %s licences with shared account vocabulary', (licenseType, label) => {
    expect(acquisitionFacts([{ licenseType }])?.licenseText).toBe(label)
  })
  it('does not carry price fields into acquisition output or its rendered block', () => {
    const ownerships = [
      {
        acquiredAt: '2016-11-04T00:00:00Z',
        licenseType: 'retail',
        pricePaidCents: 5999,
        priceSource: 'private-price-source',
      },
    ]
    expect(Object.keys(acquisitionFacts(ownerships)!)).toEqual(['acquiredAt', 'dateText', 'licenseText'])
    mount(<AcquisitionSummary ownerships={ownerships} />)
    expect(document.body.textContent).not.toMatch(/59|5999|price|paid|cost/i)
    expect(screen.getByText('Retail key')).toBeTruthy()
  })
  it('omits reception when rows are absent, have no figure, or have zero respondents', () => {
    for (const rows of [null, [], [rating('igdb_users', null, null)], [rating('igdb_users', 90, 0)]])
      expect(receptionFigures(rows)).toEqual([])
    mount(<ReceptionLine ratings={[]} />)
    expect(screen.queryByLabelText('Reception')).toBeNull()
  })
  it('orders three attributed populations without blending their figures', () => {
    const figures = receptionFigures([
      rating('steam', 91, 41203, 'Very Positive'),
      rating('igdb_critics', 85, 42),
      rating('igdb_users', 78, 1204),
    ])
    expect(figures.map((figure) => figure.source)).toEqual(['IGDB USERS', 'IGDB CRITICS', 'STEAM'])
    expect(figures.map((figure) => figure.value)).toEqual(['78', '85', '91%'])
    expect(figures.map((figure) => figure.count)).toEqual([
      '1,204 ratings',
      '42 critic scores',
      '41,203 reviews',
    ])
    expect(figures.every((figure) => figure.automationName.includes(figure.count))).toBe(true)
    expect(figures[2].tooltip).toContain('Very Positive')
    expect(figures[2].value).not.toContain('Very Positive')
    expect(figures[2].compactValue).toBe('Very Positive')
  })
  it('keeps full reception evidence accessible when the desktop line uses compact labels', () => {
    mount(<ReceptionLine compact ratings={[rating('steam', 91, 41203, 'Very Positive')]} />)
    expect(screen.getByText('Steam: Very Positive')).toBeTruthy()
    expect(screen.getByTitle('Very Positive on Steam: 91% positive, from 41,203 reviews.')).toBeTruthy()
  })
  it('keeps a missing playtime series silent and describes a single reading as one reading', () => {
    expect(playtimeRecordLine([])).toBe('')
    expect(playtimeRecordLine([{ playtimeMinutes: 600, observedAt: '2026-09-01T00:00:00Z' }])).toMatch(
      /^Checked once, on /,
    )
  })
  it('describes only the change actually observed in time order', () => {
    const rows = [
      { playtimeMinutes: 243, observedAt: '2026-09-03T00:00:00Z' },
      { playtimeMinutes: 176, observedAt: '2026-09-01T00:00:00Z' },
      { playtimeMinutes: 200, observedAt: '2026-09-02T00:00:00Z' },
    ]
    expect(playtimeRecordLine(rows)).toMatch(/^Checked 3 times since .* — up 1h 7m\.$/)
    expect(rows[0].playtimeMinutes).toBe(243)
  })
  it.each([600, 500])('does not invent gained playtime for a flat or downward series (%s)', (last) => {
    expect(
      playtimeRecordLine([
        { playtimeMinutes: 600, observedAt: '2026-09-01T00:00:00Z' },
        { playtimeMinutes: last, observedAt: '2026-09-02T00:00:00Z' },
      ]),
    ).toMatch(/— no change\.$/)
  })
  it.each([
    [45, '45m'],
    [180, '3h'],
    [200, '3h 20m'],
  ])('formats %s minutes as the original duration', (minutes, text) =>
    expect(durationText(minutes)).toBe(text),
  )
  it.each([
    'javascript:alert(1)',
    'file:///C:/private',
    'https://example.test/\n',
    'https://name:password@example.test/',
    '',
    null,
  ])('withholds unsafe or missing patch-note links (%s)', (url) => expect(updatePageUrl(url)).toBeNull())
  it.each(['http://example.test/patch', 'https://example.test/patch'])(
    'keeps supported patch-note pages (%s)',
    (url) => expect(updatePageUrl(url)).toBe(url),
  )
})

describe.each(['desktop', 'fullscreen'])('%s details fact rendering', (mode) => {
  it('retains an update with a hostile URL while offering no open button', () => {
    mount(
      <div className={`mode-${mode}`}>
        <UpdateSignals
          details={facts({
            events: [
              {
                id: 1,
                releaseId: 1,
                occurredAt: '2026-09-01T00:00:00Z',
                kind: 'announcement',
                title: 'Patch one',
                url: 'javascript:alert(1)',
              },
            ],
          })}
        />
      </div>,
    )
    expect(screen.getByText('Patch one')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Read' })).toBeNull()
    expect(screen.getByText('No patch notes page was recorded for this update.')).toBeTruthy()
  })
  it('shows no screenshot frames for missing data or artwork-only rows', () => {
    mount(
      <div className={`mode-${mode}`}>
        <Screenshots
          details={facts({ images: [{ source: 'igdb', kind: 'artwork', imageIds: 'art_one' }] })}
        />
      </div>,
    )
    expect(screen.queryByLabelText('Screenshots')).toBeNull()
    expect(screen.queryByRole('img')).toBeNull()
  })
  it('preserves publisher screenshot order, count, selection and source rendition', async () => {
    const { artwork } = mount(
      <div className={`mode-${mode}`}>
        <Screenshots
          details={facts({
            images: [{ source: 'igdb', kind: 'screenshot', imageIds: 'co6m51,ab12cd,ef34gh' }],
          })}
        />
      </div>,
    )
    expect(screen.getByText('3 screenshots from IGDB')).toBeTruthy()
    const buttons = screen.getAllByRole('button', { name: /Open screenshot/ })
    expect(buttons).toHaveLength(3)
    expect(buttons.map((button) => button.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'false'])
    fireEvent.click(buttons[1])
    await screen.findByRole('dialog', { name: 'Screenshot 2 of 3' })
    expect(buttons[1].getAttribute('aria-pressed')).toBe('true')
    await waitFor(() => expect(artwork).toHaveBeenCalledWith('igdb-shot', 'ab12cd', 1280))
  })
})

describe.each(['desktop', 'fullscreen'])('%s screenshot lightbox contracts', (mode) => {
  function gallery(ids = 'aa1,bb2,cc3') {
    return mount(
      <div className={`mode-${mode}`}>
        <Screenshots details={facts({ images: [{ source: 'igdb', kind: 'screenshot', imageIds: ids }] })} />
      </div>,
    )
  }
  it('can show two overview previews while the gallery retains all screenshots', async () => {
    mount(<Screenshots previewCount={2} details={facts({images:[{source:'igdb',kind:'screenshot',imageIds:'aa1,bb2,cc3'}]})} />)
    expect(screen.getAllByRole('button',{name:/Open screenshot/})).toHaveLength(2)
    expect(screen.getByText('3 screenshots from IGDB')).toBeTruthy()
    const origin = screen.getByRole('button',{name:'View gallery →'})
    fireEvent.click(origin)
    const dialog = await screen.findByRole('dialog',{name:'Screenshot 1 of 3'})
    fireEvent.keyDown(dialog,{key:'ArrowLeft'})
    expect(screen.getByRole('dialog',{name:'Screenshot 3 of 3'})).toBeTruthy()
    fireEvent.click(screen.getByRole('button',{name:'Close screenshots'}))
    await waitFor(() => expect(document.activeElement).toBe(origin))
  })
  it('opens on the pressed shot, wraps both ways and keeps the strip selection in sync', async () => {
    gallery()
    fireEvent.click(screen.getByRole('button', { name: 'Open screenshot 3 of 3' }))
    const dialog = await screen.findByRole('dialog', { name: 'Screenshot 3 of 3' })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Close screenshots' }))
    fireEvent.keyDown(dialog, { key: 'ArrowRight' })
    expect(screen.getByRole('dialog', { name: 'Screenshot 1 of 3' })).toBeTruthy()
    fireEvent.keyDown(dialog, { key: 'ArrowLeft' })
    expect(screen.getByRole('dialog', { name: 'Screenshot 3 of 3' })).toBeTruthy()
    expect(document.querySelectorAll('.screenshot-strip [aria-pressed="true"]')).toHaveLength(1)
    expect(screen.getByText('Screenshot 3 of 3').getAttribute('aria-live')).toBe('polite')
  })
  it('omits navigation for one screenshot and leaves arrow input on that image', async () => {
    gallery('aa1')
    fireEvent.click(screen.getByRole('button', { name: 'Open screenshot 1 of 1' }))
    const dialog = await screen.findByRole('dialog')
    expect(screen.queryByRole('button', { name: 'Previous screenshot' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Next screenshot' })).toBeNull()
    fireEvent.keyDown(dialog, { key: 'ArrowLeft' })
    fireEvent.keyDown(dialog, { key: 'ArrowRight' })
    expect(screen.getByRole('dialog', { name: 'Screenshot 1 of 1' })).toBeTruthy()
  })
  it('drops the open image while retaining its mark and returns focus to the original thumbnail', async () => {
    gallery()
    const origin = screen.getByRole('button', { name: 'Open screenshot 1 of 3' })
    origin.focus()
    fireEvent.click(origin)
    const dialog = await screen.findByRole('dialog')
    fireEvent.keyDown(dialog, { key: 'ArrowRight' })
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(document.querySelector('.screenshot-frame img')).toBeNull()
    expect(screen.getByRole('button', { name: 'Open screenshot 2 of 3' }).getAttribute('aria-pressed')).toBe(
      'true',
    )
    await waitFor(() => expect(document.activeElement).toBe(origin))
    fireEvent.keyDown(origin, { key: 'ArrowRight' })
    expect(screen.getByRole('button', { name: 'Open screenshot 2 of 3' }).getAttribute('aria-pressed')).toBe(
      'true',
    )
  })
  it('keeps wheel, Shift-wheel and horizontal input in the strip including both edges', () => {
    gallery()
    const strip = screen.getByLabelText('Screenshots')
    Object.defineProperties(strip, { scrollWidth: { value: 1000 }, clientWidth: { value: 300 } })
    const parentWheel = vi.fn()
    strip.parentElement!.addEventListener('wheel', parentWheel)
    for (const init of [{ deltaY: 120 }, { deltaY: 120, shiftKey: true }, { deltaX: 120 }]) {
      strip.scrollLeft = 0
      const event = new WheelEvent('wheel', { ...init, bubbles: true, cancelable: true })
      fireEvent(strip, event)
      expect(strip.scrollLeft).toBe(120)
      expect(event.defaultPrevented).toBe(true)
    }
    for (const [start, delta, expected] of [
      [700, 120, 700],
      [0, -120, 0],
      [700, -120, 580],
    ]) {
      strip.scrollLeft = start
      const event = new WheelEvent('wheel', { deltaY: delta, bubbles: true, cancelable: true })
      fireEvent(strip, event)
      expect(strip.scrollLeft).toBe(expected)
      expect(event.defaultPrevented).toBe(true)
    }
    expect(parentWheel).not.toHaveBeenCalled()
  })
})

describe('metadata refetch outcomes', () => {
  it.each([
    'Updated',
    'NothingNew',
    'NoSourceToAsk',
    'NotConfigured',
    'Unreachable',
    'TooSoon',
    'WorkNotFound',
  ])('names %s with no invented percentage and supports the wire enum', (outcome) => {
    const names = [
      'Updated',
      'NothingNew',
      'NoSourceToAsk',
      'NotConfigured',
      'Unreachable',
      'TooSoon',
      'WorkNotFound',
    ]
    const status = refetchStatus({ outcome, retryAfter: '00:03:00' })
    expect(status.message).not.toContain('%')
    expect(status.problem).toBe(!['Updated', 'NothingNew'].includes(outcome))
    expect(refetchStatus({ outcome: names.indexOf(outcome), retryAfter: '00:03:00' })).toEqual(status)
  })
  it.each([
    ['00:00:00.2', '1 second'],
    ['00:00:30.1', '31 seconds'],
    ['00:01:00', '1 minute'],
    ['00:01:00.1', '2 minutes'],
  ])('rounds the retry delay %s up in the appropriate unit', (retryAfter, text) => {
    expect(refetchStatus({ outcome: 5, retryAfter }).message).toBe(`Try again in ${text}.`)
  })
  it.each([0, 1, 2, 3, 4, 5, 6])(
    'stays quiet initially and refreshes snapshots only after a write (%s)',
    async (outcome) => {
      const { client, request } = mount(<MetadataRefresh workId={1} />, { outcome })
      const invalidation = vi.spyOn(client, 'invalidateQueries')
      expect(screen.queryByRole('status')).toBeNull()
      fireEvent.click(screen.getByRole('button', { name: 'Refetch metadata' }))
      await screen.findByText(refetchStatus({ outcome }).message)
      expect(request).toHaveBeenCalledTimes(1)
      expect(invalidation).toHaveBeenCalledTimes(outcome === 0 ? 1 : 0)
    },
  )
  it('keeps a carried success visible when refreshed details reconstruct the control', async () => {
    const view = mount(<MetadataRefresh workId={1} />, { outcome: 0 })
    fireEvent.click(screen.getByRole('button', { name: 'Refetch metadata' }))
    await screen.findByText('Metadata updated.')
    view.unmount()
    mount(<MetadataRefresh workId={1} />)
    expect(screen.getByRole('status').textContent).toBe('Metadata updated.')
  })
  it('cancels its observation and does not announce a late result after closing', async () => {
    const view = mount(<MetadataRefresh workId={1} />)
    let finish!: (result: unknown) => void
    view.request.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Refetch metadata' }))
    view.unmount()
    await act(async () => finish({ ok: true, status: 200, data: { outcome: 0 } }))
    mount(<MetadataRefresh workId={1} />)
    expect(screen.queryByRole('status')).toBeNull()
  })
})
