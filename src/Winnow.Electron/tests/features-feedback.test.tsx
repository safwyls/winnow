// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { FeedbackHistory } from '../src/renderer/features/Settings'
import { HideGame } from '../src/renderer/features/Details'

afterEach(() => cleanup())
function client() {
  return new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
}
describe('library visibility and recommendation feedback', () => {
  it('only allows undo for active known feedback and sends its exact numeric kind', async () => {
    const verdict = { releaseId: 10, kind: 1, createdAt: '2026-09-01T12:00:00Z', status: 0 }
    const request = vi
      .fn()
      .mockImplementation(async ({ route }: { route: string }) => ({
        ok: true,
        status: 200,
        data:
          route === 'feed.history'
            ? [verdict, { ...verdict, kind: 0, status: 1 }, { ...verdict, status: 2 }]
            : route === 'library.get'
              ? {
                  games: [
                    {
                      workId: 1,
                      title: 'A game',
                      bucket: 'unplayed',
                      playtimeMinutes: 0,
                      entries: [
                        {
                          ownershipId: 20,
                          releaseId: 10,
                          workId: 1,
                          title: 'A game',
                          store: 'steam',
                          installed: true,
                          playtimeMinutes: 0,
                        },
                      ],
                    },
                  ],
                  lists: [],
                }
              : true,
      }))
    Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
    render(
      <QueryClientProvider client={client()}>
        <FeedbackHistory />
      </QueryClientProvider>,
    )
    const undo = await screen.findByRole('button', { name: 'Undo Not now for A game' })
    expect(screen.getAllByRole('button')).toHaveLength(1)
    fireEvent.click(undo)
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'feed.revoke',
        params: undefined,
        body: { releaseId: 10, kind: 1 },
      }),
    )
    await screen.findByText('Your feedback was undone. This game can appear in recommendations again.')
  })
  it('hides a game only after confirmation, then returns to the previous page', async () => {
    const request = vi.fn().mockResolvedValue({ ok: true, status: 204 })
    const onHidden = vi.fn()
    Object.defineProperty(window, 'winnow', { value: { request }, configurable: true })
    render(
      <QueryClientProvider client={client()}>
        <HideGame workId={99} onHidden={onHidden} />
      </QueryClientProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Hide game…' }))
    expect(request).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Hide game' }))
    await waitFor(() => expect(onHidden).toHaveBeenCalledTimes(1))
    expect(request).toHaveBeenCalledWith({
      route: 'hidden.put',
      params: undefined,
      body: { workIds: [99], hidden: true },
    })
  })
})
