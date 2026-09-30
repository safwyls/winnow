// @vitest-environment jsdom
import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { StartupPresentation } from '../src/renderer/startup/StartupPresentation'

vi.mock('../src/renderer/startup/LoadingDragon', () => ({ LoadingDragon: () => <span /> }))
afterEach(cleanup)
function fixture(mode: 'desktop' | 'fullscreen', failed = false) {
  const retry = vi.fn(),
    exit = vi.fn()
  render(
    <StartupPresentation
      mode={mode}
      exit={exit}
      preparation={{
        mode,
        phase: failed ? 'failed' : 'loading',
        opacity: 1,
        tracing: !failed,
        visible: true,
        generation: mode,
        retry,
        trace: vi.fn(),
        fail: vi.fn(),
      }}
    />,
  )
  return { retry, exit }
}
describe.each(['desktop', 'fullscreen'] as const)('%s startup input', (mode) => {
  it('blocks library shortcuts while preserving native fullscreen and close keys', () => {
    fixture(mode)
    const receive = vi.fn()
    document.addEventListener('keydown', receive)
    try {
      expect(fireEvent.keyDown(document.activeElement!, { key: 'k', ctrlKey: true })).toBe(false)
      expect(receive).not.toHaveBeenCalled()
      for (const options of [{ key: 'F11' }, { key: 'F4', altKey: true }])
        expect(fireEvent.keyDown(document.activeElement!, options)).toBe(true)
      expect(receive).toHaveBeenCalledTimes(2)
    } finally {
      document.removeEventListener('keydown', receive)
    }
  })
})
it('fullscreen failure traps directional and tab focus between retry and back', () => {
  const { retry, exit } = fixture('fullscreen', true),
    again = screen.getByRole('button', { name: 'Try again' }),
    back = screen.getByRole('button', { name: 'Back to desktop' })
  expect(document.activeElement).toBe(again)
  fireEvent.keyDown(again, { key: 'ArrowDown' })
  expect(document.activeElement).toBe(back)
  fireEvent.keyDown(back, { key: 'Tab' })
  expect(document.activeElement).toBe(again)
  fireEvent.click(again)
  expect(retry).toHaveBeenCalledOnce()
  fireEvent.keyDown(again, { key: 'Escape' })
  expect(exit).toHaveBeenCalledOnce()
})
