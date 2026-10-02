// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LinkNotifications } from '../src/renderer/features/LinkNotifications'
import { openExternal } from '../src/renderer/api/client'
afterEach(cleanup)
describe.each(['desktop', 'fullscreen'])('%s link outcome notices', (mode) => {
  it.each(['button', 'Escape'])(
    'keeps fallback dismissal inside the current dialog and restores the link after %s',
    async (method) => {
      Object.defineProperty(window, 'winnow', {
        configurable: true,
        value: { openExternal: vi.fn(async () => ({ opened: true, message: 'Opened in your browser.' })) },
      })
      const outside = vi.fn()
      render(
        <>
          <div
            role="dialog"
            aria-label="Details actions"
            aria-modal="true"
            className={`mode-${mode}`}
            onPointerDown={outside}
            onKeyDown={outside}
          >
            <button>Read patch notes</button>
          </div>
          <LinkNotifications />
        </>,
      )
      const origin = screen.getByRole('button', { name: 'Read patch notes' })
      origin.focus()
      await act(async () => {
        await openExternal('https://example.com/notes')
      })
      const notice = screen.getByRole('complementary', { name: 'Link status' })
      expect(screen.getByRole('dialog').contains(notice)).toBe(true)
      expect(document.activeElement).toBe(origin)
      const dismiss = screen.getByRole('button', { name: 'Dismiss link status' })
      dismiss.focus()
      fireEvent.pointerDown(dismiss)
      document.addEventListener('keydown', outside, true)
      try {
        if (method === 'Escape') fireEvent.keyDown(dismiss, { key: 'Escape' })
        else fireEvent.click(dismiss)
      } finally {
        document.removeEventListener('keydown', outside, true)
      }
      expect(screen.queryByRole('status')).toBeNull()
      expect(document.activeElement).toBe(origin)
      expect(outside).not.toHaveBeenCalled()
    },
  )
  it('opens another notice after the previous dialog and notice have closed', async () => {
    Object.defineProperty(window, 'winnow', {
      configurable: true,
      value: { openExternal: vi.fn(async () => ({ opened: true, message: 'Opened in your browser.' })) },
    })
    const showPopover = vi.fn(function (this: HTMLElement) {
      if (!this.isConnected) throw new DOMException('Detached popover', 'InvalidStateError')
      this.style.display = 'flex'
    })
    Object.defineProperty(HTMLElement.prototype, 'showPopover', { configurable: true, value: showPopover })
    try {
      const content = (id: number) => (
        <>
          <section key={id} role="dialog">
            <button>Read {id}</button>
          </section>
          <LinkNotifications />
        </>
      )
      const view = render(content(1))
      for (const id of [1, 2]) {
        view.rerender(content(id))
        const origin = screen.getByRole('button', { name: `Read ${id}` })
        origin.focus()
        await act(async () => {
          await openExternal('https://example.com/notes')
        })
        expect(screen.getByRole('dialog').contains(screen.getByRole('status'))).toBe(true)
        fireEvent.click(screen.getByRole('button', { name: 'Dismiss link status' }))
        expect(document.activeElement).toBe(origin)
      }
      expect(showPopover).toHaveBeenCalled()
    } finally {
      delete (HTMLElement.prototype as unknown as { showPopover?: unknown }).showPopover
    }
  })
  it('keeps a notice reachable when its owning dialog disappears', async () => {
    Object.defineProperty(window, 'winnow', {
      configurable: true,
      value: { openExternal: vi.fn(async () => ({ opened: true, message: 'Opened in your browser.' })) },
    })
    const content = (show: boolean) => (
      <>
        {show && (
          <section role="dialog" aria-modal="true">
            <button>Read</button>
          </section>
        )}
        <LinkNotifications />
      </>
    )
    const view = render(content(true))
    screen.getByRole('button', { name: 'Read' }).focus()
    await act(async () => {
      await openExternal('https://example.com/notes')
    })
    expect(screen.getByRole('dialog').contains(screen.getByRole('status'))).toBe(true)
    view.rerender(content(false))
    await waitFor(() =>
      expect(screen.getByRole('complementary', { name: 'Link status' }).parentElement).toBe(document.body),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss link status' }))
    expect(screen.queryByRole('status')).toBeNull()
  })
  it('announces a successful browser fallback without stealing focus and permits dismissal', async () => {
    Object.defineProperty(window, 'winnow', {
      configurable: true,
      value: {
        openExternal: vi.fn(async () => ({
          opened: true,
          message: 'This page cannot open in Winnow. Opened in your browser.',
        })),
      },
    })
    render(
      <div className={`mode-${mode}`}>
        <button>Read patch notes</button>
        <LinkNotifications />
      </div>,
    )
    const action = screen.getByRole('button', { name: 'Read patch notes' })
    action.focus()
    await act(async () => {
      expect((await openExternal('http://example.com/article')).opened).toBe(true)
    })
    expect(screen.getByRole('status').textContent).toContain('Opened in your browser')
    expect(document.activeElement).toBe(action)
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss link status' }))
    expect(screen.queryByRole('status')).toBeNull()
  })
  it('reports failure instead of an opened fallback for rejected or missing native results', async () => {
    const native = vi
      .fn()
      .mockRejectedValueOnce(Error('protocol unavailable'))
      .mockResolvedValueOnce(undefined)
      .mockResolvedValueOnce({ opened: false, message: 'This link is unavailable.' })
    Object.defineProperty(window, 'winnow', { configurable: true, value: { openExternal: native } })
    render(<LinkNotifications />)
    for (const url of ['https://example.com/', 'https://example.com/', 'file:///private']) {
      await act(async () => {
        expect((await openExternal(url)).opened).toBe(false)
      })
      expect(screen.getByRole('alert').textContent).not.toContain('Opened')
    }
    expect(screen.getByRole('alert').textContent).toBe('This link is unavailable.')
  })
  it('does not add a notice for an ordinary successful open', async () => {
    Object.defineProperty(window, 'winnow', {
      configurable: true,
      value: { openExternal: vi.fn(async () => ({ opened: true })) },
    })
    render(<LinkNotifications />)
    await act(async () => {
      await openExternal('https://example.com/')
    })
    expect(screen.queryByRole('complementary')).toBeNull()
  })
  it('clears stale failure on retry and leaves inline failures to their caller', async () => {
    const native = vi
      .fn()
      .mockResolvedValueOnce({ opened: false, message: 'Could not open this link.' })
      .mockResolvedValueOnce({ opened: true })
      .mockResolvedValueOnce({ opened: false, message: 'Unavailable destination' })
    Object.defineProperty(window, 'winnow', { configurable: true, value: { openExternal: native } })
    render(<LinkNotifications />)
    await act(async () => {
      await openExternal('https://example.com/')
    })
    expect(screen.getByRole('alert')).toBeTruthy()
    await act(async () => {
      await openExternal('https://example.com/')
    })
    expect(screen.queryByRole('alert')).toBeNull()
    await act(async () => {
      await expect(openExternal('https://example.com/', { failure: 'inline' })).rejects.toThrow(
        'Unavailable destination',
      )
    })
    expect(screen.queryByRole('alert')).toBeNull()
  })
})
