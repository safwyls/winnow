// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { useState } from 'react'
import { ConfirmationDialog } from '../src/renderer/features/ConfirmationDialog'

afterEach(cleanup)
function Harness({ pending = false, confirm = () => {}, error }: { pending?: boolean; confirm?: () => void; error?: unknown }) {
  const [open, setOpen] = useState(false)
  return <ConfirmationDialog open={open} onOpenChange={setOpen} trigger={<button>Delete list…</button>}
    title="Delete Weekend?" description="Its games will stay in your library." confirmLabel="Delete list"
    cancelLabel="Keep list" pending={pending} error={error} onConfirm={confirm} />
}
it('starts a destructive confirmation on cancel and returns focus to its unchanged trigger', async () => {
  const confirm = vi.fn()
  render(<Harness confirm={confirm} />)
  const trigger = screen.getByRole('button', { name: 'Delete list…' })
  trigger.focus()
  fireEvent.click(trigger)
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Keep list' })))
  fireEvent.click(document.activeElement!)
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  await waitFor(() => expect(document.activeElement).toBe(trigger))
  expect(confirm).not.toHaveBeenCalled()
})
it('traps forward and reverse Tab, dismisses with Escape, and never invokes the destructive action', async () => {
  const confirm = vi.fn()
  render(<Harness confirm={confirm} />)
  const trigger = screen.getByRole('button', { name: 'Delete list…' })
  trigger.focus(); fireEvent.click(trigger)
  const dialog = await screen.findByRole('dialog', { name: 'Delete Weekend?' })
  const safe = within(dialog).getByRole('button', { name: 'Keep list' }), destructive = within(dialog).getByRole('button', { name: 'Delete list' })
  await waitFor(() => expect(document.activeElement).toBe(safe))
  fireEvent.keyDown(safe, { key: 'Tab', code: 'Tab' })
  expect(document.activeElement).toBe(destructive)
  fireEvent.keyDown(destructive, { key: 'Tab', code: 'Tab', shiftKey: true })
  expect(document.activeElement).toBe(safe)
  fireEvent.keyDown(safe, { key: 'Escape' })
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  expect(confirm).not.toHaveBeenCalled()
})
it('keeps pending and failed confirmations attached with actions disabled while saving', async () => {
  const confirm = vi.fn()
  const view = render(<Harness confirm={confirm} />)
  fireEvent.click(screen.getByRole('button', { name: 'Delete list…' }))
  await screen.findByRole('dialog')
  fireEvent.click(screen.getByRole('button', { name: 'Delete list' }))
  expect(confirm).toHaveBeenCalledTimes(1)
  view.rerender(<Harness pending confirm={confirm} />)
  expect((screen.getByRole('button', { name: 'Delete list' }) as HTMLButtonElement).disabled).toBe(true)
  expect((screen.getByRole('button', { name: 'Keep list' }) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
  expect(screen.getByRole('dialog')).toBeTruthy()
  view.rerender(<Harness confirm={confirm} error={new Error('Saved list changed.')} />)
  expect(within(screen.getByRole('dialog')).getByRole('alert').textContent).toContain('Saved list changed.')
  expect(confirm).toHaveBeenCalledTimes(1)
})
