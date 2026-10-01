// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Details, GameLinks } from '../src/renderer/features/Details'
import { cachedGogPatchNotes, GogPatchNotesText } from '../src/renderer/features/GogPatchNotes'
import { gameLinks } from '../src/renderer/api/gameLinks'
import type { GameEntry, LibraryGame, Workspace } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'

vi.mock('../src/renderer/components/Artwork', () => ({ Artwork: () => null }))
vi.mock('../src/renderer/themes/avalon-backdrop', () => ({ AvalonBackdrop: () => null }))
afterEach(cleanup)
const panzerUrl = 'https://www.gog.com/game/panzer_general_2'
const hadesUrl = 'https://store.epicgames.com/p/hades'
const notes = 'Internal Update\nCloud Saves support'
function fixture(mode: 'desktop' | 'fullscreen', presentation: 'shared' | 'avalon') {
  const entry: GameEntry = {
    ownershipId: 1,
    releaseId: 1,
    workId: 1,
    title: 'Panzer General 2',
    store: 'gog',
    installed: false,
    playtimeMinutes: 0,
  }
  const game: LibraryGame = {
    workId: 1,
    title: entry.title,
    bucket: 'never_played',
    playtimeMinutes: 0,
    entries: [entry],
  }
  const workspace: Workspace = {
    preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
    works: [{ id: 1, name: game.title }],
    externalIds: [{ releaseId: 1, provider: 'gog', providerId: '1207658871' }],
    epicLaunchKeys: {},
    pluginActions: {},
    storefronts: { 'gog:1207658871': { storeUrl: panzerUrl, patchNotes: notes } },
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
  const openExternal = vi.fn().mockResolvedValue({ opened: true })
  Object.defineProperty(window, 'winnow', {
    configurable: true,
    value: { request, openExternal, artwork: vi.fn().mockResolvedValue(null) },
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <Details workId={1} mode={mode} presentation={presentation} />
    </QueryClientProvider>,
  )
  return { entry, game, workspace, request, openExternal, client }
}

describe.each(['desktop', 'fullscreen'] as const)('source storefront presentation in %s', (mode) => {
  it.each(['avalon', 'shared'] as const)(
    'Details_offer_store_link_and_readable_gog_notes_and_hide_missing_epic_link (%s)',
    async (presentation) => {
      const f = fixture(mode, presentation)
      await screen.findByRole(presentation === 'avalon' ? 'tab' : 'button', { name: 'Updates' })
      fireEvent.click(screen.getByRole(presentation === 'avalon' ? 'tab' : 'button', { name: 'Updates' }))
      if (mode === 'desktop') {
        const expander = await screen.findByText('GOG patch notes', { selector: 'summary' })
        fireEvent.click(expander)
        expect(expander.closest('details')?.open).toBe(true)
      } else {
        const action = await screen.findByRole('button', { name: 'Patch notes' })
        fireEvent.click(action)
        const region = screen.getByRole('region', { name: 'Patch notes' })
        expect(region.textContent).toContain('Cloud Saves support')
        const back = screen.getByRole('button', { name: 'Back to Updates' })
        expect(document.activeElement).toBe(back)
        expect(screen.getByRole('group', { name: 'Patch notes controls' }).textContent).toContain(
          'Up/Down Read',
        )
        fireEvent.keyDown(back, { key: 'Escape' })
        expect(screen.queryByRole('region', { name: 'Patch notes' })).toBeNull()
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Patch notes' }))
        fireEvent.click(screen.getByRole('button', { name: 'Patch notes' }))
      }
      const text = document.querySelector('.gog-patch-notes-text')!
      expect(text.textContent).toBe(notes)
      expect(text.textContent).not.toContain('bad()')
      expect(text.textContent).not.toContain('<')
      expect(text.querySelector('script, h4, ul, li')).toBeNull()
      expect(gameLinks(f.game, f.workspace).find((link) => link.label === 'GOG store page')?.url).toBe(
        panzerUrl,
      )
      const epic = { ...f.game, entries: [{ ...f.entry, store: 'epic' }] }
      const epicWorkspace: Workspace = {
        ...f.workspace,
        externalIds: [{ releaseId: 1, provider: 'epic', providerId: 'catalog-id' }],
        epicLaunchKeys: {
          'catalog-id': { namespace: 'min', catalogItemId: 'catalog-id', artifactId: 'Hades' },
        },
      }
      const renderedLinks = render(<GameLinks links={gameLinks(epic, epicWorkspace)} />)
      expect(within(renderedLinks.container).queryByRole('button')).toBeNull()
      epicWorkspace.storefronts = { ...epicWorkspace.storefronts, 'epic:min': { storeUrl: hadesUrl } }
      expect(gameLinks(epic, epicWorkspace).find((link) => link.url === hadesUrl)).toBeTruthy()
      renderedLinks.rerender(<GameLinks links={gameLinks(epic, epicWorkspace)} />)
      fireEvent.click(within(renderedLinks.container).getByRole('button'))
      expect(f.openExternal).toHaveBeenCalledWith(hadesUrl)
      renderedLinks.rerender(<GameLinks links={gameLinks(f.game, f.workspace)} />)
      fireEvent.click(within(renderedLinks.container).getByRole('button', { name: 'GOG store page' }))
      expect(f.openExternal).toHaveBeenCalledWith(panzerUrl)
      expect(f.request.mock.calls.some(([input]) => input.route === 'game.refetch')).toBe(false)
    },
  )

  it('Sync_uses_owned_store_ids_and_warms_the_read_only_projection', () => {
    const f = fixture(mode, 'avalon')
    const epic: GameEntry = {
      ...f.entry,
      ownershipId: 2,
      releaseId: 2,
      workId: 2,
      store: 'epic',
      title: 'Owned Epic',
    }
    const workspace: Workspace = {
      ...f.workspace,
      works: [
        { id: 1, name: 'Owned GOG' },
        { id: 2, name: 'Owned Epic' },
        { id: 3, name: 'Unowned GOG' },
      ],
      externalIds: [
        ...f.workspace.externalIds,
        { releaseId: 2, provider: 'epic', providerId: 'catalog-id' },
        { releaseId: 3, provider: 'gog', providerId: '99' },
      ],
      epicLaunchKeys: {
        'catalog-id': { namespace: 'fn', catalogItemId: 'catalog-id', artifactId: 'Fortnite' },
      },
      storefronts: {
        ...f.workspace.storefronts,
        'epic:fn': { storeUrl: 'https://store.epicgames.com/p/fortnite' },
        'gog:99': { patchNotes: 'Unowned unrelated notes' },
      },
    }
    expect(gameLinks(f.game, workspace).find((link) => link.label === 'GOG store page')?.url).toBe(panzerUrl)
    expect(cachedGogPatchNotes(f.entry, workspace)).toBe(notes)
    expect(
      gameLinks({ ...f.game, workId: 2, entries: [epic] }, workspace).some(
        (link) => link.url === 'https://store.epicgames.com/p/fortnite',
      ),
    ).toBe(true)
    expect(cachedGogPatchNotes(epic, workspace)).toBeNull()
    expect(cachedGogPatchNotes({ ...f.entry, releaseId: 2 }, workspace)).toBeNull()
  })
})

it('renders unexpected cached markup as inert text and omits whitespace-only notes', () => {
  const text = '<h4>Internal Update</h4><script>bad()</script>'
  render(<GogPatchNotesText notes={text} />)
  expect(document.querySelector('.gog-patch-notes-text')!.textContent).toBe(text)
  expect(document.querySelector('script, h4')).toBeNull()
  const entry = { store: 'gog', releaseId: 1 } as GameEntry
  const workspace: Workspace = {
    preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
    works: [],
    epicLaunchKeys: {},
    pluginActions: {},
    externalIds: [{ releaseId: 1, provider: 'gog', providerId: '1207658871' }],
    storefronts: { 'gog:1207658871': { patchNotes: ' \n ' } },
  }
  expect(cachedGogPatchNotes(entry, workspace)).toBeNull()
})
