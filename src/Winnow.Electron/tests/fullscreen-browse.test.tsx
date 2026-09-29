// @vitest-environment jsdom
import { useState } from 'react'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { libraryRole, libraryLabel, returnToLibrary } from './library-controls'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonLibrary, AvalonShell } from '../src/renderer/themes/avalon'
import { useLibrary } from '../src/renderer/api/hooks'
import { clearViewState } from '../src/renderer/viewState'
import { DEFAULT_PROFILE, type ThemeContext } from '../src/shared/theme'
import type { LibraryGame, Mode } from '../src/renderer/api/types'
import type { ApiRequest } from '../src/shared/bridge'

vi.mock('../src/renderer/features/SettingsPreferences', () => ({
  usePresentationPreferences: () => ({ loaded: true, values: { DefaultSort: 'DormantLongest' } }),
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
    measure() {},
    scrollToIndex() {},
    scrollToOffset() {},
  }),
}))
const games: LibraryGame[] = Array.from({ length: 12 }, (_, index) => ({
  workId: index + 1,
  title: `Game ${String(index + 1).padStart(2, '0')}`,
  bucket: ['never_played', 'stale_but_patched', 'derelict'][index % 3],
  playtimeMinutes: index * 60,
  entries: [
    {
      ownershipId: index + 1,
      releaseId: index + 1,
      workId: index + 1,
      title: 'Edition',
      store: 'steam',
      installed: index % 2 === 0,
      playtimeMinutes: index * 60,
    },
  ],
}))
const workspace = { works: [], externalIds: [], epicLaunchKeys: {}, pluginActions: {} }
function Fixture() {
  const [mode, setMode] = useState<Mode>('desktop'),
    library = useLibrary()
  const context: ThemeContext = {
    mode,
    page: 'library',
    games: library.data?.games ?? [],
    loading: false,
    selectedWorkId: null,
    feed: undefined,
    profile: DEFAULT_PROFILE,
    children: null,
    setPage: vi.fn(),
    openGame: vi.fn(),
    toggleFullscreen: vi.fn(),
    renderScreen: () => null,
    actions: { launch: vi.fn() },
    components: {
      Artwork: () => <span />,
      Impression: ({ children }) => <>{children}</>,
      GameCard: () => null,
      GamePreview: () => null,
      ArtworkEffects: ({ children }) => <>{children}</>,
    },
  }
  return (
    <>
      <button onClick={() => setMode(mode === 'desktop' ? 'fullscreen' : 'desktop')}>Switch surface</button>
      <AvalonShell {...context}>
        <AvalonLibrary {...context} />
      </AvalonShell>
    </>
  )
}
function setup() {
  const request = vi.fn(async (input: ApiRequest) => ({
    ok: true,
    status: 200,
    data: input.route === 'library.get' ? { games, lists: [] } : workspace,
  }))
  Object.defineProperty(window, 'winnow', { configurable: true, value: { request } })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  client.setQueryData(['api', 'library.get'], { games, lists: [] })
  client.setQueryData(['api', 'library.workspace', undefined], workspace)
  render(
    <QueryClientProvider client={client}>
      <Fixture />
    </QueryClientProvider>,
  )
  return request
}
const changeSurface = () => fireEvent.click(libraryRole('button', { name: 'Switch surface' }))
const value = (name: string) => (libraryLabel(name) as HTMLInputElement).value
const change = (element: HTMLElement, value: string) => fireEvent.change(element, { target: { value } })
const filterPanel = () => libraryRole('dialog', { name: 'Library filters' })
const cards = () =>
  [...document.querySelectorAll('[data-row-active="true"] [data-avalon-game]')].map((element) =>
    Number(element.getAttribute('data-avalon-game')),
  )
beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  for (const mode of ['desktop', 'fullscreen'])
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
})

it('keeps fullscreen filter, collection and sort edits local until Apply without changing desktop', () => {
  const request = setup()
  change(libraryLabel('Search games'), 'desktop')
  change(libraryLabel('Sort'), 'time')
  changeSurface()
  const before = cards()
  fireEvent.click(libraryRole('button', { name: 'Filters' }))
  const panel = within(filterPanel())
  change(panel.getByLabelText('Installation'), 'true')
  change(panel.getByLabelText('Collection'), 'never_played')
  change(panel.getByLabelText('Sort'), 'title-desc')
  expect(cards()).toEqual(before)
  expect(document.querySelector('.avalon-fullscreen-library-summary')?.textContent).toContain(
    'Dormant longest',
  )
  fireEvent.click(panel.getByRole('button', { name: 'Apply filters' }))
  expect(cards()).toEqual([7, 1])
  expect(document.querySelector('.avalon-fullscreen-library-summary')?.textContent).toContain('Name Z–A')
  changeSurface()
  expect(value('Search games')).toBe('desktop')
  expect(value('Sort')).toBe('time')
  expect(request.mock.calls.some(([input]) => input.route === 'preferences.save')).toBe(false)
})

it('discards changed fullscreen sort and collection on Cancel or Back and clears only draft filters', () => {
  setup()
  changeSurface()
  change(libraryLabel('Sort'), 'time-low')
  for (const cancel of ['Cancel', 'Back']) {
    fireEvent.click(libraryRole('button', { name: 'Filters' }))
    const panel = within(filterPanel())
    change(panel.getByLabelText('Sort'), 'title-desc')
    change(panel.getByLabelText('Collection'), 'derelict')
    change(panel.getByLabelText('Installation'), 'true')
    if (cancel === 'Cancel') fireEvent.click(panel.getByRole('button', { name: 'Cancel' }))
    else fireEvent.keyDown(filterPanel(), { key: 'Escape' })
    expect(value('Sort')).toBe('time-low')
    expect(libraryRole('button', { name: 'All games12' }).getAttribute('aria-pressed')).toBe('true')
  }
  fireEvent.click(libraryRole('button', { name: 'Filters' }))
  const panel = within(filterPanel())
  change(panel.getByLabelText('Sort'), 'title-desc')
  change(panel.getByLabelText('Collection'), 'derelict')
  change(panel.getByLabelText('Installation'), 'true')
  const beforeClear = cards()
  fireEvent.click(panel.getByRole('button', { name: 'Clear filters' }))
  expect((panel.getByLabelText('Sort') as HTMLSelectElement).value).toBe('title-desc')
  expect((panel.getByLabelText('Collection') as HTMLSelectElement).value).toBe('all')
  expect((panel.getByLabelText('Installation') as HTMLSelectElement).value).toBe('')
  expect(cards()).toEqual(beforeClear)
  fireEvent.click(panel.getByRole('button', { name: 'Apply filters' }))
  expect(value('Sort')).toBe('title-desc')
})

it('offers exactly four fullscreen shortcuts and clears its current cut without touching desktop', () => {
  setup()
  change(libraryLabel('Search games'), 'desktop')
  changeSurface()
  const shortcuts = within(libraryRole('group', { name: 'Library collections' }))
  expect(shortcuts.getAllByRole('button').map((button) => button.textContent)).toEqual([
    'All games12',
    'Installed6',
    'Never played4',
    'Patched4',
  ])
  change(libraryLabel('Search games'), 'Game 01')
  fireEvent.click(libraryRole('button', { name: 'Filters' }))
  change(within(filterPanel()).getByLabelText('Installation'), 'false')
  fireEvent.click(within(filterPanel()).getByRole('button', { name: 'Apply filters' }))
  expect(cards()).toEqual([])
  fireEvent.click(shortcuts.getByRole('button', { name: 'Installed6' }))
  expect(value('Search games')).toBe('')
  returnToLibrary()
  expect(cards().every((id) => id % 2 === 1)).toBe(true)
  fireEvent.click(shortcuts.getByRole('button', { name: 'Installed6' }))
  expect(shortcuts.getByRole('button', { name: 'Installed6' }).getAttribute('aria-pressed')).toBe('true')
  changeSurface()
  expect(value('Search games')).toBe('desktop')
})
