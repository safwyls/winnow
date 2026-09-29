// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Details, EntryActions } from '../src/renderer/features/Details'
import type { GameEntry, LibraryGame, Workspace } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'
import { clearViewState } from '../src/renderer/viewState'

vi.mock('../src/renderer/components/Artwork', () => ({ Artwork: () => null }))
const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  clearViewState('details:1:refetch')
})
const entry: GameEntry = {
  ownershipId: 1,
  releaseId: 10,
  workId: 1,
  title: 'Fez',
  store: 'steam',
  installed: true,
  playtimeMinutes: 0,
}
const workspace: Workspace = {
  preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'AdultsOnly' },
  externalIds: [
    { releaseId: 10, provider: 'steam', providerId: '620' },
    { releaseId: 10, provider: 'gog', providerId: '1971477531' },
    { releaseId: 10, provider: 'epic', providerId: 'catalog' },
  ],
  epicLaunchKeys: { catalog: { namespace: 'ns', catalogItemId: 'catalog', artifactId: 'Bluebird' } },
  pluginActions: {},
  works: [{ id: 1, name: 'Fez', igdbId: 1942 }],
}
function mount(
  ui: React.ReactNode,
  entries = [entry],
  facts = workspace,
  details: Record<string, unknown> = {},
  refetchOutcome = 1,
) {
  const game: LibraryGame = { workId: 1, title: 'Fez', bucket: 'never_played', playtimeMinutes: 0, entries }
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      input.route === 'library.get'
        ? { games: [game], lists: [] }
        : input.route === 'library.workspace'
          ? facts
          : input.route === 'actions.execute'
            ? 0
            : input.route === 'game.refetch'
              ? { outcome: refetchOutcome }
              : input.route === 'journal.preferences.get'
                ? { promptAfterPlay: true }
                : {
                    workId: 1,
                    events: [],
                    sessions: {},
                    journalEntries: [],
                    ratings: [],
                    achievements: [],
                    ...details,
                  },
  }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, artwork: vi.fn().mockResolvedValue(null), openExternal: vi.fn() },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  return { request, ...render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>) }
}

describe.each(['desktop', 'fullscreen'] as const)('original game actions in %s details', (mode) => {
  it.each(['steam', 'gog', 'epic'])(
    'dispatches the %s play or install command using its ownership and a unique operation',
    async (store) => {
      for (const installed of [true, false]) {
        const current = { ...entry, store, installed }
        const { request, unmount } = mount(<Details presentation="avalon" workId={1} mode={mode} />, [
          current,
        ])
        const action = installed ? 'Play' : 'Install'
        fireEvent.click(await screen.findByRole('button', { name: action }))
        await waitFor(() =>
          expect(request.mock.calls.some(([input]) => input.route === 'actions.execute')).toBe(true),
        )
        const call = request.mock.calls.find(([input]) => input.route === 'actions.execute')![0]
        expect(call.params).toEqual({ ownershipId: 1 })
        expect(call.body).toEqual({ operationId: expect.stringMatching(/^[0-9a-f-]{36}$/), action })
        expect(request.mock.calls.filter(([input]) => input.route === 'actions.execute')).toHaveLength(1)
        unmount()
      }
    },
  )
  it('offers the viable copy instead of the installed offline copy', async () => {
    const waiting = { ...entry, ownershipId: 2, installed: false }
    const facts = {
      ...workspace,
      buckets: [
        {
          ownershipId: 1,
          resolvedWorkId: 1,
          lifecycle: { status: 5, confidence: 0.98, reason: 'IGDB reports offline status.' },
        },
      ],
    }
    const { request } = mount(
      <Details presentation="avalon" workId={1} mode={mode} />,
      [entry, waiting],
      facts,
    )
    fireEvent.click(await screen.findByRole('button', { name: 'Install' }))
    await waitFor(() =>
      expect(request.mock.calls.find(([input]) => input.route === 'actions.execute')?.[0].params).toEqual({
        ownershipId: 2,
      }),
    )
  })
  it('renders attributed reception and lifecycle evidence in the original reading surfaces', async () => {
    const facts = {
      ...workspace,
      buckets: [
        {
          ownershipId: 1,
          resolvedWorkId: 1,
          game: {
            lifecycle: {
              status: 6,
              confidence: 0.9,
              reason: 'Steam confirms the listing was removed.',
              isExemptFromDerelict: true,
            },
          },
        },
      ],
    }
    const ratings = [
      { source: 'igdb_users', score: 83, ratingCount: 10, hasFigure: true },
      { source: 'igdb_critics', score: 91, ratingCount: 5, hasFigure: true },
      { source: 'steam', score: 96, ratingCount: 200, label: 'Overwhelmingly Positive', hasFigure: true },
    ]
    mount(<Details presentation="avalon" workId={1} mode={mode} />, [entry], facts, { ratings })
    expect(
      await screen.findByText(
        'Delisted · 90% confidence. Steam confirms the listing was removed. Kept out of Derelict by your choice.',
      ),
    ).toBeTruthy()
    if (mode === 'fullscreen') fireEvent.click(screen.getByRole('button', { name: 'Read more →' }))
    expect(screen.getAllByLabelText('Reception').at(-1)!.textContent).toBe(
      'IGDB USERS 83 / 10 ratings · IGDB CRITICS 91 / 5 critic scores · STEAM 96% / 200 reviews',
    )
    expect(
      screen.getAllByLabelText(/Overwhelmingly Positive on Steam: 96% positive, from 200 reviews./),
    ).toHaveLength(mode === 'desktop' ? 2 : 1)
  })
  it.each([0, 1])('keeps refetch outcome %s in the More menu across close and reopen', async (outcome) => {
    const { request } = mount(
      <Details presentation="avalon" workId={1} mode={mode} />,
      [entry],
      workspace,
      {},
      outcome,
    )
    const more = await screen.findByRole('button', { name: 'More' })
    fireEvent.click(more)
    expect(screen.queryByText(/Metadata updated\.|Checked\. Nothing new/)).toBeNull()
    const reads = request.mock.calls.filter(([input]) => input.route === 'game.details').length
    fireEvent.click(screen.getByRole('button', { name: 'Refetch metadata' }))
    await screen.findByText(outcome === 0 ? 'Metadata updated.' : 'Checked. Nothing new from the sources.')
    await waitFor(() =>
      expect(request.mock.calls.filter(([input]) => input.route === 'game.details')).toHaveLength(
        reads + (outcome === 0 ? 1 : 0),
      ),
    )
    fireEvent.click(more)
    fireEvent.click(more)
    expect(
      screen.getByText(outcome === 0 ? 'Metadata updated.' : 'Checked. Nothing new from the sources.'),
    ).toBeTruthy()
  })
})

it('keeps the unknown installation state out of both the action and chip', () => {
  mount(<EntryActions entry={{ ...entry, installed: null } as unknown as GameEntry} workspace={workspace} />)
  expect(screen.queryByRole('button', { name: /^(Play|Install)$/ })).toBeNull()
  expect(document.body.textContent).not.toMatch(/Installed|Not installed/)
})

it.each([true, false])(
  'explains a missing Epic key when installed is %s without inventing an action',
  (installed) => {
    mount(
      <EntryActions
        entry={{ ...entry, store: 'epic', installed }}
        workspace={{ ...workspace, epicLaunchKeys: {} }}
      />,
    )
    expect(screen.queryByRole('button', { name: /^(Play|Install)$/ })).toBeNull()
    expect(
      screen.getByText('Winnow does not yet hold the identifier this store needs to reach this game.'),
    ).toBeTruthy()
  },
)

it('does not offer GOG management without a valid product identity', () => {
  mount(<EntryActions entry={{ ...entry, store: 'gog' }} workspace={{ ...workspace, externalIds: [] }} />)
  expect(screen.queryByRole('button', { name: /Manage/ })).toBeNull()
})
