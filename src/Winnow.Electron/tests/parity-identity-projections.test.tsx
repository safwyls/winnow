// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Details } from '../src/renderer/features/Details'
import { DetailsRelationships, detailsRelationships } from '../src/renderer/features/parity-details-identity'
import { projectLibraryGames } from '../src/renderer/features/parity-library-grain'
import { AvalonLibrary, AvalonShell } from '../src/renderer/themes/avalon'
import { useLibrary } from '../src/renderer/api/hooks'
import { dateLabel } from '../src/renderer/api/client'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'
import type { ThemeContext } from '../src/shared/theme'
import type { GameDetails, LibraryGame, Mode, Workspace } from '../src/renderer/api/types'
import { coverProfile, FixtureCoverArt } from './cover-fixtures'

// API snapshots preserve the frozen source figures. These tests verify their rendered
// projection; repository tests separately establish resolution, visibility and stored facts.
const identityNow = '2026-08-23T12:00:00Z',
  expansionNow = '2026-09-01T12:00:00Z'
const civTitle = "Sid Meier's Civilization IV",
  packTitle = `${civTitle}: Beyond the Sword`
const ago = (days: number, now = identityNow) => new Date(Date.parse(now) - days * 86400000).toISOString()
function fixtureBucket(minutes: number, lastPlayedAt: LibraryGame['lastPlayedAt']) {
  if (minutes === 0 && !lastPlayedAt) return 'never_played'
  if (minutes >= 6000) return 'retired'
  if (minutes >= 120) return 'bounced'
  return 'active'
}
function game(
  id: number,
  title: string,
  minutes: number,
  lastPlayedAt: string | null,
  store = 'steam',
): LibraryGame {
  return {
    workId: id,
    title,
    firstReleaseYear: 2017,
    bucket: fixtureBucket(minutes, lastPlayedAt),
    coverUrl: `https://fixture.invalid/steam-${(title.startsWith(civTitle) ? 800000 : 500000) + id}.jpg`,
    playtimeMinutes: minutes,
    lastPlayedAt,
    entries: [
      {
        ownershipId: id,
        releaseId: id,
        workId: id,
        title,
        store,
        installed: false,
        platform: 'windows',
        playtimeMinutes: minutes,
        lastPlayedAt,
      },
    ],
  }
}
const link = (id: number, parentWorkId: number, childWorkId: number, kind = 'same_game') => ({
  id,
  parentWorkId,
  childWorkId,
  kind,
})
type Link = ReturnType<typeof link>
function grouped(
  primary: LibraryGame,
  children: LibraryGame[],
  minutes: number,
  lastPlayedAt = primary.lastPlayedAt,
): LibraryGame {
  return {
    ...primary,
    playtimeMinutes: minutes,
    lastPlayedAt,
    bucket: fixtureBucket(minutes, lastPlayedAt),
    entries: [primary, ...children].flatMap((item) => item.entries),
  }
}
function expansionFixture(packMinutes = 0, packId = 2) {
  const base = {
    ...game(1, civTitle, 12000, ago(900, expansionNow)),
    firstReleaseYear: 2005,
    publisher: '2K Games',
  }
  const pack = {
    ...game(packId, packTitle, packMinutes, packMinutes ? ago(10, expansionNow) : null),
    firstReleaseYear: 2007,
    publisher: '2K Games',
  }
  return { base, pack }
}
const clients: QueryClient[] = []
vi.mock('../src/renderer/components/Artwork', () => ({
  Artwork: ({ workId }: { workId: number }) => <span data-art-for={workId} />,
}))
vi.mock('../src/renderer/themes/avalon-backdrop', () => ({ AvalonBackdrop: () => null }))
vi.mock('@tanstack/react-virtual', () => ({
  useVirtualizer: (options: { count: number; estimateSize(): number }) => ({
    getTotalSize: () => options.count * options.estimateSize(),
    getVirtualItems: () =>
      Array.from({ length: options.count }, (_, index) => ({
        key: index,
        index,
        start: index * options.estimateSize(),
      })),
    measure: vi.fn(),
    scrollToOffset: vi.fn(),
    scrollToIndex: vi.fn(),
  }),
}))
beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
      unobserve() {}
    },
  )
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)
})
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of [
      'query',
      'bucket',
      'store',
      'list',
      'sort',
      'view',
      'tools',
      'selected',
      'selection',
      'rules',
      'rows',
    ])
      clearViewState(`avalon:library:${mode}:${key}`)
    for (const id of [1, 2, 3]) clearViewState(`${mode}:details:${id}:editing`)
  }
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})
function setup(mode: Mode, originals: LibraryGame[], initialGames = originals, initialLinks: Link[] = []) {
  let games = initialGames
  const workspace: Workspace = {
    preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
    works: originals.map((item) => ({ id: item.workId, name: item.title })),
    externalIds: originals.map((item) => ({
      releaseId: item.workId,
      provider: 'steam',
      providerId: String((item.title.startsWith(civTitle) ? 800000 : 500000) + item.workId),
    })),
    epicLaunchKeys: {},
    pluginActions: {},
    identityLinks: initialLinks,
  }
  const details = new Map<number, GameDetails>(
    originals.map((item) => [
      item.workId,
      {
        workId: item.workId,
        readAtUtc: identityNow,
        events: [],
        sessions: {},
        journalEntries: [],
        ratings: [],
        achievements: [],
      },
    ]),
  )
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      input.route === 'library.get'
        ? { games, lists: [] }
        : input.route === 'library.workspace'
          ? workspace
          : input.route === 'game.details'
            ? details.get(Number(input.params?.workId))
            : input.route === 'preferences.presentation.get'
              ? []
              : {},
  }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      request,
      artwork: vi.fn(async () => null),
      openExternal: vi.fn(),
      cancelRequest: vi.fn(async () => {}),
    },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  clients.push(client)
  client.setQueryData(['api', 'library.get'], { games, lists: [] })
  client.setQueryData(['api', 'library.workspace', undefined], { ...workspace })
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
  const openGame = vi.fn()
  function Wall() {
    const library = useLibrary()
    const context: ThemeContext = {
      mode,
      page: 'library',
      games: library.data?.games ?? [],
      loading: false,
      selectedWorkId: null,
      profile: coverProfile(),
      feed: undefined,
      children: null,
      setPage: vi.fn(),
      openGame,
      toggleFullscreen: vi.fn(),
      renderScreen: () => null,
      actions: { launch: vi.fn() },
      components: {
        Artwork: FixtureCoverArt,
        Impression: ({ children }) => <>{children}</>,
        GameCard: () => null,
        GamePreview: () => null,
        ArtworkEffects: ({ children }) => <>{children}</>,
      },
    }
    return (
      <AvalonShell {...context}>
        <AvalonLibrary {...context} />
      </AvalonShell>
    )
  }
  async function publish(next: LibraryGame[], links = workspace.identityLinks as Link[]) {
    games = next
    workspace.identityLinks = links
    await act(async () => {
      client.setQueryData(['api', 'library.get'], { games, lists: [] })
      client.setQueryData(['api', 'library.workspace', undefined], { ...workspace })
    })
  }
  function showDetails(id = 1) {
    return render(<Details workId={id} mode={mode} presentation="avalon" onClose={() => {}} />, { wrapper })
  }
  return { wrapper, Wall, workspace, details, request, client, publish, showDetails, openGame }
}
const cards = () => [...document.querySelectorAll<HTMLButtonElement>('.avalon-library [data-avalon-game]')]
function copies() {
  return screen.getByRole('heading', { name: 'Owned copies' }).closest('section')!
}
function section(name: string) {
  return screen.getByRole('heading', { name }).closest('section')!
}
async function libraryTab() {
  fireEvent.click(await screen.findByRole('tab', { name: 'Library' }))
}
async function assertStoreCounts(mode: Mode) {
  if (mode === 'fullscreen') {
    fireEvent.click(screen.getByRole('button', { name: 'Filter & sort' }))
    fireEvent.click(screen.getByRole('button', { name: /^PLATFORM ·/ }))
    expect(screen.getByRole('button', { name: 'Steam, 2 matching titles' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Epic Games, 1 matching title' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Back to filters' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    return
  }
  for (const [store, count] of [
    ['steam', 2],
    ['epic', 1],
  ] as const) {
    fireEvent.change(screen.getByLabelText('Store'), { target: { value: store } })
    await waitFor(() => expect(cards()).toHaveLength(count))
  }
  fireEvent.change(screen.getByLabelText('Store'), { target: { value: 'all' } })
}

describe.each(['desktop', 'fullscreen'] as const)('%s frozen identity projections', (mode) => {
  it('An_expansion_link_moves_no_number_anywhere', async () => {
    const { base, pack } = expansionFixture()
    const f = setup(mode, [base, pack])
    render(<f.Wall />, { wrapper: f.wrapper })
    const before = cards().map((card) => [card.dataset.avalonGame, card.getAttribute('aria-description')])
    const assertRetiredCount = () => {
      const projected = projectLibraryGames([base, pack], f.workspace.identityLinks as Link[], false).games
      expect(projected.filter((item) => item.bucket === 'retired')).toHaveLength(1)
      expect(cards().filter((card) => card.getAttribute('aria-description') === 'Invested')).toHaveLength(1)
      if (mode === 'desktop')
        expect(screen.getByRole('button', { name: /^Invested/ }).textContent).toContain('1')
    }
    expect(cards()).toHaveLength(2)
    expect(screen.getByRole('button', { name: /^All games/ }).textContent).toContain('2')
    assertRetiredCount()
    await f.publish([base, pack], [link(10, 1, 2, 'expansion_of')])
    assertRetiredCount()
    expect(cards().map((card) => [card.dataset.avalonGame, card.getAttribute('aria-description')])).toEqual(
      before,
    )
    expect(projectLibraryGames([base, pack], f.workspace.identityLinks as Link[], false).games).toEqual([
      base,
      pack,
    ])
    expect(base.playtimeMinutes).toBe(12000)
    expect(pack.playtimeMinutes).toBe(0)
    expect(
      cards()
        .find((card) => card.dataset.avalonGame === '2')
        ?.getAttribute('aria-description'),
    ).toBe('Never played')
  })
  it('Linking_collapses_one_tile_and_leaves_the_store_counts_alone', async () => {
    const steam = game(1, 'Prey', 300, ago(30)),
      epic = game(2, 'Prey', 90, ago(400), 'epic'),
      dishonored = game(3, 'Dishonored', 0, null)
    const f = setup(mode, [steam, epic, dishonored])
    render(<f.Wall />, { wrapper: f.wrapper })
    expect(cards()).toHaveLength(3)
    await assertStoreCounts(mode)
    await f.publish([grouped(steam, [epic], 390), dishonored], [link(11, 1, 2)])
    await waitFor(() => expect(cards()).toHaveLength(2))
    expect(screen.getByRole('button', { name: /^All games/ }).textContent).toContain('2')
    await assertStoreCounts(mode)
  })
  it('A_linked_pair_is_one_tile_under_the_primary_title_and_cover', async () => {
    const steam = game(1, 'Prey', 300, ago(30)),
      epic = game(2, 'Prey (2017)', 90, ago(40), 'epic')
    const f = setup(mode, [steam, epic])
    render(<f.Wall />, { wrapper: f.wrapper })
    expect(
      cards()
        .map((card) => card.querySelector('[data-art-for]')?.getAttribute('data-art-for'))
        .sort(),
    ).toEqual(['1', '2'])
    const composite = grouped(steam, [epic], 390)
    await f.publish([composite], [link(11, 1, 2)])
    await waitFor(() => expect(cards()).toHaveLength(1))
    expect(cards()[0].getAttribute('aria-label')).toMatch(/^View Prey\./)
    expect(cards()[0].querySelector('[data-art-for]')?.getAttribute('data-art-for')).toBe('1')
    expect(cards()[0].getAttribute('aria-label')).toContain('Steam')
    expect(cards()[0].getAttribute('aria-label')).toContain('Epic')
    expect(composite.entries).toEqual([...steam.entries, ...epic.entries])
    expect(composite.playtimeMinutes).toBe(390)
    expect(composite.lastPlayedAt).toBe(ago(30))
  })
  it('The_modal_lists_the_titles_this_game_covers_with_their_own_figures', async () => {
    const steam = game(1, 'Prey', 300, ago(30)),
      epic = game(2, 'Prey Deluxe', 90, ago(400), 'epic')
    const f = setup(mode, [steam, epic], [grouped(steam, [epic], 390)], [link(11, 1, 2)])
    f.showDetails()
    await libraryTab()
    for (const [title, store, time, date] of [
      ['Prey', 'Steam', '5h', ago(30)],
      ['Prey Deluxe', 'Epic Games', '1h', ago(400)],
    ]) {
      const row = within(copies()).getByRole('heading', { name: title }).closest('.avalon-copy')!
      expect(row.textContent).toContain(store)
      expect(row.textContent).toContain(time)
      expect(row.textContent).toContain(dateLabel(date))
    }
    expect(copies().querySelectorAll('.avalon-copy')).toHaveLength(2)
  })
  it('The_modal_shows_per_release_achievement_rows_and_never_a_blended_percentage', async () => {
    const steam = game(1, 'Prey', 300, ago(30)),
      epic = game(2, 'Prey', 90, ago(40), 'epic')
    const f = setup(mode, [steam, epic], [grouped(steam, [epic], 390)], [link(11, 1, 2)])
    f.details.get(1)!.achievements = [
      { releaseId: 1, total: 10, unlocked: 10, hasKnownProgress: true, isStale: false, percentComplete: 100 },
      { releaseId: 2, total: 10, unlocked: 3, hasKnownProgress: true, isStale: false, percentComplete: 30 },
    ]
    f.showDetails()
    await libraryTab()
    const achievements = await screen.findByLabelText('Achievements by release')
    expect(achievements.textContent).toMatch(/Steam.*10 of 10 unlocked.*100%/)
    expect(achievements.textContent).toMatch(/Epic Games.*3 of 10 unlocked.*30%/)
    expect(achievements.textContent).not.toContain('65%')
  })
  it('An_unsupported_release_distinguishes_unknown_progress_from_zero', async () => {
    const steam = game(1, 'Prey', 300, ago(30)),
      epic = game(2, 'Prey', 90, ago(40), 'epic')
    const f = setup(mode, [steam, epic], [grouped(steam, [epic], 390)], [link(11, 1, 2)])
    f.details.get(1)!.achievements = [
      { releaseId: 1, total: 4, unlocked: 1, hasKnownProgress: true, isStale: false, percentComplete: 25 },
      { releaseId: 2, total: 0, unlocked: 0, hasKnownProgress: false, isStale: false, percentComplete: null },
    ]
    f.showDetails()
    await libraryTab()
    const achievements = await screen.findByLabelText('Achievements by release')
    expect(achievements.textContent).toMatch(/Steam.*1 of 4 unlocked.*25%/)
    expect(achievements.textContent).toMatch(/Epic Games.*Not supported/)
    expect(achievements.textContent).not.toMatch(/0\/0|0%/)
  })
  it('A_game_that_covers_nothing_draws_no_coverage_section', async () => {
    const own = game(1, 'Dishonored', 40, ago(9)),
      f = setup(mode, [own])
    f.showDetails()
    await libraryTab()
    expect(copies().querySelectorAll('.avalon-copy')).toHaveLength(1)
    expect(screen.queryByText(/summed across/)).toBeNull()
    expect(screen.queryByRole('heading', { name: 'Related games & editions' })).toBeNull()
    expect(screen.queryByRole('button', { name: /^Separate/ })).toBeNull()
  })
  it('An_expansion_never_enters_the_coverage_sum', async () => {
    const { base, pack } = expansionFixture(4000),
      f = setup(mode, [base, pack], [base, pack], [link(10, 1, 2, 'expansion_of')])
    render(<DetailsRelationships game={base} mode={mode} />, { wrapper: f.wrapper })
    const row = await screen.findByRole('article', { name: packTitle })
    expect(row.textContent).toContain('66h')
    expect(row.textContent).toContain(dateLabel(ago(10, expansionNow)))
    expect(screen.queryByRole('heading', { name: 'Related games & editions' })).toBeNull()
    expect(section('Expansions').textContent).toContain('Counted separately. Not added above.')
    expect(section('Expansions').textContent).not.toMatch(/266h|12000|16000/)
    expect(base.playtimeMinutes).toBe(12000)
  })
  it('The_pack_says_what_it_extends_and_the_two_sections_stay_apart', async () => {
    const base = expansionFixture().base,
      gog = {
        ...game(2, civTitle, 60, ago(5, expansionNow), 'gog'),
        firstReleaseYear: 2005,
        publisher: '2K Games',
      },
      pack = expansionFixture(0, 3).pack
    const composite = grouped(base, [gog], 12060, ago(5, expansionNow)),
      links = [link(11, 1, 2), link(12, 1, 3, 'expansion_of')]
    const f = setup(mode, [base, gog, pack], [composite, pack], links)
    const view = render(<DetailsRelationships game={composite} mode={mode} onOpenGame={f.openGame} />, {
      wrapper: f.wrapper,
    })
    expect(await screen.findByRole('heading', { name: 'Expansions' })).toBeTruthy()
    expect(section('Expansions').textContent).toContain(packTitle)
    expect(section('Related games & editions').textContent).not.toContain(packTitle)
    expect(screen.queryByRole('heading', { name: 'Extends' })).toBeNull()
    expect(composite.playtimeMinutes).toBe(12060)
    expect(composite.entries).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: `View ${packTitle}` }))
    expect(f.openGame.mock.calls).toEqual([[3]])
    view.rerender(<DetailsRelationships game={pack} mode={mode} onOpenGame={f.openGame} />)
    expect(await screen.findByRole('heading', { name: 'Extends' })).toBeTruthy()
    expect(section('Extends').textContent).toContain(civTitle)
    expect(screen.queryByRole('heading', { name: 'Expansions' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: `View ${civTitle}` }))
    expect(f.openGame.mock.calls).toEqual([[3], [1]])
    expect(f.request.mock.calls.some(([input]) => input.route === 'identity.separate')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: `Separate ${packTitle}…` }))
    fireEvent.click(screen.getByRole('button', { name: 'Separate games' }))
    await waitFor(() =>
      expect(f.request.mock.calls.find(([input]) => input.route === 'identity.separate')?.[0]).toMatchObject({
        params: { childWorkId: 3 },
        body: { expectedLinkId: 12 },
      }),
    )
  })
  it('Re_parenting_a_base_game_keeps_its_expansions_expansions', async () => {
    const steam = expansionFixture().base,
      gog = {
        ...game(2, civTitle, 60, ago(5, expansionNow), 'gog'),
        firstReleaseYear: 2005,
        publisher: '2K Games',
      },
      pack = expansionFixture(4000, 3).pack
    const composite = grouped(gog, [steam], 12060),
      links = [link(11, 2, 1), link(12, 2, 3, 'expansion_of')]
    const f = setup(mode, [steam, gog, pack], [composite, pack], links)
    render(<f.Wall />, { wrapper: f.wrapper })
    expect(cards()).toHaveLength(2)
    expect(
      projectLibraryGames([composite, pack], links, false).games.map((item) => item.playtimeMinutes),
    ).toEqual([12060, 4000])
    expect(detailsRelationships(pack, f.workspace, 'expansions', [composite, pack])).toEqual([links[1]])
    expect(detailsRelationships(pack, f.workspace, 'editions', [composite, pack])).toEqual([])
  })
  it('Separate_retracts_one_link_and_leaves_the_rest_of_the_act', async () => {
    const steam = game(1, 'Prey', 300, ago(30)),
      epic = game(2, 'Prey Epic', 90, ago(40), 'epic'),
      gog = game(3, 'Prey GOG', 20, ago(50), 'gog')
    const f = setup(
      mode,
      [steam, epic, gog],
      [grouped(steam, [epic, gog], 410)],
      [link(11, 1, 2), link(12, 1, 3)],
    )
    f.showDetails()
    await libraryTab()
    fireEvent.click(await screen.findByRole('button', { name: 'Separate Prey Epic…' }))
    fireEvent.click(screen.getByRole('button', { name: 'Separate games' }))
    await waitFor(() =>
      expect(f.request.mock.calls.filter(([input]) => input.route === 'identity.separate')).toHaveLength(1),
    )
    expect(f.request.mock.calls.find(([input]) => input.route === 'identity.separate')?.[0]).toMatchObject({
      params: { childWorkId: 2 },
      body: { expectedLinkId: 11 },
    })
    await f.publish([grouped(steam, [gog], 320), epic], [link(12, 1, 3)])
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Separate Prey Epic…' })).toBeNull())
    expect(screen.getByRole('button', { name: 'Separate Prey GOG…' })).toBeTruthy()
    expect(within(copies()).queryByRole('heading', { name: 'Prey Epic' })).toBeNull()
    expect(within(copies()).getByRole('heading', { name: 'Prey GOG' })).toBeTruthy()
    expect(
      f.client
        .getQueryData<{ games: LibraryGame[] }>(['api', 'library.get'])!
        .games.find((item) => item.workId === 2)?.title,
    ).toBe('Prey Epic')
  })
  it('immediate nested Escape retains Details and restores the relationship opener', async () => {
    const steam = game(1, 'Prey', 300, ago(30)),
      epic = game(2, 'Prey Epic', 90, ago(40), 'epic')
    const f = setup(mode, [steam, epic], [grouped(steam, [epic], 390)], [link(11, 1, 2)])
    const onClose = vi.fn()
    render(<Details workId={1} mode={mode} presentation="avalon" onClose={onClose} />, {
      wrapper: f.wrapper,
    })
    await libraryTab()
    const details = document.querySelector('.avalon-details')!
    const opener = await screen.findByRole('button', { name: 'Separate Prey Epic…' })
    opener.focus()
    fireEvent.click(opener)
    fireEvent.keyDown(screen.getByRole('button', { name: 'Keep relationship' }), { key: 'Escape' })
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Keep relationship' })).toBeNull()
      expect(document.activeElement).toBe(opener)
    })
    expect(details.isConnected).toBe(true)
    expect(screen.getByRole('tab', { name: 'Library' }).getAttribute('aria-selected')).toBe('true')
    expect(onClose).not.toHaveBeenCalled()
    expect(f.request.mock.calls.some(([input]) => input.route === 'identity.separate')).toBe(false)
  })
  it('omits counterpart rows absent from the current visible library scope', async () => {
    const { base, pack } = expansionFixture(4000),
      f = setup(mode, [base, pack], [base], [link(10, 1, 2, 'expansion_of')])
    const view = render(<DetailsRelationships game={base} mode={mode} />, { wrapper: f.wrapper })
    expect(screen.queryByRole('heading', { name: 'Expansions' })).toBeNull()
    expect(screen.queryByText(packTitle)).toBeNull()
    await f.publish([pack])
    view.rerender(<DetailsRelationships game={pack} mode={mode} />)
    expect(screen.queryByRole('heading', { name: 'Extends' })).toBeNull()
    expect(screen.queryByText(civTitle)).toBeNull()
  })
  it.each([
    {
      name: 'unknown Steam progress',
      availability: 0,
      total: 4,
      unlocked: 0,
      known: false,
      stale: false,
      expected: 'Not fetched',
    },
    {
      name: 'unavailable progress',
      availability: 1,
      total: 4,
      unlocked: 0,
      known: false,
      stale: true,
      expected: 'Unavailable',
    },
    {
      name: 'confirmed empty schema',
      availability: 2,
      total: 0,
      unlocked: 0,
      known: false,
      stale: false,
      expected: 'No achievements',
    },
    {
      name: 'known zero progress',
      availability: 3,
      total: 4,
      unlocked: 0,
      known: true,
      stale: false,
      expected: '0 of 4 unlocked · 0%',
    },
    {
      name: 'stale known progress',
      availability: 1,
      total: 4,
      unlocked: 1,
      known: true,
      stale: true,
      expected: '1 of 4 unlocked · 25% · last known',
    },
  ])('renders $name without using another non-visible release', async (row) => {
    const steam = game(1, 'Prey', 300, ago(30)),
      epic = game(2, 'Prey Deluxe', 90, ago(40), 'epic')
    const f = setup(mode, [steam, epic], [steam], [link(11, 1, 2)])
    f.details.get(1)!.achievements = [
      {
        releaseId: 1,
        total: row.total,
        unlocked: row.unlocked,
        hasKnownProgress: row.known,
        isStale: row.stale,
        availability: row.availability,
      },
      { releaseId: 2, total: 10, unlocked: 10, hasKnownProgress: true, isStale: false, availability: 3 },
    ]
    f.showDetails()
    await libraryTab()
    const achievements = await screen.findByLabelText('Achievements by release')
    expect(achievements.querySelectorAll('[data-release-id]')).toHaveLength(1)
    expect(achievements.querySelector('[data-release-id="1"]')?.textContent).toContain(row.expected)
    expect(achievements.querySelector('[data-release-id="2"]')).toBeNull()
    expect(achievements.textContent).not.toContain('Epic')
    if (!row.known) expect(achievements.textContent).not.toMatch(/0%|unlocked|last known/)
    expect(screen.queryByRole('button', { name: 'Separate Prey Deluxe…' })).toBeNull()
  })
})
