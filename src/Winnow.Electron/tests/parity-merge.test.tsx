// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MergeQueue } from '../src/renderer/features/parity-merge'
import { useApiQuery } from '../src/renderer/api/hooks'
import type { MergeReview } from '../src/renderer/features/parity-merge-model'
import type { ApiRequest } from '../src/shared/bridge'
import { clearViewState, useViewState } from '../src/renderer/viewState'
import { mergeFixture, sourceSortFixture } from './parity-merge-fixtures'
import { MergeRefresh } from '../src/renderer/features/parity-merge-refresh'
import { useIdentityReview } from '../src/renderer/features/parity-merge-query'

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  for (const key of [
    'queue-section',
    'queue-sort',
    'queue-choices',
    'queue-answered-keys',
    'queue-positions',
    'queue-applied-platform',
    'review-undo',
    'queue-busy',
    'queue-problem',
    'queue-refresh-required',
    'desktop:detail-return',
    'fullscreen:detail-return',
  ])
    clearViewState(`identity:${key}`)
})
function setup(
  mode: 'desktop' | 'fullscreen',
  override?: (input: ApiRequest, review: MergeReview) => unknown,
  prepare?: (review: MergeReview) => void,
) {
  const review = mergeFixture()
  prepare?.(review)
  const original = structuredClone(review),
    refused = new Set<string>()
  let sequence = 1
  const request = vi.fn(async (input: ApiRequest) => {
    const replacement = await override?.(input, review)
    if (replacement !== undefined) return replacement
    if (input.route === 'identity.get') {
      const fresh = structuredClone(review)
      fresh.expansions = fresh.expansions
        .map((group) => ({
          ...group,
          members: group.members.filter(
            (member) =>
              !refused.has(`${group.base.workId}:${member.work.workId}`) &&
              !fresh.history.some((link) => link.childWorkId === member.work.workId),
          ),
        }))
        .filter((group) => group.members.length)
      return { ok: true, status: 200, data: fresh }
    }
    if (input.route === 'identity.refresh') {
      review.revision = `r${++sequence}`
      return { ok: true, status: 200, data: { truncated: false } }
    }
    if (input.route === 'artworkState')
      return { ok: true, status: 200, data: { current: null, revision: 'art' } }
    if (input.route === 'preferences.presentation.get') return { ok: true, status: 200, data: [] }
    if (input.route === 'preferences.presentation.put') return { ok: true, status: 204 }
    const body = input.body as Record<string, unknown>
    if (body.expectedRevision !== review.revision)
      return { ok: false, status: 409, message: 'Review changed' }
    review.revision = `r${++sequence}`
    if (input.route === 'identity.header')
      review.workspace.preferredHeaderStores = {
        ...((review.workspace.preferredHeaderStores as object) ?? {}),
        [body.workId as number]: body.store,
      }
    if (input.route === 'identity.link' || input.route === 'identity.dismiss')
      for (const pair of body.refusedPairs as { baseWorkId: number; childWorkId: number }[])
        refused.add(`${pair.baseWorkId}:${pair.childWorkId}`)
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
      for (const pair of body.refusedPairs as { baseWorkId: number; childWorkId: number }[])
        refused.delete(`${pair.baseWorkId}:${pair.childWorkId}`)
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
    const query = useIdentityReview()
    return (
      <div className={`mode-${mode}`}>
        <MergeRefresh disabled={false} onBusy={() => {}} />
        {query.data && (
          <MergeQueue mode={mode} review={query.data} onReview={onReview} onOpenGame={onOpenGame} />
        )}
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
for (const mode of ['desktop', 'fullscreen'] as const)
  describe(`${mode} grouped identity review`, () => {
    function kind(name: string) {
      if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: /^Kind ·/ }))
      fireEvent.click(screen.getByRole('button', { name }))
    }
    async function platform(value: string) {
      if (mode === 'desktop')
        fireEvent.change(screen.getByRole('combobox', { name: 'Preferred main platform' }), {
          target: { value },
        })
      else {
        fireEvent.click(screen.getByRole('button', { name: /^Preferred platform ·/ }))
        fireEvent.click(screen.getByRole('button', { name: value === 'gog' ? 'GOG' : 'None' }))
      }
      await waitFor(() =>
        expect(
          (
            screen.getByRole(mode === 'desktop' ? 'combobox' : 'button', {
              name: mode === 'desktop' ? 'Preferred main platform' : /^Preferred platform ·/,
            }) as HTMLButtonElement
          ).disabled,
        ).toBe(false),
      )
    }
    async function open(title: string, saved = false) {
      const card = await screen.findByRole('article', {
        name: `${title} ${saved ? 'saved group' : 'proposal'}`,
      })
      if (mode === 'desktop') return card
      fireEvent.click(within(card).getByRole('button'))
      return screen.getByRole('dialog')
    }
    async function bulk(name: string) {
      fireEvent.click(await screen.findByRole('button', { name }))
      if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    }
    async function select(title: string) {
      const card = await open(title)
      fireEvent.click(
        within(card).getByRole(mode === 'desktop' ? 'checkbox' : 'button', {
          name: mode === 'desktop' ? `Select ${title} group` : 'Select for grouping',
        }),
      )
      if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Back to proposals' }))
    }
    async function promoteSecond(title: string) {
      const card = await open(title)
      if (mode === 'desktop') fireEvent.click(within(card).getAllByRole('radio')[1]!)
      else {
        fireEvent.click(within(card).getAllByRole('button', { name: / · (Header|Included|Left out)$/ })[1]!)
        fireEvent.click(screen.getByRole('button', { name: 'Make header' }))
        fireEvent.click(screen.getByRole('button', { name: 'Back to proposals' }))
      }
    }
    async function secondIsHeader(title: string) {
      const card = await open(title)
      if (mode === 'desktop')
        expect((within(card).getAllByRole('radio')[1] as HTMLInputElement).checked).toBe(true)
      else {
        expect(
          within(card).getAllByRole('button', { name: / · (Header|Included|Left out)$/ })[1]!.textContent,
        ).toMatch(/Header$/)
        fireEvent.click(screen.getByRole('button', { name: 'Back to proposals' }))
      }
    }
    async function answer(title: string, action: string, saved = false) {
      const card = await open(title, saved)
      fireEvent.click(within(card).getByRole('button', { name: action }))
      if (mode === 'fullscreen' && action === 'Same game')
        fireEvent.click(screen.getByRole('button', { name: 'Continue' }))
    }
    it('accepts only exact cross-store groups and one Undo retracts precisely that saved act', async () => {
      const { request } = setup(mode)
      await bulk('Accept 1 exact match')
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
      await promoteSecond('Bastion')
      await select('Bastion')
      await select('Prey 2006')
      kind('Editions')
      expect(screen.queryByRole('article', { name: 'Bastion proposal' })).toBeNull()
      await bulk('Merge 2 selected')
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
      await promoteSecond('Bastion')
      const bastion = await screen.findByRole('article', { name: 'Bastion proposal' })
      const card = mode === 'desktop' ? bastion : await open('Bastion')
      const rows = within(card).getAllByRole('button', {
        name: mode === 'desktop' ? /^Choose Bastion/ : / · (Header|Included|Left out)$/,
      })
      rows[0]!.focus()
      fireEvent.keyDown(rows[0]!, { key: 'ArrowUp' })
      expect(document.activeElement).toBe(rows[0])
      fireEvent.keyDown(rows[0]!, { key: 'ArrowDown' })
      expect(document.activeElement).toBe(rows[1])
      if (mode === 'desktop')
        fireEvent.click(within(card).getByRole('button', { name: 'Details for Bastion (GOG)' }))
      else {
        fireEvent.click(rows[1]!)
        fireEvent.click(screen.getByRole('button', { name: 'Open game' }))
      }
      expect(onOpenGame).toHaveBeenCalledWith(2)
      // The departing page remains mounted while its exit animation finishes.
      await act(async () => {
        await new Promise<void>((done) => requestAnimationFrame(() => requestAnimationFrame(() => done())))
      })
      view.unmount()
      render(component)
      await waitFor(() =>
        expect(document.activeElement).toBe(
          screen.getByRole('button', {
            name: mode === 'desktop' ? 'Choose Bastion (GOG)' : /^Bastion ·/,
          }),
        ),
      )
      await secondIsHeader('Bastion')
    })
    it('accumulates consecutive dismissals and restores their candidate IDs in one Undo', async () => {
      const { request } = setup(mode)
      await answer('Bastion', 'Different games')
      await waitFor(() => expect(screen.queryByRole('article', { name: 'Bastion proposal' })).toBeNull())
      await answer('Prey 2006', 'Different games')
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
      await bulk('Accept 1 exact match')
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
      await bulk('Accept 1 exact match')
      await answer('Bastion', 'Separate again', true)
      await screen.findByRole('article', { name: 'Bastion proposal' })
      await bulk('Accept 1 exact match')
      await screen.findByRole('article', { name: 'Bastion saved group' })
      expect(request.mock.calls.filter(([input]) => input.route === 'identity.link')).toHaveLength(2)
      expect(request.mock.calls.find(([input]) => input.route === 'identity.undo')?.[0].body).toMatchObject({
        actIds: [102],
      })
    })
    it('applies a saved preferred platform to pending groups and saves None without changing the current header', async () => {
      const { request } = setup(mode)
      await screen.findByRole('article', { name: 'Bastion proposal' })
      await platform('gog')
      await secondIsHeader('Bastion')
      await platform('')
      await secondIsHeader('Bastion')
      expect(
        request.mock.calls
          .filter(([input]) => input.route === 'preferences.presentation.put')
          .map(([input]) => input.body),
      ).toEqual([{ value: 'gog' }, { value: '' }])
    })
    it('keeps a user header override when a fresh snapshot arrives under the saved platform preference', async () => {
      const { client, review } = setup(mode, (input) =>
        input.route === 'preferences.presentation.get'
          ? { ok: true, status: 200, data: [{ preference: 'PreferredMergePlatform', value: 'gog' }] }
          : undefined,
      )
      if (mode === 'desktop')
        await waitFor(() =>
          expect(
            (screen.getByRole('combobox', { name: 'Preferred main platform' }) as HTMLSelectElement).value,
          ).toBe('gog'),
        )
      else await screen.findByRole('button', { name: 'Preferred platform · GOG' })
      await secondIsHeader('Bastion')
      let card = await open('Bastion')
      if (mode === 'desktop') fireEvent.click(within(card).getAllByRole('radio')[0]!)
      else {
        fireEvent.click(within(card).getByRole('button', { name: 'Bastion (Steam) · Included' }))
        fireEvent.click(screen.getByRole('button', { name: 'Make header' }))
        fireEvent.click(screen.getByRole('button', { name: 'Back to proposals' }))
      }
      act(() =>
        client.setQueryData(['api', 'identity.get', undefined], {
          ...structuredClone(review),
          revision: 'remote-r2',
        }),
      )
      card = await open('Bastion')
      if (mode === 'desktop')
        expect((within(card).getAllByRole('radio')[0] as HTMLInputElement).checked).toBe(true)
      else expect(within(card).getByRole('button', { name: 'Bastion (Steam) · Header' })).toBeTruthy()
    })
    it('advances focus to the row taking an answered card’s place and keeps focus when a write fails', async () => {
      let fail = true
      setup(mode, (input) =>
        input.route === 'identity.dismiss' && fail
          ? { ok: false, status: 409, message: 'Changed review' }
          : undefined,
      )
      const card = await screen.findByRole('article', { name: 'Bastion proposal' })
      const row = within(card).getAllByRole('button', {
        name: mode === 'desktop' ? /^Choose Bastion/ : /^Bastion ·/,
      })[0]!
      row.focus()
      await answer('Bastion', 'Different games')
      await screen.findByRole('button', { name: 'Check saved review' })
      await waitFor(() => expect(document.activeElement).toBe(row))
      fail = false
      fireEvent.click(screen.getByRole('button', { name: 'Check saved review' }))
      await waitFor(() => expect(screen.queryByRole('button', { name: 'Check saved review' })).toBeNull())
      await answer('Bastion', 'Different games')
      await waitFor(() =>
        expect(document.activeElement).toBe(
          screen.getByRole('button', { name: mode === 'desktop' ? 'Choose Prey 2006' : /^Prey 2006 ·/ }),
        ),
      )
    })
    it('keeps an answered card in its original slot, separates in place, and puts saved strips last only after sorting', async () => {
      setup(mode, undefined, (review) => {
        ;(review.workspace.ownerships as { store: string }[]).forEach((ownership) => {
          ownership.store = 'steam'
        })
      })
      const first = await screen.findByRole('article', { name: 'Bastion proposal' })
      const before = screen.getAllByRole('article')
      await answer('Bastion', 'Same game')
      const saved = await screen.findByRole('article', { name: 'Bastion saved group' })
      expect(saved).toBe(first)
      expect(screen.getAllByRole('article')).toEqual(before)
      await answer('Bastion', 'Separate again', true)
      expect(await screen.findByRole('article', { name: 'Bastion proposal' })).toBe(first)
      expect(screen.getAllByRole('article')).toEqual(before)
      await answer('Bastion', 'Same game')
      await screen.findByRole('article', { name: 'Bastion saved group' })
      if (mode === 'desktop')
        fireEvent.change(screen.getByRole('combobox', { name: 'Sort proposals' }), {
          target: { value: 'title' },
        })
      else {
        fireEvent.click(screen.getByRole('button', { name: 'Sort · Strongest match' }))
        fireEvent.click(screen.getByRole('button', { name: 'Title' }))
      }
      expect(screen.getAllByRole('article').at(-1)).toBe(first)
    })
    it('restores consecutive dismissed groups to their original slots with one Undo', async () => {
      setup(mode, undefined, (review) => {
        ;(review.workspace.ownerships as { store: string }[]).forEach((ownership) => {
          ownership.store = 'steam'
        })
      })
      await screen.findByRole('article', { name: 'Bastion proposal' })
      const before = screen.getAllByRole('article').map((article) => article.getAttribute('aria-label'))
      await answer('Bastion', 'Different games')
      await waitFor(() => expect(screen.queryByRole('article', { name: 'Bastion proposal' })).toBeNull())
      await answer('Prey 2006', 'Different games')
      await waitFor(() => expect(screen.queryAllByRole('article')).toHaveLength(0))
      fireEvent.click(screen.getByRole('button', { name: 'Undo review decisions' }))
      await screen.findByRole('article', { name: 'Bastion proposal' })
      expect(screen.getAllByRole('article').map((article) => article.getAttribute('aria-label'))).toEqual(
        before,
      )
    })
    it('changes the saved header storefront and title without rewriting the group membership', async () => {
      const { review, request } = setup(mode, undefined, (review) => {
        review.workspace.works[1]!.name = 'Bastion GOG edition'
      })
      await answer('Bastion', 'Same game')
      await screen.findByRole('article', { name: 'Bastion saved group' })
      const links = structuredClone(review.history)
      if (mode === 'desktop')
        fireEvent.change(screen.getByRole('combobox', { name: 'Header store for Bastion' }), {
          target: { value: 'gog' },
        })
      else {
        await open('Bastion', true)
        fireEvent.click(screen.getByRole('button', { name: 'Header store · Automatic' }))
        fireEvent.click(screen.getByRole('button', { name: 'GOG' }))
      }
      if (mode === 'fullscreen') {
        await screen.findByRole('dialog', { name: 'Bastion GOG edition' })
        fireEvent.click(screen.getByRole('button', { name: 'Back to proposals' }))
      }
      await screen.findByRole('article', { name: 'Bastion GOG edition saved group' })
      expect(review.history).toEqual(links)
      expect(
        request.mock.calls
          .filter(([input]) => input.route === 'identity.header')
          .map(([input]) => input.body),
      ).toEqual([{ expectedRevision: 'r2', workId: 1, store: 'gog' }])
      fireEvent.click(screen.getByRole('button', { name: 'Undo review decisions' }))
      await screen.findByRole('article', { name: 'Bastion proposal' })
      expect(review.history).toHaveLength(0)
    })
    it('refreshes after dismissing and merging without losing either previous answer or the current Undo revision', async () => {
      const { request } = setup(mode)
      await answer('Prey 2006', 'Different games')
      await waitFor(() => expect(screen.queryByRole('article', { name: 'Prey 2006 proposal' })).toBeNull())
      await answer('Bastion', 'Same game')
      await screen.findByRole('article', { name: 'Bastion saved group' })
      fireEvent.click(screen.getByRole('button', { name: 'Refresh suggestions' }))
      await screen.findByText('Suggestions refreshed. Your previous answers are kept.')
      expect(screen.queryByRole('article', { name: 'Prey 2006 proposal' })).toBeNull()
      expect(screen.getByRole('article', { name: 'Bastion saved group' })).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Undo review decisions' }))
      await screen.findByRole('article', { name: 'Bastion proposal' })
      expect(request.mock.calls.find(([input]) => input.route === 'identity.undo')?.[0].body).toMatchObject({
        expectedRevision: 'r4',
        actIds: [103],
      })
    })
    it('refreshes a dismissal then restores the current pending proposal with Undo', async () => {
      setup(mode)
      await answer('Bastion', 'Different games')
      await waitFor(() => expect(screen.queryByRole('article', { name: 'Bastion proposal' })).toBeNull())
      fireEvent.click(screen.getByRole('button', { name: 'Refresh suggestions' }))
      await screen.findByText('Suggestions refreshed. Your previous answers are kept.')
      fireEvent.click(screen.getByRole('button', { name: 'Undo review decisions' }))
      await screen.findByRole('article', { name: 'Bastion proposal' })
      await answer('Bastion', 'Same game')
      await screen.findByRole('article', { name: 'Bastion saved group' })
    })
    it('keeps all five empty sections and distinguishes an unfinished sweep from nothing left to decide', async () => {
      const { client, review } = setup(mode, undefined, (review) => {
        review.candidates = []
        review.hasCompletedSweep = false
      })
      await screen.findByText('nothing waiting')
      expect(screen.getAllByRole('region').map((section) => section.getAttribute('aria-label'))).toEqual([
        'Across stores',
        'Editions',
        'Expansions',
        'Parts',
        'Test builds',
      ])
      expect(screen.getAllByText('Still scanning your library.')).toHaveLength(2)
      expect(screen.getAllByText('Nothing left to decide here.')).toHaveLength(3)
      act(() =>
        client.setQueryData(['api', 'identity.get', undefined], { ...review, hasCompletedSweep: true }),
      )
      await waitFor(() => expect(screen.getAllByText('Nothing left to decide here.')).toHaveLength(5))
      expect(screen.queryAllByText('Still scanning your library.')).toHaveLength(0)
    })
    it('filters kinds with the total-to-shown count and preserves selected groups outside the filter', async () => {
      setup(mode)
      await select('Prey 2006')
      kind('Across stores')
      expect(screen.getAllByRole('region').map((section) => section.getAttribute('aria-label'))).toEqual([
        'Across stores',
      ])
      expect(screen.getByLabelText('Filtered proposals').textContent).toBe('2 → 1')
      expect(screen.getByText('1 proposal · non-destructive', { exact: false })).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Merge 1 selected' })).toBeTruthy()
      kind('All proposals')
      expect(screen.getAllByRole('region')).toHaveLength(5)
      expect(screen.getByText('2 proposals · non-destructive')).toBeTruthy()
    })
    it('applies platform changes to hidden cards, preserves unavailable headers, and accepts the chosen parent', async () => {
      const { request } = setup(mode)
      await screen.findByRole('article', { name: 'Bastion proposal' })
      kind('Editions')
      await platform('gog')
      const unchanged = await open('Prey 2006')
      if (mode === 'desktop')
        expect((within(unchanged).getAllByRole('radio')[0] as HTMLInputElement).checked).toBe(true)
      else {
        expect(within(unchanged).getByRole('button', { name: 'Prey 2006 · Header' })).toBeTruthy()
        fireEvent.click(screen.getByRole('button', { name: 'Back to proposals' }))
      }
      expect(request.mock.calls.filter(([input]) => input.route === 'identity.link')).toHaveLength(0)
      kind('All proposals')
      await bulk('Accept 1 exact match')
      await screen.findByRole('article', { name: 'Bastion saved group' })
      expect(request.mock.calls.find(([input]) => input.route === 'identity.link')?.[0].body).toMatchObject({
        parentWorkId: 2,
        childWorkIds: [1],
      })
    })
    it('orders all three source confidence bands, summed playtime and titles and identifies the selected sort', async () => {
      setup(mode, undefined, sourceSortFixture)
      await screen.findByRole('article', { name: 'The Stanley Parable proposal' })
      const titles = () =>
        screen
          .getAllByRole('article')
          .map((card) => card.getAttribute('aria-label')!.replace(' proposal', ''))
      expect(titles()).toEqual(['The Stanley Parable', 'The Witcher 3: Wild Hunt', 'Prey'])
      for (const [value, label, expected] of [
        ['playtime', 'Playtime at stake', ['Prey', 'The Witcher 3: Wild Hunt', 'The Stanley Parable']],
        ['title', 'Title', ['Prey', 'The Stanley Parable', 'The Witcher 3: Wild Hunt']],
      ] as const) {
        if (mode === 'desktop')
          fireEvent.change(screen.getByRole('combobox', { name: 'Sort proposals' }), { target: { value } })
        else {
          fireEvent.click(screen.getByRole('button', { name: /^Sort ·/ }))
          fireEvent.click(screen.getByRole('button', { name: label }))
        }
        expect(titles()).toEqual(expected)
        if (mode === 'desktop')
          expect((screen.getByRole('option', { name: label }) as HTMLOptionElement).selected).toBe(true)
        else expect(screen.getByRole('button', { name: `Sort · ${label}` })).toBeTruthy()
      }
    })
    it('does not answer a group twice and retains exact zero-hour resolved metadata', async () => {
      const { request } = setup(mode, undefined, (review) => {
        ;(review.workspace.buckets as { playtimeMinutes: number; lastPlayedAt: string | null }[]).forEach(
          (entry) => {
            entry.playtimeMinutes = 0
            entry.lastPlayedAt = null
          },
        )
      })
      const card = await open('Bastion')
      const action = within(card).getByRole('button', { name: 'Same game' })
      fireEvent.click(action)
      if (mode === 'fullscreen') {
        const confirm = screen.getByRole('button', { name: 'Continue' })
        fireEvent.click(confirm)
        fireEvent.click(confirm)
      } else fireEvent.click(action)
      await screen.findByRole('article', { name: 'Bastion saved group' })
      const saved = await open('Bastion', true)
      expect(within(saved).queryByRole('button', { name: 'Same game' })).toBeNull()
      expect(within(saved).queryByRole('button', { name: 'Different games' })).toBeNull()
      expect(request.mock.calls.filter(([input]) => input.route === 'identity.link')).toHaveLength(1)
      expect(request.mock.calls.filter(([input]) => input.route === 'identity.dismiss')).toHaveLength(0)
      if (mode === 'desktop')
        expect(within(saved).getByText('2 entries · 0h · nested, nothing deleted')).toBeTruthy()
    })
    it('reincludes an excluded preferred member and syncs a cached preference when the page reopens', async () => {
      const { client, view, component } = setup(mode)
      const card = await open('Bastion')
      if (mode === 'desktop')
        fireEvent.click(within(card).getByRole('checkbox', { name: 'Include Bastion (GOG)' }))
      else {
        fireEvent.click(within(card).getByRole('button', { name: 'Bastion (GOG) · Included' }))
        fireEvent.click(screen.getByRole('button', { name: 'Leave out' }))
        fireEvent.click(screen.getByRole('button', { name: 'Back to proposals' }))
      }
      view.unmount()
      client.setQueryData(
        ['api', 'preferences.presentation.get', undefined],
        [{ preference: 'PreferredMergePlatform', value: 'gog' }],
      )
      render(component)
      await secondIsHeader('Bastion')
      const reopened = await open('Bastion')
      if (mode === 'desktop')
        expect(
          (within(reopened).getByRole('checkbox', { name: 'Include Bastion (Steam)' }) as HTMLInputElement)
            .checked,
        ).toBe(true)
      else expect(within(reopened).getByRole('button', { name: 'Bastion (Steam) · Included' })).toBeTruthy()
    })
    it('retains the current matching header when multiple rows offer the preferred platform', async () => {
      setup(mode, undefined, (review) => {
        ;(review.workspace.ownerships as { id: number; releaseId: number; store: string }[]).push({
          id: 5,
          releaseId: 101,
          store: 'gog',
        })
      })
      await promoteSecond('Bastion')
      await platform('gog')
      const card = await open('Bastion')
      if (mode === 'desktop')
        expect((within(card).getAllByRole('radio')[1] as HTMLInputElement).checked).toBe(true)
      else expect(within(card).getByRole('button', { name: 'Bastion (GOG) · Header' })).toBeTruthy()
    })
    it('treats an unknown saved platform as None while keeping the automatic header available', async () => {
      setup(mode, (input) =>
        input.route === 'preferences.presentation.get'
          ? {
              ok: true,
              status: 200,
              data: [{ preference: 'PreferredMergePlatform', value: 'unknown-platform' }],
            }
          : undefined,
      )
      const card = await open('Bastion')
      if (mode === 'desktop') {
        expect(
          (screen.getByRole('combobox', { name: 'Preferred main platform' }) as HTMLSelectElement).value,
        ).toBe('')
        expect((within(card).getAllByRole('radio')[0] as HTMLInputElement).checked).toBe(true)
      } else {
        expect(within(card).getByRole('button', { name: 'Bastion (Steam) · Header' })).toBeTruthy()
        fireEvent.click(screen.getByRole('button', { name: 'Back to proposals' }))
        expect(screen.getByRole('button', { name: 'Preferred platform · None' })).toBeTruthy()
      }
    })
    it('writes and retracts all directional expansion refusals without regrouping or reviving them on refresh', async () => {
      const { request, review } = setup(mode, undefined, (review) => {
        review.candidates = []
        review.expansions = [
          {
            base: { workId: 1, title: 'Bastion' },
            members: [2, 3].map((id) => ({
              work: { workId: id, title: review.workspace.works[id - 1]!.name },
              kind: 'expansion_of',
              relationLabel: 'expansion',
              fromMetadata: false,
            })),
          },
        ]
      })
      await answer('Bastion', 'Different games')
      await waitFor(() => expect(screen.queryAllByRole('article')).toHaveLength(0))
      expect(review.history).toHaveLength(0)
      expect(
        request.mock.calls.find(([input]) => input.route === 'identity.dismiss')?.[0].body,
      ).toMatchObject({
        candidateIds: [],
        refusedPairs: [
          { baseWorkId: 1, childWorkId: 2 },
          { baseWorkId: 1, childWorkId: 3 },
        ],
      })
      fireEvent.click(screen.getByRole('button', { name: 'Refresh suggestions' }))
      await screen.findByText('Suggestions refreshed. Your previous answers are kept.')
      expect(screen.queryAllByRole('article')).toHaveLength(0)
      fireEvent.click(screen.getByRole('button', { name: 'Undo review decisions' }))
      await screen.findByRole('article', { name: 'Bastion proposal' })
      expect(request.mock.calls.find(([input]) => input.route === 'identity.undo')?.[0].body).toMatchObject({
        refusedPairs: [
          { baseWorkId: 1, childWorkId: 2 },
          { baseWorkId: 1, childWorkId: 3 },
        ],
      })
      await answer('Bastion', 'Same game')
      await screen.findByRole('article', { name: 'Bastion saved group' })
      expect(review.history).toHaveLength(2)
      expect(new Set(review.history.map((link) => link.actId)).size).toBe(1)
      expect(review.history.every((link) => link.kind === 'expansion_of' && link.parentWorkId === 1)).toBe(
        true,
      )
      await answer('Bastion', 'Separate again', true)
      await screen.findByRole('article', { name: 'Bastion proposal' })
      expect(review.history).toHaveLength(0)
    })
    it('retains only completed bulk acts for Undo when a later group conflicts', async () => {
      const { request } = setup(mode, (input) =>
        input.route === 'identity.link' && (input.body as { parentWorkId: number }).parentWorkId === 3
          ? { ok: false, status: 409, message: 'Second group changed' }
          : undefined,
      )
      await select('Bastion')
      await select('Prey 2006')
      await bulk('Merge 2 selected')
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
    it('leaving every child out disables grouping and making it the header restores inclusion', async () => {
      const { request } = setup(mode)
      let card = await open('Bastion')
      if (mode === 'desktop')
        fireEvent.click(within(card).getByRole('checkbox', { name: 'Include Bastion (GOG)' }))
      else {
        fireEvent.click(within(card).getByRole('button', { name: 'Bastion (GOG) · Included' }))
        fireEvent.click(screen.getByRole('button', { name: 'Leave out' }))
      }
      expect((within(card).getByRole('button', { name: 'Same game' }) as HTMLButtonElement).disabled).toBe(
        true,
      )
      expect(request.mock.calls.some(([input]) => input.route === 'identity.link')).toBe(false)
      if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Back to proposals' }))
      await promoteSecond('Bastion')
      card = await open('Bastion')
      expect((within(card).getByRole('button', { name: 'Same game' }) as HTMLButtonElement).disabled).toBe(
        false,
      )
      if (mode === 'desktop')
        expect(within(card).queryByRole('checkbox', { name: 'Include Bastion (GOG)' })).toBeNull()
      else expect(within(card).getByRole('button', { name: 'Bastion (GOG) · Header' })).toBeTruthy()
    })
    it('keeps expansion parents fixed and leaves member details independently available', async () => {
      const { onOpenGame, request } = setup(mode, (input, review) => {
        if (input.route === 'identity.get')
          return {
            ok: true,
            status: 200,
            data: {
              ...review,
              candidates: [],
              expansions: [
                {
                  base: { workId: 1, title: 'Bastion' },
                  members: [
                    {
                      work: { workId: 3, title: 'Prey 2006' },
                      kind: 'expansion_of',
                      relationLabel: null,
                      fromMetadata: true,
                    },
                  ],
                },
              ],
            },
          }
      })
      const card = await open('Bastion')
      if (mode === 'desktop') {
        expect(within(card).queryByRole('radio')).toBeNull()
        fireEvent.click(within(card).getByRole('button', { name: 'Choose Prey 2006' }))
        expect(within(card).getByRole('button', { name: 'Choose Bastion' }).textContent).toContain('Header')
        fireEvent.click(within(card).getByRole('button', { name: 'Details for Prey 2006' }))
      } else {
        fireEvent.click(within(card).getByRole('button', { name: 'Prey 2006 · Included' }))
        expect(screen.queryByRole('button', { name: 'Make header' })).toBeNull()
        fireEvent.click(screen.getByRole('button', { name: 'Open game' }))
      }
      expect(onOpenGame).toHaveBeenCalledWith(3)
      expect(request.mock.calls.some(([input]) => input.route === 'identity.link')).toBe(false)
    })
  })

it('desktop row body promotes while Details, include and radio remain separate actions', async () => {
  const { onOpenGame } = setup('desktop')
  const card = await screen.findByRole('article', { name: 'Bastion proposal' })
  const second = within(card).getByRole('button', { name: 'Choose Bastion (GOG)' })
  fireEvent.click(second.closest('.merge-row')!)
  expect((within(card).getAllByRole('radio')[1] as HTMLInputElement).checked).toBe(true)
  expect(onOpenGame).not.toHaveBeenCalled()
  fireEvent.click(within(card).getByRole('button', { name: 'Details for Bastion (Steam)' }))
  expect(onOpenGame).toHaveBeenCalledWith(1)
  expect((within(card).getAllByRole('radio')[1] as HTMLInputElement).checked).toBe(true)
  fireEvent.click(within(card).getByRole('checkbox', { name: 'Include Bastion (Steam)' }))
  expect((within(card).getAllByRole('radio')[1] as HTMLInputElement).checked).toBe(true)
  fireEvent.click(within(card).getAllByRole('radio')[0]!)
  expect((within(card).getAllByRole('radio')[0] as HTMLInputElement).checked).toBe(true)
  expect(onOpenGame).toHaveBeenCalledTimes(1)
})

it('desktop covers react to the display toggle without reloading review or cover state', async () => {
  const { client, request } = setup('desktop')
  const card = await screen.findByRole('article', { name: 'Bastion proposal' })
  const covers = [...card.querySelectorAll<HTMLElement>('.merge-cover')]
  const initial = covers.map((cover) => cover.style.getPropertyValue('--merge-dormancy'))
  expect(initial.every((value) => value.includes('saturate(0.22)'))).toBe(true)
  await waitFor(() =>
    expect(request.mock.calls.filter(([input]) => input.route === 'artworkState')).toHaveLength(4),
  )
  for (const value of ['false', 'true']) {
    act(() =>
      client.setQueryData(
        ['api', 'preferences.presentation.get', undefined],
        [{ preference: 'DimDormantCovers', value }],
      ),
    )
    await waitFor(() =>
      expect(covers.map((cover) => cover.style.getPropertyValue('--merge-dormancy'))).toEqual(
        value === 'false' ? ['none', 'none'] : initial,
      ),
    )
  }
  expect(request.mock.calls.filter(([input]) => input.route === 'identity.get')).toHaveLength(1)
  expect(request.mock.calls.filter(([input]) => input.route === 'artworkState')).toHaveLength(4)
})

it.each(['user', 'igdb', 'steam'])(
  'desktop queue rows use the authoritative %s artwork key without substituting a store capsule',
  async (provider) => {
    const artwork = vi.fn(
      async (_provider: string, _id: string, _width: number) => 'data:image/png;base64,iVBORw0KGgo=',
    )
    setup('desktop', (input) =>
      input.route === 'artworkState'
        ? {
            ok: true,
            status: 200,
            data: {
              revision: 'selected-art',
              current: {
                previewKey: {
                  provider: input.params?.workId === 1 ? provider : 'steam',
                  id: String(input.params?.workId),
                },
              },
            },
          }
        : undefined,
    )
    window.winnow.artwork = artwork
    const card = await screen.findByRole('article', { name: 'Bastion proposal' })
    await waitFor(() => expect(card.querySelectorAll('img')).toHaveLength(2))
    expect(artwork).toHaveBeenCalledWith(provider, '1', 600)
    expect(artwork).toHaveBeenCalledWith('steam', '2', 600)
    expect(artwork.mock.calls.filter((call) => call[1] === '1')).toHaveLength(1)
  },
)

it('desktop asks for covers only in visible sections and reuses them when the filter is cleared', async () => {
  function Filter() {
    const [, set] = useViewState('identity:queue-section', 'all')
    return <button onClick={() => set('stores')}>Stage filter</button>
  }
  const filter = render(<Filter />)
  fireEvent.click(screen.getByRole('button', { name: 'Stage filter' }))
  filter.unmount()
  const { request } = setup('desktop')
  await screen.findByRole('article', { name: 'Bastion proposal' })
  await waitFor(() =>
    expect(request.mock.calls.filter(([input]) => input.route === 'artworkState')).toHaveLength(2),
  )
  expect(
    request.mock.calls
      .filter(([input]) => input.route === 'artworkState')
      .map(([input]) => input.params?.workId),
  ).toEqual([1, 2])
  fireEvent.click(screen.getByRole('button', { name: 'All proposals' }))
  await screen.findByRole('article', { name: 'Prey 2006 proposal' })
  await waitFor(() =>
    expect(request.mock.calls.filter(([input]) => input.route === 'artworkState')).toHaveLength(4),
  )
  fireEvent.click(screen.getByRole('button', { name: 'Across stores' }))
  fireEvent.click(screen.getByRole('button', { name: 'All proposals' }))
  expect(request.mock.calls.filter(([input]) => input.route === 'artworkState')).toHaveLength(4)
})

it('desktop keyboard promotion keeps the cursor and row movement asks the viewport to follow', async () => {
  setup('desktop')
  await screen.findByRole('article', { name: 'Bastion proposal' })
  const rows = screen.getAllByRole('button', { name: /^Choose / })
  const follow = vi.fn()
  rows.forEach((row) => {
    row.scrollIntoView = follow
  })
  rows[0]!.focus()
  fireEvent.keyDown(rows[0]!, { key: 'ArrowDown' })
  expect(document.activeElement).toBe(rows[1])
  expect(follow).toHaveBeenCalledWith({ block: 'nearest' })
  fireEvent.keyDown(rows[1]!, { key: ' ' })
  expect(document.activeElement).toBe(rows[1])
  expect(
    (screen.getByRole('radio', { name: 'Make Bastion (GOG) the main game' }) as HTMLInputElement).checked,
  ).toBe(true)
  fireEvent.keyDown(rows[1]!, { key: 'End' })
  expect(document.activeElement).toBe(rows.at(-1))
  fireEvent.keyDown(rows.at(-1)!, { key: 'ArrowDown' })
  expect(document.activeElement).toBe(rows.at(-1))
  fireEvent.keyDown(rows.at(-1)!, { key: 'Home' })
  expect(document.activeElement).toBe(rows[0])
  const calls = follow.mock.calls.length
  rows[1]!.focus()
  expect(follow).toHaveBeenCalledTimes(calls)
})

it('desktop starts its keyboard cursor on the first pending row and an empty queue has no action target', async () => {
  const { client, request, review } = setup('desktop')
  await screen.findByRole('article', { name: 'Bastion proposal' })
  const queue = screen.getByRole('group', { name: 'Possible matches' }),
    rows = screen.getAllByRole('button', { name: /^Choose / })
  queue.focus()
  fireEvent.keyDown(queue, { key: 'ArrowDown' })
  expect(document.activeElement).toBe(rows[1])
  act(() => client.setQueryData(['api', 'identity.get', undefined], { ...review, candidates: [] }))
  await waitFor(() => expect(screen.queryAllByRole('article')).toHaveLength(0))
  queue.focus()
  for (const key of ['ArrowDown', 'ArrowUp', ' ', 's', 'd']) fireEvent.keyDown(queue, { key })
  expect(document.activeElement).toBe(queue)
  expect(
    request.mock.calls.filter(
      ([input]) => input.route === 'identity.link' || input.route === 'identity.dismiss',
    ),
  ).toHaveLength(0)
})

it('fullscreen Escape traverses member and confirmation layers before returning focus to the proposal', async () => {
  const { request } = setup('fullscreen')
  const card = await screen.findByRole('article', { name: 'Bastion proposal' })
  const proposal = within(card).getByRole('button')
  proposal.focus()
  fireEvent.click(proposal)
  fireEvent.click(screen.getByRole('button', { name: 'Bastion (GOG) · Included' }))
  expect(screen.getByRole('button', { name: 'Open game' })).toBe(document.activeElement)
  fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
  expect(screen.getByRole('button', { name: 'Same game' })).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'Same game' }))
  expect(screen.getByRole('dialog', { name: 'Group these entries?' })).toBeTruthy()
  fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
  expect(screen.getByRole('button', { name: 'Same game' })).toBeTruthy()
  fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
  await waitFor(() => expect(document.activeElement).toBe(proposal))
  expect(screen.queryByRole('dialog')).toBeNull()
  expect(request.mock.calls.some(([input]) => input.route === 'identity.link')).toBe(false)
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
