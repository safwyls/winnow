// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MergeQueue } from '../src/renderer/features/parity-merge'
import { useApiQuery } from '../src/renderer/api/hooks'
import type { MergeReview } from '../src/renderer/features/parity-merge-model'
import type { ApiRequest } from '../src/shared/bridge'
import { clearViewState } from '../src/renderer/viewState'
import { mergeFixture } from './parity-merge-fixtures'

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  for (const key of [
    'queue-section',
    'queue-sort',
    'queue-choices',
    'review-undo',
    'queue-busy',
    'queue-problem',
    'queue-refresh-required',
  ])
    clearViewState(`identity:${key}`)
})
function setup(mode: string, override?: (input: ApiRequest, review: MergeReview) => unknown) {
  const review = mergeFixture(),
    original = structuredClone(review)
  let sequence = 1
  const request = vi.fn(async (input: ApiRequest) => {
    const replacement = await override?.(input, review)
    if (replacement !== undefined) return replacement
    if (input.route === 'identity.get') return { ok: true, status: 200, data: structuredClone(review) }
    if (input.route === 'preferences.presentation.get') return { ok: true, status: 200, data: [] }
    if (input.route === 'preferences.presentation.put') return { ok: true, status: 204 }
    const body = input.body as Record<string, unknown>
    if (body.expectedRevision !== review.revision)
      return { ok: false, status: 409, message: 'Review changed' }
    review.revision = `r${++sequence}`
    if (input.route === 'identity.link') {
      const actId = 100 + sequence
      for (const child of body.childWorkIds as number[])
        review.history.push({
          id: actId + child,
          actId,
          parentWorkId: body.parentWorkId as number,
          childWorkId: child,
          kind: body.kind as string,
        })
      review.candidates = review.candidates.filter(
        (candidate) => !(body.rejectedCandidateIds as number[]).includes(candidate.id),
      )
      return { ok: true, status: 200, data: { revision: review.revision, actId } }
    }
    if (input.route === 'identity.dismiss')
      review.candidates = review.candidates.filter(
        (candidate) => !(body.candidateIds as number[]).includes(candidate.id),
      )
    if (input.route === 'identity.undo') {
      review.history = review.history.filter((link) => !(body.actIds as number[]).includes(link.actId))
      review.candidates.push(
        ...original.candidates.filter(
          (candidate) =>
            (body.candidateIds as number[]).includes(candidate.id) &&
            !review.candidates.some((current) => current.id === candidate.id),
        ),
      )
    }
    return { ok: true, status: 200, data: { revision: review.revision } }
  })
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const onOpenGame = vi.fn(),
    onReview = vi.fn()
  function Host() {
    const query = useApiQuery<MergeReview>('identity.get')
    return (
      <div className={`mode-${mode}`}>
        {query.data && <MergeQueue review={query.data} onReview={onReview} onOpenGame={onOpenGame} />}
      </div>
    )
  }
  const component = (
    <QueryClientProvider client={client}>
      <Host />
    </QueryClientProvider>
  )
  const view = render(component)
  return { request, review, client, component, view, onOpenGame, onReview }
}
for (const mode of ['desktop', 'fullscreen'])
  describe(`${mode} grouped identity review`, () => {
    it('accepts only exact cross-store groups and one Undo retracts precisely that saved act', async () => {
      const { request } = setup(mode)
      fireEvent.click(await screen.findByRole('button', { name: 'Accept 1 exact match' }))
      await screen.findByRole('article', { name: 'Bastion saved group' })
      expect(
        request.mock.calls.filter(([input]) => input.route === 'identity.link').map(([input]) => input.body),
      ).toEqual([
        {
          expectedRevision: 'r1',
          parentWorkId: 1,
          childWorkIds: [2],
          kind: 'same_game',
          relationLabel: null,
          rejectedCandidateIds: [],
          refusedPairs: [],
        },
      ])
      expect(screen.getByRole('article', { name: 'Prey 2006 proposal' })).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Undo review decisions' }))
      await screen.findByRole('article', { name: 'Bastion proposal' })
      expect(request.mock.calls.find(([input]) => input.route === 'identity.undo')?.[0].body).toEqual({
        expectedRevision: 'r2',
        actIds: [102],
        candidateIds: [],
        refusedPairs: [],
      })
      expect(screen.queryByRole('complementary', { name: 'Review undo' })).toBeNull()
    })
    it('merges all selected groups under their chosen parents with sequential revisions and one bulk Undo', async () => {
      const { request } = setup(mode)
      const bastion = await screen.findByRole('article', { name: 'Bastion proposal' })
      fireEvent.click(within(bastion).getAllByRole('radio')[1]!)
      fireEvent.click(within(bastion).getByRole('checkbox', { name: 'Select Bastion group' }))
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Prey 2006 group' }))
      fireEvent.click(screen.getByRole('button', { name: 'Editions' }))
      expect(screen.queryByRole('article', { name: 'Bastion proposal' })).toBeNull()
      fireEvent.click(screen.getByRole('button', { name: 'Merge 2 selected' }))
      await screen.findByRole('article', { name: 'Prey 2006 saved group' })
      const links = request.mock.calls.filter(([input]) => input.route === 'identity.link')
      expect(links.map(([input]) => input.body)).toMatchObject([
        { expectedRevision: 'r1', parentWorkId: 2, childWorkIds: [1] },
        { expectedRevision: 'r2', parentWorkId: 3, childWorkIds: [4] },
      ])
      fireEvent.click(screen.getByRole('button', { name: 'Undo review decisions' }))
      await screen.findByRole('article', { name: 'Prey 2006 proposal' })
      expect(request.mock.calls.find(([input]) => input.route === 'identity.undo')?.[0].body).toMatchObject({
        expectedRevision: 'r3',
        actIds: [102, 103],
      })
    })
    it('retains current group choices across detail navigation, and arrows walk visible rows without wrapping', async () => {
      const { view, component, onOpenGame } = setup(mode)
      const bastion = await screen.findByRole('article', { name: 'Bastion proposal' })
      fireEvent.click(within(bastion).getAllByRole('radio')[1]!)
      const row = within(bastion).getAllByRole('button', { name: /^View Bastion/ })[0]!
      row.focus()
      fireEvent.keyDown(row, { key: 'ArrowUp' })
      expect(document.activeElement).toBe(row)
      fireEvent.keyDown(row, { key: 'ArrowDown' })
      const second = within(bastion).getAllByRole('button', { name: /^View Bastion/ })[1]!
      expect(document.activeElement).toBe(second)
      fireEvent.click(second)
      expect(onOpenGame).toHaveBeenCalledWith(2)
      view.unmount()
      render(component)
      const restored = await screen.findByRole('article', { name: 'Bastion proposal' })
      expect((within(restored).getAllByRole('radio')[1] as HTMLInputElement).checked).toBe(true)
    })
    it('accumulates consecutive dismissals and restores their candidate IDs in one Undo', async () => {
      const { request } = setup(mode)
      fireEvent.click(
        within(await screen.findByRole('article', { name: 'Bastion proposal' })).getByRole('button', {
          name: 'Different games',
        }),
      )
      await waitFor(() => expect(screen.queryByRole('article', { name: 'Bastion proposal' })).toBeNull())
      fireEvent.click(
        within(screen.getByRole('article', { name: 'Prey 2006 proposal' })).getByRole('button', {
          name: 'Different games',
        }),
      )
      await screen.findByText('2 left separate · Nothing deleted')
      fireEvent.click(screen.getByRole('button', { name: 'Undo review decisions' }))
      await screen.findByRole('article', { name: 'Bastion proposal' })
      expect(screen.getByRole('article', { name: 'Prey 2006 proposal' })).toBeTruthy()
      expect(request.mock.calls.find(([input]) => input.route === 'identity.undo')?.[0].body).toEqual({
        expectedRevision: 'r3',
        actIds: [],
        candidateIds: [10, 11],
        refusedPairs: [],
      })
    })
    it('keeps a pending write disabled after a mode remount and preserves a conflict until saved review is checked', async () => {
      let finish!: (value: unknown) => void
      let failed = false
      const { view, component, request } = setup(mode, (input) => {
        if (input.route === 'identity.link' && !failed)
          return new Promise((resolve) => {
            finish = resolve
          })
      })
      fireEvent.click(await screen.findByRole('button', { name: 'Accept 1 exact match' }))
      await screen.findByText('Saving your review…')
      view.unmount()
      render(component)
      expect(
        ((await screen.findByRole('button', { name: 'Accept 1 exact match' })) as HTMLButtonElement).disabled,
      ).toBe(true)
      failed = true
      finish({ ok: false, status: 409, message: 'Review changed elsewhere' })
      await screen.findByRole('button', { name: 'Check saved review' })
      expect(
        (screen.getByRole('button', { name: 'Accept 1 exact match' }) as HTMLButtonElement).disabled,
      ).toBe(true)
      expect(request.mock.calls.filter(([input]) => input.route === 'identity.link')).toHaveLength(1)
      fireEvent.click(screen.getByRole('button', { name: 'Check saved review' }))
      await waitFor(() =>
        expect(
          (screen.getByRole('button', { name: 'Accept 1 exact match' }) as HTMLButtonElement).disabled,
        ).toBe(false),
      )
    })
    it('retracts a standing group from its act and can link the restored proposal again', async () => {
      const { request } = setup(mode)
      fireEvent.click(await screen.findByRole('button', { name: 'Accept 1 exact match' }))
      fireEvent.click(
        within(await screen.findByRole('article', { name: 'Bastion saved group' })).getByRole('button', {
          name: 'Separate again',
        }),
      )
      await screen.findByRole('article', { name: 'Bastion proposal' })
      fireEvent.click(screen.getByRole('button', { name: 'Accept 1 exact match' }))
      await screen.findByRole('article', { name: 'Bastion saved group' })
      expect(request.mock.calls.filter(([input]) => input.route === 'identity.link')).toHaveLength(2)
      expect(request.mock.calls.find(([input]) => input.route === 'identity.undo')?.[0].body).toMatchObject({
        actIds: [102],
      })
    })
    it('applies a saved preferred platform to pending groups and saves None without changing the current header', async () => {
      const { request } = setup(mode)
      await screen.findByRole('article', { name: 'Bastion proposal' })
      fireEvent.change(screen.getByRole('combobox', { name: 'Preferred main platform' }), {
        target: { value: 'gog' },
      })
      await waitFor(() =>
        expect(
          (
            within(screen.getByRole('article', { name: 'Bastion proposal' })).getAllByRole(
              'radio',
            )[1] as HTMLInputElement
          ).checked,
        ).toBe(true),
      )
      fireEvent.change(screen.getByRole('combobox', { name: 'Preferred main platform' }), {
        target: { value: '' },
      })
      await waitFor(() =>
        expect(
          (screen.getByRole('combobox', { name: 'Preferred main platform' }) as HTMLSelectElement).disabled,
        ).toBe(false),
      )
      expect(
        (
          within(screen.getByRole('article', { name: 'Bastion proposal' })).getAllByRole(
            'radio',
          )[1] as HTMLInputElement
        ).checked,
      ).toBe(true)
      expect(
        request.mock.calls
          .filter(([input]) => input.route === 'preferences.presentation.put')
          .map(([input]) => input.body),
      ).toEqual([{ value: 'gog' }, { value: '' }])
    })
    it('advances focus to the row taking an answered card’s place and keeps focus when a write fails', async () => {
      let fail = true
      setup(mode, (input) =>
        input.route === 'identity.dismiss' && fail
          ? { ok: false, status: 409, message: 'Changed review' }
          : undefined,
      )
      const card = await screen.findByRole('article', { name: 'Bastion proposal' })
      const row = within(card).getAllByRole('button', { name: /^View Bastion/ })[0]!
      row.focus()
      fireEvent.click(within(card).getByRole('button', { name: 'Different games' }))
      await screen.findByRole('button', { name: 'Check saved review' })
      expect(document.activeElement).toBe(row)
      fail = false
      fireEvent.click(screen.getByRole('button', { name: 'Check saved review' }))
      await waitFor(() => expect(screen.queryByRole('button', { name: 'Check saved review' })).toBeNull())
      fireEvent.click(within(card).getByRole('button', { name: 'Different games' }))
      await waitFor(() =>
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'View Prey 2006' })),
      )
    })
    it('retains only completed bulk acts for Undo when a later group conflicts', async () => {
      const { request } = setup(mode, (input) =>
        input.route === 'identity.link' && (input.body as { parentWorkId: number }).parentWorkId === 3
          ? { ok: false, status: 409, message: 'Second group changed' }
          : undefined,
      )
      fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Bastion group' }))
      fireEvent.click(screen.getByRole('checkbox', { name: 'Select Prey 2006 group' }))
      fireEvent.click(screen.getByRole('button', { name: 'Merge 2 selected' }))
      await screen.findByRole('button', { name: 'Check saved review' })
      expect(screen.getByText('1 rolled up · Nothing deleted')).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Check saved review' }))
      await screen.findByRole('article', { name: 'Bastion saved group' })
      fireEvent.click(screen.getByRole('button', { name: 'Undo review decisions' }))
      await screen.findByRole('article', { name: 'Bastion proposal' })
      expect(request.mock.calls.find(([input]) => input.route === 'identity.undo')?.[0].body).toMatchObject({
        expectedRevision: 'r2',
        actIds: [102],
      })
    })
  })

it('expires the undo dock after seven seconds and lets Dismiss close it early without another write', async () => {
  const { request } = setup('desktop')
  fireEvent.click(await screen.findByRole('button', { name: 'Accept 1 exact match' }))
  await screen.findByRole('article', { name: 'Bastion saved group' })
  const oldCalls = request.mock.calls.length
  fireEvent.click(screen.getByRole('button', { name: 'Dismiss review undo' }))
  expect(screen.queryByRole('button', { name: 'Undo review decisions' })).toBeNull()
  expect(request.mock.calls.length).toBe(oldCalls)
  fireEvent.click(
    within(screen.getByRole('article', { name: 'Prey 2006 proposal' })).getByRole('button', {
      name: 'Different games',
    }),
  )
  await screen.findByText('1 left separate · Nothing deleted')
  vi.useFakeTimers()
  // Remount starts the remaining duration from the stored wall-clock deadline.
  cleanup()
  const review = mergeFixture(),
    client = new QueryClient()
  render(
    <QueryClientProvider client={client}>
      <MergeQueue review={review} onReview={vi.fn()} />
    </QueryClientProvider>,
  )
  await act(async () => {
    await vi.advanceTimersByTimeAsync(7001)
  })
  expect(screen.queryByRole('button', { name: 'Undo review decisions' })).toBeNull()
  expect(request.mock.calls.filter(([input]) => input.route === 'identity.undo')).toHaveLength(0)
})
