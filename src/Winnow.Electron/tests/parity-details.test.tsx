// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Details } from '../src/renderer/features/Details'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'
import type { Mode } from '../src/renderer/api/types'

vi.mock('../src/renderer/components/Artwork', () => ({ Artwork: () => <div data-testid="artwork" /> }))
const game = {
  workId: 1,
  title: 'Original game',
  bucket: 'derelict',
  playtimeMinutes: 50,
  entries: [
    {
      ownershipId: 10,
      releaseId: 100,
      workId: 1,
      title: 'Original game',
      store: 'steam',
      installed: true,
      playtimeMinutes: 50,
    },
  ],
}
const facts = {
  workId: 1,
  readAtUtc: '2026-09-27T00:00:00Z',
  events: [
    { id: 30, releaseId: 100, occurredAt: '2026-09-22T00:00:00Z', kind: 'build_change', title: 'Patch one' },
    { id: 31, releaseId: 200, occurredAt: '2026-09-23T00:00:00Z', kind: 'build_change', title: 'Patch two' },
  ],
  acknowledgements: { 100: '2026-09-22T00:00:00Z' },
  sessions: {},
  journalEntries: [],
  ratings: [],
  achievements: [],
  ownerships: [
    {
      id: 10,
      releaseId: 100,
      store: 'steam',
      acquiredAt: '2024-01-03T00:00:00Z',
      installPath: 'C:\\Games\\Original',
    },
  ],
  history: { 10: [{ id: 7, playtimeMinutes: 50, observedAt: '2026-09-21T00:00:00Z' }] },
  images: [{ source: 'igdb', kind: 'screenshot', imageIds: 'shot_one,shot_two' }],
}
const metadata = {
  workId: 1,
  title: 'Original game',
  isPinned: false,
  revision: 'metadata-1',
  fields: [
    { field: 'name', value: 'Original game', source: 'igdb' },
    { field: 'summary', value: 'Old description', source: 'user' },
    { field: 'cover_url', value: '', source: 'user' },
    { field: 'first_release_year', value: '2000', source: 'igdb' },
  ],
}
const workspace = {
  preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'AdultsOnly' },
  works: [{ id: 1, name: 'Original game' }],
  externalIds: [{ releaseId: 100, provider: 'steam', providerId: '480' }],
  pluginActions: {},
  epicLaunchKeys: {},
}
function setup(
  mode: Mode,
  handler?: (input: ApiRequest) => unknown,
  presentation: 'shared' | 'avalon' = 'shared',
) {
  const close = vi.fn()
  const request = vi.fn(async (input: ApiRequest) => {
    const override = await handler?.(input)
    if (override !== undefined) return override
    const data =
      input.route === 'library.get'
        ? { games: [game], lists: [] }
        : input.route === 'library.workspace'
          ? workspace
          : input.route === 'game.details'
            ? facts
            : input.route === 'metadata.get'
              ? metadata
              : input.route === 'metadata.igdb'
                ? { workId: 1, revision: 'igdb-1', mappingRevision: 1, pin: null }
                : input.route === 'identity.get'
                  ? { revision: 'identity-1' }
                  : { outcome: 'Applied', result: 'Stored' }
    return { ok: true, status: 200, data }
  })
  const artwork = vi.fn().mockResolvedValue('data:image/png;base64,aA==')
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, artwork, openExternal: vi.fn() },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const view = render(
    <QueryClientProvider client={client}>
      <div data-testid="detail-scroll" style={{ overflowY: 'auto', height: 640 }}>
        <Details
          workId={1}
          mode={mode}
          presentation={presentation}
          onClose={presentation === 'avalon' ? close : undefined}
        />
      </div>
    </QueryClientProvider>,
  )
  return { request, artwork, client, view, close }
}
afterEach(() => {
  cleanup()
  for (const key of [
    'desktop:details:1:tab',
    'fullscreen:details:1:tab',
    'desktop:details:1:previous-section',
    'fullscreen:details:1:previous-section',
    'draft:igdb:1',
    'igdb:1:holder',
    'igdb:1:offer-revision',
    'igdb:1:sending',
    'draft:metadata-fields:1',
    'metadata-fields:1:sending',
  ])
    clearViewState(key)
})

describe.each<Mode>(['desktop', 'fullscreen'])('%s original Avalon Details composition', (mode) => {
  it('opens a merged member through its grouped facts while edits retain the requested identity', async () => {
    const { request } = setup(
      mode,
      (input) =>
        input.route === 'library.get'
          ? {
              ok: true,
              status: 200,
              data: { games: [{ ...game, workId: 2, title: 'Grouped game' }], lists: [] },
            }
          : input.route === 'library.workspace'
            ? {
                ok: true,
                status: 200,
                data: {
                  ...workspace,
                  buckets: [{ workId: 1, resolvedWorkId: 2, game: { unreadUpdateCount: 0 } }],
                },
              }
            : undefined,
      'avalon',
    )
    await screen.findByRole('heading', { name: 'Grouped game', level: 1 })
    expect(screen.queryByRole('heading', { name: 'Game details' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'More' }))
    fireEvent.click(screen.getByRole('button', { name: 'Edit metadata…' }))
    await screen.findByRole('textbox', { name: 'Name' })
    expect(
      request.mock.calls.some(([input]) => input.route === 'metadata.get' && input.params?.workId === 1),
    ).toBe(true)
  })
  it('keeps the original sparse section set and uses tab selection semantics', async () => {
    setup(
      mode,
      (input) =>
        input.route === 'game.details'
          ? { ok: true, status: 200, data: { ...facts, ownerships: [], history: {}, events: [], images: [] } }
          : undefined,
      'avalon',
    )
    await screen.findByRole('heading', { name: 'Original game', level: 1 })
    const names =
      mode === 'desktop'
        ? ['Overview', 'Activity', 'Updates', 'Journal', 'Library']
        : ['Overview', 'Updates', 'Journal', 'Library']
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(names)
    expect(screen.queryByRole('button', { name: 'Metadata' })).toBeNull()
    for (const name of names) {
      fireEvent.click(screen.getByRole('tab', { name }))
      expect(screen.getByRole('tab', { name }).getAttribute('aria-selected')).toBe('true')
      expect(screen.getAllByRole('tabpanel')).toHaveLength(1)
      expect((screen.getByRole('tab', { name }) as HTMLButtonElement).disabled).toBe(false)
      expect(screen.queryByText('Installation & identifiers')).toBeNull()
    }
    const first = screen.getByRole('tab', { name: 'Overview' })
    first.focus()
    fireEvent.keyDown(first, { key: 'ArrowRight' })
    expect(document.activeElement).toBe(screen.getByRole('tab', { name: names[1] }))
    fireEvent.keyDown(document.activeElement!, { key: 'End' })
    expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Library' }))
    fireEvent.keyDown(document.activeElement!, { key: 'Home' })
    expect(document.activeElement).toBe(first)
    expect(screen.getByText('No description yet. Metadata fills in automatically.')).toBeDefined()
    expect(screen.queryByText(/unread updates/)).toBeNull()
    expect(screen.queryByText('Related games & expansions')).toBeNull()
  })
  it('retains each reading offset and restores its selected tab after a focused metadata draft', async () => {
    const { request, close } = setup(mode, undefined, 'avalon')
    await screen.findByRole('heading', { name: 'Original game', level: 1 })
    const overview = screen.getByRole('tabpanel')
    overview.scrollTop = 320
    fireEvent.scroll(overview)
    fireEvent.click(screen.getByRole('tab', { name: 'Library' }))
    expect(screen.getByRole('tabpanel').scrollTop).toBe(0)
    fireEvent.click(screen.getByRole('tab', { name: 'Overview' }))
    expect(screen.getByRole('tabpanel').scrollTop).toBe(320)
    fireEvent.click(screen.getByRole('tab', { name: 'Library' }))
    fireEvent.click(screen.getByRole('button', { name: 'More' }))
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Wrong game…' }))
    fireEvent.click(screen.getByRole('button', { name: 'Edit metadata…' }))
    const input = await screen.findByRole('textbox', { name: 'Name' })
    await waitFor(() => expect(document.activeElement).toBe(input))
    fireEvent.change(input, { target: { value: 'Unfinished title' } })
    expect(screen.queryAllByRole('tab')).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'Back to Library' }))
    expect(screen.getByRole('tab', { name: 'Library' }).getAttribute('aria-selected')).toBe('true')
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'More' }))
    fireEvent.click(screen.getByRole('button', { name: 'More' }))
    fireEvent.click(screen.getByRole('button', { name: 'Edit metadata…' }))
    expect(((await screen.findByRole('textbox', { name: 'Name' })) as HTMLInputElement).value).toBe(
      'Unfinished title',
    )
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    await waitFor(() => expect(screen.queryAllByRole('tab')).toHaveLength(mode === 'desktop' ? 5 : 4))
    expect(close).not.toHaveBeenCalled()
    expect(request.mock.calls.some(([input]) => input.route === 'metadata.edit')).toBe(false)
    expect(request.mock.calls.filter(([input]) => input.route === 'metadata.get')).toHaveLength(1)
  })
  it('keeps refresh in place and routes the unread shortcut to the named Updates tab', async () => {
    const { client } = setup(
      mode,
      (input) =>
        input.route === 'library.workspace'
          ? {
              ok: true,
              status: 200,
              data: { ...workspace, buckets: [{ resolvedWorkId: 1, game: { unreadUpdateCount: 2 } }] },
            }
          : undefined,
      'avalon',
    )
    fireEvent.click(await screen.findByRole('button', { name: '2 unread updates →' }))
    const updates = screen.getByRole('tab', { name: 'Updates, 2 unread updates' })
    expect(updates.getAttribute('aria-selected')).toBe('true')
    await client.invalidateQueries({ queryKey: ['api', 'game.details'] })
    expect(updates.getAttribute('aria-selected')).toBe('true')
  })
})

it.each([false, true])(
  'fullscreen Overview omits missing screenshots, reception and unsaved note copy with saved notes %s',
  async (hasNotes) => {
    setup(
      'fullscreen',
      (input) =>
        input.route === 'game.details'
          ? {
              ok: true,
              status: 200,
              data: {
                ...facts,
                images: [],
                ratings: [],
                journalEntries: hasNotes
                  ? [
                      { sessionId: 1, sessionAt: '2026-09-27T00:00:00Z', note: 'An older saved note.' },
                      {
                        sessionId: 2,
                        sessionAt: '2026-09-28T00:00:00Z',
                        note: 'Return to the mountain camp.',
                      },
                    ]
                  : [],
              },
            }
          : undefined,
      'avalon',
    )
    await screen.findByRole('heading', { name: hasNotes ? 'Latest note' : 'Journal', level: 2 })
    expect(Boolean(screen.queryByText('Return to the mountain camp.'))).toBe(hasNotes)
    expect(screen.queryByText('An older saved note.')).toBeNull()
    expect(screen.queryByText(/No notes yet/)).toBeNull()
    expect(screen.queryAllByRole('button', { name: /^Open screenshot/ })).toHaveLength(0)
    expect(screen.queryByLabelText('Reception')).toBeNull()
    expect(screen.getByText('No description yet. Metadata fills in automatically.')).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Open journal →' }))
    expect(screen.getByRole('tab', { name: 'Journal' }).getAttribute('aria-selected')).toBe('true')
  },
)

it('desktop original Details bounds description previews and starts a new opening at Overview', async () => {
  const summary = 'An expedition through forgotten places with a friend. '.repeat(15)
  const handler = (input: ApiRequest) =>
    input.route === 'library.get'
      ? { ok: true, status: 200, data: { games: [{ ...game, summary }], lists: [] } }
      : undefined
  const first = setup('desktop', handler, 'avalon')
  await screen.findByRole('heading', { name: 'Original game', level: 1 })
  const paragraph = document.querySelector('.game-summary')!
  expect(paragraph.textContent!.length).toBeLessThanOrEqual(361)
  fireEvent.click(screen.getByRole('button', { name: 'Read more' }))
  expect(paragraph.textContent).toBe(summary.trim())
  fireEvent.click(screen.getByRole('button', { name: 'Show less' }))
  expect(paragraph.textContent!.length).toBeLessThanOrEqual(361)
  fireEvent.click(screen.getByRole('tab', { name: 'Library' }))
  first.view.unmount()
  setup('desktop', handler, 'avalon')
  await screen.findByRole('heading', { name: 'Original game', level: 1 })
  expect(screen.getByRole('tab', { name: 'Overview' }).getAttribute('aria-selected')).toBe('true')
})

it('fullscreen original Overview shows only the latest saved note and uses separate reading pages', async () => {
  setup(
    'fullscreen',
    (input) =>
      input.route === 'game.details'
        ? {
            ok: true,
            status: 200,
            data: {
              ...facts,
              journalEntries: [
                { sessionId: 2, sessionAt: '2026-09-28T00:00:00Z', note: 'Return to the camp.', rating: 4 },
                { sessionId: 1, sessionAt: '2026-09-27T00:00:00Z', note: 'Older note.', rating: 2 },
              ],
            },
          }
        : undefined,
    'avalon',
  )
  await screen.findByText('Return to the camp.')
  expect(screen.queryByText('Older note.')).toBeNull()
  const open = screen.getByRole('button', { name: 'Read more →' })
  fireEvent.click(open)
  expect(screen.queryAllByRole('tab')).toHaveLength(0)
  expect(screen.getByRole('region', { name: 'About' })).toBeDefined()
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Back to Overview' }))
  fireEvent.click(screen.getByRole('button', { name: 'Back to Overview' }))
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Read more →' }))
  fireEvent.click(screen.getByRole('button', { name: 'Open journal →' }))
  expect(screen.getByText('Older note.')).toBeDefined()
})

describe.each<Mode>(['desktop', 'fullscreen'])('%s details parity', (mode) => {
  it('confirms separation from expansion details with the child identity and preserves a failed confirmation', async () => {
    let failed = false,
      separated = false
    const { request } = setup(mode, (input) => {
      if (input.route === 'library.workspace')
        return {
          ok: true,
          status: 200,
          data: {
            ...workspace,
            works: [
              { id: 1, name: 'Expansion' },
              { id: 2, name: 'Base game' },
            ],
            identityLinks: separated
              ? []
              : [
                  {
                    id: 44,
                    parentWorkId: 2,
                    childWorkId: 1,
                    kind: 'expansion_of',
                    relationLabel: 'expansion',
                  },
                ],
          },
        }
      if (input.route === 'identity.separate') {
        if (!failed) {
          failed = true
          return { ok: false, status: 400, message: 'Could not separate these games' }
        }
        separated = true
        return { ok: true, status: 204 }
      }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Library' }))
    const trigger = await screen.findByRole('button', { name: 'Separate Expansion…' })
    fireEvent.click(trigger)
    expect(screen.getByRole('dialog', { name: 'Separate Expansion from Base game?' })).toBeTruthy()
    expect(request.mock.calls.some(([input]) => input.route === 'identity.separate')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Keep relationship' }))
    await waitFor(() => expect(document.activeElement).toBe(trigger))
    fireEvent.click(trigger)
    fireEvent.click(screen.getByRole('button', { name: 'Separate games' }))
    await screen.findByText('Could not separate these games')
    expect(screen.getByRole('dialog')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Separate games' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const writes = request.mock.calls.filter(([input]) => input.route === 'identity.separate')
    expect(writes).toHaveLength(2)
    for (const [input] of writes)
      expect(input).toEqual({
        route: 'identity.separate',
        params: { childWorkId: 1 },
        body: { expectedLinkId: 44 },
      })
    expect(screen.queryByRole('button', { name: 'Separate Expansion…' })).toBeNull()
  })
  it('opens unread updates from the header without replacing metadata drafts or counting duplicate editions', async () => {
    setup(mode, (input) =>
      input.route === 'library.workspace'
        ? {
            ok: true,
            status: 200,
            data: {
              ...workspace,
              buckets: [
                { resolvedWorkId: 1, releaseId: 100, game: { unreadUpdateCount: 2 } },
                { resolvedWorkId: 1, releaseId: 101, game: { unreadUpdateCount: 2 } },
                { resolvedWorkId: 2, releaseId: 200, game: { unreadUpdateCount: 20 } },
              ],
            },
          }
        : undefined,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Metadata' }))
    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'Preserved title' } })
    fireEvent.click(await screen.findByRole('button', { name: '2 unread updates' }))
    expect(screen.getByRole('button', { name: 'Updates' }).getAttribute('aria-pressed')).toBe('true')
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Updates' }))
    expect(screen.getByText('Patch one')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Metadata' }))
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Preserved title')
  })
  it('keeps sparse detail sections available without claiming unread updates', async () => {
    setup(mode, (input) =>
      input.route === 'game.details'
        ? { ok: true, status: 200, data: { ...facts, events: [], images: [], acknowledgements: {} } }
        : undefined,
    )
    await screen.findByText('About the game')
    for (const section of ['Overview', 'History', 'Updates', 'Journal', 'Library']) {
      const button = screen.getByRole('button', { name: section })
      expect((button as HTMLButtonElement).disabled).toBe(false)
      fireEvent.click(button)
      expect(button.getAttribute('aria-pressed')).toBe('true')
    }
    expect(screen.queryByRole('button', { name: /unread update/ })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Updates' }))
    expect(screen.getByText('No update signals recorded.')).toBeTruthy()
  })
  it.each(['button', 'Escape'])(
    'returns from metadata to the selected section with its unsaved fields and focus using %s',
    async (action) => {
      const { request } = setup(mode)
      fireEvent.click(screen.getByRole('button', { name: 'Library' }))
      fireEvent.click(screen.getByRole('button', { name: 'Metadata' }))
      const name = await screen.findByLabelText('Name')
      expect(document.activeElement).toBe(name)
      fireEvent.change(name, { target: { value: 'An unfinished title' } })
      if (action === 'button') fireEvent.click(screen.getByRole('button', { name: 'Back to Library' }))
      else {
        const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
        fireEvent(name, event)
        expect(event.defaultPrevented).toBe(true)
      }
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Library' }))
      expect(screen.getByRole('button', { name: 'Library' }).getAttribute('aria-pressed')).toBe('true')
      fireEvent.click(screen.getByRole('button', { name: 'Metadata' }))
      expect(((await screen.findByLabelText('Name')) as HTMLInputElement).value).toBe('An unfinished title')
      expect(request.mock.calls.filter(([input]) => input.route === 'metadata.get')).toHaveLength(1)
      expect(request.mock.calls.some(([input]) => input.route === 'metadata.put')).toBe(false)
    },
  )
  it('returns from game matching without losing the search query or candidates and restores query focus', async () => {
    const { request } = setup(mode, (input) =>
      input.route === 'metadata.search'
        ? { ok: true, status: 200, data: [{ igdbId: 404, name: 'Astral cartographers', platforms: ['PC'] }] }
        : undefined,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Library' }))
    fireEvent.click(screen.getByRole('button', { name: 'Game match' }))
    const input = await screen.findByLabelText('Game title or IGDB ID')
    fireEvent.change(input, { target: { value: 'Astral cartographers' } })
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Search IGDB' }) as HTMLButtonElement).disabled).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Search IGDB' }))
    await screen.findByRole('button', { name: 'Use this match' })
    fireEvent.click(screen.getByRole('button', { name: 'Back to Library' }))
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Library' }))
    fireEvent.click(screen.getByRole('button', { name: 'Game match' }))
    expect((screen.getByLabelText('Game title or IGDB ID') as HTMLInputElement).value).toBe(
      'Astral cartographers',
    )
    expect(document.activeElement).toBe(screen.getByLabelText('Game title or IGDB ID'))
    expect(screen.getByRole('button', { name: 'Use this match' })).toBeTruthy()
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    expect(screen.getByRole('button', { name: 'Library' }).getAttribute('aria-pressed')).toBe('true')
    expect(request.mock.calls.filter(([value]) => value.route === 'metadata.search')).toHaveLength(1)
  })
  it('keeps separate scroll positions when moving between populated detail sections', async () => {
    setup(mode)
    await screen.findByText('About the game')
    const viewport = screen.getByTestId('detail-scroll')
    viewport.scrollTop = 1600
    fireEvent.scroll(viewport)
    fireEvent.click(screen.getByRole('button', { name: 'History' }))
    expect(viewport.scrollTop).toBe(0)
    viewport.scrollTop = 480
    fireEvent.scroll(viewport)
    fireEvent.click(screen.getByRole('button', { name: 'Overview' }))
    expect(viewport.scrollTop).toBe(1600)
    fireEvent.click(screen.getByRole('button', { name: 'History' }))
    expect(viewport.scrollTop).toBe(480)
  })
  it('looks up numeric titles by title and ID, then offers grouping when the match is held elsewhere', async () => {
    const candidate = {
      igdbId: 2064,
      name: '2064: Read Only Memories',
      firstReleaseYear: 2015,
      platforms: ['PC', 'Linux'],
    }
    const { request } = setup(mode, (input) => {
      if (input.route === 'metadata.search') return { ok: true, status: 200, data: [candidate] }
      if (input.route === 'metadata.candidate') return { ok: true, status: 200, data: candidate }
      if (input.route === 'metadata.assign')
        return { ok: true, status: 200, data: { outcome: 'IgdbIdClaimedByAnotherWork' } }
      if (input.route === 'metadata.claiming')
        return {
          ok: true,
          status: 200,
          data: { workId: 2, title: 'Existing edition', firstReleaseYear: 2015 },
        }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Game match' }))
    fireEvent.change(await screen.findByLabelText('Game title or IGDB ID'), { target: { value: '2064' } })
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Search IGDB' }) as HTMLButtonElement).disabled).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Search IGDB' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Use this match' }))
    await screen.findByText('Is this the same game as Existing edition?')
    expect(screen.getAllByRole('button', { name: 'Use this match' })).toHaveLength(1)
    expect(request).toHaveBeenCalledWith({
      route: 'metadata.search',
      params: { title: '2064' },
      body: undefined,
    })
    expect(request).toHaveBeenCalledWith({
      route: 'metadata.candidate',
      params: { igdbId: 2064 },
      body: undefined,
    })
    expect(request).toHaveBeenCalledWith({
      route: 'metadata.assign',
      params: { workId: 1 },
      body: { igdbId: 2064, expectedRevision: 'igdb-1' },
    })
    expect(request.mock.calls.some(([input]) => input.route === 'identity.link')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Yes, group these editions' }))
    await waitFor(() =>
      expect(request.mock.calls.find(([input]) => input.route === 'identity.link')?.[0].body).toMatchObject({
        expectedRevision: 'identity-1',
        parentWorkId: 2,
        childWorkIds: [1],
        kind: 'same_game',
      }),
    )
  })
  it('keeps other metadata drafts when a field saves and advances only unchanged field revisions', async () => {
    let saved = structuredClone(metadata)
    const { request } = setup(mode, (input) => {
      if (input.route === 'metadata.get') return { ok: true, status: 200, data: saved }
      if (input.route === 'metadata.put') {
        const body = input.body as { field: string; value: string }
        saved = {
          ...saved,
          revision: 'metadata-2',
          fields: saved.fields.map((field) =>
            field.field === body.field ? { ...field, value: body.value, source: 'user' } : field,
          ),
        }
        return { ok: true, status: 200, data: { outcome: 'Applied' } }
      }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Metadata' }))
    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'My title' } })
    fireEvent.change(screen.getByLabelText('About'), { target: { value: 'Still unsaved' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))
    await screen.findByText('Saved.')
    expect((screen.getByLabelText('About') as HTMLTextAreaElement).value).toBe('Still unsaved')
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Save about' }) as HTMLButtonElement).disabled).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Save about' }))
    await waitFor(() =>
      expect(request.mock.calls.filter(([input]) => input.route === 'metadata.put').at(-1)?.[0].body).toEqual(
        { field: 'summary', value: 'Still unsaved', expectedRevision: 'metadata-2' },
      ),
    )
  })
  it('retains an in-flight game search across navigation and restores its query focus', async () => {
    let finish!: (value: unknown) => void
    setup(mode, (input) =>
      input.route === 'metadata.search'
        ? new Promise((resolve) => {
            finish = resolve
          })
        : undefined,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Game match' }))
    const input = await screen.findByLabelText('Game title or IGDB ID')
    fireEvent.change(input, { target: { value: 'Remember this search' } })
    await waitFor(() =>
      expect((screen.getByRole('button', { name: 'Search IGDB' }) as HTMLButtonElement).disabled).toBe(false),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Search IGDB' }))
    await waitFor(() => expect(finish).toBeTypeOf('function'))
    fireEvent.click(screen.getByRole('button', { name: 'Overview' }))
    fireEvent.click(screen.getByRole('button', { name: 'Game match' }))
    expect((screen.getByLabelText('Game title or IGDB ID') as HTMLInputElement).disabled).toBe(true)
    finish({
      ok: true,
      status: 200,
      data: [{ igdbId: 14, name: 'Remember this search', firstReleaseYear: 2010, platforms: [] }],
    })
    await screen.findByRole('button', { name: 'Use this match' })
    fireEvent.click(screen.getByRole('button', { name: 'Overview' }))
    fireEvent.click(screen.getByRole('button', { name: 'Game match' }))
    expect(document.activeElement).toBe(screen.getByLabelText('Game title or IGDB ID'))
    expect((screen.getByLabelText('Game title or IGDB ID') as HTMLInputElement).value).toBe(
      'Remember this search',
    )
    expect(screen.getByRole('button', { name: 'Use this match' })).toBeTruthy()
  })
  it('refuses invalid metadata years locally without losing other drafts', async () => {
    const { request } = setup(mode)
    fireEvent.click(screen.getByRole('button', { name: 'Metadata' }))
    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'Still drafting' } })
    fireEvent.change(screen.getByLabelText('Release year'), { target: { value: '1899' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save release year' }))
    await screen.findByText('Enter a release year between 1900 and 2200.')
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Still drafting')
    expect(request.mock.calls.some(([input]) => input.route === 'metadata.put')).toBe(false)
  })
  it('preserves conflicting text until the user explicitly adopts the new saved revision', async () => {
    let revision = 'metadata-1'
    const { request } = setup(mode, (input) => {
      if (input.route === 'metadata.get') return { ok: true, status: 200, data: { ...metadata, revision } }
      if (input.route === 'metadata.put') {
        revision = 'metadata-2'
        return { ok: false, status: 409, message: 'Metadata changed.' }
      }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Metadata' }))
    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'Kept title' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))
    await screen.findByText('This game changed elsewhere. Your name draft is preserved.')
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Kept title')
    expect((screen.getByRole('button', { name: 'Save name' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(await screen.findByRole('button', { name: 'Keep this draft for the next save' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))
    await waitFor(() =>
      expect(request.mock.calls.filter(([input]) => input.route === 'metadata.put').at(-1)?.[0].body).toEqual(
        { field: 'name', value: 'Kept title', expectedRevision: 'metadata-2' },
      ),
    )
  })
  it('imports local art through the backend and rejects images beyond its byte limit', async () => {
    const { request } = setup(mode)
    fireEvent.click(screen.getByRole('button', { name: 'Metadata' }))
    const file = new File(['image bytes'], 'cover.png', { type: 'image/png' })
    fireEvent.change(await screen.findByLabelText('Choose cover art file'), { target: { files: [file] } })
    await waitFor(() =>
      expect(request.mock.calls.find(([input]) => input.route === 'metadata.art-upload')?.[0].body).toEqual({
        field: 'cover_url',
        content: btoa('image bytes'),
        expectedRevision: 'metadata-1',
      }),
    )
    await waitFor(() =>
      expect((screen.getByLabelText('Choose cover art file') as HTMLInputElement).disabled).toBe(false),
    )
    const big = new File(['x'], 'big.png', { type: 'image/png' })
    Object.defineProperty(big, 'size', { value: 16 * 1024 * 1024 + 1 })
    fireEvent.change(screen.getByLabelText('Choose cover art file'), { target: { files: [big] } })
    await screen.findByText('Choose an image no larger than 16 MiB.')
    expect(request.mock.calls.filter(([input]) => input.route === 'metadata.art-upload')).toHaveLength(1)
  })
  it('acknowledges each captured update set and restores stored release watermarks', async () => {
    const { request } = setup(mode)
    fireEvent.click(await screen.findByRole('button', { name: 'Mark these updates read' }))
    await screen.findByText('These update flags are marked read.')
    expect(request).toHaveBeenCalledWith({
      route: 'updates.acknowledge',
      params: { releaseId: 100 },
      body: { observedEventIds: [30] },
    })
    expect(request).toHaveBeenCalledWith({
      route: 'updates.acknowledge',
      params: { releaseId: 200 },
      body: { observedEventIds: [31] },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Restore update flags' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'updates.restore',
        params: { releaseId: 100 },
        body: undefined,
      }),
    )
  })
  it('shows acquisition, identifiers and store readings and can retain a derelict game', async () => {
    const { request } = setup(mode)
    fireEvent.click(screen.getByRole('button', { name: 'Library' }))
    await screen.findByText('C:\\Games\\Original')
    expect(screen.getByText('steam: 480')).toBeTruthy()
    expect(screen.getByText(/50 min/, { selector: 'p' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Keep this game in circulation' }))
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'library.derelict-exemptions',
        params: undefined,
        body: { workIds: [1] },
      }),
    )
  })
  it('opens screenshot browsing and navigates with arrow keys', async () => {
    const { artwork } = setup(mode)
    fireEvent.click(await screen.findByRole('button', { name: 'Open screenshot 1 of 2' }))
    const dialog = await screen.findByRole('dialog')
    expect(screen.getByText('Screenshot 1 of 2')).toBeTruthy()
    fireEvent.keyDown(dialog, { key: 'ArrowRight' })
    expect(screen.getByText('Screenshot 2 of 2')).toBeTruthy()
    await waitFor(() => expect(artwork).toHaveBeenCalledWith('igdb-shot', 'shot_two', 1280))
    fireEvent.click(screen.getByRole('button', { name: 'Close screenshots' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })
  it('switches detail sections with arrows and keeps focus on the chosen section', async () => {
    setup(mode)
    const overview = screen.getByRole('button', { name: 'Overview' })
    overview.focus()
    fireEvent.keyDown(overview, { key: 'ArrowRight' })
    const history = screen.getByRole('button', { name: 'History' })
    expect(history.getAttribute('aria-pressed')).toBe('true')
    expect(document.activeElement).toBe(history)
    fireEvent.keyDown(history, { key: 'Home' })
    expect(document.activeElement).toBe(overview)
    expect(overview.getAttribute('aria-pressed')).toBe('true')
  })
  it('scrolls the screenshot strip sideways and retains wheel input at its edge', async () => {
    setup(mode)
    const strip = await screen.findByLabelText('Screenshots')
    Object.defineProperties(strip, { scrollWidth: { value: 1000 }, clientWidth: { value: 300 } })
    const event = new WheelEvent('wheel', { deltaY: 120, bubbles: true, cancelable: true })
    fireEvent(strip, event)
    expect(strip.scrollLeft).toBe(120)
    expect(strip.scrollTop).toBe(0)
    expect(event.defaultPrevented).toBe(true)
    strip.scrollLeft = 700
    const edge = new WheelEvent('wheel', { deltaY: 120, bubbles: true, cancelable: true })
    fireEvent(strip, edge)
    expect(edge.defaultPrevented).toBe(true)
  })
  it('keeps a new patch out of a pending acknowledgement batch', async () => {
    let finish!: (value: unknown) => void
    const { request, client } = setup(mode, (input) => {
      if (input.route === 'updates.acknowledge' && input.params?.releaseId === 100)
        return new Promise((resolve) => {
          finish = resolve
        })
    })
    fireEvent.click(await screen.findByRole('button', { name: 'Mark these updates read' }))
    await waitFor(() => expect(finish).toBeTypeOf('function'))
    client.setQueryData(['api', 'game.details', { workId: 1 }], {
      ...facts,
      events: [
        ...facts.events,
        {
          id: 99,
          releaseId: 200,
          occurredAt: '2026-09-29T00:00:00Z',
          title: 'Just arrived',
          kind: 'build_change',
        },
      ],
    })
    await screen.findByText('Just arrived')
    finish({ ok: true, status: 200, data: { result: 'Stored' } })
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith({
        route: 'updates.acknowledge',
        params: { releaseId: 200 },
        body: { observedEventIds: [31] },
      }),
    )
    expect(request.mock.calls.filter(([input]) => input.route === 'updates.acknowledge')).toHaveLength(2)
  })
  it('reports a declined update write without claiming that no flags needed changing', async () => {
    setup(mode, (input) =>
      input.route === 'updates.acknowledge'
        ? { ok: true, status: 200, data: { result: 'NotStored' } }
        : undefined,
    )
    fireEvent.click(await screen.findByRole('button', { name: 'Mark these updates read' }))
    await screen.findByText('Some update flags could not be saved. Check the refreshed flags and try again.')
    expect(screen.queryByText('No update flags needed changing.')).toBeNull()
    expect(screen.queryByText('These update flags are marked read.')).toBeNull()
  })
})
