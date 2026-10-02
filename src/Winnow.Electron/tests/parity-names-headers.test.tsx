// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useLibrary } from '../src/renderer/api/hooks'
import { projectLibraryHeaders } from '../src/renderer/api/library-headers'
import type { LibraryGame, LibraryResponse, Mode } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'
import type { ThemeContext } from '../src/shared/theme'
import { primaryEntry } from '../src/shared/game-actions'
import { Details } from '../src/renderer/features/Details'
import { MergeQueue } from '../src/renderer/features/parity-merge'
import {
  buildMergeCards,
  mergeMemberLabels,
  mergeTitle,
  type MergeReview,
} from '../src/renderer/features/parity-merge-model'
import { useIdentityReview } from '../src/renderer/features/parity-merge-query'
import { AvalonLibrary } from '../src/renderer/themes/avalon'
import { clearViewState, useViewState } from '../src/renderer/viewState'
import { coverProfile, FixtureCoverArt } from './cover-fixtures'

vi.mock('../src/renderer/components/Artwork', () => ({
  Artwork: ({ workId }: { workId: number }) => <span data-art-for={workId} />,
}))
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
const clients: QueryClient[] = []
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
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
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
  for (const mode of ['desktop', 'fullscreen'])
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
})
function game(id: number, title: string, store = 'steam'): LibraryGame {
  return {
    workId: id,
    title,
    firstReleaseYear: 2011,
    bucket: 'never_played',
    playtimeMinutes: 0,
    lastPlayedAt: null,
    entries: [
      {
        ownershipId: id,
        releaseId: id,
        workId: id,
        title,
        store,
        installed: false,
        playtimeMinutes: 0,
        lastPlayedAt: null,
      },
    ],
  }
}
function fixture(originals = [game(1, 'Steam title'), game(2, 'GOG title', 'gog')], saved = true) {
  const workspace: MergeReview['workspace'] = {
    works: originals.map((item) => ({
      id: item.workId,
      name: item.title,
      firstReleaseYear: item.firstReleaseYear,
      nameIsProvisional: false,
    })),
    releases: originals.map((item) => ({
      id: item.workId,
      workId: item.workId,
      name: item.entries[0]!.title,
    })),
    ownerships: originals.map((item) => ({
      id: item.workId,
      releaseId: item.workId,
      store: item.entries[0]!.store,
      installed: false,
    })),
    buckets: originals.map((item) => ({
      ownershipId: item.workId,
      releaseId: item.workId,
      workId: item.workId,
      playtimeMinutes: 0,
      lastPlayedAt: null,
      bucket: 'never_played',
    })),
    preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
    externalIds: originals.map((item) => ({
      releaseId: item.workId,
      provider: item.entries[0]!.store,
      providerId: String(700000 + item.workId),
    })),
    epicLaunchKeys: {},
    pluginActions: {},
    preferredHeaderStores: {},
  }
  const review: MergeReview = {
    revision: 'r1',
    hasCompletedSweep: true,
    workspace,
    expansions: [],
    candidates:
      saved || originals.length < 2
        ? []
        : [
            {
              id: 10,
              leftReleaseId: 1,
              rightReleaseId: 2,
              score: 0.98,
              status: 'pending',
              signalsJson: '{"band":"Priority","title_similarity":1}',
            },
          ],
    history: saved ? [{ id: 1, actId: 1, parentWorkId: 1, childWorkId: 2, kind: 'same_game' }] : [],
  }
  workspace.identityLinks = review.history
  let library: LibraryResponse = {
    games: saved ? [{ ...originals[0]!, entries: originals.flatMap((item) => item.entries) }] : originals,
    lists: [],
  }
  let pending: Promise<HeaderResponse> | undefined
  let sequence = 1
  const request = vi.fn(async (input: ApiRequest) => {
    if (input.route === 'identity.header') {
      const response = pending && (await pending)
      if (response && (!response.ok || response.data?.changed === false)) return response
      const body = input.body as { workId: number; store: string | null }
      workspace.preferredHeaderStores = { [body.workId]: body.store }
      review.revision = `r${++sequence}`
      return { ok: true, status: 200, data: { revision: review.revision } }
    }
    const data =
      input.route === 'library.get'
        ? library
        : input.route === 'library.workspace'
          ? workspace
          : input.route === 'identity.get'
            ? review
            : input.route === 'preferences.presentation.get'
              ? []
              : input.route === 'game.details'
                ? { workId: 1, events: [], sessions: {}, journalEntries: [], ratings: [], achievements: [] }
                : {}
    return { ok: true, status: 200, data: structuredClone(data) }
  })
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
  client.setQueryData(['api', 'library.get'], structuredClone(library))
  client.setQueryData(['api', 'library.workspace', undefined], structuredClone(workspace))
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
  function Wall({ mode }: { mode: Mode }) {
    const loaded = useLibrary()
    const [, query] = useViewState(`avalon:library:${mode}:query`, '')
    const [, sort] = useViewState(`avalon:library:${mode}:sort`, 'title')
    const context: ThemeContext = {
      mode,
      page: 'library',
      games: loaded.data?.games ?? [],
      loading: false,
      selectedWorkId: null,
      profile: coverProfile(),
      feed: undefined,
      children: null,
      setPage: vi.fn(),
      openGame: vi.fn(),
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
      <>
        <button
          onClick={() => {
            query('Zeni')
            sort('title')
          }}
        >
          Probe search user title
        </button>
        <button onClick={() => query('Storefront')}>Probe search former storefront title</button>
        <AvalonLibrary {...context} />
      </>
    )
  }
  function Queue({ mode }: { mode: Mode }) {
    const result = useIdentityReview()
    return result.data ? <MergeQueue mode={mode} review={result.data} onReview={vi.fn()} /> : null
  }
  async function publish() {
    await act(async () => {
      client.setQueryData(['api', 'library.get'], structuredClone(library))
      client.setQueryData(['api', 'library.workspace', undefined], structuredClone(workspace))
      client.setQueryData(['api', 'identity.get', undefined], structuredClone(review))
    })
  }
  return {
    workspace,
    review,
    request,
    client,
    wrapper,
    Wall,
    Queue,
    publish,
    get library() {
      return library
    },
    setLibrary: (value: LibraryResponse) => {
      library = value
    },
    hold: (value: typeof pending) => {
      pending = value
    },
  }
}
function cards() {
  return [...document.querySelectorAll<HTMLElement>('.avalon-library [data-avalon-game]')]
}
type HeaderResponse = {
  ok: boolean
  status: number
  message?: string
  data?: { revision: string; changed: boolean }
}
function labelFixture(facts: [string, number | null, string | null, string | null][]) {
  const f = fixture(
    facts.map(([title, , , store], index) => game(index + 1, title, store ?? '')),
    false,
  )
  f.review.candidates = facts.slice(1).map((_, index) => ({
    id: index + 1,
    leftReleaseId: 1,
    rightReleaseId: index + 2,
    score: 0.98,
    status: 'pending',
  }))
  f.workspace.works = facts.map(([name, year, publisher], index) => ({
    id: index + 1,
    name,
    firstReleaseYear: year,
    publisher,
  }))
  f.workspace.ownerships = (f.workspace.ownerships as { store: string }[]).filter((row) => !!row.store)
  return f
}
const labelCases = [
  {
    name: 'A_title_that_names_one_member_is_the_whole_label',
    facts: [
      ['Prey', 2017, 'Bethesda Softworks', 'steam'],
      ['Bastion', 2011, 'Supergiant Games', 'steam'],
    ],
    labels: ['Prey', 'Bastion'],
  },
  {
    name: 'Two_members_with_one_title_take_their_stores',
    facts: [
      ['Prey', 2017, 'Bethesda Softworks', 'steam'],
      ['Prey', 2017, 'Bethesda Softworks', 'epic'],
    ],
    labels: ['Prey (Steam)', 'Prey (Epic Games)'],
  },
  {
    name: 'Two_members_with_one_title_and_one_store_take_their_years',
    facts: [
      ['Prey', 2017, 'Bethesda Softworks', 'steam'],
      ['Prey', 2006, '3D Realms', 'steam'],
    ],
    labels: ['Prey (Steam, 2017)', 'Prey (Steam, 2006)'],
  },
  {
    name: 'Members_a_storefront_describes_identically_take_a_position',
    facts: Array.from({ length: 3 }, () => ['Prey', 2017, 'Bethesda Softworks', 'steam']),
    labels: [1, 2, 3].map((index) => `Prey (Steam, 2017, Bethesda Softworks, ${index} of 3)`),
  },
  {
    name: 'No_label_carries_an_entry_number',
    facts: [
      ['Prey', 2017, 'Bethesda Softworks', 'steam'],
      ['Prey', null, null, null],
    ],
    labels: ['Prey (Steam)', 'Prey'],
  },
] as { name: string; facts: [string, number | null, string | null, string | null][]; labels: string[] }[]

describe.each(['desktop', 'fullscreen'] as const)('%s frozen names and headers', (mode) => {
  it.each(labelCases)('$name', async ({ facts, labels }) => {
    const f = labelFixture(facts),
      card = buildMergeCards(f.review)[0]!
    expect(mergeMemberLabels(card)).toEqual(labels)
    expect(mergeMemberLabels({ ...card, rows: card.rows.map((row) => ({ ...row, workId: 1 })) })).toEqual(
      labels,
    )
    expect(new Set(labels).size).toBe(labels.length)
    expect(labels.every((label) => !label.includes('#'))).toBe(true)
    render(<f.Queue mode={mode} />, { wrapper: f.wrapper })
    const rendered = await screen.findByRole('article', { name: `${mergeTitle(card)} proposal` })
    if (mode === 'fullscreen') fireEvent.click(within(rendered).getByRole('button'))
    for (const label of labels)
      expect(
        screen.getByRole('button', {
          name:
            mode === 'desktop'
              ? `Choose ${label}`
              : new RegExp(`^${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')} ·`),
        }),
      ).toBeTruthy()
  })
  it('A_user_set_name_reaches_the_grid_tile', async () => {
    const own = game(1, 'My Own Name')
    own.entries[0]!.title = 'Storefront Title'
    const f = fixture([own], false)
    render(<f.Wall mode={mode} />, { wrapper: f.wrapper })
    expect(cards()).toHaveLength(1)
    expect(cards()[0]!.getAttribute('aria-label')).toBe('View My Own Name')
  })
  it('A_user_set_name_reaches_the_details_modal_headline', async () => {
    const own = game(1, 'My Own Name')
    own.entries[0]!.title = 'Storefront Title'
    const f = fixture([own], false)
    render(<Details workId={1} mode={mode} presentation="avalon" />, { wrapper: f.wrapper })
    expect(await screen.findByRole('heading', { name: 'My Own Name', level: 1 })).toBeTruthy()
    expect(screen.queryByText(/Name not yet available/)).toBeNull()
  })
  it('A_user_set_name_reaches_search_and_the_title_sort', async () => {
    const own = game(1, 'Zenith')
    own.entries[0]!.title = 'Storefront Title'
    const f = fixture([own, game(2, 'Almanac')], false)
    render(<f.Wall mode={mode} />, { wrapper: f.wrapper })
    expect(cards().map((row) => row.dataset.avalonGame)).toEqual(['2', '1'])
    fireEvent.click(screen.getByRole('button', { name: 'Probe search user title' }))
    await waitFor(() => expect(cards()).toHaveLength(1))
    expect(cards()[0]!.getAttribute('aria-label')).toContain('Zenith')
    fireEvent.click(screen.getByRole('button', { name: 'Probe search former storefront title' }))
    await waitFor(() => expect(cards()).toHaveLength(0))
  })
  it('A_user_set_name_reaches_the_merges_queue', async () => {
    const f = fixture([game(1, 'Prey (2017)'), game(2, 'Prey', 'gog')], false)
    render(<f.Queue mode={mode} />, { wrapper: f.wrapper })
    const card = await screen.findByRole('article', { name: 'Prey (2017) proposal' })
    if (mode === 'fullscreen') fireEvent.click(within(card).getByRole('button'))
    expect(
      screen.getByRole('button', {
        name: mode === 'desktop' ? 'Choose Prey (2017)' : 'Prey (2017) · Header',
      }),
    ).toBeTruthy()
  })
  it('A_game_the_user_has_not_named_keeps_the_title_it_had', () => {
    const own = game(1, 'Automatic Name')
    own.entries[0]!.title = 'Storefront Title'
    const f = fixture([own], false)
    render(<f.Wall mode={mode} />, { wrapper: f.wrapper })
    expect(cards()[0]!.getAttribute('aria-label')).toContain('Automatic Name')
  })
  it('Handing_the_name_back_to_automatic_marks_it_provisional_again', async () => {
    const f = fixture([game(1, 'My Own Name')], false)
    render(<Details workId={1} mode={mode} presentation="avalon" />, { wrapper: f.wrapper })
    await screen.findByRole('heading', { name: 'My Own Name', level: 1 })
    expect(screen.queryByText(/Name not yet available/)).toBeNull()
    f.workspace.works[0]!.nameIsProvisional = true
    await f.publish()
    expect(
      await screen.findByText('Name not yet available. Showing the app id until metadata loads.'),
    ).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'My Own Name', level: 1 })).toBeTruthy()
  })
  it('saved header choice and Automatic preserve identity and refresh mounted tiles', async () => {
    const f = fixture()
    render(
      <>
        <f.Queue mode={mode} />
        <f.Wall mode={mode} />
      </>,
      { wrapper: f.wrapper },
    )
    const before = structuredClone(f.library),
      history = structuredClone(f.review.history)
    const card = await screen.findByRole('article', { name: 'Steam title saved group' })
    if (mode === 'fullscreen') fireEvent.click(within(card).getByRole('button'))
    async function choose(store: string, current: string) {
      if (mode === 'desktop') {
        const select = screen.getByRole('combobox', { name: `Header store for ${current}` })
        select.focus()
        fireEvent.change(select, { target: { value: store } })
      } else {
        fireEvent.click(screen.getByRole('button', { name: /^Header store ·/ }))
        fireEvent.click(screen.getByRole('button', { name: store ? 'GOG' : 'Automatic' }))
      }
      await waitFor(() =>
        expect(f.request.mock.calls.filter(([input]) => input.route === 'identity.header')).toHaveLength(
          store ? 1 : 2,
        ),
      )
      await waitFor(() =>
        expect(
          document.activeElement?.getAttribute('aria-label') ?? document.activeElement?.textContent,
        ).toBe(
          mode === 'desktop'
            ? `Header store for ${store ? 'GOG title' : 'Steam title'}`
            : `Header store · ${store ? 'GOG' : 'Automatic'}`,
        ),
      )
    }
    await choose('gog', 'Steam title')
    await waitFor(() => expect(cards()[0]!.getAttribute('aria-label')).toContain('GOG title'))
    expect(f.client.getQueryData(['api', 'library.get'])).toEqual(before)
    const projected = projectLibraryHeaders(f.library, f.workspace).games[0]!
    expect(projected).toMatchObject({
      workId: 1,
      title: 'GOG title',
      firstReleaseYear: 2011,
      headerWorkId: 2,
    })
    expect(primaryEntry(projected.entries, f.workspace)?.store).toBe('gog')
    await choose('', 'GOG title')
    await waitFor(() => expect(cards()[0]!.getAttribute('aria-label')).toContain('Steam title'))
    expect(f.review.history).toEqual(history)
    expect(
      f.request.mock.calls
        .filter(([input]) => input.route === 'identity.header')
        .map(([input]) => input.body),
    ).toEqual([
      { expectedRevision: 'r1', workId: 1, store: 'gog' },
      { expectedRevision: 'r2', workId: 1, store: null },
    ])
  })
  it.each([
    {
      name: 'HTTP 409',
      response: { ok: false, status: 409, message: 'This group changed. Check the saved review.' },
    },
    {
      name: 'HTTP 200 changed false',
      response: { ok: true, status: 200, data: { revision: 'r1', changed: false } },
    },
  ])('$name header refusal retains the saved choice and exposes a focused recovery', async ({ response }) => {
    const f = fixture()
    let release!: (value: HeaderResponse) => void
    f.hold(
      new Promise((resolve) => {
        release = resolve
      }),
    )
    render(<f.Queue mode={mode} />, { wrapper: f.wrapper })
    const card = await screen.findByRole('article', { name: 'Steam title saved group' })
    if (mode === 'fullscreen') {
      fireEvent.click(within(card).getByRole('button'))
      fireEvent.click(screen.getByRole('button', { name: 'Header store · Automatic' }))
      const gog = screen.getByRole('button', { name: 'GOG' })
      gog.focus()
      fireEvent.click(gog)
      expect(await screen.findByText('Saving your header choice…')).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Automatic' }).getAttribute('aria-pressed')).toBe('true')
    } else {
      const select = screen.getByRole('combobox', { name: 'Header store for Steam title' })
      select.focus()
      fireEvent.change(select, { target: { value: 'gog' } })
    }
    await act(async () => release(response))
    const scope = mode === 'fullscreen' ? within(screen.getByRole('dialog')) : screen
    expect(await scope.findByRole('alert')).toBeTruthy()
    expect(scope.getByRole('alert').textContent).toContain('This group changed. Check the saved review')
    await waitFor(() =>
      expect(document.activeElement).toBe(scope.getByRole('button', { name: 'Check saved review' })),
    )
    expect(f.workspace.preferredHeaderStores).toEqual({})
    expect(f.review.revision).toBe('r1')
    expect(f.request.mock.calls.filter(([input]) => input.route === 'identity.header')).toHaveLength(1)
    expect(f.review.history).toHaveLength(1)
    if (mode === 'fullscreen')
      expect(scope.getByRole('button', { name: 'Automatic' }).getAttribute('aria-pressed')).toBe('true')
    else
      expect(
        (screen.getByRole('combobox', { name: 'Header store for Steam title' }) as HTMLSelectElement).value,
      ).toBe('')
  })
})

describe('canonical header projection', () => {
  it('keeps a fresh canonical group intact until its workspace links arrive', () => {
    const f = fixture([game(1, 'Original game')], false)
    const library = { games: [{ ...f.library.games[0]!, workId: 2, title: 'Grouped game' }], lists: [] }
    const projected = projectLibraryHeaders(library, f.workspace)
    expect(projected).toBe(library)
    expect(projected.games[0]!).toMatchObject({ workId: 2, title: 'Grouped game' })
    expect(projected.games[0]!.entries[0]!.workId).toBe(1)
  })
  it('keeps the canonical library name authoritative over an older workspace name', () => {
    const f = fixture()
    f.workspace.works[0]!.name = 'Older automatic name'
    expect(projectLibraryHeaders(f.library, f.workspace).games[0]!.title).toBe('Steam title')
  })
  it('further links inherit the choice and undo restores the original group projection', () => {
    const f = fixture([game(1, 'Steam title'), game(2, 'GOG title', 'gog'), game(3, 'Epic title', 'epic')])
    const originalHistory = structuredClone(f.review.history)
    f.review.history.push({ id: 2, actId: 2, parentWorkId: 3, childWorkId: 1, kind: 'same_game' })
    f.workspace.preferredHeaderStores = { '3': 'gog' }
    const joined = {
      games: [{ ...game(3, 'Epic title', 'epic'), entries: f.library.games[0]!.entries }],
      lists: [],
    }
    expect(projectLibraryHeaders(joined, f.workspace).games[0]!).toMatchObject({
      workId: 3,
      title: 'GOG title',
      headerWorkId: 2,
    })
    expect(buildMergeCards(f.review).map(mergeTitle)).toEqual(['GOG title', 'GOG title'])
    f.review.history = originalHistory
    f.workspace.identityLinks = originalHistory
    f.workspace.preferredHeaderStores = { '1': 'gog' }
    const original = {
      games: [
        { ...game(1, 'Steam title'), entries: f.library.games[0]!.entries.slice(0, 2) },
        game(3, 'Epic title', 'epic'),
      ],
      lists: [],
    }
    expect(projectLibraryHeaders(original, f.workspace).games.map((item) => item.title)).toEqual([
      'GOG title',
      'Epic title',
    ])
    expect(buildMergeCards(f.review).map(mergeTitle)).toEqual(['GOG title'])
    expect(f.review.history).toEqual(originalHistory)
  })
  it('keeps scalar metadata and canonical snapshots intact while choosing only visible owned headers', () => {
    const f = fixture()
    Object.assign(f.library.games[0]!, {
      summary: 'Canonical summary',
      publisher: 'Canonical publisher',
      coverUrl: 'canonical-cover',
      backgroundUrl: 'canonical-hero',
    })
    Object.assign(f.workspace.works[1]!, {
      firstReleaseYear: 2020,
      summary: 'Other summary',
      publisher: 'Other publisher',
      backgroundUrl: 'other-hero',
    })
    f.workspace.preferredHeaderStores = { '1': 'gog' }
    const original = structuredClone(f.library)
    const projected = projectLibraryHeaders(f.library, f.workspace)
    expect(projected.games[0]!).toMatchObject({
      workId: 1,
      headerWorkId: 2,
      title: 'GOG title',
      firstReleaseYear: 2011,
      summary: 'Canonical summary',
      publisher: 'Canonical publisher',
      coverUrl: 'canonical-cover',
      backgroundUrl: 'canonical-hero',
    })
    expect(f.library).toEqual(original)
    expect(projectLibraryHeaders(f.library, f.workspace)).toBe(projected)
    const scoped = {
      games: [{ ...f.library.games[0]!, entries: [f.library.games[0]!.entries[0]!] }],
      lists: [],
    }
    expect(projectLibraryHeaders(scoped, f.workspace).games[0]!).toMatchObject({
      title: 'Steam title',
      headerWorkId: 1,
    })
    const canonicalAway = {
      games: [{ ...f.library.games[0]!, entries: [f.library.games[0]!.entries[1]!] }],
      lists: [],
    }
    expect(
      projectLibraryHeaders(canonicalAway, { ...f.workspace, preferredHeaderStores: {} }).games[0]!,
    ).toMatchObject({ workId: 1, title: 'GOG title', headerWorkId: 2 })
  })
  it('orders the preferred store within the root when other group members are outside visible scope', () => {
    const f = fixture()
    f.workspace.preferredHeaderStores = { '1': 'gog' }
    const rootOnly = {
      games: [
        {
          ...f.library.games[0]!,
          entries: f.library.games[0]!.entries.map((entry) => ({ ...entry, workId: 1 })),
        },
      ],
      lists: [],
    }
    const projected = projectLibraryHeaders(rootOnly, f.workspace).games[0]!
    expect(projected).toMatchObject({ title: 'Steam title', workId: 1, headerWorkId: 1 })
    expect(projected.entries.map((entry) => entry.store)).toEqual(['gog', 'steam'])
  })
  it('ignores a retained preference after every same-game link is retracted', () => {
    const f = fixture()
    f.workspace.preferredHeaderStores = { '1': 'gog' }
    const rootOnly = {
      games: [
        {
          ...f.library.games[0]!,
          entries: f.library.games[0]!.entries.map((entry) => ({ ...entry, workId: 1 })),
        },
      ],
      lists: [],
    }
    const workspace: MergeReview['workspace'] = {
      ...f.workspace,
      identityLinks: f.review.history.map((link) => ({ ...link, retractedAt: '2026-09-30T00:00:00Z' })),
    }
    expect(projectLibraryHeaders(rootOnly, workspace)).toBe(rootOnly)
    expect(primaryEntry(projectLibraryHeaders(rootOnly, workspace).games[0]!.entries, workspace)?.store).toBe(
      'steam',
    )
    expect(workspace.preferredHeaderStores).toEqual({ '1': 'gog' })
  })
  it('unavailable preferences fall back without losing the saved choice and restore when ownership returns', () => {
    const f = fixture()
    f.workspace.preferredHeaderStores = { '1': 'gog' }
    const full = f.library
    const missing = { ...full, games: [{ ...full.games[0]!, entries: [full.games[0]!.entries[0]!] }] }
    expect(projectLibraryHeaders(missing, f.workspace).games[0]!.title).toBe('Steam title')
    f.workspace.ownerships = (f.workspace.ownerships as { store: string }[]).filter(
      (row) => row.store !== 'gog',
    )
    const card = buildMergeCards(f.review)[0]!
    expect(card.header?.options).toContainEqual({ value: 'gog', label: 'GOG (unavailable)' })
    expect(card.header?.store).toBe('gog')
    expect(mergeTitle(card)).toBe('Steam title')
    expect(projectLibraryHeaders(full, f.workspace).games[0]!.title).toBe('GOG title')
    expect(f.workspace.preferredHeaderStores).toEqual({ '1': 'gog' })
  })
  it('keeps installed and non-Derelict launch priority ahead of the preferred store', () => {
    const f = fixture()
    f.workspace.preferredHeaderStores = { '1': 'gog' }
    f.library.games[0]!.entries[0]!.installed = true
    const projected = projectLibraryHeaders(f.library, f.workspace).games[0]!
    expect(projected.entries[0]!.store).toBe('gog')
    expect(primaryEntry(projected.entries, f.workspace)?.store).toBe('steam')
  })
  it('joined groups use the inherited explicit choice independently of pending platform defaults', () => {
    const f = fixture([
      game(1, 'Steam title'),
      game(2, 'GOG title', 'gog'),
      game(3, 'Epic title', 'epic'),
      game(4, 'Manual title', 'manual'),
    ])
    f.review.history.push(
      { id: 2, actId: 2, parentWorkId: 3, childWorkId: 4, kind: 'same_game' },
      { id: 3, actId: 3, parentWorkId: 1, childWorkId: 3, kind: 'same_game' },
    )
    f.workspace.preferredHeaderStores = { '1': 'manual' }
    expect(buildMergeCards(f.review).map(mergeTitle)).toEqual([
      'Manual title',
      'Manual title',
      'Manual title',
    ])
    expect(projectLibraryHeaders(f.library, f.workspace).games[0]!).toMatchObject({
      workId: 1,
      title: 'Manual title',
      headerWorkId: 4,
      firstReleaseYear: 2011,
    })
  })
  it('does not offer a group-header preference for expansion relations or unlinked works', () => {
    const f = fixture()
    f.review.history[0]!.kind = 'expansion_of'
    expect(buildMergeCards(f.review)[0]!.header).toBeUndefined()
    f.review.history = []
    expect(buildMergeCards(f.review)).toEqual([])
    f.workspace.preferredHeaderStores = { '1': 'gog' }
    expect(
      projectLibraryHeaders({ games: [game(1, 'Steam title')], lists: [] }, f.workspace).games[0]!.title,
    ).toBe('Steam title')
  })
})
