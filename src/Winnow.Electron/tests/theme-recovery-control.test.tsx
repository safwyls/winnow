// @vitest-environment jsdom
import { StrictMode, type ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { ThemeRecoveryButton, ThemeRecoveryProvider } from '../src/renderer/theming/ThemeRecovery'
import { ThemeBoundary } from '../src/renderer/theming/runtime'

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('theme recovery control placement', () => {
  it('keeps a working host fallback for a theme without a footer slot', () => {
    const restore = vi.fn()
    render(
      <ThemeRecoveryProvider disabled={false} restore={restore}>
        <main>Your library</main>
      </ThemeRecoveryProvider>,
    )
    const button = screen.getByRole('button', { name: 'Restore default theme' })
    expect(button.className).toBe('recovery-shortcut')
    fireEvent.click(button)
    expect(restore).toHaveBeenCalledOnce()
  })

  it('uses only the footer control when a shell reserves space, including StrictMode remounts', () => {
    const restore = vi.fn()
    render(
      <StrictMode>
        <ThemeRecoveryProvider disabled={false} restore={restore}>
          <footer>
            <ThemeRecoveryButton />
            <span>Your library. Rediscovered.</span>
          </footer>
        </ThemeRecoveryProvider>
      </StrictMode>,
    )
    const button = screen.getByRole('button', { name: 'Restore default theme' })
    expect(button.closest('footer')).not.toBeNull()
    expect(document.querySelector('.recovery-shortcut')).toBeNull()
    fireEvent.click(button)
    expect(restore).toHaveBeenCalledOnce()
  })

  it('restores the fallback if a previously mounted theme crashes', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    function Shell({ crash = false }: { crash?: boolean }): ReactNode {
      if (crash) throw new Error('Broken theme')
      return (
        <footer>
          <ThemeRecoveryButton />
        </footer>
      )
    }
    function Host({ crash = false }: { crash?: boolean }) {
      return (
        <ThemeRecoveryProvider disabled={false} restore={() => {}}>
          <ThemeBoundary resetKey="theme" fallback={<p>Theme unavailable</p>}>
            <Shell crash={crash} />
          </ThemeBoundary>
        </ThemeRecoveryProvider>
      )
    }
    const view = render(<Host />)
    expect(document.querySelector('.recovery-shortcut')).toBeNull()
    view.rerender(<Host crash />)
    expect(screen.getByRole('button', { name: 'Restore default theme' }).className).toBe('recovery-shortcut')
  })

  it('keeps hosted recovery disabled during startup preparation', () => {
    const restore = vi.fn()
    render(
      <ThemeRecoveryProvider disabled restore={restore}>
        <ThemeRecoveryButton />
      </ThemeRecoveryProvider>,
    )
    const button = screen.getByRole('button', { name: 'Restore default theme' }) as HTMLButtonElement
    expect(button.disabled).toBe(true)
    fireEvent.click(button)
    expect(restore).not.toHaveBeenCalled()
  })
})
