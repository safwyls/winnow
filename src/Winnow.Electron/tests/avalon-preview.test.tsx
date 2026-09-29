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
    value: { request: vi.fn().mockResolvedValue({ ok: true, status: 200, data: { ratings } }) },
  })
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

it('opens an immediate noninteractive preview with compact attributed ratings', async () => {
  render(<AvalonCover context={context} game={game()} />)
  expect(window.winnow.request).not.toHaveBeenCalled()
  fireEvent.mouseEnter(screen.getByRole('button', { name: 'View Game 1' }))
  const preview = screen.getByRole('tooltip')
  expect(within(preview).getByText('Game 1')).toBeDefined()
  await waitFor(() => expect(preview.textContent).toContain('IGDB critics: 88 · Steam: Very Positive'))
  expect(preview.textContent).not.toContain('93%')
  expect(preview.textContent).not.toContain('1,500')
  expect(preview.querySelector('[title]')).toBeNull()
  expect(within(preview).queryAllByRole('button')).toHaveLength(0)
  expect(screen.getByRole('button', { name: 'View Game 1' }).getAttribute('aria-describedby')).toBe(
    preview.id,
  )
})

it.each(['exit', 'recycle', 'detach'])('ignores late metadata after preview %s', async (action) => {
  let complete!: (value: ApiResult<unknown>) => void
  vi.mocked(window.winnow.request).mockReturnValue(
    new Promise((resolve) => {
      complete = resolve
    }),
  )
  const mounted = render(<AvalonCover context={context} game={game()} />)
  const button = screen.getByRole('button', { name: 'View Game 1' })
  fireEvent.mouseEnter(button)
  expect(screen.getByRole('tooltip')).toBeDefined()
  if (action === 'exit') fireEvent.mouseLeave(button)
  else if (action === 'recycle') mounted.rerender(<AvalonCover context={context} game={game(2)} />)
  else mounted.unmount()
  expect(screen.queryByRole('tooltip')).toBeNull()
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
  expect(screen.getByRole('tooltip').textContent).toContain('A new place to start.')
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(screen.queryByRole('tooltip')).toBeNull()
})

it('omits absent reception figures and never blends separate rating populations', () => {
  expect(compactAvalonRatings([])).toBe('')
  expect(
    compactAvalonRatings([
      ...ratings,
      { source: 'igdb_users', score: 81.5, ratingCount: 200, hasFigure: true, observedAt: '' },
    ]),
  ).toBe('IGDB: 82 · IGDB critics: 88 · Steam: Very Positive')
  expect(compactAvalonRatings([{ ...ratings[0], hasFigure: false }])).toBe('')
  expect(compactAvalonRatings([{ ...ratings[0], label: '' }])).toBe('Steam: Unclassified')
})
