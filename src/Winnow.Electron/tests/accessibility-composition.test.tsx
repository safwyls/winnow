// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { FullscreenHints } from '../src/renderer/components/FullscreenHints'
import { AvalonCollectionLists } from '../src/renderer/themes/avalon-collection-lists'
import { AvalonFilterPanel } from '../src/renderer/themes/avalon-filter-panel'
import type { LibraryGame, GameList } from '../src/renderer/api/types'
import { clearViewState } from '../src/renderer/viewState'
import { openFilterGroup } from './library-controls'

vi.mock('../src/renderer/api/hooks', () => ({ useWorkspace: () => ({ data: undefined }) }))
afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  clearViewState('avalon:collections:manual-expanded')
  clearViewState('avalon:collections:live-expanded')
})
const game = (id: number, store = 'Steam'): LibraryGame => ({
  workId: id,
  title: `Game ${id}`,
  bucket: 'never_played',
  playtimeMinutes: 0,
  entries: [
    {
      workId: id,
      releaseId: id,
      ownershipId: id,
      title: `Game ${id}`,
      store,
      installed: false,
      playtimeMinutes: 0,
    },
  ],
})

describe('live accessibility composition', () => {
  it('keeps the displayed list host and announces Co-op two to Weekend two to Weekend one without an arrival announcement', () => {
    const games = [game(1), game(2)]
    const list: GameList = { id: 1, name: 'Co-op', isLive: false, releaseIds: [1, 2], revision: 'r1' }
    const props = { games, selected: null, select: vi.fn() }
    const view = render(<AvalonCollectionLists {...props} lists={[list]} />)
    const button = screen.getByRole('button', { name: 'Co-op, 2 games' })
    const status = button.nextElementSibling!
    expect(status.getAttribute('role')).toBe('status')
    expect(status.getAttribute('aria-live')).toBe('polite')
    expect(status.textContent).toBe('')
    view.rerender(<AvalonCollectionLists {...props} lists={[{ ...list, name: 'Weekend' }]} />)
    expect(screen.getByRole('button', { name: 'Weekend, 2 games' })).toBe(button)
    expect(status.textContent).toBe('Weekend, 2 games')
    view.rerender(
      <AvalonCollectionLists {...props} lists={[{ ...list, name: 'Weekend', releaseIds: [1] }]} />,
    )
    expect(screen.getByRole('button', { name: 'Weekend, 1 game' })).toBe(button)
    expect(status.textContent).toBe('Weekend, 1 game')
  })
  it.each([false, true])(
    'updates and announces the same Steam choice from one matching title to two (fullscreen=%s)',
    (fullscreen) => {
      const allGames = [game(1), game(2), game(3, 'GOG')]
      const props = { filter: {}, allGames, facts: new Map(), fullscreen, apply: vi.fn(), close: vi.fn() }
      const view = render(<AvalonFilterPanel {...props} games={[allGames[0], allGames[2]]} />)
      if (fullscreen) openFilterGroup('PLATFORM')
      else fireEvent.click(screen.getByText('Stores').closest('summary')!)
      const role = fullscreen ? 'button' : 'checkbox'
      const choice = screen.getByRole(role, { name: 'Steam, 1 matching title' })
      const status = (fullscreen ? choice : choice.closest('label')!).nextElementSibling!
      expect(status.textContent).toBe('')
      view.rerender(<AvalonFilterPanel {...props} games={allGames} />)
      expect(screen.getByRole(role, { name: 'Steam, 2 matching titles' })).toBe(choice)
      expect(status.textContent).toBe('Steam, 2 matching titles')
      expect(status.getAttribute('aria-atomic')).toBe('true')
    },
  )
})

describe('fullscreen guidance connection policy', () => {
  it('switches from keyboard to the connected standard controller, retains it during keyboard input, and returns after disconnect', () => {
    vi.useFakeTimers()
    let pads: (Gamepad | null)[] = []
    vi.stubGlobal('navigator', { getGamepads: () => pads })
    const view = render(<FullscreenHints page="discover" />)
    expect(screen.getByRole('group', { name: 'Keyboard guidance' }).textContent).toContain('Enter to select')
    pads = [{ connected: true, mapping: 'standard', index: 0, buttons: [], axes: [] } as unknown as Gamepad]
    act(() => window.dispatchEvent(new Event('gamepadconnected')))
    const guide = screen.getByRole('group', { name: 'Controller guidance' })
    expect(guide.textContent).toContain('Y · More')
    expect(document.querySelectorAll('[data-input-glyph]')).toHaveLength(6)
    fireEvent.keyDown(window, { key: 'ArrowDown' })
    expect(screen.getByRole('group', { name: 'Controller guidance' })).toBe(guide)
    view.rerender(<FullscreenHints page="library" />)
    expect(guide.textContent).toContain('Y · Library options')
    expect(document.querySelector('[data-input-glyph="LT"]')).toBeNull()
    pads = []
    act(() => window.dispatchEvent(new Event('gamepaddisconnected')))
    expect(screen.getByRole('group', { name: 'Keyboard guidance' })).toBe(guide)
    expect(guide.textContent).toContain('Esc to go back')
    view.unmount()
    expect(vi.getTimerCount()).toBe(0)
  })
  it('ignores unmapped devices and detects a newly exposed standard controller through the existing polling fallback', () => {
    vi.useFakeTimers()
    let mapping = ''
    vi.stubGlobal('navigator', {
      getGamepads: () => [
        { connected: true, mapping, index: 0, buttons: [], axes: [] } as unknown as Gamepad,
      ],
    })
    render(<FullscreenHints page="settings" />)
    expect(screen.getByRole('group', { name: 'Keyboard guidance' })).toBeTruthy()
    mapping = 'standard'
    act(() => vi.advanceTimersByTime(1000))
    expect(screen.getByRole('group', { name: 'Controller guidance' })).toBeTruthy()
    expect(document.querySelector('[data-input-glyph="Y"]')).toBeNull()
  })
})
