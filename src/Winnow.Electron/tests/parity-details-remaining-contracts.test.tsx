// @vitest-environment jsdom
import { readFileSync } from 'node:fs'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Details } from '../src/renderer/features/Details'
import type { GameDetails, LibraryGame, Mode } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'
import { clearViewState, useViewState } from '../src/renderer/viewState'

const now = '2026-09-08T12:00:00Z'
const candidates = Array.from({ length: 5 }, (_, index) => ({
  igdbId: index + 1,
  name: `The Astral Cartographers ${index + 1}`,
  coverUrl: null,
  firstReleaseYear: 2020 + index,
  platforms: ['PC (Microsoft Windows)', 'PlayStation 5'],
}))
const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  vi.restoreAllMocks()
  for (const key of ['draft:igdb:1', 'igdb:1:error', 'igdb:1:message', 'igdb:1:holder', 'igdb:1:sending'])
    clearViewState(key)
})

function setup(
  mode: Mode,
  options: { title?: string; summary?: string; achievement?: GameDetails['achievements'][number] } = {},
) {
  const title = options.title ?? 'An unplayed game'
  const game: LibraryGame = {
    workId: 1,
    title,
    summary: options.summary,
    bucket: 'never_played',
    playtimeMinutes: 0,
    entries: [
      {
        workId: 1,
        releaseId: 1,
        ownershipId: 1,
        title,
        store: 'steam',
        installed: false,
        playtimeMinutes: 0,
      },
    ],
  }
  const facts: GameDetails = {
    workId: 1,
    readAtUtc: now,
    events: [],
    sessions: {},
    journalEntries: [],
    ratings: [],
    achievements: options.achievement ? [options.achievement] : [],
    images: [{ source: 'igdb', kind: 'screenshot', imageIds: 'aa1,bb2,cc3,dd4,ee5' }],
  }
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      input.route === 'library.get'
        ? { games: [game], lists: [] }
        : input.route === 'library.workspace'
          ? { works: [{ id: 1, name: title }], externalIds: [], pluginActions: {}, epicLaunchKeys: {} }
          : input.route === 'game.details'
            ? facts
            : input.route === 'metadata.igdb'
              ? { workId: 1, mappingRevision: 0, revision: 'igdb-1', pin: null }
              : input.route === 'metadata.get'
                ? { workId: 1, title, revision: 'metadata-1', fields: [], isPinned: false }
                : input.route === 'metadata.search'
                  ? candidates
                  : input.route === 'artwork.backdrops'
                    ? { candidates: [], coverKey: null }
                    : input.route === 'artwork.sources'
                      ? []
                      : {},
  }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, artwork: vi.fn().mockResolvedValue(null) },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  clients.push(client)
  const close = vi.fn()
  let draft: { results?: unknown[] } = {}
  function DraftProbe() {
    ;[draft] = useViewState('draft:igdb:1', {})
    return null
  }
  render(
    <QueryClientProvider client={client}>
      <Details workId={1} mode={mode} presentation="avalon" onClose={close} />
      <DraftProbe />
    </QueryClientProvider>,
  )
  return { request, close, results: () => draft.results }
}
const click = (name: string) => fireEvent.click(screen.getByRole('button', { name }))
const select = (name: string) => fireEvent.click(screen.getByRole('tab', { name }))

describe.each<Mode>(['desktop', 'fullscreen'])('%s remaining frozen Details contracts', (mode) => {
  it.each([
    [0, 0, 0, false, false, 0, 'Not fetched'],
    [1, 0, 0, false, false, 2, 'No achievements'],
    [2, 1, 0, true, false, 3, '0 of 1 unlocked · 0%'],
    [3, 0, 0, false, false, 1, 'Unavailable'],
    [4, 1, 1, true, true, 1, '1 of 1 unlocked · 100% · last known'],
  ] as const)(
    'Both_surfaces_render_ingested_account_states_without_inventing_zero: state %s',
    async (_state, total, unlocked, hasKnownProgress, isStale, availability, expected) => {
      setup(mode, {
        title: 'Achievement fixture',
        achievement: { releaseId: 1, total, unlocked, hasKnownProgress, isStale, availability },
      })
      await screen.findByRole('heading', { name: 'Achievement fixture', level: 1 })
      select('Library')
      const row = screen.getByLabelText('Achievements by release').querySelector('[data-release-id="1"]')!
      expect(row.textContent).toBe(`Achievement fixture · Steam: ${expected}`)
      if (!hasKnownProgress) expect(row.textContent).not.toMatch(/0%|unlocked|last known/)
      expect(screen.getByRole('heading', { name: 'Achievements', level: 3 })).toBeTruthy()
      expect(screen.getByRole('group', { name: 'Achievements by release' })).toBeTruthy()
      expect(
        screen.getByLabelText('Achievements by release').querySelectorAll('[data-release-id]'),
      ).toHaveLength(1)
    },
  )

  it('Wrong_game_menu_focuses_search_and_returns_without_losing_query_or_results', async () => {
    const view = setup(mode)
    await screen.findByRole('heading', { name: 'An unplayed game', level: 1 })
    select('Library')
    click('More')
    click('Wrong game?')
    const query = (await screen.findByLabelText('Game title or IGDB ID')) as HTMLInputElement
    await waitFor(() => expect(document.activeElement).toBe(query))
    fireEvent.change(query, { target: { value: 'Astral cartographers' } })
    fireEvent.submit(query.closest('form')!)
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Use this match' })).toHaveLength(5))
    const results = view.results()
    expect(results).toEqual(candidates)
    click('Back to Library')
    expect(screen.getByRole('tab', { name: 'Library' }).getAttribute('aria-selected')).toBe('true')
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'More' }))
    click('More')
    click('Wrong game?')
    const reopened = (await screen.findByLabelText('Game title or IGDB ID')) as HTMLInputElement
    await waitFor(() => expect(document.activeElement).toBe(reopened))
    expect(reopened.value).toBe('Astral cartographers')
    expect(screen.getAllByRole('button', { name: 'Use this match' })).toHaveLength(5)
    expect(view.results()).toBe(results)
    expect(view.request.mock.calls.filter(([input]) => input.route === 'metadata.search')).toHaveLength(1)
    fireEvent.keyDown(reopened, { key: 'Escape' })
    expect(screen.getByRole('tab', { name: 'Library' }).getAttribute('aria-selected')).toBe('true')
    expect(view.results()).toBe(results)
    expect(view.close).not.toHaveBeenCalled()
  })

  it('Long_overview_keeps_its_scroll_position_when_activity_is_opened', async () => {
    const summary = Array(40)
      .fill('This paragraph explains the places to explore and the choices to make in the game.')
      .join('\n\n')
    setup(mode, { title: 'A game with a long description', summary })
    await screen.findByRole('heading', { name: 'A game with a long description', level: 1 })
    if (mode === 'fullscreen') click('Read more →')
    else click('Read more')
    expect(document.querySelector('.game-summary')?.textContent).toBe(summary)
    const reading = document.querySelector<HTMLElement>('.avalon-details-reading')!
    // Native tests measure the actual end; here the source offset state must survive a region switch.
    reading.scrollTop = 1234
    fireEvent.scroll(reading)
    if (mode === 'desktop') {
      select('Activity')
      select('Overview')
    } else {
      click('Back to Overview')
      click('Play history →')
      click('Back to Overview')
      click('Read more →')
    }
    expect(reading.scrollTop).toBe(1234)
    expect(document.querySelector('.game-summary')?.textContent).toBe(summary)
  })

  it('keeps the lightbox in the document above Details with complete non-placeholder copy and one-layer close', async () => {
    const view = setup(mode)
    await screen.findByRole('heading', { name: 'An unplayed game', level: 1 })
    const details = document.querySelector('.avalon-details')!
    const origin = screen.getByRole('button', { name: 'Open screenshot 2 of 5' })
    origin.focus()
    fireEvent.click(origin)
    const dialog = await screen.findByRole('dialog', { name: 'Screenshot 2 of 5' })
    expect(dialog.ownerDocument).toBe(details.ownerDocument)
    expect(dialog.parentElement).toBe(document.body)
    expect(details.compareDocumentPosition(dialog) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(details.isConnected).toBe(true)
    expect(dialog.querySelector('iframe, webview')).toBeNull()
    const controls = ['Close screenshots', 'Previous screenshot', 'Next screenshot'].map((name) =>
      within(dialog).getByRole('button', { name }),
    )
    const strings = [
      dialog.textContent!,
      screen.getByText('Screenshot 2 of 5').textContent!,
      origin.title,
      ...controls.flatMap((button) => [button.getAttribute('aria-label')!, button.title]),
    ]
    for (const text of strings) {
      expect(text.trim().length).toBeGreaterThan(0)
      expect(text).not.toMatch(/TODO|PLACEHOLDER/i)
    }
    expect(origin.title).toBe('View screenshot 2 of 5')
    expect(document.activeElement).toBe(controls[0])
    fireEvent.keyDown(controls[0], { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Screenshot 2 of 5' })).toBeNull())
    expect(details.isConnected).toBe(true)
    expect(view.close).not.toHaveBeenCalled()
    await waitFor(() => expect(document.activeElement).toBe(origin))
  })
})

it('Keyboard_arrows_switch_tabs_and_leave_focus_on_the_selected_tab', async () => {
  setup('desktop')
  const overview = await screen.findByRole('tab', { name: 'Overview' })
  overview.focus()
  for (const [key, name] of [
    ['ArrowRight', 'Activity'],
    ['ArrowRight', 'Updates'],
    ['ArrowLeft', 'Activity'],
    ['End', 'Library'],
    ['Home', 'Overview'],
  ]) {
    fireEvent.keyDown(document.activeElement!, { key })
    const selected = screen.getByRole('tab', { name })
    expect(selected.getAttribute('aria-selected')).toBe('true')
    expect(document.activeElement).toBe(selected)
    expect(selected.tabIndex).toBe(0)
  }
})

describe('production presentation policies', () => {
  it('The_width_floor_stays_above_the_cards_own_minimum for unmeasured 0 and 1 inputs', () => {
    const css = readFileSync('src/renderer/features/details-layout.css', 'utf8')
    const cap = css.match(/--details-width-cap:\s*clamp\((\d+)px,\s*(\d+)vw,\s*(\d+)px\)/)!
    expect(cap).not.toBeNull()
    const [, least, fraction, most] = cap.map(Number)
    for (const input of [0, 1])
      expect(Math.max(least, Math.min((input * fraction) / 100, most))).toBeGreaterThan(700)
    expect(css).toContain('width: min(calc(100vw - 80px), var(--details-width-cap))')
    expect(css).toContain('box-shadow: 0 2px 0 var(--accent-foreground, var(--accent))')
  })
  it('Control_fills_use_theme_tokens_without_deferred_runtime_bindings', () => {
    const css = readFileSync('src/renderer/features/parity-details.css', 'utf8')
    expect(css).toContain(
      'background: var(--avalon-lightbox-fill, color-mix(in srgb, var(--surface) 70%, transparent))',
    )
    expect(css).toContain(
      'background: var(--avalon-lightbox-active-fill, color-mix(in srgb, var(--raised) 85%, transparent))',
    )
    expect(css).not.toMatch(/expression\(|javascript:/i)
  })
})
