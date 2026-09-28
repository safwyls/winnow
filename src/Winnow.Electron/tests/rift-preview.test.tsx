// @vitest-environment jsdom
import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { CoverPreview } from '../src/renderer/themes/rift/Preview'
import { JourneyContext, type RiftJourney } from '../src/renderer/themes/rift/journey'
import type { LibraryGame } from '../src/renderer/api/types'

vi.mock('../src/renderer/components/Artwork', () => ({ Artwork: () => <div /> }))
vi.mock('../src/renderer/components/portal-effects', () => ({
  PortalSurface: ({ children, reducedMotion }: { children: React.ReactNode; reducedMotion: boolean }) => (
    <div data-still={reducedMotion}>{children}</div>
  ),
}))
const game: LibraryGame = {
  workId: 20,
  title: 'A title from the library',
  summary: 'The complete library synopsis.',
  bucket: 'dormant',
  playtimeMinutes: 180,
  entries: [],
}
let card: HTMLButtonElement
beforeEach(() => {
  card = document.createElement('button')
  card.textContent = 'Cover'
  document.body.append(card)
  vi.spyOn(card, 'getBoundingClientRect').mockReturnValue(new DOMRect(100, 140, 140, 210))
})
afterEach(() => {
  cleanup()
  card.remove()
  vi.restoreAllMocks()
})
function mount(keyboard = true) {
  const close = vi.fn(),
    open = vi.fn(),
    hold = vi.fn(),
    leave = vi.fn()
  const journey: RiftJourney = {
    origin: { current: null },
    options: { roundness: 60, waviness: 42, activity: 40 },
    reducedMotion: false,
    coverSize: 'balanced',
    open,
    finish: vi.fn(),
  }
  render(
    <JourneyContext.Provider value={journey}>
      <CoverPreview
        target={{ game, card, keyboard }}
        fullscreen={false}
        close={close}
        hold={hold}
        leave={leave}
      />
    </JourneyContext.Provider>,
  )
  return { close, open, hold, leave }
}
describe('Rift cover preview interaction', () => {
  it('lets keyboard users enter and leave the preview and consumes Escape before shell navigation', () => {
    const { close } = mount()
    card.focus()
    fireEvent.keyDown(card, { key: 'Tab' })
    const view = screen.getByRole('button', { name: 'View game' })
    expect(document.activeElement).toBe(view)
    expect(view.closest('[data-still]')?.getAttribute('data-still')).toBe('true')
    fireEvent.keyDown(view, { key: 'Tab', shiftKey: true })
    expect(document.activeElement).toBe(card)
    const navigate = vi.fn()
    window.addEventListener('keydown', navigate)
    fireEvent.keyDown(card, { key: 'Escape' })
    expect(close).toHaveBeenCalledWith(true)
    expect(navigate).not.toHaveBeenCalled()
    window.removeEventListener('keydown', navigate)
  })
  it('uses the actual game and expanded preview bounds for the full details journey', () => {
    const { open, hold, leave } = mount(false)
    const preview = screen.getByRole('region', { name: `${game.title} preview` })
    expect(preview.textContent).toContain(game.summary)
    expect(preview.textContent).toContain('3h played')
    fireEvent.pointerEnter(preview)
    expect(hold).toHaveBeenCalled()
    fireEvent.pointerLeave(preview)
    expect(leave).toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'View game' }))
    expect(open).toHaveBeenCalledWith(game.workId, preview)
  })
})
