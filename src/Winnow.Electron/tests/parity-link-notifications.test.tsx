// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { LinkNotifications } from '../src/renderer/features/LinkNotifications'
import { openExternal } from '../src/renderer/api/client'
afterEach(cleanup)
describe.each(['desktop', 'fullscreen'])('%s link outcome notices', (mode) => {
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
