// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Details } from '../src/renderer/features/Details'
import { gameLinks } from '../src/renderer/api/gameLinks'
import { clearViewState } from '../src/renderer/viewState'
import type { ApiRequest } from '../src/shared/bridge'
import type { LibraryGame, Mode, Workspace } from '../src/renderer/api/types'

vi.mock('../src/renderer/components/Artwork', () => ({ Artwork: () => null }))
vi.mock('../src/renderer/themes/avalon-backdrop', () => ({ AvalonBackdrop: () => null }))
const clients: QueryClient[] = []
afterEach(() => {
  cleanup()
  clients.splice(0).forEach((client) => client.clear())
  clearViewState('draft:metadata-fields:1')
  clearViewState('metadata-fields:1:sending')
})

function mount(mode: Mode, store: 'epic' | 'steam' = 'epic', cacheFixture = false) {
  let installed = store === 'steam'
  const url = `https://store.epicgames.com/${cacheFixture ? '' : 'en-US/'}p/moonlighter`
  const catalog = cacheFixture ? 'catalog-id' : 'sample-catalog'
  const namespace = cacheFixture ? 'moon-ns' : 'sample-namespace'
  const title = store === 'steam' ? 'Portal 2' : 'Moonlighter'
  const game = (): LibraryGame => ({
    workId: 1,
    title,
    bucket: 'never_played',
    playtimeMinutes: 0,
    lastPlayedAt: null,
    entries: [
      {
        ownershipId: 1,
        releaseId: 1,
        workId: 1,
        title,
        store,
        installed,
        playtimeMinutes: 0,
        lastPlayedAt: null,
      },
    ],
  })
  const workspace: Workspace = {
    preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'Mature' },
    works: [{ id: 1, name: title }],
    externalIds: [{ releaseId: 1, provider: store, providerId: store === 'steam' ? '620' : catalog }],
    epicLaunchKeys: {
      [catalog]: { namespace, catalogItemId: catalog, artifactId: cacheFixture ? 'Eagle' : 'Moonlighter' },
    },
    storefronts: { [`epic:${namespace}`]: { storeUrl: url } },
    pluginActions: {},
  }
  let release!: () => void
  let pending: Promise<void> | undefined
  const dispatches: { ownershipId: number; action: string; installed: boolean }[] = []
  const request = vi.fn(async (input: ApiRequest) => {
    let data: unknown = {}
    if (input.route === 'library.get') data = { games: [game()], lists: [] }
    if (input.route === 'library.workspace') data = workspace
    if (input.route === 'game.details')
      data = {
        workId: 1,
        sessions: {},
        events: [],
        journalEntries: [],
        ratings: [],
        achievements: [],
        ownerships: [
          { id: 1, releaseId: 1, store, installed, installPath: installed ? 'C:\\Games\\Moonlighter' : null },
        ],
      }
    if (input.route === 'metadata.get')
      data = {
        workId: 1,
        title,
        revision: 'unchanged',
        isPinned: false,
        fields: [{ field: 'name', value: title, source: 'igdb' }],
      }
    if (input.route === 'actions.execute') {
      dispatches.push({
        ownershipId: Number(input.params?.ownershipId),
        action: (input.body as { action: string }).action,
        installed,
      })
      await pending
      data = 0
    }
    return { ok: true, status: 200, data }
  })
  const openExternal = vi.fn().mockResolvedValue({ opened: true })
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request, openExternal } })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  const close = vi.fn()
  const view = render(
    <QueryClientProvider client={client}>
      <Details workId={1} mode={mode} presentation="avalon" onClose={close} />
    </QueryClientProvider>,
  )
  return {
    ...view,
    client,
    request,
    openExternal,
    dispatches,
    close,
    url,
    game,
    workspace,
    hold() {
      pending = new Promise<void>((resolve) => {
        release = resolve
      })
    },
    async finish() {
      await act(async () => release())
    },
    async refresh(value: boolean) {
      installed = value
      await act(async () => {
        await client.invalidateQueries({ queryKey: ['api'] })
      })
      await waitFor(() => expect(primary().textContent).toBe(value ? 'Play' : 'Install'))
    },
  }
}
function more() {
  return document.querySelector<HTMLButtonElement>('.avalon-details-more > button')!
}
async function openMore() {
  if (more().getAttribute('aria-expanded') !== 'true') fireEvent.click(more())
  return screen.findByRole('navigation', { name: 'Game links' })
}
function closeMore() {
  fireEvent.keyDown(screen.getByRole('navigation', { name: 'Game links' }), { key: 'Escape' })
}
function primary() {
  return document.querySelector<HTMLButtonElement>('[data-controller-play]')!
}

describe.each(['desktop', 'fullscreen'] as const)('install refresh in %s', (mode) => {
  it('reloads Moonlighter Install to Play and back while retaining the open editor draft and cached store URL', async () => {
    const fixture = mount(mode, 'epic', true)
    await screen.findByRole('button', { name: 'Install' })
    const detail = document.querySelector('.avalon-details')
    await openMore()
    expect(screen.getByRole('button', { name: 'Epic Games store page' }).title).toBe(fixture.url)
    fireEvent.click(screen.getByRole('button', { name: 'Edit details' }))
    if (mode === 'fullscreen') fireEvent.click(await screen.findByRole('button', { name: 'Name · IGDB' }))
    const name = (await screen.findByLabelText('Name')) as HTMLInputElement
    const editor = name.closest('[role="dialog"]')
    fireEvent.change(name, { target: { value: 'An unsaved edit' } })
    for (const installed of [true, false]) {
      await fixture.refresh(installed)
      expect(document.querySelector('.avalon-details')).toBe(detail)
      expect(screen.getByLabelText('Name')).toBe(name)
      expect(name.closest('[role="dialog"]')).toBe(editor)
      expect(name.value).toBe('An unsaved edit')
      expect(primary().textContent).toBe(installed ? 'Play' : 'Install')
      const snapshot = fixture.client.getQueryData<{ games: LibraryGame[] }>(['api', 'library.get'])!
      expect(snapshot.games[0].entries[0]).toMatchObject({ ownershipId: 1, releaseId: 1, installed })
      expect(gameLinks(snapshot.games[0], fixture.workspace)).toEqual([
        { label: 'Epic Games store page', url: fixture.url },
      ])
      const details = fixture.client.getQueryData<{ ownerships: { installPath: string | null }[] }>([
        'api',
        'game.details',
        { workId: 1 },
      ])!
      expect(details.ownerships[0].installPath).toBe(installed ? 'C:\\Games\\Moonlighter' : null)
    }
    expect(fixture.request.mock.calls.filter(([input]) => input.route === 'metadata.put')).toHaveLength(0)
    expect(fixture.dispatches).toEqual([])
  })

  it('removes the focusable Steam 620 uninstall action after ownership refresh and exposes Install', async () => {
    const fixture = mount(mode, 'steam')
    await screen.findByRole('button', { name: 'Play' })
    await openMore()
    const uninstall = screen.getByRole('button', { name: 'Uninstall in Steam' })
    uninstall.focus()
    expect(document.activeElement).toBe(uninstall)
    closeMore()
    await fixture.refresh(false)
    expect(primary().textContent).toBe('Install')
    await openMore()
    expect(screen.queryByRole('button', { name: 'Uninstall in Steam' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Store page' }).title).toBe(
      'https://store.steampowered.com/app/620/',
    )
    expect(fixture.dispatches).toEqual([])
  })

  it('refreshes the retained Moonlighter header and dispatches Play with the current ownership while preserving three store visits', async () => {
    const fixture = mount(mode)
    await screen.findByRole('button', { name: 'Install' })
    const detail = document.querySelector('.avalon-details')
    expect(primary().querySelector('.lucide-download')).not.toBeNull()
    fireEvent.click(within(await openMore()).getByRole('button', { name: 'Epic Games store page' }))
    closeMore()
    await fixture.refresh(true)
    expect(document.querySelector('.avalon-details')).toBe(detail)
    expect(primary().textContent).toBe('Play')
    expect(primary().querySelector('.lucide-play')).not.toBeNull()
    expect(primary().querySelector('.lucide-download')).toBeNull()
    fireEvent.click(within(await openMore()).getByRole('button', { name: 'Epic Games store page' }))
    closeMore()
    fireEvent.click(primary())
    await waitFor(() => expect(primary().disabled).toBe(false))
    expect(fixture.dispatches).toEqual([{ ownershipId: 1, action: 'Play', installed: true }])
    const link = within(await openMore()).getByRole('button', { name: 'Epic Games store page' })
    await fixture.refresh(true)
    expect(screen.getByRole('button', { name: 'Epic Games store page' })).toBe(link)
    fireEvent.click(link)
    await waitFor(() =>
      expect(fixture.openExternal.mock.calls.map(([url]) => url)).toEqual(Array(3).fill(fixture.url)),
    )
  })

  it('keeps the store link enabled through one pending Install dispatch and after completion', async () => {
    const fixture = mount(mode)
    await screen.findByRole('button', { name: 'Install' })
    fixture.hold()
    fireEvent.click(primary())
    fireEvent.click(primary())
    expect(primary().disabled).toBe(true)
    expect(primary().textContent).toBe('Install')
    expect(primary().getAttribute('aria-busy')).toBe('true')
    const links = await openMore()
    const link = within(links).getByRole('button', { name: 'Epic Games store page' }) as HTMLButtonElement
    expect(link.disabled).toBe(false)
    expect(link.title).toBe(fixture.url)
    fireEvent.click(link)
    expect(fixture.dispatches).toEqual([{ ownershipId: 1, action: 'Install', installed: false }])
    await fixture.finish()
    await waitFor(() => expect(primary().disabled).toBe(false))
    expect(primary().textContent).toBe('Install')
    expect(primary().hasAttribute('aria-busy')).toBe(false)
    expect(screen.getByRole('navigation', { name: 'Game links' })).toBe(links)
    expect(screen.getByRole('button', { name: 'Epic Games store page' })).toBe(link)
    fireEvent.click(link)
    await waitFor(() =>
      expect(fixture.openExternal.mock.calls.map(([url]) => url)).toEqual([fixture.url, fixture.url]),
    )
    expect(fixture.dispatches).toHaveLength(1)
  })
})
