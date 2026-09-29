// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { gameLinks } from '../src/renderer/api/gameLinks'
import { Details, GameLinks } from '../src/renderer/features/Details'
import type { LibraryGame, Workspace } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'

vi.mock('../src/renderer/components/Artwork', () => ({ Artwork: () => null }))
afterEach(cleanup)

const game: LibraryGame = {
  workId: 1,
  title: 'Test game',
  bucket: 'never_played',
  playtimeMinutes: 0,
  entries: [
    {
      ownershipId: 3,
      releaseId: 2,
      workId: 1,
      title: 'Test game',
      store: 'steam',
      installed: false,
      playtimeMinutes: 0,
    },
  ],
}
const workspace: Workspace = {
  preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'Mature' },
  externalIds: [{ releaseId: 2, provider: 'steam', providerId: '10' }],
  works: [{ id: 1, name: 'Test game', igdbId: 1942 }],
  epicLaunchKeys: {},
  pluginActions: {},
}
const expected = [
  'steam://nav/games/details/10',
  'https://store.steampowered.com/app/10/',
  'https://store.steampowered.com/news/app/10',
  'https://steamdb.info/app/10/',
  'https://www.steamgriddb.com/steam/10',
  'https://www.igdb.com/g/1hy',
]

describe('game destinations', () => {
  it.each([
    [1942, '1hy'],
    [233, '6h'],
    [35, 'z'],
    [36, '10'],
  ])('uses IGDB identity %s as a numeric short reference', (igdbId, shortId) => {
    const links = gameLinks(game, {
      ...workspace,
      works: [{ id: 1, name: 'A different display title', igdbId: Number(igdbId) }],
    })
    expect(links.find((link) => link.label === 'IGDB')?.url).toBe(`https://www.igdb.com/g/${shortId}`)
  })
  it.each([null, 0, -1])('omits an invalid IGDB identity: %s', (igdbId) => {
    expect(
      gameLinks(game, { ...workspace, works: [{ id: 1, name: 'Unmatched', igdbId }] }).some(
        (link) => link.label === 'IGDB',
      ),
    ).toBe(false)
  })
  it('retains deduplicated Steam references when the installed copy is from GOG', () => {
    const entries = [
      { ...game.entries[0], store: 'gog', ownershipId: 7, releaseId: 8, installed: true },
      game.entries[0],
      { ...game.entries[0], ownershipId: 9 },
    ]
    const links = gameLinks({ ...game, entries }, workspace)
    expect(links.filter((link) => link.label === 'SteamDB')).toEqual([
      { label: 'SteamDB', url: 'https://steamdb.info/app/10/' },
    ])
    expect(links.filter((link) => link.label === 'SteamGridDB')).toEqual([
      { label: 'SteamGridDB', url: 'https://www.steamgriddb.com/steam/10' },
    ])
  })
  it('offers GOG browse navigation and omits invented Epic destinations', () => {
    expect(
      gameLinks(
        { ...game, entries: [{ ...game.entries[0], store: 'gog' }] },
        {
          ...workspace,
          works: [],
          externalIds: [{ releaseId: 2, provider: 'gog', providerId: '1971477531' }],
        },
      ),
    ).toEqual([{ label: 'Show in GOG Galaxy', url: 'goggalaxy://opengameview/gog_1971477531' }])
    expect(
      gameLinks(
        { ...game, entries: [{ ...game.entries[0], store: 'epic' }] },
        { ...workspace, works: [], externalIds: [] },
      ),
    ).toEqual([])
  })
  it('uses the selected game identities and deduplicates copies', () => {
    const links = gameLinks(
      { ...game, entries: [...game.entries, { ...game.entries[0], ownershipId: 4 }] },
      {
        ...workspace,
        externalIds: [...workspace.externalIds, { releaseId: 999, provider: 'steam', providerId: '999' }],
      },
    )
    expect(links.map((link) => link.url)).toEqual(expected)
  })
  it('does not guess URLs from names or malformed IDs', () => {
    expect(
      gameLinks(game, {
        ...workspace,
        works: [],
        externalIds: [
          { releaseId: 2, provider: 'steam', providerId: '10?run=1' },
          { releaseId: 2, provider: 'steam', providerId: '0' },
        ],
      }),
    ).toEqual([])
    expect(
      gameLinks(game, { ...workspace, externalIds: [], works: [{ id: 1, name: 'Game', igdbId: -1 }] }),
    ).toEqual([])
  })
  it('uses known Epic/GOG store URLs and the latest valid update URL', () => {
    const other = {
      ...game,
      entries: [
        { ...game.entries[0], store: 'epic' },
        { ...game.entries[0], store: 'gog', releaseId: 5, ownershipId: 6 },
      ],
    }
    const links = gameLinks(
      other,
      {
        ...workspace,
        works: [],
        externalIds: [
          { releaseId: 2, provider: 'epic', providerId: 'catalog-id' },
          { releaseId: 5, provider: 'gog', providerId: '123' },
        ],
        epicLaunchKeys: {
          'catalog-id': { namespace: 'namespace-id', catalogItemId: 'catalog-id', artifactId: 'artifact' },
        },
        storefronts: {
          'epic:namespace-id': { storeUrl: 'https://store.epicgames.com/en-US/p/test-game' },
          'gog:123': { storeUrl: 'https://www.gog.com/en/game/test_game' },
        },
      },
      [
        {
          id: 1,
          releaseId: 2,
          kind: 'announcement',
          occurredAt: '2026-01-01',
          url: 'https://example.com/old',
        },
        {
          id: 2,
          releaseId: 2,
          kind: 'announcement',
          occurredAt: '2026-02-01',
          url: 'https://example.com/new',
        },
        { id: 3, releaseId: 2, kind: 'announcement', occurredAt: '2026-03-01', url: 'javascript:alert(1)' },
        {
          id: 4,
          releaseId: 99,
          kind: 'announcement',
          occurredAt: '2026-04-01',
          url: 'https://example.com/unrelated',
        },
      ],
    )
    expect(links).toEqual([
      { label: 'Epic Games store page', url: 'https://store.epicgames.com/en-US/p/test-game' },
      { label: 'GOG store page', url: 'https://www.gog.com/en/game/test_game' },
      { label: 'Show in GOG Galaxy', url: 'goggalaxy://opengameview/gog_123' },
      { label: 'Latest patch notes', url: 'https://example.com/new' },
    ])
  })
  it('opens Steam store navigation for a mapped game owned elsewhere', () => {
    expect(gameLinks({ ...game, entries: [{ ...game.entries[0], store: 'manual' }] }, workspace)[0].url).toBe(
      'steam://store/10',
    )
  })
  it('keeps distinct editions identifiable and filters unsafe provider links', () => {
    const links = gameLinks(
      { ...game, entries: [...game.entries, { ...game.entries[0], releaseId: 4, title: 'Other edition' }] },
      {
        ...workspace,
        externalIds: [...workspace.externalIds, { releaseId: 4, provider: 'steam', providerId: '20' }],
      },
    )
    expect(links.filter((link) => link.label === 'View in Steam').map((link) => link.detail)).toEqual([
      'Test game · 10',
      'Other edition · 20',
    ])
    for (const storeUrl of [
      'file:///a',
      'https://user:password@example.com',
      'https://localhost/a',
      'not a url',
    ])
      expect(
        gameLinks(
          { ...game, entries: [{ ...game.entries[0], store: 'gog' }] },
          {
            ...workspace,
            works: [],
            externalIds: [{ releaseId: 2, provider: 'gog', providerId: '123' }],
            storefronts: { 'gog:123': { storeUrl } },
          },
        ),
      ).toEqual([{ label: 'Show in GOG Galaxy', url: 'goggalaxy://opengameview/gog_123' }])
  })
})

it.each(['desktop', 'fullscreen'] as const)(
  'routes all six details links without dispatching a game action in %s',
  async (mode) => {
    const openExternal = vi.fn().mockResolvedValue(undefined)
    const request = vi.fn(async ({ route }: ApiRequest) => ({
      ok: true,
      status: 200,
      data:
        route === 'library.get'
          ? { games: [game], lists: [] }
          : route === 'library.workspace'
            ? workspace
            : { workId: 1, events: [], sessions: {}, journalEntries: [], ratings: [], achievements: [] },
    }))
    Object.defineProperty(window, 'winnow', { value: { request, openExternal }, configurable: true })
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <Details workId={1} mode={mode} />
      </QueryClientProvider>,
    )
    const links = await screen.findByRole('navigation', { name: 'Game links' })
    for (const label of ['View in Steam', 'Store page', 'Patch notes', 'SteamDB', 'SteamGridDB', 'IGDB'])
      fireEvent.click(within(links).getByRole('button', { name: label }))
    await waitFor(() => expect(openExternal.mock.calls.map(([url]) => url)).toEqual(expected))
    expect(request.mock.calls.some(([input]) => input.route === 'actions.execute')).toBe(false)
  },
)

it('reports failed external navigation and allows retry', async () => {
  const openExternal = vi
    .fn()
    .mockRejectedValueOnce(new Error('No handler'))
    .mockResolvedValue({ opened: true })
  Object.defineProperty(window, 'winnow', { value: { openExternal }, configurable: true })
  render(<GameLinks links={[{ label: 'View in Steam', url: expected[0] }]} />)
  fireEvent.click(screen.getByRole('button', { name: 'View in Steam' }))
  expect((await screen.findByRole('alert')).textContent).toContain('Could not open this link')
  fireEvent.click(screen.getByRole('button', { name: 'View in Steam' }))
  await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
  expect(openExternal).toHaveBeenCalledTimes(2)
})
