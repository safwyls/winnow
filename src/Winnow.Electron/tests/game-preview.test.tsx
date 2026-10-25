// @vitest-environment jsdom
import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { GamePreview, previewPlacement } from '../src/renderer/components/GamePreview'
import type { LibraryGame } from '../src/renderer/api/types'

const game: LibraryGame = {
  workId: 1,
  title: 'A very long game title',
  summary: 'The actual library description.',
  bucket: 'dormant',
  playtimeMinutes: 120,
  entries: [
    {
      ownershipId: 1,
      releaseId: 1,
      workId: 1,
      title: 'A very long game title',
      store: 'Steam',
      installed: true,
      playtimeMinutes: 120,
    },
  ],
}
const box = (x = 100, y = 120, width = 180, height = 270) => new DOMRect(x, y, width, height)
const mount = (props = {}) =>
  render(
    <GamePreview game={game} {...props}>
      <button>View game</button>
    </GamePreview>,
  )

beforeEach(() => {
  vi.useFakeTimers()
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue(box())
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('reusable game preview', () => {
  it('delays hover, exposes library data, and allows crossing into the panel', () => {
    mount()
    fireEvent.pointerEnter(screen.getByRole('button'))
    act(() => vi.advanceTimersByTime(179))
    expect(screen.queryByRole('tooltip')).toBeNull()
    act(() => vi.advanceTimersByTime(1))
    const popup = screen.getByRole('tooltip')
    expect(popup.textContent).toContain('The actual library description.')
    expect(popup.textContent).toContain('Steam2h playedInstalled')
    fireEvent.pointerLeave(screen.getByRole('button'))
    act(() => vi.advanceTimersByTime(100))
    fireEvent.pointerEnter(popup)
    act(() => vi.advanceTimersByTime(300))
    expect(screen.getByRole('tooltip')).toBe(popup)
    fireEvent.pointerLeave(popup)
    act(() => vi.advanceTimersByTime(180))
    expect(screen.queryByRole('tooltip')).toBeNull()
  })
  it('supports immediate keyboard focus, associated description and Escape before shell navigation', () => {
    mount()
    const navigate = vi.fn()
    window.addEventListener('keydown', navigate)
    const button = screen.getByRole('button')
    fireEvent.focus(button)
    expect(button.getAttribute('aria-describedby')).toBe(screen.getByRole('tooltip').id)
    fireEvent.keyDown(button, { key: 'Escape' })
    expect(navigate).not.toHaveBeenCalled()
    expect(screen.queryByRole('tooltip')).toBeNull()
    expect(button.hasAttribute('aria-describedby')).toBe(false)
    window.removeEventListener('keydown', navigate)
  })
  it('clears pointer focus suppression when the keyboard returns', () => {
    mount()
    const button = screen.getByRole('button')
    fireEvent.focus(button)
    fireEvent.pointerDown(button)
    fireEvent.blur(button)
    fireEvent.focus(button)
    expect(screen.getByRole('tooltip')).toBeTruthy()
  })
  it('uses the content edge with a full-height navigation rail', () => {
    const view = render(
      <>
        <header className="app-header" />
        <main id="main-content">
          <GamePreview game={game}>
            <button>View game</button>
          </GamePreview>
        </main>
      </>,
    )
    vi.spyOn(view.container.querySelector('header')!, 'getBoundingClientRect').mockReturnValue(
      box(0, 0, 200, 768),
    )
    vi.spyOn(view.container.querySelector('main')!, 'getBoundingClientRect').mockReturnValue(
      box(200, 0, 800, 700),
    )
    fireEvent.focus(screen.getByRole('button'))
    expect(screen.getByRole('tooltip')).toBeTruthy()
  })
  it('shows only one preview when keyboard focus moves between themed cards', () => {
    render(
      <>
        <GamePreview game={game}>
          <button>First</button>
        </GamePreview>
        <GamePreview game={{ ...game, workId: 2, title: 'Another game' }}>
          <button>Second</button>
        </GamePreview>
      </>,
    )
    fireEvent.focus(screen.getByRole('button', { name: 'First' }))
    fireEvent.focus(screen.getByRole('button', { name: 'Second' }))
    expect(screen.getAllByRole('tooltip')).toHaveLength(1)
    expect(screen.getByRole('tooltip').textContent).toContain('Another game')
  })
  it('closes pointer previews on scrolling, and cancels pending hover on unmount', () => {
    const view = mount()
    fireEvent.pointerEnter(screen.getByRole('button'))
    act(() => vi.advanceTimersByTime(180))
    fireEvent.scroll(document)
    expect(screen.queryByRole('tooltip')).toBeNull()
    fireEvent.pointerEnter(screen.getByRole('button'))
    view.unmount()
    act(() => vi.advanceTimersByTime(500))
    expect(screen.queryByRole('tooltip')).toBeNull()
  })
  it('provides honest missing-description text and respects disabled previews', () => {
    const view = mount({ game: { ...game, summary: ' ' } })
    fireEvent.focus(screen.getByRole('button'))
    expect(screen.getByRole('tooltip').textContent).toContain('No description available yet.')
    view.rerender(
      <GamePreview game={game} disabled>
        <button>View game</button>
      </GamePreview>,
    )
    expect(screen.queryByRole('tooltip')).toBeNull()
  })
  it('flips at the right edge and docks in narrow viewports', () => {
    expect(previewPlacement(box(700), 1000, 80, 700).side).toBe('left')
    const narrow = previewPlacement(box(40, 100, 200), 390, 80, 650)
    expect(narrow.side).toBe('docked')
    expect(narrow.width).toBeLessThanOrEqual(362)
    expect(narrow.maxHeight).toBe(313.5)
  })
})
