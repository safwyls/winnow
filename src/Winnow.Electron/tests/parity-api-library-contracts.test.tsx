// @vitest-environment jsdom
import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ApiRequest, WinnowBridge } from '../src/shared/bridge'
import type { LibraryResponse, ManualGame, Metadata, Mode } from '../src/renderer/api/types'
import { request as apiRequest } from '../src/renderer/api/client'
import { App } from '../src/renderer/App'
import { LibraryTools } from '../src/renderer/features/LibraryTools'
import { ManualEditor } from '../src/renderer/features/ManualEditor'
import { MetadataDialog } from '../src/renderer/features/metadata-dialog'
import { clearViewState } from '../src/renderer/viewState'
import { libraryLabel, returnToLibrary } from './library-controls'

// Source: ApiLibraryViewModelTests at cf45d9f1127243a987d3cf6e664a32fc767ecb67.
// The app routes and API publication stay real. Native suites own frame timing and viewport geometry.
configure({ asyncUtilTimeout: 5000 })
vi.mock('../src/renderer/startup/preparation', async (original) => ({
  ...(await original<typeof import('../src/renderer/startup/preparation')>()),
  createBrowserPreparationClock: () => {
    let elapsed = 0
    return {
      hidden: () => false,
      frame: async (signal: AbortSignal) => {
        await new Promise<void>((resolve) => setTimeout(resolve, 0))
        signal.throwIfAborted()
        return (elapsed += 200)
      },
    }
  },
}))
vi.mock('../src/renderer/startup/LoadingDragon', () => ({
  LoadingDragon: ({ tracing, onFrame }: { tracing: boolean; onFrame(elapsed: number): void }) => {
    React.useEffect(() => {
      if (tracing) onFrame(1800)
    }, [tracing, onFrame])
    return null
  },
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
const ok = (data: unknown) => ({ ok: true, status: 200, data: structuredClone(data) })
const hiddenAt = new Date(Date.now() - 3 * 86_400_000).toISOString()
const lastPlayedAt = new Date(new Date().setFullYear(new Date().getFullYear() - 2)).toISOString()
const library: LibraryResponse = {
  games: [
    {
      workId: 1,
      title: 'Shared title',
      firstReleaseYear: 2007,
      summary: 'Shared metadata',
      bucket: 'bounced',
      playtimeMinutes: 150,
      lastPlayedAt,
      entries: [
        {
          ownershipId: 1,
          releaseId: 11,
          workId: 1,
          title: 'Steam',
          store: 'steam',
          installed: false,
          playtimeMinutes: 100,
        },
        {
          ownershipId: 2,
          releaseId: 12,
          workId: 1,
          title: 'GOG',
          store: 'gog',
          installed: false,
          playtimeMinutes: 50,
        },
      ],
    },
  ],
  lists: [],
}
const workspace = {
  works: [{ id: 1, name: 'Shared title', firstReleaseYear: 2007, summary: 'Shared metadata' }],
  externalIds: [],
  epicLaunchKeys: {},
  pluginActions: {},
}
let fullscreen: (value: boolean) => void

beforeEach(() => {
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)
  for (const name of ['ResizeObserver', 'IntersectionObserver'])
    vi.stubGlobal(
      name,
      class {
        observe() {}
        disconnect() {}
        unobserve() {}
      },
    )
  window.matchMedia = vi.fn().mockImplementation((media) => ({
    media,
    matches: false,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
  }))
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
      'default-sort',
      'sort-before-list',
      'list-base',
      'view',
      'density',
      'tools',
      'rows',
      'viewport',
      'selected',
      'selection',
      'rules',
      'filter-order',
      'filters-open',
    ])
      clearViewState(`avalon:library:${mode}:${key}`)
    clearViewState(`${mode}:library-tools:tab`)
    clearViewState(`${mode}:manual:editing`)
  }
  for (const key of [
    'draft:metadata-fields:1',
    'metadata-fields:1:sending',
    'draft:list:7',
    'draft:manual:new',
    'setup:suspended',
  ])
    clearViewState(key)
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

function installBridge(respond: (input: ApiRequest) => unknown) {
  const request = vi.fn(async (input: ApiRequest) => respond(input))
  window.winnow = {
    request,
    connection: async () => ({ connected: true, message: 'Connected' }),
    onConnection: () => () => {},
    onEvent: () => () => {},
    isFullscreen: async () => false,
    onFullscreen: (callback) => {
      fullscreen = callback
      return () => {}
    },
    setFullscreen: async (value) => fullscreen(value),
    artwork: async () => null,
    loadPreferences: async () => null,
    savePreferences: async () => {},
    listThemes: async () => [],
    importProfile: async () => null,
    exportProfile: async () => true,
    installTheme: async () => null,
    openExternal: async () => ({ opened: true }),
  } as WinnowBridge
  return request
}
function mount(content: React.ReactNode, respond: (input: ApiRequest) => unknown) {
  const request = installBridge(respond)
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  clients.push(client)
  const view = render(<QueryClientProvider client={client}>{content}</QueryClientProvider>)
  return { ...view, request, client }
}
function shellResponse({ route }: ApiRequest) {
  const responses: Record<string, unknown> = {
    'library.get': library,
    'library.workspace': workspace,
    'feed.get': { shelves: [], candidateCount: 0, confidence: 0, failed: false },
    'feed.supplement': { shelves: [], candidateCount: 0 },
    'preferences.library.get': { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
    'preferences.presentation.get': [],
    'connections.get': { steam: { hasUsableCredential: false } },
    'connections.igdb.get': { clientId: '', hasSavedCredentials: false },
    'setup.get': {},
    'game.details': {
      workId: 1,
      events: [],
      sessions: {},
      journalEntries: [],
      ratings: [],
      achievements: [],
    },
    artworkState: { current: null, revision: 'a' },
    'activity.query': { rows: [], next: null },
    'statistics.gameplay': {
      recordedSeconds: 0,
      gamesPlayedCount: 0,
      startedSessionCount: 0,
      periods: [],
      topGames: [],
    },
  }
  if (!(route in responses)) throw new Error(`Unexpected shell API route: ${route}`)
  return ok(responses[route])
}
async function showLibrary(mode: Mode) {
  await waitFor(() => expect(document.querySelector('.avalon-shell.desktop')).not.toBeNull())
  if (mode === 'fullscreen') act(() => fullscreen(true))
  await waitFor(() => expect(document.querySelector('.startup-presentation')).toBeNull())
  await waitFor(() => expect(document.querySelector(`.avalon-shell.${mode}`)).not.toBeNull())
  fireEvent.click(screen.getByRole('button', { name: 'Library' }))
  await waitFor(() => expect(document.querySelector('[data-avalon-game="1"]')).not.toBeNull())
}

it('FrontendCompositionBuildsBothSurfacesWithoutStorageOrWorkers: the named bridge composes separate desktop and fullscreen library state', async () => {
  const { request } = mount(<App />, shellResponse)
  await showLibrary('desktop')
  const desktopLibrary = document.querySelector('.avalon-library')
  fireEvent.change(screen.getByRole('textbox', { name: 'Search games' }), { target: { value: 'Shared' } })
  act(() => fullscreen(true))
  await waitFor(() => expect(document.querySelector('.avalon-shell.fullscreen')).not.toBeNull())
  await waitFor(() => expect(document.querySelector('.startup-presentation')).toBeNull())
  fireEvent.click(screen.getByRole('button', { name: 'Library' }))
  expect(document.querySelector('.avalon-library')).not.toBe(desktopLibrary)
  expect((libraryLabel('Search games') as HTMLInputElement).value).toBe('')
  fireEvent.change(libraryLabel('Search games'), { target: { value: 'title' } })
  returnToLibrary()
  act(() => fullscreen(false))
  await waitFor(() => expect(document.querySelector('.avalon-shell.desktop')).not.toBeNull())
  await waitFor(() => expect(document.querySelector('.startup-presentation')).toBeNull())
  expect((screen.getByRole('textbox', { name: 'Search games' }) as HTMLInputElement).value).toBe('Shared')
  expect(request.mock.calls.some(([input]) => input.route === 'library.get')).toBe(true)
  expect(request.mock.calls.some(([input]) => input.route === 'library.workspace')).toBe(true)
  expect(screen.queryByRole('alert')).toBeNull()
})

describe.each(['desktop', 'fullscreen'] as const)('%s source API library contracts', (mode) => {
  it('LibraryUsesBackendGroupingAndMetadataWithoutRepositories: projects one backend work with both storefront entries', async () => {
    const { client } = mount(<App />, shellResponse)
    await showLibrary(mode)
    expect(document.querySelectorAll('[data-avalon-game="1"]')).toHaveLength(1)
    expect(document.querySelectorAll('[data-avalon-game]')).toHaveLength(1)
    const saved = client.getQueryData<LibraryResponse>(['api', 'library.get'])!
    expect(saved.games).toEqual(library.games)
    expect(saved.games[0]).toMatchObject({
      title: 'Shared title',
      playtimeMinutes: 150,
      bucket: 'bounced',
      firstReleaseYear: 2007,
    })
    expect(saved.games[0].entries.map(({ store, playtimeMinutes }) => [store, playtimeMinutes])).toEqual([
      ['steam', 100],
      ['gog', 50],
    ])
    fireEvent.click(document.querySelector<HTMLButtonElement>('[data-avalon-game="1"]')!)
    await screen.findByRole('heading', { name: 'Shared title' })
    expect(screen.getByText('Shared metadata')).toBeDefined()
    expect(document.querySelector('.avalon-details')?.textContent).toContain('2007')
    expect(document.querySelector('.avalon-details')?.textContent).toContain('2hplayed')
  })

  it('MetadataConflictPreservesObservedRevisionForReview: both rejected writes keep seen and the name draft', async () => {
    const original: Metadata = { workId: 1, title: 'Original', isPinned: false, fields: [], revision: 'seen' }
    const writes: ApiRequest[] = []
    let fieldsVisible = false
    const respond = (input: ApiRequest) => {
      if (input.route === 'metadata.get')
        return ok({
          ...original,
          fields: fieldsVisible ? [{ field: 'name', value: 'Original', source: null }] : [],
        })
      if (input.route !== 'metadata.put') throw new Error(`Unexpected metadata API route: ${input.route}`)
      writes.push(input)
      return { ok: false, status: 409 }
    }
    installBridge(respond)
    expect(await apiRequest<Metadata>('metadata.get', { workId: 1 })).toEqual(original)
    // The source service accepts an empty field collection. A rendered edit requires an exposed field.
    fieldsVisible = true
    for (let attempt = 1; attempt <= 2; attempt++) {
      const view = mount(
        <MetadataDialog workId={1} title="Original" mode={mode} onClose={vi.fn()} />,
        respond,
      )
      if (mode === 'fullscreen') fireEvent.click(await screen.findByRole('button', { name: /^Name ·/ }))
      const name = (await screen.findByLabelText('Name')) as HTMLInputElement
      if (attempt === 1) fireEvent.change(name, { target: { value: 'Draft' } })
      else expect(name.value).toBe('Draft')
      fireEvent.click(screen.getByRole('button', { name: 'Save name' }))
      await waitFor(() => expect(writes).toHaveLength(attempt))
      await screen.findByText(/Your name draft is preserved/)
      expect((screen.getByRole('button', { name: 'Save name' }) as HTMLButtonElement).disabled).toBe(true)
      expect(name.value).toBe('Draft')
      // Surface remounts retain the session draft; they must not silently advance its captured revision.
      view.unmount()
    }
    expect(writes).toEqual(
      [1, 2].map(() =>
        expect.objectContaining({
          route: 'metadata.put',
          params: { workId: 1 },
          body: { field: 'name', value: 'Draft', expectedRevision: 'seen' },
        }),
      ),
    )
  })

  it('ListEditsUseDisplayedRevisionAndPreserveStateOnConflict: rename retains Next and the rejected draft', async () => {
    const original: LibraryResponse = {
      games: [],
      lists: [
        {
          id: 7,
          name: 'Next',
          description: null,
          isLive: false,
          releaseIds: [1],
          revision: 'displayed-revision',
        },
      ],
    }
    const { request, client } = mount(<LibraryTools mode={mode} />, (input) => {
      if (input.route === 'library.get') return ok(original)
      if (input.route === 'list.update') return { ok: false, status: 409 }
      throw new Error(`Unexpected list API route: ${input.route}`)
    })
    const panel = (await screen.findByRole('heading', { name: 'Next' })).closest('section')!
    fireEvent.click(within(panel).getByRole('button', { name: 'Edit' }))
    fireEvent.change(within(panel).getByLabelText('List name'), { target: { value: 'Changed elsewhere' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save list' }))
    await screen.findByText(/Your draft is preserved/)
    expect(screen.getByRole('alert').textContent).toContain('409')
    expect((within(panel).getByLabelText('List name') as HTMLInputElement).value).toBe('Changed elsewhere')
    expect(within(panel).getByRole('heading', { name: 'Next' })).toBeDefined()
    expect(client.getQueryData<LibraryResponse>(['api', 'library.get'])?.lists).toEqual(original.lists)
    expect(
      request.mock.calls.filter(([input]) => input.route === 'list.update').map(([input]) => input),
    ).toEqual([
      expect.objectContaining({
        route: 'list.update',
        params: { listId: 7 },
        body: { name: 'Changed elsewhere', description: null, expectedRevision: 'displayed-revision' },
      }),
    ])
  })

  it('SettingsLoadsRemoteHiddenAndManualEntriesWithoutRepositories: preserves server titles, edition count and addedAt', async () => {
    const hidden = [{ workId: 2, title: 'Hidden', hiddenAt, storeEntryCount: 3 }]
    const manual: ManualGame & { addedAt: string; updatedAt: string } = {
      ownershipId: 1,
      releaseId: 2,
      workId: 3,
      title: 'Manual',
      executablePath: null,
      installPath: null,
      firstReleaseYear: 2001,
      platformLabel: 'PC',
      igdbId: null,
      steamAppId: null,
      addedAt: hiddenAt,
      updatedAt: hiddenAt,
      revision: 'manual-revision',
      igdbMappingRevision: 0,
    }
    const { client, request } = mount(<LibraryTools mode={mode} />, ({ route }) => {
      if (route === 'library.get') return ok({ games: [], lists: [] })
      if (route === 'hidden.get') return ok(hidden)
      if (route === 'manual.get') return ok([manual])
      throw new Error(`Unexpected settings API route: ${route}`)
    })
    fireEvent.click(screen.getByRole('button', { name: 'Hidden games' }))
    expect(await screen.findByText('Hidden', { exact: true })).toBeDefined()
    expect(screen.getByText(/3 editions/)).toBeDefined()
    expect(client.getQueryData(['api', 'hidden.get', undefined])).toEqual(hidden)
    fireEvent.click(screen.getByRole('button', { name: 'Manual games' }))
    expect(await screen.findByRole('button', { name: 'Manual' })).toBeDefined()
    expect(screen.getByText('2001 · PC')).toBeDefined()
    expect(client.getQueryData<ManualGame[]>(['api', 'manual.get', undefined])?.[0]).toEqual(manual)
    expect(client.getQueryData<(typeof manual)[]>(['api', 'manual.get', undefined])?.[0].addedAt).toBe(
      hiddenAt,
    )
    expect(new Set(request.mock.calls.map(([input]) => input.route))).toEqual(
      new Set(['library.get', 'hidden.get', 'manual.get']),
    )
  })

  it('RemoteManualIdentifierConflictRemainsAnInlineFieldError: create-time MappingChanged stays on IGDB', async () => {
    const close = vi.fn()
    const { request } = mount(
      <div className={`mode-${mode}`}>
        <ManualEditor initial={null} onClose={close} />
      </div>,
      () => ({ ok: false, status: 409, data: { field: 'IgdbId', reason: 'MappingChanged' } }),
    )
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Manual' } })
    fireEvent.change(screen.getByLabelText('IGDB ID'), { target: { value: '42' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save game' }))
    const alert = await screen.findByRole('alert')
    const input = screen.getByLabelText('IGDB ID') as HTMLInputElement
    expect(input.getAttribute('aria-invalid')).toBe('true')
    expect(document.getElementById(input.getAttribute('aria-describedby')!)).toBe(alert)
    expect(alert.textContent).toBe(
      "The game's IGDB match changed. Cancel and reopen this form before saving.",
    )
    expect(screen.getAllByRole('alert')).toHaveLength(1)
    expect(screen.queryByText(/The request could not finish/)).toBeNull()
    expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('Manual')
    expect(input.value).toBe('42')
    expect(close).not.toHaveBeenCalled()
    expect(request).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({
        route: 'manual.create',
        body: expect.objectContaining({ title: 'Manual', igdbId: 42 }),
      }),
    )
  })
})
