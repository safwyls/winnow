// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Details } from '../src/renderer/features/Details'
import { librarySourceSummary } from '../src/renderer/features/library-source'
import { primaryAction, storeLabel } from '../src/renderer/api/client'
import { ownershipStores } from '../src/renderer/themes/avalon-store-marks'
import type { GameEntry, LibraryGame, Workspace } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'

vi.mock('../src/renderer/components/Artwork', () => ({ Artwork: () => null }))
vi.mock('../src/renderer/themes/avalon-backdrop', () => ({ AvalonBackdrop: () => null }))

const history = 'Played history — not proof of ownership.'
const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
})
function entry(id: number, store: string): GameEntry {
  return {
    ownershipId: id,
    releaseId: id,
    workId: 1,
    title: 'Fixture',
    store,
    installed: null,
    playtimeMinutes: 0,
    lastPlayedAt: null,
  }
}
function fixture(mode: 'desktop' | 'fullscreen', presentation: 'shared' | 'avalon', psn = false) {
  const now = new Date()
  const entries = psn
    ? [
        {
          ...entry(1, 'plugin:psn'),
          playtimeMinutes: 60,
          lastPlayedAt: new Date(now.getTime() - 86400000).toISOString(),
        },
      ]
    : [entry(1, 'steam'), entry(2, 'plugin:xbox')]
  const game: LibraryGame = {
    workId: 1,
    title: 'Fixture',
    bucket: psn ? 'active' : 'never_played',
    playtimeMinutes: psn ? 60 : 0,
    lastPlayedAt: entries.at(-1)!.lastPlayedAt,
    entries,
  }
  const workspace: Workspace = {
    preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
    works: [{ id: 1, name: game.title }],
    externalIds: [],
    epicLaunchKeys: {},
    storefronts: {},
    pluginActions: { [psn ? '1' : '2']: { sourceLabel: history, canPlay: false, canOpenStore: false } },
  }
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      input.route === 'library.get'
        ? { games: [game], lists: [] }
        : input.route === 'library.workspace'
          ? workspace
          : input.route === 'game.details'
            ? { workId: 1, events: [], sessions: {}, journalEntries: [], ratings: [], achievements: [] }
            : input.route === 'metadata.get' || input.route === 'metadata.igdb'
              ? { available: false }
              : {},
  }))
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: {
      request,
      openExternal: vi.fn(),
      artwork: vi.fn().mockResolvedValue(null),
    },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  render(
    <QueryClientProvider client={client}>
      <Details workId={1} mode={mode} presentation={presentation} />
    </QueryClientProvider>,
  )
  return { game, workspace, request, client }
}

describe.each(['desktop', 'fullscreen'] as const)('%s plugin provenance', (mode) => {
  it.each(['avalon', 'shared'] as const)(
    'Grouped_history_retains_its_source_explanation_on_both_surfaces (%s)',
    async (presentation) => {
      const f = fixture(mode, presentation)
      const summary = await screen.findByText(`Xbox: ${history}`)
      expect(summary.hasAttribute('data-library-source-summary')).toBe(true)
      expect(document.querySelectorAll('[data-library-source-summary]')).toHaveLength(1)
      expect(f.game.entries.map((copy) => [copy.store, copy.playtimeMinutes, copy.lastPlayedAt])).toEqual([
        ['steam', 0, null],
        ['plugin:xbox', 0, null],
      ])
      expect(f.request.mock.calls.some(([input]) => input.route === 'actions.execute')).toBe(false)
    },
  )

  it.each(['avalon', 'shared'] as const)(
    'Imported_history_shows_its_PlayStation_source_without_a_launch_or_install_action (%s)',
    async (presentation) => {
      const f = fixture(mode, presentation, true)
      const summary = await screen.findByText(`PlayStation: ${history}`)
      expect(summary.hasAttribute('data-library-source-summary')).toBe(true)
      expect(document.querySelectorAll('[data-library-source-summary]')).toHaveLength(1)
      expect(storeLabel(f.game.entries[0].store)).toBe('PlayStation')
      expect(ownershipStores(f.game)[0].badge).toBe('PLAYSTATION')
      expect(f.game.entries[0].installed).toBeNull()
      expect(f.game.entries[0].playtimeMinutes).toBe(60)
      expect(primaryAction(f.game.entries[0], f.workspace)).toBeNull()
      expect(screen.queryByRole('button', { name: /^(Play|Install)$/ })).toBeNull()
      expect(screen.queryByText(/Not installed|^Installed/)).toBeNull()
      expect(f.request.mock.calls.some(([input]) => input.route === 'actions.execute')).toBe(false)
    },
  )
})

it('groups distinct prefixed sources in entry order and omits whitespace-only labels', () => {
  const copies = [
    entry(1, 'steam'),
    entry(2, 'plugin:xbox'),
    entry(3, 'plugin:xbox'),
    entry(4, 'plugin:psn'),
    entry(5, 'plugin:empty'),
  ]
  const actions = Object.fromEntries(
    copies.slice(1).map((copy) => [
      String(copy.ownershipId),
      {
        sourceLabel: copy.ownershipId === 5 ? ' \n ' : history,
        canPlay: false,
        canOpenStore: false,
      },
    ]),
  )
  expect(librarySourceSummary(copies, { pluginActions: actions } as Workspace)).toBe(
    `Xbox: ${history} · PlayStation: ${history}`,
  )
})
