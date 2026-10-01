// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonCover } from '../src/renderer/themes/avalon'
import { compactAvalonRatings } from '../src/renderer/themes/avalon-preview'
import { DEFAULT_PROFILE, type ThemeContext } from '../src/shared/theme'
import type { GameDetails, LibraryGame } from '../src/renderer/api/types'
import type { ApiResult } from '../src/shared/bridge'

const ratings: GameDetails['ratings'] = [
  { source: 'steam', score: 93, ratingCount: 1500, label: 'Very Positive', observedAt: '', hasFigure: true },
  { source: 'igdb_critics', score: 88, ratingCount: 12, observedAt: '', hasFigure: true },
  { source: 'igdb_users', score: 81, ratingCount: 100, observedAt: '', hasFigure: true },
]
function game(workId = 1): LibraryGame {
  return {
    workId,
    title: `Game ${workId}`,
    summary: 'A world worth revisiting.',
    playtimeMinutes: 60,
    bucket: 'active',
    entries: [],
  }
}
const context = {
  mode: 'desktop',
  profile: DEFAULT_PROFILE,
  components: { Artwork: () => <div className="artwork" /> },
  openGame: vi.fn(),
} as unknown as ThemeContext

beforeEach(() => {
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      request: vi.fn().mockResolvedValue({ ok: true, status: 200, data: { ratings } }),
      cancelRequest: vi.fn().mockResolvedValue(true),
    },
  })
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

it('opens an immediate noninteractive preview with compact attributed ratings', async () => {
  render(<AvalonCover context={context} game={{ ...game(), title: 'Hades' }} />)
  expect(window.winnow.request).not.toHaveBeenCalled()
  fireEvent.mouseEnter(screen.getByRole('button', { name: 'View Hades' }))
  const preview = screen.getByRole('tooltip')
  expect(within(preview).getByText('Hades')).toBeDefined()
  await waitFor(() =>
    expect(preview.textContent).toContain('IGDB: 81 · IGDB critics: 88 · Steam: Very Positive'),
  )
  expect(window.winnow.request).toHaveBeenCalledTimes(1)
  expect(preview.textContent).not.toContain('93%')
  expect(preview.textContent).not.toContain('1,500')
  expect(preview.textContent).not.toContain('12')
  expect(preview.textContent).not.toContain('100')
  const accessible = within(preview).getByRole('group', {
    name: 'IGDB user rating: 81 out of 100, from 100 ratings. Critic score aggregated by IGDB: 88 out of 100, from 12 critic scores. Very Positive on Steam: 93% positive, from 1,500 reviews.',
  })
  expect(accessible.textContent).toBe('IGDB: 81 · IGDB critics: 88 · Steam: Very Positive')
  expect(preview.querySelector('[title]')).toBeNull()
  expect(within(preview).queryAllByRole('button')).toHaveLength(0)
  expect(screen.getByRole('button', { name: 'View Hades' }).getAttribute('aria-describedby')).toBe(preview.id)
  fireEvent.mouseLeave(screen.getByRole('button', { name: 'View Hades' }))
  expect(screen.queryByRole('tooltip')).toBeNull()
  expect(screen.queryByRole('group', { name: /IGDB user rating/ })).toBeNull()
})

it.each(['exit', 'recycle', 'detach'])('ignores late metadata after preview %s', async (action) => {
  let complete!: (value: ApiResult<unknown>) => void
  vi.mocked(window.winnow.request).mockReturnValue(
    new Promise((resolve) => {
      complete = resolve
    }),
  )
  const mounted = render(<AvalonCover context={context} game={{ ...game(), title: 'Hades' }} />)
  const button = screen.getByRole('button', { name: 'View Hades' })
  fireEvent.mouseEnter(button)
  expect(screen.getByRole('tooltip')).toBeDefined()
  if (action === 'exit') fireEvent.mouseLeave(button)
  else if (action === 'recycle')
    mounted.rerender(<AvalonCover context={context} game={{ ...game(), title: 'Replacement' }} />)
  else mounted.unmount()
  expect(screen.queryByRole('tooltip')).toBeNull()
  expect(window.winnow.cancelRequest).toHaveBeenCalledWith(
    vi.mocked(window.winnow.request).mock.calls[0][0].requestId,
  )
  if (action === 'recycle') expect(screen.getByRole('button', { name: 'View Replacement' })).toBe(button)
  await act(async () => complete({ ok: true, status: 200, data: { ratings } }))
  expect(screen.queryByRole('tooltip')).toBeNull()
  expect(button.hasAttribute('aria-describedby')).toBe(false)
})

it('opening a feed preview closes the library preview without a pointer exit', async () => {
  render(
    <>
      <AvalonCover context={context} game={game()} />
      <AvalonCover context={context} game={game(2)} reason="A new place to start." />
    </>,
  )
  fireEvent.mouseEnter(screen.getByRole('button', { name: 'View Game 1' }))
  await waitFor(() => expect(screen.getByRole('tooltip').textContent).toContain('Steam: Very Positive'))
  fireEvent.mouseEnter(screen.getByRole('button', { name: 'View Game 2' }))
  expect(screen.getAllByRole('tooltip')).toHaveLength(1)
  expect(screen.getByRole('tooltip').textContent).toContain('Game 2')
  expect(screen.getByRole('tooltip').textContent).not.toContain('A new place to start.')
  expect(screen.getByText('A new place to start.')).toBeDefined()
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(screen.queryByRole('tooltip')).toBeNull()
})

it('omits absent reception figures and never blends separate rating populations', () => {
  expect(compactAvalonRatings([])).toBe('')
  expect(
    compactAvalonRatings([
      ...ratings.filter((rating) => rating.source !== 'igdb_users'),
      { source: 'igdb_users', score: 81.5, ratingCount: 200, hasFigure: true, observedAt: '' },
    ]),
  ).toBe('IGDB: 82 · IGDB critics: 88 · Steam: Very Positive')
  expect(compactAvalonRatings([{ ...ratings[0], hasFigure: false }])).toBe('')
  expect(compactAvalonRatings([{ ...ratings[0], label: '' }])).toBe('Steam: Unclassified')
})

it('missing ratings leave the pending Hades preview content unchanged and reserve no rating row', async () => {
  let complete!: (value: ApiResult<unknown>) => void
  vi.mocked(window.winnow.request).mockReturnValue(
    new Promise((resolve) => {
      complete = resolve
    }),
  )
  render(<AvalonCover context={context} game={{ ...game(), title: 'Hades' }} />)
  expect(window.winnow.request).not.toHaveBeenCalled()
  fireEvent.mouseEnter(screen.getByRole('button', { name: 'View Hades' }))
  const preview = screen.getByRole('tooltip')
  const content = preview.querySelector('.avalon-preview-copy')!.innerHTML
  expect(preview.querySelector('.avalon-preview-ratings')).toBeNull()
  await act(async () => complete({ ok: true, status: 200, data: { ratings: [] } }))
  expect(preview.querySelector('.avalon-preview-ratings')).toBeNull()
  expect(preview.querySelector('.avalon-preview-copy')!.innerHTML).toBe(content)
})

it.each([
  [0, undefined, 'Steam · never opened'],
  [48, undefined, 'Steam · 48m'],
  [125, undefined, 'Steam · 2h'],
  [125, '2026-11-23T12:00:00Z', 'Steam · 2h · idle 8d'],
] as const)(
  'preview metadata retains source statistics for %s minutes',
  async (minutes, lastPlayedAt, expected) => {
    vi.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-12-01T12:00:00Z'))
    const hades = {
      ...game(),
      title: 'Hades',
      playtimeMinutes: minutes,
      lastPlayedAt,
      entries: [
        {
          workId: 1,
          ownershipId: 1,
          releaseId: 1,
          title: 'Hades',
          store: 'steam',
          installed: true,
          playtimeMinutes: minutes,
        },
      ],
    }
    render(<AvalonCover context={context} game={hades} />)
    fireEvent.mouseEnter(screen.getByRole('button', { name: 'View Hades' }))
    expect(within(screen.getByRole('tooltip')).getByText(expected)).toBeDefined()
    await act(async () => {})
  },
)

it('fullscreen covers keep selection in the hero without a hover metadata reader or bubble', () => {
  const openGame = vi.fn()
  render(
    <AvalonCover
      context={{ ...context, mode: 'fullscreen', openGame }}
      game={{ ...game(), title: 'Hades' }}
    />,
  )
  const cover = screen.getByRole('button', { name: 'View Hades' })
  fireEvent.mouseEnter(cover)
  fireEvent.mouseMove(cover)
  fireEvent.focus(cover)
  expect(screen.queryByRole('tooltip')).toBeNull()
  expect(window.winnow.request).not.toHaveBeenCalled()
  fireEvent.click(cover)
  expect(openGame).toHaveBeenCalledExactlyOnceWith(1)
})
