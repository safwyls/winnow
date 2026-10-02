// @vitest-environment jsdom
import { useState } from 'react'
import { readFileSync } from 'node:fs'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Details } from '../src/renderer/features/Details'
import { IgdbMatch } from '../src/renderer/features/igdb-match'
import { ManualEditor } from '../src/renderer/features/ManualEditor'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'
import type { Mode } from '../src/renderer/api/types'

const wrongCover = 'https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg'
const rightCover = 'https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg'
const candidate = {
  igdbId: 5678,
  name: 'Prey',
  coverUrl: rightCover,
  firstReleaseYear: 2017,
  platforms: ['PC (Microsoft Windows)', 'PlayStation 4'],
}
const initialGame = {
  workId: 1,
  title: 'Prey',
  bucket: 'bounced',
  firstReleaseYear: 2006,
  publisher: '2K Games',
  summary: 'A Cherokee garage mechanic is abducted.',
  coverUrl: wrongCover,
  playtimeMinutes: 120,
  lastPlayedAt: '2024-01-02T00:00:00Z',
  entries: [
    {
      ownershipId: 10,
      releaseId: 100,
      workId: 1,
      title: 'Prey',
      store: 'gog',
      installed: false,
      playtimeMinutes: 120,
      lastPlayedAt: '2024-01-02T00:00:00Z',
    },
  ],
}
const detail = {
  workId: 1,
  readAtUtc: '2026-09-01T00:00:00Z',
  events: [],
  sessions: {},
  journalEntries: [],
  ratings: [],
  achievements: [],
  images: [],
  ownerships: [{ id: 10, releaseId: 100, store: 'gog' }],
  history: { 10: [{ id: 1, playtimeMinutes: 120, observedAt: '2026-09-01T00:00:00Z' }] },
}
const clients: QueryClient[] = []
const ok = (data: unknown) => ({ ok: true, status: 200, data })
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  clearViewState('draft:manual:new')
  clearViewState('draft:igdb:1')
  for (const key of ['holder', 'offer-revision', 'sending', 'operation', 'message', 'error'])
    clearViewState(`igdb:1:${key}`)
})

// This bridge fixture holds the frozen source facts. Native/API tests separately establish
// database pinning, live artwork precedence, and real persistent identity links.
function setup(
  mode: Mode,
  options: {
    surface?: 'avalon' | 'shared' | 'match' | 'settings'
    available?: boolean
    claimed?: boolean
    namedHolder?: boolean
    refuseLink?: boolean
    searchUnavailable?: boolean
    candidates?: (typeof candidate)[]
  } = {},
) {
  let assigned = false,
    linked = false
  let pin: { igdbId: number } | null = null
  let available = options.available
  const onClose = vi.fn()
  const request = vi.fn(async (input: ApiRequest) => {
    const game = assigned
      ? {
          ...initialGame,
          firstReleaseYear: 2017,
          publisher: 'Bethesda Softworks',
          summary: 'Morgan Yu wakes on Talos I.',
          coverUrl: rightCover,
        }
      : initialGame
    switch (input.route) {
      case 'library.get':
        return ok({ games: [game], lists: [] })
      case 'library.workspace':
        return ok({
          preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'AdultsOnly' },
          works: [{ id: 1, name: 'Prey', igdbId: assigned ? 5678 : 1234 }],
          externalIds: [],
          pluginActions: {},
          epicLaunchKeys: {},
          identityLinks: [],
        })
      case 'game.details':
        return ok(detail)
      case 'journal.preferences.get':
        return ok({ promptAfterPlay: false })
      case 'metadata.igdb':
        return ok({
          workId: 1,
          revision: assigned || linked ? 'match-2' : 'match-1',
          mappingRevision: assigned ? 2 : 1,
          pin,
          available,
        })
      case 'metadata.search':
        return options.searchUnavailable ? ok([]) : ok(options.candidates ?? [candidate])
      case 'metadata.assign':
        if (options.claimed) return ok({ outcome: 'IgdbIdClaimedByAnotherWork' })
        assigned = true
        pin = { igdbId: 5678 }
        return ok({ outcome: 'Assigned' })
      case 'metadata.claiming':
        return options.namedHolder
          ? ok({ workId: 2, title: 'Prey', firstReleaseYear: 2017, coverUrl: rightCover })
          : { ok: false, status: 404 }
      case 'identity.get':
        return ok({ revision: 'identity-1' })
      case 'identity.link':
        if (options.refuseLink)
          return {
            ok: false,
            status: 409,
            message: 'The chosen parent is already a child.',
            data: { refusal: 'ParentIsAlreadyAChild' },
          }
        linked = true
        return ok({ revision: 'identity-2', actId: 1 })
      case 'metadata.clear':
        pin = null
        return ok(true)
      case 'artworkState':
        return ok({
          current: { previewKey: { provider: 'igdb', id: assigned ? 'co2abc' : 'co1r76' } },
          revision: assigned ? 'art-2' : 'art-1',
        })
      default:
        return ok({})
    }
  })
  const artwork = vi.fn().mockResolvedValue('data:image/png;base64,aA==')
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, artwork, cancelRequest: vi.fn(), openExternal: vi.fn() },
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } },
  })
  clients.push(client)
  function View() {
    const [open, setOpen] = useState(true)
    const close = () => {
      onClose()
      setOpen(false)
    }
    return (
      <div className={`mode-${mode}`}>
        {open && mode === 'fullscreen' && <button onClick={close}>Close host details</button>}
        {!open ? (
          <button onClick={() => setOpen(true)}>Reopen Prey</button>
        ) : options.surface === 'match' ? (
          <IgdbMatch workId={1} title="Prey" />
        ) : options.surface === 'settings' ? (
          <ManualEditor initial={null} onClose={close} />
        ) : (
          <Details workId={1} mode={mode} presentation={options.surface ?? 'avalon'} onClose={close} />
        )}
      </div>
    )
  }
  const view = render(
    <QueryClientProvider client={client}>
      <View />
    </QueryClientProvider>,
  )
  return {
    ...view,
    request,
    artwork,
    client,
    onClose,
    setAvailable: async (value: boolean) => {
      available = value
      await act(async () => {
        await client.invalidateQueries({ queryKey: ['api', 'metadata.igdb'] })
      })
    },
  }
}
function click(name: string) {
  fireEvent.click(screen.getByRole('button', { name }))
}
async function openMatch(surface: 'avalon' | 'shared' = 'avalon') {
  if (surface === 'shared') click('Game match')
  else {
    click('More')
    click('Wrong game?')
  }
  await waitFor(() =>
    expect((screen.getByRole('button', { name: 'Search IGDB' }) as HTMLButtonElement).disabled).toBe(false),
  )
}
async function search() {
  await waitFor(() =>
    expect((screen.getByRole('button', { name: 'Search IGDB' }) as HTMLButtonElement).disabled).toBe(false),
  )
  click('Search IGDB')
  return screen.findByRole('button', { name: 'Use this match' })
}
async function offer() {
  fireEvent.click(await search())
  return screen.findByRole('heading', { name: 'Is this the same game as Prey?' })
}

describe.each<Mode>(['desktop', 'fullscreen'])('%s frozen IGDB assignment modal contracts', (mode) => {
  it.each(['Overview', 'Activity', 'Updates', 'Journal', 'Library'])(
    'Choosing_a_candidate_rewrites_the_metadata_and_the_cover_and_preserves_the_tab: %s',
    async (section) => {
      const view = setup(mode)
      const title = await screen.findByRole('heading', { name: 'Prey', level: 1 })
      const frame = title.closest('.avalon-details')
      expect(screen.getByText('2006 · 2K Games')).toBeTruthy()
      if (mode === 'fullscreen' && section === 'Activity') click('Play history →')
      else fireEvent.click(screen.getByRole('tab', { name: section }))
      await openMatch()
      expect((screen.getByLabelText('Game title or IGDB ID') as HTMLInputElement).value).toBe('Prey')
      const choose = await search()
      const row = choose.closest('article')!
      expect(row.querySelector('.igdb-candidate-year')?.textContent).toBe('2017 · ')
      expect(row.querySelector('.igdb-candidate-platforms')?.textContent).toBe(
        'PC (Microsoft Windows), PlayStation 4',
      )
      expect(view.artwork).toHaveBeenCalledWith('igdb', 'co2abc', 100)
      fireEvent.click(choose)
      await screen.findByText('Now using Prey.')
      expect(screen.getByRole('heading', { name: 'Prey', level: 1 }).closest('.avalon-details')).toBe(frame)
      expect(screen.getByText('2017 · Bethesda Softworks')).toBeTruthy()
      expect(screen.queryByRole('alert')).toBeNull()
      if (mode === 'fullscreen' && section === 'Activity')
        expect(screen.getByRole('region', { name: 'History' })).toBeTruthy()
      else expect(screen.getByRole('tab', { name: section }).getAttribute('aria-selected')).toBe('true')
      const snapshot = view.client.getQueryData<{ games: (typeof initialGame)[] }>(['api', 'library.get'])!
      expect(snapshot.games[0]).toMatchObject({
        firstReleaseYear: 2017,
        publisher: 'Bethesda Softworks',
        summary: 'Morgan Yu wakes on Talos I.',
        coverUrl: rightCover,
        entries: [{ ownershipId: 10 }],
      })
      expect(view.request).toHaveBeenCalledWith(
        expect.objectContaining({
          route: 'metadata.assign',
          params: { workId: 1 },
          body: { igdbId: 5678, expectedRevision: 'match-1' },
        }),
      )
      await openMatch()
      expect(await screen.findByRole('button', { name: 'Return to automatic matching' })).toBeTruthy()
      expect(
        view.request.mock.calls.filter(([input]) => input.route === 'metadata.igdb').length,
      ).toBeGreaterThanOrEqual(2)
      if (mode === 'fullscreen')
        fireEvent.keyDown(screen.getByLabelText('Game title or IGDB ID'), { key: 'Escape' })
      else click(`Back to ${section}`)
      // Closing the modal, rather than switching away from its matching tool, starts a new opening.
      if (mode === 'fullscreen') {
        if (section === 'Activity') click('Back to Overview')
        click('Close host details')
      } else click('Close game details')
      await screen.findByRole('button', { name: 'Reopen Prey' })
      click('Reopen Prey')
      expect((await screen.findByRole('tab', { name: 'Overview' })).getAttribute('aria-selected')).toBe(
        'true',
      )
    },
  )

  it('A_second_game_cannot_claim_an_entry_another_game_already_holds', async () => {
    const view = setup(mode, { claimed: true })
    await screen.findByRole('heading', { name: 'Prey', level: 1 })
    await openMatch()
    fireEvent.click(await search())
    expect((await screen.findByRole('alert')).textContent).toContain(
      'Another game in your library already uses that IGDB entry.',
    )
    expect(screen.getByText('2006 · 2K Games')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Return to automatic matching' })).toBeNull()
    expect(view.request.mock.calls.some(([input]) => input.route === 'identity.link')).toBe(false)
  })
  it('The_assignment_can_be_cleared_and_the_pin_goes_with_it', async () => {
    const view = setup(mode)
    await screen.findByRole('heading', { name: 'Prey', level: 1 })
    await openMatch()
    fireEvent.click(await search())
    await screen.findByText('Now using Prey.')
    await openMatch()
    click('Return to automatic matching')
    await screen.findByText('Returned to automatic metadata matching.')
    await openMatch()
    expect(screen.queryByRole('button', { name: 'Return to automatic matching' })).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
    expect(view.request).toHaveBeenCalledWith(
      expect.objectContaining({
        route: 'metadata.clear',
        params: { workId: 1 },
        body: { expectedRevision: 'match-2' },
      }),
    )
  })
  it.each(['avalon', 'shared', 'match'] as const)('No_service_means_no_control: %s', async (surface) => {
    const view = setup(mode, { surface, available: false })
    await waitFor(() =>
      expect(view.client.getQueryData(['api', 'metadata.igdb', { workId: 1 }])).toMatchObject({
        available: false,
      }),
    )
    if (surface === 'avalon') {
      await screen.findByRole('button', { name: 'More' })
      click('More')
    }
    expect(screen.queryByRole('button', { name: 'Wrong game?' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Game match' })).toBeNull()
    expect(screen.queryByLabelText('Game title or IGDB ID')).toBeNull()
    expect(view.request.mock.calls.some(([input]) => input.route === 'metadata.search')).toBe(false)
  })
  it.each([true, undefined])(
    'a configured service without usable credentials remains visible, available=%s',
    async (available) => {
      setup(mode, { available, searchUnavailable: true })
      await screen.findByRole('heading', { name: 'Prey', level: 1 })
      await openMatch()
      click('Search IGDB')
      expect(await screen.findByText('No matching games. Try a different title or an IGDB ID.')).toBeTruthy()
    },
  )
  it.each(['avalon', 'shared'] as const)(
    'removing the optional service closes an already open %s tool',
    async (surface) => {
      const view = setup(mode, { surface, available: true })
      await screen.findByRole('heading', { name: 'Prey', level: 1 })
      await openMatch(surface)
      await view.setAvailable(false)
      await waitFor(() => expect(screen.queryByLabelText('Game title or IGDB ID')).toBeNull())
      const overview = screen.getByRole(surface === 'avalon' ? 'tab' : 'button', { name: 'Overview' })
      expect(overview.getAttribute(surface === 'avalon' ? 'aria-selected' : 'aria-pressed')).toBe('true')
    },
  )
  it('Accepting_the_offer_writes_a_same_game_link_and_pins_nothing', async () => {
    const view = setup(mode, { surface: 'match', claimed: true, namedHolder: true })
    await offer()
    expect(screen.getAllByText('2017').length).toBeGreaterThan(0)
    click('Yes, group these editions')
    await screen.findByText('Linked with Prey.')
    expect(view.request).toHaveBeenCalledWith(
      expect.objectContaining({
        route: 'identity.link',
        body: {
          expectedRevision: 'identity-1',
          parentWorkId: 2,
          childWorkIds: [1],
          kind: 'same_game',
          relationLabel: null,
          rejectedCandidateIds: [],
          refusedPairs: [],
        },
      }),
    )
    expect(view.request.mock.calls.filter(([input]) => input.route === 'metadata.assign')).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Return to automatic matching' })).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
  })
  it('Declining_the_offer_leaves_both_games_as_they_were', async () => {
    const view = setup(mode, { surface: 'match', claimed: true, namedHolder: true })
    await offer()
    const before = view.request.mock.calls.length
    click('Keep them separate')
    expect(screen.getByRole('alert').textContent).toContain(
      'Another game in your library already uses that IGDB entry.',
    )
    expect(screen.getByRole('button', { name: 'Use this match' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Yes, group these editions' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Return to automatic matching' })).toBeNull()
    expect(view.request.mock.calls).toHaveLength(before)
  })
  it('A_refused_details_link_stays_in_the_modal', async () => {
    const view = setup(mode, { claimed: true, namedHolder: true, refuseLink: true })
    await screen.findByRole('heading', { name: 'Prey', level: 1 })
    await openMatch()
    const claim = await offer()
    click('Yes, group these editions')
    expect((await screen.findByRole('alert')).textContent).toContain("Couldn't link those. Nothing changed.")
    expect(screen.getByRole('heading', { name: 'Is this the same game as Prey?' })).toBe(claim)
    expect(
      (screen.getByRole('button', { name: 'Yes, group these editions' }) as HTMLButtonElement).disabled,
    ).toBe(true)
    expect(screen.getByRole('button', { name: 'Refresh saved match' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Use this match' })).toBeTruthy()
    expect(screen.getByText('2006 · 2K Games')).toBeTruthy()
    expect(view.onClose).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: 'Return to automatic matching' })).toBeNull()
  })
  it('shared Details keeps its selected section through assignment and resets when reopened', async () => {
    setup(mode, { surface: 'shared' })
    await screen.findByRole('heading', { name: 'Prey', level: 1 })
    click('Library')
    await openMatch('shared')
    fireEvent.click(await search())
    await screen.findByText('Now using Prey.')
    expect(screen.getByRole('button', { name: 'Library' }).getAttribute('aria-pressed')).toBe('true')
    click('Back to your library')
    click('Reopen Prey')
    expect(screen.getByRole('button', { name: 'Overview' }).getAttribute('aria-pressed')).toBe('true')
  })
})

const candidateCss = readFileSync('src/renderer/features/igdb-candidate.css', 'utf8')
function rule(selector: string) {
  const start = candidateCss.indexOf(`${selector} {`)
  expect(start).toBeGreaterThanOrEqual(0)
  return candidateCss.slice(start, candidateCss.indexOf('}', start))
}
describe.each<Mode>(['desktop', 'fullscreen'])('%s frozen candidate row source guards', (mode) => {
  describe.each(['match', 'settings'] as const)('%s candidate rows', (surface) => {
    async function row() {
      const view = setup(mode, { surface })
      if (surface === 'match') await search()
      else {
        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Prey' } })
        click('Find IGDB matches')
        await screen.findByRole('button', { name: 'Use these details' })
      }
      return { ...view, row: screen.getByRole('article', { name: 'Prey' }) }
    }
    it('Every_child_of_the_candidate_row_declares_its_column', async () => {
      const view = await row()
      expect([...view.row.children].map((node) => node.tagName)).toEqual(['SPAN', 'DIV', 'BUTTON'])
      expect(rule('.igdb-candidate-cover')).toContain('grid-column: 1;')
      expect(rule('.igdb-candidate-text')).toContain('grid-column: 2;')
      expect(rule('.igdb-candidate-row > button')).toContain('grid-column: 3;')
      expect(view.row.children[0].className).toBe('igdb-candidate-cover')
      expect(view.row.children[1].className).toBe('igdb-candidate-text')
      await waitFor(() => expect(view.row.querySelector('img')).not.toBeNull())
      expect(view.artwork).toHaveBeenCalledWith('igdb', 'co2abc', 100)
    })
    it('The_candidate_row_gives_the_text_the_star_column_and_the_button_the_last', async () => {
      const view = await row()
      expect(rule('.igdb-candidate-row')).toContain('display: grid;')
      expect(rule('.igdb-candidate-row')).toContain('grid-template-columns: 34px minmax(0, 1fr) auto;')
      expect(rule('.igdb-candidate-cover')).toContain('height: 51px;')
      expect(rule('.igdb-candidates')).toContain('max-height: 238px;')
      expect(view.row.lastElementChild).toBe(
        within(view.row).getByRole('button', {
          name: surface === 'match' ? 'Use this match' : 'Use these details',
        }),
      )
    })
    it('The_detail_line_is_a_grid_and_not_a_horizontal_stack', async () => {
      const view = await row()
      expect(rule('.igdb-candidate-row .igdb-candidate-detail')).toContain('display: grid;')
      expect(rule('.igdb-candidate-row .igdb-candidate-detail')).toContain(
        'grid-template-columns: auto minmax(0, 1fr);',
      )
      expect(view.row.querySelector('.igdb-candidate-detail')?.textContent).toBe(
        '2017 · PC (Microsoft Windows), PlayStation 4',
      )
    })
    it('The_platforms_trim_inside_the_star_column_and_keep_their_full_value', async () => {
      const view = await row()
      expect(rule('.igdb-candidate-platforms')).toContain('grid-column: 2;')
      expect(rule('.igdb-candidate-platforms')).toContain('text-overflow: ellipsis;')
      expect(rule('.igdb-candidate-platforms')).toContain('white-space: nowrap;')
      expect(rule('.igdb-candidate-platforms')).toContain('overflow: hidden;')
      expect(rule('.igdb-candidate-platforms')).toContain('min-width: 0;')
      expect(rule('.igdb-candidate-year')).toContain('grid-column: 1;')
      const platforms = view.row.querySelector('.igdb-candidate-platforms')!
      expect(platforms.getAttribute('title')).toBe(candidate.platforms.join(', '))
      expect(platforms.textContent).toBe(candidate.platforms.join(', '))
    })
  })
})
