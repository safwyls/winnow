// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { LibraryTools } from '../src/renderer/features/LibraryTools'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'
import type { Mode } from '../src/renderer/api/types'

const list = {
  id: 30,
  name: 'Quiet evenings',
  description: '',
  isLive: true,
  releaseIds: [100],
  revision: 'list-1',
  filter: { installed: false, genreIds: [9], customFutureField: 'keep' },
}
const workspace = {
  works: [
    { id: 1, name: 'Original game' },
    { id: 2, name: 'An expansion' },
  ],
  releases: [
    { id: 100, workId: 1 },
    { id: 200, workId: 2 },
  ],
  ownerships: [{ releaseId: 100, store: 'steam' }],
  facets: [{ id: 9, kind: 'genre', name: 'Adventure', slug: 'adventure' }],
}
const review = {
  revision: 'identity-1',
  hasCompletedSweep: true,
  candidates: [],
  history: [],
  expansions: [
    {
      base: { workId: 1, title: 'Original game' },
      members: [
        {
          work: { workId: 2, title: 'An expansion' },
          kind: 'expansion_of',
          relationLabel: 'expansion',
          fromMetadata: true,
        },
      ],
    },
  ],
  workspace,
}
function setup(mode: Mode, handler?: (request: ApiRequest) => unknown) {
  const request = vi.fn(async (input: ApiRequest) => {
    const override = await handler?.(input)
    if (override !== undefined) return override
    const data =
      input.route === 'library.get'
        ? { games: [], lists: [list] }
        : input.route === 'library.workspace'
          ? workspace
          : input.route === 'identity.get'
            ? review
            : ['manual.get', 'metadata.search'].includes(input.route)
              ? []
              : { revision: 'identity-2', actId: 14 }
    return { ok: true, status: 200, data }
  })
  const chooseManualExecutable = vi.fn().mockResolvedValue('C:\\Games\\Small Adventure.exe')
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, chooseManualExecutable } })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const view = render(
    <QueryClientProvider client={client}>
      <LibraryTools mode={mode} />
    </QueryClientProvider>,
  )
  return { request, client, view, chooseManualExecutable }
}
afterEach(() => {
  cleanup()
  for (const key of [
    'desktop:library-tools:tab',
    'fullscreen:library-tools:tab',
    'draft:list-filter:30',
    'draft:list:new',
    'draft:manual:new',
    'draft:manual:77',
    'draft:identity-link',
    'identity:last-undo',
    'desktop:manual:editing',
    'fullscreen:manual:editing',
  ])
    clearViewState(key)
})

it.each(['desktop', 'fullscreen'] as const)(
  'returns focus to the live-filter action when its draft is canceled in %s',
  async (mode) => {
    setup(mode)
    const edit = await screen.findByRole('button', { name: 'Edit live filters' })
    fireEvent.click(edit)
    expect(document.activeElement).toBe(screen.getByLabelText('Title contains'))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel filter changes' }))
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Edit live filters' }))
  },
)

it.each(['desktop', 'fullscreen'] as const)(
  'returns focus to list editing after cancel without submitting changes in %s',
  async (mode) => {
    const { request } = setup(mode)
    const edit = await screen.findByRole('button', { name: 'Edit' })
    fireEvent.click(edit)
    expect(document.activeElement).toBe(screen.getAllByLabelText('List name')[1])
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(document.activeElement).toBe(edit)
    expect(request.mock.calls.some(([input]) => input.route === 'list.update')).toBe(false)
  },
)

describe.each<Mode>(['desktop', 'fullscreen'])('%s library parity', (mode) => {
  it('edits live filters, preserves all existing facets, and submits the captured revision', async () => {
    const { request } = setup(mode)
    fireEvent.click(await screen.findByRole('button', { name: 'Edit live filters' }))
    expect((screen.getByLabelText('Installation') as HTMLSelectElement).value).toBe('false')
    fireEvent.change(screen.getByLabelText('Title contains'), { target: { value: 'Moon' } })
    fireEvent.change(screen.getByLabelText('Update status'), { target: { value: 'true' } })
    fireEvent.change(screen.getByLabelText('Released from'), { target: { value: '2015' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save live filters' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'list.filter',
        params: { listId: 30 },
        body: {
          expectedRevision: 'list-1',
          filter: { ...list.filter, search: 'Moon', hasUnread: true, yearFrom: 2015 },
        },
      }),
    )
  })
  it('reorders handpicked list members with revision checks and can remove an edition', async () => {
    const { request } = setup(mode, (input) =>
      input.route === 'library.get'
        ? {
            ok: true,
            status: 200,
            data: { games: [], lists: [{ ...list, isLive: false, releaseIds: [100, 200] }] },
          }
        : undefined,
    )
    fireEvent.click(await screen.findByText('Games in this list · 2'))
    expect(
      (screen.getByRole('button', { name: 'Move Edition 100 earlier' }) as HTMLButtonElement).disabled,
    ).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Move Edition 200 earlier' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'list.order',
        params: { listId: 30 },
        body: { expectedRevision: 'list-1', releaseIds: [200, 100] },
      }),
    )
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Remove Edition 200 from Quiet evenings' }) as HTMLButtonElement)
          .disabled,
      ).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Remove Edition 200 from Quiet evenings' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'list.member.remove',
        params: { listId: 30 },
        body: { expectedRevision: 'list-1', releaseIds: [200] },
      }),
    )
  })
  it('keeps a conflicting live-filter draft until explicitly rebased on the refreshed list', async () => {
    let revision = 'list-1'
    const { request } = setup(mode, (input) => {
      if (input.route === 'library.get')
        return { ok: true, status: 200, data: { games: [], lists: [{ ...list, revision }] } }
      if (input.route === 'list.filter') {
        revision = 'list-2'
        return { ok: false, status: 409, message: 'The list changed.' }
      }
    })
    fireEvent.click(await screen.findByRole('button', { name: 'Edit live filters' }))
    fireEvent.change(screen.getByLabelText('Title contains'), { target: { value: 'Unsent filter' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save live filters' }))
    await screen.findByText('This list changed elsewhere. Your filter choices are preserved.')
    expect((screen.getByLabelText('Title contains') as HTMLInputElement).value).toBe('Unsent filter')
    expect((screen.getByRole('button', { name: 'Save live filters' }) as HTMLButtonElement).disabled).toBe(
      true,
    )
    await waitFor(() =>
      expect(
        (screen.getByRole('button', { name: 'Keep my filters for the next save' }) as HTMLButtonElement)
          .disabled,
      ).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Keep my filters for the next save' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save live filters' }))
    await waitFor(() =>
      expect(
        request.mock.calls.filter(([input]) => input.route === 'list.filter').at(-1)?.[0].body,
      ).toMatchObject({ expectedRevision: 'list-2', filter: { search: 'Unsent filter' } }),
    )
  })
  it('adds executable tracking and metadata identifiers to a manual game', async () => {
    const { request, chooseManualExecutable } = setup(mode)
    fireEvent.click(screen.getByRole('button', { name: 'Manual games' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Add a game' }))
    fireEvent.click(screen.getByRole('button', { name: 'Choose executable' }))
    await waitFor(() =>
      expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Small Adventure'),
    )
    expect(chooseManualExecutable).toHaveBeenCalledOnce()
    fireEvent.change(screen.getByLabelText('Installation folder'), { target: { value: 'C:\\Games' } })
    fireEvent.change(screen.getByLabelText('IGDB ID'), { target: { value: '451' } })
    fireEvent.change(screen.getByLabelText('Steam app ID'), { target: { value: '480' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save game' }))
    await waitFor(() =>
      expect(request.mock.calls.find(([input]) => input.route === 'manual.create')?.[0].body).toMatchObject({
        title: 'Small Adventure',
        executablePath: 'C:\\Games\\Small Adventure.exe',
        installPath: 'C:\\Games',
        igdbId: 451,
        steamAppId: '480',
      }),
    )
  })
  it('refreshes a manual entry before editing and preserves corrected identifiers across a conflict', async () => {
    let conflicted = false
    const entry = {
      ownershipId: 77,
      workId: 7,
      releaseId: 70,
      title: 'Old title',
      revision: 'manual-1',
      igdbMappingRevision: 1,
      igdbId: 33,
      steamAppId: '123',
    }
    const { request } = setup(mode, (input) => {
      if (input.route === 'manual.get')
        return {
          ok: true,
          status: 200,
          data: [
            {
              ...entry,
              revision: conflicted ? 'manual-3' : entry.revision,
              igdbMappingRevision: conflicted ? 2 : 1,
            },
          ],
        }
      if (input.route === 'manual.detail')
        return {
          ok: true,
          status: 200,
          data: { ...entry, title: 'Chosen title', revision: 'manual-2', igdbMappingRevision: 2, igdbId: 44 },
        }
      if (input.route === 'manual.update') {
        conflicted = true
        return { ok: false, status: 409, message: 'The saved title changed while you were editing.' }
      }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Manual games' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Edit game' }))
    await waitFor(() =>
      expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Chosen title'),
    )
    expect((screen.getByLabelText('IGDB ID') as HTMLInputElement).value).toBe('44')
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Corrected title' } })
    fireEvent.change(screen.getByLabelText('IGDB ID'), { target: { value: '66' } })
    fireEvent.change(screen.getByLabelText('Steam app ID'), { target: { value: '456' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save game' }))
    await screen.findByText('The saved title changed while you were editing.')
    expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Corrected title')
    expect((screen.getByLabelText('IGDB ID') as HTMLInputElement).value).toBe('66')
    expect(request.mock.calls.find(([input]) => input.route === 'manual.update')?.[0].body).toMatchObject({
      expectedRevision: 'manual-2',
      expectedIgdbMappingRevision: 2,
      igdbId: 66,
      steamAppId: '456',
    })
    expect((screen.getByRole('button', { name: 'Save game' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(await screen.findByRole('button', { name: 'Keep my draft for the next save' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save game' }))
    await waitFor(() =>
      expect(
        request.mock.calls.filter(([input]) => input.route === 'manual.update').at(-1)?.[0].body,
      ).toMatchObject({
        expectedRevision: 'manual-3',
        expectedIgdbMappingRevision: 2,
        igdbId: 66,
        steamAppId: '456',
      }),
    )
  })
  it('reviews and confirms expansion relationships without merging their identity', async () => {
    const { request } = setup(mode)
    fireEvent.click(screen.getByRole('button', { name: 'Identity review' }))
    if (mode === 'fullscreen') fireEvent.click(await screen.findByRole('button', { name: /Original game · 2 entries/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Review relationship…' }))
    expect((screen.getByLabelText('Relationship') as HTMLSelectElement).value).toBe('expansion_of')
    expect(request.mock.calls.some(([input]) => input.route === 'identity.link')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Confirm relationship' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'identity.link',
        params: undefined,
        body: {
          expectedRevision: 'identity-1',
          parentWorkId: 1,
          childWorkIds: [2],
          kind: 'expansion_of',
          relationLabel: 'expansion',
          rejectedCandidateIds: [],
          refusedPairs: [],
        },
      }),
    )
    fireEvent.click(await screen.findByRole('button', { name: 'Undo last decision' }))
    await waitFor(() =>
      expect(request.mock.calls.find(([input]) => input.route === 'identity.undo')?.[0].body).toMatchObject({
        expectedRevision: 'identity-2',
        actIds: [14],
        candidateIds: [],
        refusedPairs: [],
      }),
    )
  })
  it('requires a named confirmation before separating an existing relationship', async () => {
    const { request } = setup(mode, (input) =>
      input.route === 'identity.get'
        ? {
            ok: true,
            status: 200,
            data: {
              ...review,
              expansions: [],
              history: [
                {
                  id: 44,
                  actId: 12,
                  parentWorkId: 1,
                  childWorkId: 2,
                  kind: 'expansion_of',
                  appliedAt: '2026-09-01T00:00:00Z',
                },
              ],
            },
          }
        : undefined,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Identity review' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Separate…' }))
    expect(request.mock.calls.some(([input]) => input.route === 'identity.separate')).toBe(false)
    const confirmation = screen.getByText(/Separate An expansion from Original game/).parentElement!
    fireEvent.click(within(confirmation).getByRole('button', { name: 'Separate games' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'identity.separate',
        params: { childWorkId: 2 },
        body: { expectedLinkId: 44 },
      }),
    )
  })
  it('keeps the exact undo decision after conflict and rebases it only after an explicit successful refresh', async () => {
    let revision = 'identity-1'
    let undoAttempts = 0
    let refreshFails = false
    const { request } = setup(mode, (input) => {
      if (input.route === 'identity.get')
        return refreshFails
          ? { ok: false, status: 503, message: 'Review unavailable' }
          : { ok: true, status: 200, data: { ...review, revision } }
      if (input.route === 'identity.link') {
        revision = 'identity-other-client'
        return { ok: true, status: 200, data: { revision: 'identity-2', actId: 14 } }
      }
      if (input.route === 'identity.undo') {
        undoAttempts++
        return undoAttempts === 1
          ? { ok: false, status: 409, message: 'Review changed' }
          : { ok: true, status: 200, data: { revision: 'identity-3' } }
      }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Identity review' }))
    if (mode === 'fullscreen') fireEvent.click(await screen.findByRole('button', { name: /Original game · 2 entries/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Review relationship…' }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirm relationship' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Undo last decision' }))
    await screen.findByRole('button', { name: 'Refresh relationships' })
    expect(request.mock.calls.find(([input]) => input.route === 'identity.undo')?.[0].body).toMatchObject({
      expectedRevision: 'identity-2',
      actIds: [14],
      candidateIds: [],
      refusedPairs: [],
    })
    refreshFails = true
    fireEvent.click(screen.getByRole('button', { name: 'Refresh relationships' }))
    await screen.findByText(/Review unavailable/)
    expect(screen.getByRole('button', { name: 'Refresh relationships' })).toBeTruthy()
    expect(undoAttempts).toBe(1)
    refreshFails = false
    fireEvent.click(screen.getByRole('button', { name: 'Refresh relationships' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Refresh relationships' })).toBeNull())
    fireEvent.click(screen.getByRole('button', { name: 'Undo last decision' }))
    await screen.findByText('Decision undone.')
    expect(
      request.mock.calls.filter(([input]) => input.route === 'identity.undo')[1]?.[0].body,
    ).toMatchObject({
      expectedRevision: 'identity-other-client',
      actIds: [14],
      candidateIds: [],
      refusedPairs: [],
    })
    expect(undoAttempts).toBe(2)
  })
  it('reports suggestion refresh busy, failure, retry and truncated completion without accepting a match', async () => {
    let finish!: (value: unknown) => void
    let completed = false
    const { request } = setup(mode, (input) => {
      if (input.route === 'identity.refresh')
        return new Promise((resolve) => {
          finish = resolve
        })
      if (input.route === 'identity.get' && completed)
        return {
          ok: true,
          status: 200,
          data: {
            ...review,
            candidates: [{ id: 7, leftReleaseId: 100, rightReleaseId: 200, status: 'pending' }],
          },
        }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Identity review' }))
    const refresh = await screen.findByRole('button', { name: 'Refresh suggestions' })
    await waitFor(() => expect((refresh as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(refresh)
    await screen.findByText('Checking your library for matches…')
    expect((refresh as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(refresh)
    expect(request.mock.calls.filter(([input]) => input.route === 'identity.refresh')).toHaveLength(1)
    finish({ ok: false, status: 503, message: 'Could not check suggestions' })
    await screen.findByText("Couldn't refresh suggestions. Choose Refresh suggestions to try again.")
    expect((refresh as HTMLButtonElement).disabled).toBe(false)
    fireEvent.click(refresh)
    await screen.findByText('Checking your library for matches…')
    completed = true
    finish({ ok: true, status: 200, data: { truncated: true } })
    await screen.findByText('Suggestions refreshed. Choose Refresh suggestions again to check more matches.')
    expect(await screen.findAllByRole('article', { name: 'Original game proposal' })).toHaveLength(2)
    expect((refresh as HTMLButtonElement).disabled).toBe(false)
    expect(
      request.mock.calls.some(([input]) => ['identity.link', 'identity.dismiss'].includes(input.route)),
    ).toBe(false)
  })
})
