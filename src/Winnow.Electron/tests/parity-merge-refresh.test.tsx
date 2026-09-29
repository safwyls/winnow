// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MergeRefresh } from '../src/renderer/features/parity-merge-refresh'
import { request as apiRequest } from '../src/renderer/api/client'
import type { ApiRequest } from '../src/shared/bridge'
import { mergeFixture } from './parity-merge-fixtures'

afterEach(cleanup)
const deferred = <T,>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
function setup(mode: string) {
  const response = deferred<unknown>(),
    old = mergeFixture(),
    fresh = { ...mergeFixture(), revision: 'r2' }
  const calls = vi.fn(async (input: ApiRequest) =>
    input.route === 'identity.refresh' ? response.promise : { ok: true, status: 200, data: fresh },
  )
  const cancel = vi.fn(async () => true),
    busy = vi.fn()
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request: calls, cancelRequest: cancel },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  client.setQueryData(['api', 'identity.get', undefined], old)
  const view = render(
    <QueryClientProvider client={client}>
      <div className={`mode-${mode}`}>
        <MergeRefresh disabled={false} onBusy={busy} />
      </div>
    </QueryClientProvider>,
  )
  return { response, old, fresh, calls, cancel, busy, client, view }
}
for (const mode of ['desktop', 'fullscreen'])
  describe(`${mode} suggestion refresh lifetime`, () => {
    it('announces busy state, disables duplicate requests and replaces the review without answering it', async () => {
      const { response, calls, busy, client, fresh } = setup(mode)
      const control = screen.getByRole('button', { name: 'Refresh suggestions' })
      expect(screen.queryByRole('status')).toBeNull()
      fireEvent.click(control)
      expect((control as HTMLButtonElement).disabled).toBe(true)
      expect(screen.getByRole('status').textContent).toBe('Checking your library for matches…')
      fireEvent.click(control)
      await waitFor(() => expect(calls).toHaveBeenCalledTimes(1))
      response.resolve({ ok: true, status: 200, data: { truncated: false } })
      await screen.findByText('Suggestions refreshed. Your previous answers are kept.')
      expect((control as HTMLButtonElement).disabled).toBe(false)
      expect(busy.mock.calls.map(([value]) => value)).toEqual([true, false])
      expect(client.getQueryData(['api', 'identity.get', undefined])).toEqual(fresh)
      expect(calls.mock.calls.map(([input]) => input.route)).toEqual(['identity.refresh', 'identity.get'])
    })
    it('preserves the current review on failure and offers the same control for a bounded retry', async () => {
      const { response, calls, old, client } = setup(mode)
      fireEvent.click(screen.getByRole('button', { name: 'Refresh suggestions' }))
      response.resolve({ ok: false, status: 503, message: 'offline' })
      await screen.findByText("Couldn't refresh suggestions. Choose Refresh suggestions to try again.")
      expect(client.getQueryData(['api', 'identity.get', undefined])).toEqual(old)
      calls.mockImplementation(async (input) =>
        input.route === 'identity.refresh'
          ? { ok: true, status: 200, data: { truncated: true } }
          : { ok: true, status: 200, data: old },
      )
      fireEvent.click(screen.getByRole('button', { name: 'Refresh suggestions' }))
      await screen.findByText(
        'Suggestions refreshed. Choose Refresh suggestions again to check more matches.',
      )
      expect(calls.mock.calls.filter(([input]) => input.route === 'identity.refresh')).toHaveLength(2)
    })
    it('leaving the page cancels its named request and a late success cannot reload or move focus', async () => {
      const { response, calls, cancel, old, client, view, busy } = setup(mode)
      fireEvent.click(screen.getByRole('button', { name: 'Refresh suggestions' }))
      await waitFor(() => expect(calls).toHaveBeenCalledTimes(1))
      const id = calls.mock.calls[0]![0].requestId
      expect(id).toMatch(/^[a-f0-9]{32}$/)
      view.unmount()
      render(<button autoFocus>Another page</button>)
      expect(cancel).toHaveBeenCalledExactlyOnceWith(id)
      await act(async () => {
        response.resolve({ ok: true, status: 200, data: {} })
        await response.promise
      })
      await waitFor(() => expect(calls).toHaveBeenCalledTimes(1))
      expect(client.getQueryData(['api', 'identity.get', undefined])).toEqual(old)
      expect(busy.mock.calls.map(([value]) => value)).toEqual([true])
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Another page' }))
    })
  })
it('an abort during dispatch is forwarded once and cannot return late data to the caller', async () => {
  const controller = new AbortController(),
    response = deferred<unknown>()
  const cancel = vi.fn(async () => true)
  const calls = vi.fn(() => {
    controller.abort()
    return response.promise
  })
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request: calls, cancelRequest: cancel },
  })
  const pending = apiRequest('identity.refresh', undefined, {}, controller.signal)
  response.resolve({ ok: true, status: 200, data: 'late' })
  await expect(pending).rejects.toMatchObject({ name: 'AbortError' })
  expect(cancel).toHaveBeenCalledTimes(1)
})
