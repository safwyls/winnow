// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createDesignFixture, DESIGN_PATCH_NOTES, installDesignFixture } from './preview/design-fixture'
import { DESIGN_SURFACES, DesignPreview } from './preview/design-preview'
import { clearViewState } from '../src/renderer/viewState'

// JSDOM has no layout. Native previews use the real virtualizer and geometry;
// component tests realize each supplied row without changing its production view.
vi.mock('@tanstack/react-virtual', () => ({
  useVirtualizer: (options: { count: number; estimateSize(): number }) => ({
    getTotalSize: () => options.count * options.estimateSize(),
    getVirtualItems: () =>
      Array.from({ length: options.count }, (_, index) => ({
        key: index,
        index,
        start: index * options.estimateSize(),
      })),
    measure: () => {},
    scrollToOffset: () => {},
    scrollToIndex: () => {},
  }),
}))
beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
  vi.stubGlobal('matchMedia', () => ({
    matches: false,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
  }))
  vi.stubGlobal(
    'fetch',
    vi.fn(() => Promise.reject(Error('A preview attempted network IO'))),
  )
  Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: vi.fn() })
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of [
      'query',
      'bucket',
      'store',
      'list',
      'sort',
      'view',
      'density',
      'rows',
      'viewport',
      'selected',
      'selection',
      'rules',
      'filter-order',
      'tools',
    ])
      clearViewState(`avalon:library:${mode}:${key}`)
    for (const key of ['shelf', 'column', 'positions']) clearViewState(`avalon:home:${mode}:${key}`)
    for (const key of ['section', 'range', 'source', 'currency']) clearViewState(`${mode}:stats:${key}`)
    clearViewState(`${mode}:details:4:tab`)
  }
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it('keeps the complete original preview records and relative history at the API boundary', async () => {
  const now = new Date('2026-12-01T12:00:00Z'),
    fixture = createDesignFixture({ now })
  expect(fixture.ownerships).toHaveLength(9)
  expect(fixture.releases).toHaveLength(9)
  expect(fixture.workspace.works).toHaveLength(9)
  expect(fixture.games).toHaveLength(8)
  const witcher = fixture.games.find((game) => game.workId === 3)!
  expect(witcher.entries.map((entry) => [entry.workId, entry.store, entry.playtimeMinutes])).toEqual([
    [3, 'steam', 7200],
    [30, 'gog', 1000],
  ])
  expect(witcher.playtimeMinutes).toBe(8200)
  expect(fixture.workspace.buckets.find((row) => row.ownershipId === 230)).toMatchObject({
    workId: 30,
    resolvedWorkId: 3,
    bucket: 'bounced',
    game: { bucket: 'retired' },
  })
  expect(fixture.games.find((game) => game.workId === 1)?.bucket).toBe('bounced')
  expect(fixture.games.find((game) => game.workId === 4)?.bucket).toBe('stale_but_patched')
  expect(fixture.details(4).events.map((event) => event.id)).toEqual([3, 4])
  expect(fixture.workspace.storefronts?.['gog:1453375253'].patchNotes).toBe(DESIGN_PATCH_NOTES)
  expect(fixture.games.find((game) => game.workId === 4)?.lastPlayedAt).toBe(
    new Date(now.getTime() - 270 * 86_400_000).toISOString(),
  )
  expect(fixture.feed.shelves.map((shelf) => shelf.items.map((item) => item.releaseId))).toEqual([
    [104, 101],
    [102, 107],
  ])
  for (const shelf of fixture.feed.shelves)
    for (const item of shelf.items)
      expect(
        fixture.games.some((game) =>
          game.entries.some(
            (entry) => entry.ownershipId === item.ownershipId && entry.releaseId === item.releaseId,
          ),
        ),
      ).toBe(true)
  const first = await fixture.bridge.request<{ games: unknown[] }>({ route: 'library.get' })
  first.data!.games.length = 0
  expect(
    (await fixture.bridge.request<{ games: unknown[] }>({ route: 'library.get' })).data!.games,
  ).toHaveLength(8)
  expect(await fixture.bridge.request({ route: 'launch', params: { ownershipId: 204 } })).toMatchObject({
    ok: false,
    status: 403,
  })
  expect(await fixture.bridge.openExternal('https://www.gog.com')).toMatchObject({ opened: false })
  expect(fixture.blocked).toEqual(['launch', 'openExternal'])
  expect(fetch).not.toHaveBeenCalled()
})

describe.each(['desktop', 'fullscreen'] as const)('isolated design preview in %s', (mode) => {
  it.each(DESIGN_SURFACES)(
    'attaches the actual %s surface with production descendants and bound content',
    async (surface) => {
      const fixture = createDesignFixture({ mode })
      const restore = installDesignFixture(fixture)
      const view = render(<DesignPreview fixture={fixture} surface={surface} />)
      await waitFor(() => expect(fixture.requests).toContain('library.get'))
      await waitFor(() => expect(screen.queryByText('Loading game details…')).toBeNull())
      const host =
        surface === 'GameDetailsView'
          ? await waitFor(() => {
              const node = document.querySelector<HTMLElement>('.avalon-details')
              expect(node).not.toBeNull()
              return node!
            })
          : view.container.querySelector<HTMLElement>('[data-design-surface]')!
      expect(host.querySelectorAll('*').length).toBeGreaterThan(0)
      if (surface !== 'RowCoverView') expect(host.textContent?.trim().length).toBeGreaterThan(0)
      if (surface === 'GameDetailsView') expect(host.textContent).toContain('Stardew Valley')
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 25))
      })
      expect(fixture.blocked).toEqual([])
      expect(fetch).not.toHaveBeenCalled()
      view.unmount()
      restore()
    },
  )
  it('loads the shell feed, materializes eight Library games and opens populated Stardew Details', async () => {
    const fixture = createDesignFixture({ mode }),
      restore = installDesignFixture(fixture)
    const view = render(<DesignPreview fixture={fixture} />)
    await screen.findByRole('button', { name: 'Winnow home' }, { timeout: 5000 })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: /Preparing/ })).toBeNull(), {
      timeout: 5000,
    })
    expect(fixture.requests).toContain('feed.get')
    await screen.findByRole('button', { name: 'Library' })
    fireEvent.click(screen.getByRole('button', { name: 'Library' }))
    await waitFor(() =>
      expect(document.querySelectorAll('.avalon-library [data-avalon-game]')).toHaveLength(8),
    )
    const stardew = screen.getByRole('button', { name: /^View Stardew Valley/ })
    expect(stardew.querySelector('.avalon-unread')).not.toBeNull()
    expect(
      screen.getByRole('button', { name: /^View The Witcher 3: Wild Hunt/ }).getAttribute('aria-label'),
    ).toContain('Steam, GOG')
    fireEvent.click(stardew)
    await waitFor(() =>
      expect(document.querySelector('.avalon-details')?.textContent).toContain('Stardew Valley'),
    )
    expect(fixture.requests).toContain('game.details')
    const panel = document.querySelector<HTMLElement>('.avalon-details')!
    fireEvent.click(within(panel).getByRole('tab', { name: /^Updates/ }))
    await within(panel).findByText('Patch 1.6.15 — patch notes')
    if (mode === 'desktop') expect(within(panel).getByText('GOG patch notes')).toBeTruthy()
    else expect(within(panel).getByRole('button', { name: 'Patch notes' })).toBeTruthy()
    expect(fixture.blocked).toEqual([])
    expect(fetch).not.toHaveBeenCalled()
    view.unmount()
    restore()
  })
})
