// @vitest-environment jsdom
import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen } from '@testing-library/react'
import { RiftDetails } from '../src/renderer/themes/rift/Details'
import { JourneyContext, type RiftJourney } from '../src/renderer/themes/rift/journey'
import type { ThemeContext } from '../src/shared/theme'

let complete: () => void = () => {}
vi.mock('../src/renderer/components/Artwork', () => ({ Artwork: () => <div /> }))
vi.mock('../src/renderer/components/portal-effects', () => ({
  PortalSurface: ({ children, onExpanded }: { children: React.ReactNode; onExpanded(): void }) => {
    complete = onExpanded
    return <div>{children}</div>
  },
}))
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('Rift details journey', () => {
  it('keeps real host details inert until the portal finishes, then focuses Back after enabling the reading plane', () => {
    const journey: RiftJourney = {
      origin: { current: null },
      options: { roundness: 60, waviness: 42, activity: 40 },
      reducedMotion: false,
      coverSize: 'balanced',
      open: vi.fn(),
      finish: vi.fn(),
    }
    const renderScreen = vi.fn(() => (
      <>
        <button className="back-button">Back to library</button>
        <button>Refresh metadata</button>
        <div>All shared detail actions</div>
      </>
    ))
    render(
      <JourneyContext.Provider value={journey}>
        <RiftDetails {...({ selectedWorkId: 1, renderScreen } as unknown as ThemeContext)} />
      </JourneyContext.Provider>,
    )
    const back = screen.getByRole('button', { name: 'Back to library' })
    expect(back.closest('.rift-details-scroll')?.hasAttribute('inert')).toBe(true)
    const focusedWhileInert: boolean[] = []
    const nativeFocus = back.focus.bind(back)
    vi.spyOn(back, 'focus').mockImplementation((options) => {
      focusedWhileInert.push(!!back.closest('[inert]'))
      nativeFocus(options)
    })
    act(() => complete())
    expect(focusedWhileInert).toEqual([false])
    expect(document.activeElement).toBe(back)
    expect(journey.finish).toHaveBeenCalledOnce()
    expect(renderScreen).toHaveBeenCalledWith('details')
    expect(screen.getByRole('button', { name: 'Refresh metadata' })).toBeTruthy()
  })
})
