// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { JournalDraft } from '../src/renderer/features/Journal'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})
describe('journal concurrency', () => {
  it('keeps a saving journal locked across a remount until the original request completes', async () => {
    let finish!: (value: unknown) => void
    const request = vi.fn().mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
    const client = new QueryClient()
    const close = vi.fn()
    const first = render(
      <QueryClientProvider client={client}>
        <JournalDraft
          initial={{ sessionId: 72, note: 'Original', rating: 2, revision: 'r1' }}
          onClose={close}
        />
      </QueryClientProvider>,
    )
    fireEvent.change(screen.getByLabelText('Your note'), { target: { value: 'The note being saved' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    await waitFor(() => expect(request).toHaveBeenCalledTimes(1))
    first.unmount()
    render(
      <QueryClientProvider client={client}>
        <JournalDraft
          initial={{ sessionId: 72, note: 'New snapshot', rating: 3, revision: 'r2' }}
          onClose={close}
        />
      </QueryClientProvider>,
    )
    expect((screen.getByLabelText('Your note') as HTMLTextAreaElement).value).toBe('The note being saved')
    expect((screen.getByLabelText('Your note') as HTMLTextAreaElement).disabled).toBe(true)
    expect((screen.getByLabelText('Your rating') as HTMLSelectElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Saving…' }) as HTMLButtonElement).disabled).toBe(true)
    finish({ ok: true, status: 200, data: { revision: 'r3' } })
    await waitFor(() => expect(close).toHaveBeenCalledOnce())
    expect(request).toHaveBeenCalledTimes(1)
    expect(request.mock.calls[0]?.[0].body).toEqual({
      note: 'The note being saved',
      rating: 2,
      expectedRevision: 'r1',
    })
  })
  it('retains a draft and its original revision across navigation and mode remounts', async () => {
    const request = vi.fn().mockResolvedValue({ ok: true, status: 200, data: { revision: 'r3' } })
    Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
    const client = new QueryClient()
    const close = vi.fn()
    const first = render(
      <QueryClientProvider client={client}>
        <JournalDraft
          initial={{ sessionId: 71, note: 'Original', rating: 2, revision: 'r1' }}
          onClose={close}
        />
      </QueryClientProvider>,
    )
    fireEvent.change(screen.getByLabelText('Your note'), { target: { value: 'Draft before F11' } })
    first.unmount()
    render(
      <QueryClientProvider client={client}>
        <JournalDraft
          initial={{ sessionId: 71, note: 'Changed in another frontend', rating: 5, revision: 'r2' }}
          onClose={close}
        />
      </QueryClientProvider>,
    )
    expect((screen.getByLabelText('Your note') as HTMLTextAreaElement).value).toBe('Draft before F11')
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    await waitFor(() => expect(close).toHaveBeenCalled())
    expect(request).toHaveBeenCalledWith({
      route: 'journal.put',
      params: { sessionId: 71 },
      body: { note: 'Draft before F11', rating: 2, expectedRevision: 'r1' },
    })
  })
  it('preserves the draft after a conflict and only adopts the current revision by explicit choice', async () => {
    const request = vi
      .fn()
      .mockImplementation(async ({ route }: { route: string }) =>
        route === 'journal.get'
          ? {
              ok: true,
              status: 200,
              data: { sessionId: 10, note: 'Other window note', rating: 4, revision: 'r2' },
            }
          : { ok: false, status: 409, message: 'This note changed' },
      )
    Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    })
    const close = vi.fn()
    render(
      <QueryClientProvider client={client}>
        <JournalDraft
          initial={{ sessionId: 10, note: 'Original', rating: 3, revision: 'r1' }}
          onClose={close}
        />
      </QueryClientProvider>,
    )
    fireEvent.change(screen.getByLabelText('Your note'), { target: { value: 'My unsaved draft' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    await screen.findByText('Other window note')
    expect((screen.getByLabelText('Your note') as HTMLTextAreaElement).value).toBe('My unsaved draft')
    expect(request.mock.calls[0]?.[0].body.expectedRevision).toBe('r1')
    expect((screen.getByRole('button', { name: 'Save note' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Keep my draft for the next save' }))
    request.mockResolvedValue({ ok: true, status: 200, data: { revision: 'r3' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
    await waitFor(() => expect(close).toHaveBeenCalledTimes(1))
    expect(request.mock.calls[2]?.[0].body).toEqual({
      note: 'My unsaved draft',
      rating: 3,
      expectedRevision: 'r2',
    })
  })
})
