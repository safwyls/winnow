// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ApiRequest } from '../src/shared/bridge'
import type { Mode } from '../src/renderer/api/types'
import { IdentityTools } from '../src/renderer/features/parity-library'
import { MergeQueue } from '../src/renderer/features/parity-merge'
import {
  buildMergeCards,
  promoteMergeRow,
  type MergeReview,
} from '../src/renderer/features/parity-merge-model'
import { mergeReviewKey, refreshIdentityReview } from '../src/renderer/features/parity-merge-query'
import { clearViewState } from '../src/renderer/viewState'
import { mergeFixture } from './parity-merge-fixtures'

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
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
    'last-undo',
  ])
    clearViewState(`identity:${key}`)
  clearViewState('draft:identity-link')
})

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}

// Original refresh fixture: two separate works, Steam/PSN, 2011, Supergiant Games, Windows.
function sourcePair(title = 'Bastion', start = 1): MergeReview {
  const review = mergeFixture()
  const ids = [start, start + 1]
  review.revision = `r${start}`
  review.workspace.works = ids.map((id) => ({
    id,
    name: title,
    firstReleaseYear: 2011,
    publisher: 'Supergiant Games',
  }))
  review.workspace.releases = ids.map((id) => ({
    id: 100 + id,
    workId: id,
    name: title,
    platform: 'windows',
  }))
  review.workspace.ownerships = ids.map((id, index) => ({
    id,
    releaseId: id + 100,
    store: index ? 'psn' : 'steam',
  }))
  review.workspace.buckets = ids.map((id) => ({
    ownershipId: id,
    releaseId: id + 100,
    workId: id,
    resolvedWorkId: id,
    playtimeMinutes: 0,
    lastPlayedAt: null,
    bucket: 'never_played',
  }))
  review.candidates = [
    {
      id: start,
      leftReleaseId: start + 100,
      rightReleaseId: start + 101,
      score: 1,
      status: 'pending',
      signalsJson: JSON.stringify({
        band: 'Priority',
        title_similarity: 1,
        year_delta: 0,
        publisher_match: true,
      }),
    },
  ]
  return review
}

function harness(mode: Mode, initial?: MergeReview, failInitialRead = false) {
  let saved = initial ?? sourcePair()
  if (!initial) {
    saved.candidates = []
    saved.workspace.works = []
    saved.workspace.ownerships = []
    saved.workspace.releases = []
    saved.workspace.buckets = []
  }
  let preference = ''
  const pending: ReturnType<
    typeof deferred<{ ok: boolean; status: number; data?: object; message?: string }>
  >[] = []
  const request = vi.fn(async (input: ApiRequest) => {
    if (input.route === 'identity.get') {
      if (failInitialRead) {
        failInitialRead = false
        return { ok: false, status: 503, message: 'Initial read failed' }
      }
      return { ok: true, status: 200, data: structuredClone(saved) }
    }
    if (input.route === 'identity.refresh') {
      const pass = deferred<{ ok: boolean; status: number; data?: object; message?: string }>()
      pending.push(pass)
      return pass.promise
    }
    if (input.route === 'preferences.presentation.get')
      return { ok: true, status: 200, data: [{ preference: 'PreferredMergePlatform', value: preference }] }
    if (input.route === 'preferences.presentation.put') {
      preference = (input.body as { value: string }).value
      return { ok: true, status: 204 }
    }
    if (input.route === 'artworkState')
      return { ok: true, status: 200, data: { current: null, revision: 'art' } }
    throw new Error(`Unexpected mutation: ${input.route}`)
  })
  const cancelRequest = vi.fn(async () => true)
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, cancelRequest } })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const open = vi.fn()
  const page = (surface = mode) => (
    <QueryClientProvider client={client}>
      <div className={`mode-${surface}`}>
        <IdentityTools mode={surface} onOpenGame={open} />
      </div>
    </QueryClientProvider>
  )
  const view = render(page())
  return {
    request,
    cancelRequest,
    pending,
    client,
    view,
    page,
    open,
    setSaved: (review: MergeReview) => {
      saved = review
    },
    getSaved: () => saved,
  }
}

for (const mode of ['desktop', 'fullscreen'] as const)
  describe(`${mode} integrated original merge review contracts`, () => {
    it.each(['success', 'failure'] as const)(
      'keeps one pending refresh owner when an initial read failure recovers before sweep %s',
      async (outcome) => {
        const test = harness(mode, sourcePair(), true)
        expect((await screen.findByRole('alert')).textContent).toContain('Initial read failed')
        const unloadedRefresh = screen.getByRole('button', { name: 'Refresh suggestions' })
        expect(unloadedRefresh.closest('.feature-heading')).not.toBeNull()
        fireEvent.click(unloadedRefresh)
        await waitFor(() => expect(test.pending).toHaveLength(1))
        await act(() => refreshIdentityReview(test.client))
        await waitFor(() => expect(document.querySelector('.merge-controls')).not.toBeNull())
        const loadedRefresh = screen.getByRole('button', { name: 'Refresh suggestions' })
        expect(loadedRefresh.closest('.merge-controls')).not.toBeNull()
        expect(loadedRefresh).not.toBe(unloadedRefresh)
        expect((loadedRefresh as HTMLButtonElement).disabled).toBe(true)
        expect(screen.getByText('Checking your library for matches…')).toBeTruthy()
        expect(test.cancelRequest).not.toHaveBeenCalled()
        fireEvent.click(loadedRefresh)
        expect(test.pending).toHaveLength(1)
        await act(async () => {
          test.pending[0]!.resolve(
            outcome === 'success'
              ? { ok: true, status: 200, data: {} }
              : { ok: false, status: 503, message: 'Sweep failed' },
          )
        })
        if (outcome === 'failure') {
          await screen.findByText("Couldn't refresh suggestions. Choose Refresh suggestions to try again.")
          expect((loadedRefresh as HTMLButtonElement).disabled).toBe(false)
          fireEvent.click(loadedRefresh)
          await waitFor(() => expect(test.pending).toHaveLength(2))
          await act(async () => {
            test.pending[1]!.resolve({ ok: true, status: 200, data: {} })
          })
        }
        await screen.findByText('Suggestions refreshed. Your previous answers are kept.')
        expect((loadedRefresh as HTMLButtonElement).disabled).toBe(false)
        expect(
          (screen.getByRole('button', { name: 'Create a relationship' }) as HTMLButtonElement).disabled,
        ).toBe(false)
        expect(test.cancelRequest).not.toHaveBeenCalled()
        expect(test.getSaved().history).toEqual([])
      },
    )

    it('owns refresh beside review actions and reports idle, duplicate-blocked busy, failure and successful retry without answering', async () => {
      const test = harness(mode)
      await waitFor(() => expect(document.querySelector('.merge-controls')).not.toBeNull())
      const refresh = screen.getByRole('button', { name: 'Refresh suggestions' })
      await waitFor(() => expect((refresh as HTMLButtonElement).disabled).toBe(false))
      const controls = refresh.closest('.merge-controls')!
      expect(controls).not.toBeNull()
      const buttons = within(controls as HTMLElement).getAllByRole('button')
      if (mode === 'desktop') {
        expect(refresh.textContent).toBe('')
        expect(refresh.querySelector('svg')).not.toBeNull()
        expect(buttons.indexOf(refresh) + 1).toBe(
          buttons.findIndex((button) => button.textContent === 'Merge selected'),
        )
      } else
        expect(buttons.slice(0, 4).map((button) => button.textContent)).toEqual([
          'Sort · Strongest match',
          'Kind · All proposals',
          'Preferred platform · None',
          'Refresh suggestions',
        ])
      expect(document.querySelector('.merge-refresh-status')).toBeNull()
      test.setSaved(sourcePair())
      fireEvent.click(refresh)
      await waitFor(() => expect(test.pending).toHaveLength(1))
      expect((refresh as HTMLButtonElement).disabled).toBe(true)
      expect(document.querySelector('.merge-refresh-status')?.textContent).toBe(
        'Checking your library for matches…',
      )
      expect(document.querySelector('.merge-refresh-status')?.getAttribute('aria-live')).toBe('polite')
      fireEvent.click(refresh)
      expect(test.pending).toHaveLength(1)
      await act(async () => {
        test.pending[0]!.resolve({ ok: false, status: 503, message: 'Test failure' })
      })
      await screen.findByText("Couldn't refresh suggestions. Choose Refresh suggestions to try again.")
      expect((refresh as HTMLButtonElement).disabled).toBe(false)
      refresh.focus()
      fireEvent.click(refresh)
      await waitFor(() => expect(test.pending).toHaveLength(2))
      await act(async () => {
        test.pending[1]!.resolve({ ok: true, status: 200, data: { truncated: false } })
      })
      await screen.findByText('Suggestions refreshed. Your previous answers are kept.')
      expect((refresh as HTMLButtonElement).disabled).toBe(false)
      expect(test.client.getQueryData<MergeReview>(mergeReviewKey)?.candidates).toHaveLength(1)
      expect(buildMergeCards(test.client.getQueryData<MergeReview>(mergeReviewKey)!)).toHaveLength(1)
      expect(test.getSaved().history).toEqual([])
      expect(
        test.request.mock.calls
          .filter(([input]) => input.route.startsWith('identity.'))
          .map(([input]) => input.route),
      ).toEqual(['identity.get', 'identity.refresh', 'identity.refresh', 'identity.get'])
      test.view.unmount()
      render(test.page(mode === 'desktop' ? 'fullscreen' : 'desktop'))
      await screen.findByRole('group', { name: 'Possible matches' })
      expect(screen.queryByText('nothing waiting')).toBeNull()
      expect(test.request.mock.calls.filter(([input]) => input.route === 'identity.get')).toHaveLength(2)
    })

    it('publishes Bastion to Hades after an automatic sweep with the same one-proposal count', async () => {
      const test = harness(mode, sourcePair())
      await screen.findByRole('group', { name: 'Possible matches' })
      await waitFor(() => expect(document.querySelector('.merge-card')?.textContent).toContain('Bastion'))
      const fresh = sourcePair('Hades', 3)
      test.setSaved(fresh)
      await act(() => refreshIdentityReview(test.client))
      await waitFor(() => expect(document.querySelector('.merge-card')?.textContent).toContain('Hades'))
      expect(document.querySelector('.merge-card')?.textContent).not.toContain('Bastion')
      expect(test.client.getQueryData<MergeReview>(mergeReviewKey)?.candidates).toHaveLength(1)
      expect(fresh.history).toEqual([])
      expect(test.request.mock.calls.some(([input]) => input.route === 'identity.refresh')).toBe(false)
    })

    it('unmounting the integrated page cancels only its pending sweep and ignores late completion', async () => {
      const test = harness(mode, sourcePair())
      await waitFor(() => expect(document.querySelector('.merge-controls')).not.toBeNull())
      const refresh = screen.getByRole('button', { name: 'Refresh suggestions' })
      await waitFor(() => expect((refresh as HTMLButtonElement).disabled).toBe(false))
      fireEvent.click(refresh)
      await waitFor(() => expect(test.pending).toHaveLength(1))
      const invocation = test.request.mock.calls.find(([input]) => input.route === 'identity.refresh')![0]
      const reads = test.request.mock.calls.filter(([input]) => input.route === 'identity.get').length
      test.view.unmount()
      expect(test.cancelRequest).toHaveBeenCalledExactlyOnceWith(invocation.requestId)
      render(<button autoFocus>Another page</button>)
      await act(async () => {
        test.pending[0]!.resolve({ ok: true, status: 200, data: {} })
      })
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Another page' }))
      expect(test.request.mock.calls.filter(([input]) => input.route === 'identity.get')).toHaveLength(reads)
    })

    it('applies and remembers Steam while exposing the current platform and restoring the picker origin', async () => {
      const test = harness(mode)
      if (mode === 'desktop') {
        const select = await screen.findByRole('combobox', { name: 'Preferred main platform' })
        await waitFor(() => expect((select as HTMLSelectElement).disabled).toBe(false))
        expect(select.getAttribute('aria-description')).toBe('Preferred platform · None')
        select.focus()
        fireEvent.change(select, { target: { value: 'steam' } })
        await waitFor(() =>
          expect(select.getAttribute('aria-description')).toBe('Preferred platform · Steam'),
        )
        expect((within(select).getByRole('option', { name: 'Steam' }) as HTMLOptionElement).selected).toBe(
          true,
        )
        expect(document.activeElement).toBe(select)
      } else {
        const trigger = await screen.findByRole('button', { name: 'Preferred platform · None' })
        await waitFor(() => expect((trigger as HTMLButtonElement).disabled).toBe(false))
        trigger.focus()
        fireEvent.click(trigger)
        const sheet = screen.getByRole('dialog', { name: 'Preferred platform for pending headers' })
        const none = within(sheet).getByRole('button', { name: 'None' })
        expect(document.activeElement).toBe(none)
        fireEvent.keyDown(none, { key: 'ArrowDown' })
        expect(document.activeElement).toBe(within(sheet).getByRole('button', { name: 'Steam' }))
        fireEvent.click(document.activeElement!)
        await screen.findByRole('button', { name: 'Preferred platform · Steam' })
        await waitFor(() => expect(document.activeElement).toBe(trigger))
        expect(screen.queryByRole('dialog')).toBeNull()
        fireEvent.click(trigger)
        expect(screen.getByRole('button', { name: 'Steam' }).getAttribute('aria-pressed')).toBe('true')
        expect(document.querySelectorAll('[data-merge-sheet-glyph]')).toHaveLength(2)
        fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      }
      expect(
        test.request.mock.calls
          .filter(([input]) => input.route === 'preferences.presentation.put')
          .map(([input]) => [input.params, input.body]),
      ).toEqual([[{ preference: 'PreferredMergePlatform' }, { value: 'steam' }]])
      test.view.unmount()
      render(test.page())
      if (mode === 'desktop')
        expect(
          ((await screen.findByRole('combobox', { name: 'Preferred main platform' })) as HTMLSelectElement)
            .value,
        ).toBe('steam')
      else await screen.findByRole('button', { name: 'Preferred platform · Steam' })
    })
  })

it('keeps the reusable queue usable without a refresh owner', async () => {
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request: vi.fn(async () => ({ ok: true, status: 200, data: [] })) },
  })
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MergeQueue review={sourcePair()} onReview={() => {}} />
    </QueryClientProvider>,
  )
  await screen.findByRole('group', { name: 'Possible matches' })
  expect(screen.queryByRole('button', { name: 'Refresh suggestions' })).toBeNull()
  expect(screen.getByRole('button', { name: 'Merge selected' })).toBeTruthy()
})

it('refuses further promotion after the original Bastion Steam/Epic row fixture is resolved', () => {
  const review = sourcePair()
  ;(review.workspace.ownerships as { store: string }[])[1]!.store = 'epic'
  const original = buildMergeCards(review)[0]!
  const promoted = promoteMergeRow(original, 2)
  expect(promoted.parent).toBe(2)
  const resolved = { ...original, actId: 123 }
  expect(promoteMergeRow(resolved, 2)).toBe(resolved)
  expect(resolved.parent).toBe(1)
})
