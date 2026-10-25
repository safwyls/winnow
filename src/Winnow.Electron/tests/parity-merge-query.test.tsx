// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import {
  mergeReviewKey,
  refreshIdentityReview,
  useIdentityReview,
} from '../src/renderer/features/parity-merge-query'
import { mergeFixture } from './parity-merge-fixtures'
import type { ApiRequest } from '../src/shared/bridge'

afterEach(cleanup)
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
function harness() {
  let review = mergeFixture()
  const request = vi.fn(async (_input: ApiRequest) => ({
    ok: true,
    status: 200,
    data: structuredClone(review),
  }))
  const cancelRequest = vi.fn(async () => true)
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, cancelRequest } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  function Surface({ mode }: { mode: string }) {
    const result = useIdentityReview()
    return <output aria-label={mode}>{result.data?.workspace.works[0]?.name ?? 'Loading'}</output>
  }
  function page(mode: string) {
    return (
      <QueryClientProvider client={client}>
        <Surface mode={mode} />
      </QueryClientProvider>
    )
  }
  function change() {
    review = structuredClone(review)
    review.revision = 'r2'
    review.workspace.works[0]!.name = 'Witcher'
    return review
  }
  return { request, cancelRequest, client, page, change, review }
}
for (const mode of ['desktop', 'fullscreen'])
  describe(`${mode} shared merge snapshots`, () => {
    it('publishes changed proposals after a completed sweep even when the pending count is unchanged', async () => {
      const test = harness()
      render(test.page(mode))
      await screen.findByText('Bastion')
      const fresh = test.change()
      await act(() => refreshIdentityReview(test.client))
      expect(await screen.findByText('Witcher')).toBeTruthy()
      expect(test.client.getQueryData(mergeReviewKey)).toEqual(fresh)
      expect(fresh.candidates).toHaveLength(test.review.candidates.length)
    })
    it('cancels an earlier read and cannot publish its late result over a newer refresh', async () => {
      const test = harness(),
        older = deferred<{ ok: boolean; status: number; data: typeof test.review }>()
      test.request.mockImplementationOnce(() => older.promise)
      render(test.page(mode))
      await waitFor(() => expect(test.request).toHaveBeenCalledTimes(1))
      test.change()
      await act(() => refreshIdentityReview(test.client))
      await screen.findByText('Witcher')
      await act(async () => {
        older.resolve({ ok: true, status: 200, data: test.review })
        await older.promise
      })
      expect(screen.getByText('Witcher')).toBeTruthy()
      expect(test.cancelRequest).toHaveBeenCalledWith(test.request.mock.calls[0]![0].requestId)
    })
    it('defers hidden invalidation until entry and reuses an unchanged snapshot on subsequent entry', async () => {
      const test = harness(),
        view = render(test.page(mode))
      await screen.findByText('Bastion')
      view.unmount()
      test.change()
      await test.client.invalidateQueries({ queryKey: mergeReviewKey })
      expect(test.request).toHaveBeenCalledTimes(1)
      const reopened = render(test.page(mode))
      await screen.findByText('Witcher')
      expect(test.request).toHaveBeenCalledTimes(2)
      reopened.unmount()
      render(test.page(mode))
      expect(screen.getByText('Witcher')).toBeTruthy()
      expect(test.request).toHaveBeenCalledTimes(2)
    })
    it('reopens another surface on the shared refreshed snapshot without rebuilding or rereading it', async () => {
      const test = harness(),
        first = render(test.page(mode))
      await screen.findByText('Bastion')
      first.unmount()
      const other = render(test.page(mode === 'desktop' ? 'fullscreen' : 'desktop'))
      test.change()
      await act(() => refreshIdentityReview(test.client))
      await screen.findByText('Witcher')
      other.unmount()
      render(test.page(mode))
      expect(screen.getByText('Witcher')).toBeTruthy()
      expect(test.request).toHaveBeenCalledTimes(2)
    })
    it('reloads a visible surface immediately on an external library invalidation', async () => {
      const test = harness()
      render(test.page(mode))
      await screen.findByText('Bastion')
      test.change()
      await act(() => test.client.invalidateQueries({ queryKey: ['api'] }))
      await screen.findByText('Witcher')
      expect(test.request).toHaveBeenCalledTimes(2)
    })
  })
