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
function mount(keyboard = true, fullscreen = false) {
  const close = vi.fn(),
    open = vi.fn()
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
      <CoverPreview target={{ game, card, keyboard }} fullscreen={fullscreen} close={close} />
    </JourneyContext.Provider>,
  )
  return { close, open }
}
describe('Rift cover preview interaction', () => {
  it.each([false, true])('keeps keyboard focus on the cover in fullscreen=%s', (fullscreen) => {
    const { close } = mount(true, fullscreen)
    card.focus()
    const preview = screen.getByRole('tooltip')
    expect(card.getAttribute('aria-describedby')).toBe(preview.id)
    expect(preview.querySelector('[data-still]')?.getAttribute('data-still')).toBe('true')
    expect(fireEvent.keyDown(card, { key: 'Tab' })).toBe(true)
    expect(document.activeElement).toBe(card)
    const navigate = vi.fn()
    window.addEventListener('keydown', navigate)
    fireEvent.keyDown(card, { key: 'Escape' })
    expect(close).toHaveBeenCalledWith(true)
    expect(navigate).not.toHaveBeenCalled()
    window.removeEventListener('keydown', navigate)
  })
  it.each([false, true])(
    'shows information without an interactive surface in fullscreen=%s',
    (fullscreen) => {
      const { open, close } = mount(false, fullscreen)
    const preview = screen.getByRole('tooltip')
      expect(preview.textContent).toContain(game.summary)
      expect(preview.textContent).toContain('3h played')
      expect(preview.querySelector('button, a, [tabindex]')).toBeNull()
      fireEvent.pointerEnter(preview)
      fireEvent.pointerLeave(preview)
      fireEvent.click(preview)
      expect(open).not.toHaveBeenCalled()
      expect(close).not.toHaveBeenCalled()
      fireEvent.focusIn(document.body)
      expect(close).toHaveBeenCalled()
      cleanup()
      expect(card.hasAttribute('aria-describedby')).toBe(false)
    },
  )
})
