// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { LibraryGame } from '../src/renderer/api/types'
import { AvalonFilterPanel } from '../src/renderer/themes/avalon-filter-panel'
import { openFilterGroup, libraryRole } from './library-controls'
import {
  avalonFacts,
  avalonRuleOptions,
  avalonYearRange,
  matchesAvalonRules,
  type AvalonWorkspace,
} from '../src/renderer/themes/avalon-filters'

const games: LibraryGame[] = [
  {
    workId: 1,
    title: 'Alpha',
    firstReleaseYear: 2015,
    bucket: 'active',
    playtimeMinutes: 50,
    entries: [
      {
        workId: 1,
        releaseId: 10,
        ownershipId: 1,
        title: 'Alpha',
        installed: true,
        playtimeMinutes: 50,
        store: 'steam',
      },
      {
        workId: 1,
        releaseId: 11,
        ownershipId: 2,
        title: 'Alpha',
        installed: false,
        playtimeMinutes: 0,
        store: 'gog',
      },
    ],
  },
  {
    workId: 2,
    title: 'Bravo',
    firstReleaseYear: 2025,
    bucket: 'never_played',
    playtimeMinutes: 0,
    entries: [
      {
        workId: 2,
        releaseId: 20,
        ownershipId: 3,
        title: 'Bravo',
        installed: false,
        playtimeMinutes: 0,
        store: 'xbox',
      },
    ],
  },
  { workId: 3, title: 'Charlie', bucket: 'never_played', playtimeMinutes: 0, entries: [] },
]
const workspace: AvalonWorkspace = {
  facets: [
    { id: 1, kind: 'genre', slug: 'rpg', name: 'RPG' },
    { id: 2, kind: 'genre', slug: 'adventure', name: 'Adventure' },
    { id: 3, kind: 'theme', slug: 'fantasy', name: 'Fantasy' },
    { id: 4, kind: 'game_mode', slug: 'co_op', name: 'Co-op' },
  ],
  releaseFacets: [
    { releaseId: 10, facetIds: [1], gameModes: [] },
    { releaseId: 11, facetIds: [2, 3], gameModes: ['co_op'] },
    { releaseId: 20, facetIds: [2], gameModes: [] },
  ],
  buckets: [
    { releaseId: 10, resolvedWorkId: 1, majorUpdateAt: '2026-09-01', game: { unreadUpdateCount: 1 } },
    { releaseId: 11, resolvedWorkId: 1, game: { unreadUpdateCount: 1 } },
    { releaseId: 20, resolvedWorkId: 2, game: { unreadUpdateCount: 0 } },
  ],
}
const facts = avalonFacts(games, workspace)
afterEach(cleanup)

describe('Avalon library filters', () => {
  it('compacts lone fullscreen refine rows before moving to Apply and clamps upward rows while paired arrows stay horizontal', () => {
    const matrix: AvalonWorkspace = {
      facets: ['genre', 'theme', 'game_mode', 'tag', 'feature', 'controller'].map((kind, index) => ({
        id: index + 1,
        kind,
        slug: kind === 'game_mode' ? 'co_op' : kind,
        name: kind,
      })),
      releaseFacets: [{ releaseId: 10, facetIds: [1, 2, 3, 4, 5, 6], gameModes: ['co_op'] }],
    }
    render(
      <AvalonFilterPanel
        filter={{}}
        games={games}
        facts={avalonFacts(games, matrix)}
        workspace={matrix}
        fullscreen
        apply={vi.fn()}
        close={vi.fn()}
      />,
    )
    const disk = screen.getByRole('button', { name: 'ON DISK · Any' })
    expect(disk.dataset.filterRow).toBe('7')
    disk.focus()
    fireEvent.keyDown(disk, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Apply filters' }))
    const cancel = screen.getByRole('button', { name: 'Cancel' })
    cancel.focus()
    fireEvent.keyDown(cancel, { key: 'ArrowUp' })
    expect(document.activeElement).toBe(disk)
    for (let i = 0; i < 4; i++) fireEvent.keyDown(document.activeElement!, { key: 'ArrowUp' })
    const year = screen.getByRole('button', { name: 'Release year to · Any' })
    expect(document.activeElement).toBe(year)
    fireEvent.keyDown(year, { key: 'ArrowRight' })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'STORE TAG · Any' }))
    fireEvent.keyDown(document.activeElement!, { key: 'ArrowLeft' })
    expect(document.activeElement).toBe(year)
  })

  it('uses authoritative unread facts independently of the derived bucket', () => {
    expect(facts.get(1)?.unread).toBe(true)
    expect(facts.get(2)?.unread).toBe(false)
    expect(facts.get(1)?.watermarks.get(10)).toBe('2026-09-01')
    expect(matchesAvalonRules(games[0], { hasUnread: true }, facts.get(1))).toBe(true)
  })
  it('unions facets from linked releases, ORs each group and ANDs different groups', () => {
    expect(
      matchesAvalonRules(
        games[0],
        { genreIds: [1, 99], themeIds: [3], gameModes: ['co_op'], stores: ['GOG'] },
        facts.get(1),
      ),
    ).toBe(true)
    expect(matchesAvalonRules(games[0], { genreIds: [1], themeIds: [99] }, facts.get(1))).toBe(false)
    expect(matchesAvalonRules(games[1], { genreIds: [2], gameModes: ['co_op'] }, facts.get(2))).toBe(false)
  })
  it('counts games once per option and removes only that option group for residual counts', () => {
    const options = avalonRuleOptions(games, facts, workspace, { genreIds: [1], themeIds: [3] }, 'genreIds')
    expect(options.map(({ value, count }) => [value, count])).toEqual([
      [2, 1],
      [1, 1],
    ])
    const stores = avalonRuleOptions(games, facts, workspace, { stores: ['gog'], genreIds: [1] }, 'stores')
    expect(stores.map(({ value, count }) => [value, count])).toEqual([
      ['gog', 1],
      ['steam', 1],
      ['xbox', 0],
    ])
  })
  it('keeps missing saved rules restrictive and explicitly clearable with zero counts', () => {
    expect(matchesAvalonRules(games[0], { genreIds: [999] }, facts.get(1))).toBe(false)
    const options = avalonRuleOptions(games, facts, workspace, { genreIds: [999] }, 'genreIds')
    expect(options.find((option) => option.value === 999)).toEqual({
      value: 999,
      label: 'Unavailable saved filter (999)',
      count: 0,
      missing: true,
    })
    expect(matchesAvalonRules(games[0], { stores: ['removed-provider'] }, facts.get(1))).toBe(false)
  })
  it('includes provider stores from the visible library instead of a fixed store catalogue', () => {
    expect(
      avalonRuleOptions(games, facts, workspace, {}, 'stores').find((option) => option.value === 'xbox')
        ?.count,
    ).toBe(1)
    expect(
      games
        .filter((game) => matchesAvalonRules(game, { stores: ['xbox'] }, facts.get(game.workId)))
        .map((game) => game.workId),
    ).toEqual([2])
  })
  it('excludes unknown release years from bounded ranges', () => {
    expect(
      games
        .filter((game) => matchesAvalonRules(game, { yearFrom: 2010, yearTo: 2020 }, facts.get(game.workId)))
        .map((game) => game.workId),
    ).toEqual([1])
    expect(games.filter((game) => matchesAvalonRules(game, {}, facts.get(game.workId)))).toHaveLength(3)
  })
  it.each([
    ['999', '2020', false],
    ['1000', '9999', true],
    ['9999', '9999', true],
    ['nope', '2020', false],
    ['', '', true],
    ['2030', '2020', false],
    [' 1000 ', ' 9999 ', true],
  ])('matches the original year validator for %s to %s', (from, to, valid) => {
    expect(avalonYearRange(String(from), String(to)) !== null).toBe(valid)
  })
})

describe.each([false, true])('Avalon filter panel fullscreen=%s', (fullscreen) => {
  function option(name: string | RegExp, group = 'GENRE') {
    if (fullscreen && !screen.queryByRole('button', { name })) openFilterGroup(group)
    return screen.getByRole(fullscreen ? 'button' : 'checkbox', { name }) as HTMLInputElement
  }
  function checked(element: HTMLInputElement) {
    return fullscreen ? element.getAttribute('aria-pressed') === 'true' : element.checked
  }
  it.each([
    ['gog', 'GOG'],
    ['plugin:xbox', 'Xbox'],
  ])('hides empty metadata and a universal store then exposes the arriving %s provider', (store, label) => {
    const steam = {
      ...games[0],
      firstReleaseYear: null,
      entries: [{ ...games[0].entries[0], releaseId: 900 }],
    }
    const initial = [steam],
      apply = vi.fn(),
      close = vi.fn()
    const props = {
      filter: {},
      games: initial,
      allGames: initial,
      facts: avalonFacts(initial, workspace),
      workspace,
      fullscreen,
      apply,
      close,
    }
    const view = render(<AvalonFilterPanel {...props} />)
    expect(screen.queryByText('Genres')).toBeNull()
    expect(screen.queryByText('Tags')).toBeNull()
    expect(screen.queryByText('Stores')).toBeNull()
    expect(screen.queryByRole('textbox', { name: 'From this year' })).toBeNull()
    expect(screen.getByText('No game metadata is available to filter yet.')).toBeDefined()
    expect(
      screen.getByRole(fullscreen ? 'button' : 'combobox', {
        name: fullscreen ? /^ON DISK ·/ : 'Installation',
      }),
    ).toBeDefined()
    expect(screen.queryByRole('combobox', { name: 'Update status' })).toBeNull()
    expect(screen.queryByText('Library status')).toBeNull()
    const next = [
      ...initial,
      { ...games[1], firstReleaseYear: null, entries: [{ ...games[1].entries[0], store, releaseId: 901 }] },
    ]
    view.rerender(
      <AvalonFilterPanel {...props} games={next} allGames={next} facts={avalonFacts(next, workspace)} />,
    )
    fireEvent.click(option(`${label}, 1 matching title`, 'PLATFORM'))
    expect(option('Steam, 1 matching title', 'PLATFORM')).toBeDefined()
    if (fullscreen) fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    expect(apply.mock.lastCall?.[0].stores).toEqual([store])
    const nextFacts = avalonFacts(next, workspace)
    expect(
      next
        .filter((game) => matchesAvalonRules(game, apply.mock.lastCall![0], nextFacts.get(game.workId)))
        .map((game) => game.title),
    ).toEqual(['Bravo'])
  })
  it('keeps known saved zero-count facets named and clearable even when no title carries them', () => {
    const empty = [{ ...games[2], entries: [] }],
      apply = vi.fn()
    render(
      <AvalonFilterPanel
        filter={{ genreIds: [1], stores: ['gog'] }}
        games={empty}
        facts={avalonFacts(empty, workspace)}
        workspace={workspace}
        fullscreen={fullscreen}
        apply={apply}
        close={vi.fn()}
      />,
    )
    const rpg = option('RPG, 0 matching titles')
    expect(checked(rpg)).toBe(true)
    expect(rpg.disabled).toBe(false)
    fireEvent.click(rpg)
    if (fullscreen) fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    expect(apply.mock.lastCall?.[0]).toMatchObject({ genreIds: [], stores: ['gog'] })
  })
  it('freezes common-first ordering while residual zero options disable', () => {
    const two = [games[0], games[1], { ...games[2], entries: [{ ...games[1].entries[0], releaseId: 30 }] }],
      matrix: AvalonWorkspace = {
        facets: [
          { id: 1, kind: 'genre', slug: 'rpg', name: 'RPG' },
          { id: 2, kind: 'genre', slug: 'shooter', name: 'Shooter' },
          { id: 3, kind: 'game_mode', slug: 'co_op', name: 'Co-op' },
          { id: 4, kind: 'game_mode', slug: 'single', name: 'Single player' },
        ],
        releaseFacets: [
          { releaseId: 10, facetIds: [1], gameModes: ['co_op'] },
          { releaseId: 20, facetIds: [2], gameModes: ['single'] },
          { releaseId: 30, facetIds: [2], gameModes: ['single'] },
        ],
      },
      apply = vi.fn()
    render(
      <AvalonFilterPanel
        filter={{}}
        games={two}
        facts={avalonFacts(two, matrix)}
        workspace={matrix}
        fullscreen={fullscreen}
        apply={apply}
        close={vi.fn()}
      />,
    )
    const genreOrder = () => {
      if (fullscreen) {
        if (document.querySelector('[data-filter-page]')?.getAttribute('data-filter-page') !== 'genreIds')
          openFilterGroup('GENRE')
        return document.querySelectorAll(
          '.fullscreen-filter-choices button[aria-pressed] > span:nth-child(2)',
        )
      }
      return [...document.querySelectorAll('details')]
        .find((group) => group.querySelector('summary')?.textContent.startsWith('Genres'))!
        .querySelectorAll('.avalon-filter-options label > span')
    }
    const before = [...genreOrder()].map((node) => node.textContent)
    expect(before).toEqual(['Shooter', 'RPG'])
    fireEvent.click(option('Co-op, 1 matching title', 'GAME MODE'))
    expect(option('Shooter, 0 matching titles').disabled).toBe(true)
    fireEvent.click(option('RPG, 1 matching title'))
    fireEvent.click(option('Co-op, 1 matching title', 'GAME MODE'))
    // Select the other mode before RPG so its selected zero remains a way out.
    fireEvent.click(option('RPG, 1 matching title'))
    fireEvent.click(option('Single player, 2 matching titles', 'GAME MODE'))
    expect([...genreOrder()].map((node) => node.textContent)).toEqual(before)
    expect(option('RPG, 0 matching titles').disabled).toBe(true)
  })
  it('keeps a checked zero-count option enabled so a restored empty cut can be widened', () => {
    const apply = vi.fn()
    render(
      <AvalonFilterPanel
        filter={{ genreIds: [1], stores: ['xbox'] }}
        games={games}
        facts={facts}
        workspace={workspace}
        fullscreen={fullscreen}
        apply={apply}
        close={vi.fn()}
      />,
    )
    const selected = option('RPG, 0 matching titles')
    expect(checked(selected)).toBe(true)
    expect(selected.disabled).toBe(false)
    fireEvent.click(selected)
    if (fullscreen) fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    const filter = apply.mock.lastCall![0]
    expect(
      games
        .filter((game) => matchesAvalonRules(game, filter, facts.get(game.workId)))
        .map((game) => game.title),
    ).toEqual(['Bravo'])
  })
  it.each([
    ['999', '2020', false],
    ['1000', '9999', true],
    ['9999', '9999', true],
    ['nope', '2020', false],
    ['', '', true],
    ['2030', '2020', false],
    [' 1000 ', ' 9999 ', true],
  ])('validates year edits %s to %s without replacing the applied filter on failure', (from, to, valid) => {
    const apply = vi.fn(),
      close = vi.fn()
    render(
      <AvalonFilterPanel
        filter={{ yearFrom: 2010, yearTo: 2020 }}
        games={games}
        facts={facts}
        workspace={workspace}
        fullscreen={fullscreen}
        apply={apply}
        close={close}
      />,
    )
    fireEvent.change(libraryRole('textbox', { name: 'From this year' }), { target: { value: from } })
    fireEvent.change(libraryRole('textbox', { name: 'Up to this year' }), { target: { value: to } })
    if (fullscreen) fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    if (valid) {
      expect(apply.mock.lastCall?.[0]).toEqual(avalonYearRange(String(from), String(to)))
      if (fullscreen) expect(close).toHaveBeenCalledOnce()
    } else {
      expect(apply).not.toHaveBeenCalled()
      expect(close).not.toHaveBeenCalled()
    }
  })
  it('preserves the applied year range when draft years are invalid', () => {
    const apply = vi.fn(),
      close = vi.fn()
    render(
      <AvalonFilterPanel
        filter={{ yearFrom: 2010, yearTo: 2020 }}
        games={games}
        facts={facts}
        workspace={workspace}
        fullscreen={fullscreen}
        apply={apply}
        close={close}
      />,
    )
    fireEvent.change(libraryRole('textbox', { name: 'From this year' }), { target: { value: '2030' } })
    expect(screen.getByRole('alert').textContent).toContain('start no later than the end')
    expect(apply).not.toHaveBeenCalled()
    if (fullscreen)
      expect(screen.getByRole('button', { name: 'Apply filters' }).hasAttribute('disabled')).toBe(true)
  })
  it('exposes and clears a missing saved facet', () => {
    const apply = vi.fn()
    render(
      <AvalonFilterPanel
        filter={{ genreIds: [999] }}
        games={games}
        facts={facts}
        workspace={workspace}
        fullscreen={fullscreen}
        apply={apply}
        close={vi.fn()}
      />,
    )
    fireEvent.click(option(/Unavailable saved filter \(999\)/))
    if (fullscreen) {
      expect(apply).not.toHaveBeenCalled()
      fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    }
    expect(apply).toHaveBeenCalledWith(expect.objectContaining({ genreIds: [] }))
  })
  it('removes a year rule even when no dated games remain', () => {
    const apply = vi.fn()
    render(
      <AvalonFilterPanel
        filter={{ yearFrom: 2010, yearTo: 2020 }}
        games={[games[2]]}
        facts={facts}
        workspace={workspace}
        fullscreen={fullscreen}
        apply={apply}
        close={vi.fn()}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    if (fullscreen) fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    expect(apply.mock.lastCall?.[0].yearFrom ?? null).toBeNull()
    expect(apply.mock.lastCall?.[0].yearTo ?? null).toBeNull()
  })
})

it('keeps the standalone year editor inside the focus loop without intercepting caret movement', () => {
  render(
    <AvalonFilterPanel
      filter={{}}
      games={games}
      facts={avalonFacts(games, workspace)}
      workspace={workspace}
      fullscreen
      apply={vi.fn()}
      close={vi.fn()}
    />,
  )
  fireEvent.click(screen.getByRole('button', { name: 'Release year from · Any' }))
  const input = screen.getByRole('textbox', { name: 'From this year' })
  const back = screen.getByRole('button', { name: 'Back to filters' })
  expect(document.activeElement).toBe(input)
  expect(fireEvent.keyDown(input, { key: 'ArrowLeft' })).toBe(true)
  expect(document.activeElement).toBe(input)
  fireEvent.keyDown(input, { key: 'Tab', shiftKey: true })
  expect(document.activeElement).toBe(back)
  fireEvent.keyDown(back, { key: 'Tab' })
  expect(document.activeElement).toBe(input)
})
