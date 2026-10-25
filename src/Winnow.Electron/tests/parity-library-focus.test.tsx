// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { libraryRole } from './library-controls'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AvalonLibrary, avalon } from '../src/renderer/themes/avalon'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeContext } from '../src/shared/theme'
import { clearViewState, libraryScroll } from '../src/renderer/viewState'
import type { LibraryGame } from '../src/renderer/api/types'

const fixtures = vi.hoisted(() => ({ games: [] as LibraryGame[], offsets: [] as number[] }))
vi.mock('../src/renderer/api/hooks', () => ({
  useLibrary: () => ({ data: { games: fixtures.games, lists: [] } }),
  useWorkspace: () => ({ data: undefined }),
}))
vi.mock('../src/renderer/features/LibraryTools', () => ({
  CreateListButton: () => <button>New list…</button>,
  LibraryTools: () => (
    <section aria-label="Library management">
      <nav>
        <button>Lists</button>
        <button>Manual games</button>
      </nav>
      <button>Save tool</button>
    </section>
  ),
}))
vi.mock('../src/renderer/features/SettingsPreferences', () => ({
  usePresentationPreferences: () => ({ values: { DefaultSort: 'NameAscending' } }),
}))
vi.mock('@tanstack/react-virtual', () => ({
  useVirtualizer: (options: {
    count: number
    estimateSize(): number
    getScrollElement(): HTMLElement | null
  }) => ({
    getTotalSize: () => options.count * options.estimateSize(),
    getVirtualItems: () =>
      [50, 51, 52]
        .filter((index) => index < options.count)
        .map((index) => ({ key: index, index, start: index * options.estimateSize() })),
    measure: vi.fn(),
    scrollToIndex: vi.fn(),
    scrollToOffset: (value: number) => {
      fixtures.offsets.push(value)
      const element = options.getScrollElement()
      if (element) element.scrollTop = value
    },
  }),
}))
let sequence = 0
const frames = new Map<number, FrameRequestCallback>()
function flushFrames() {
  act(() => {
    const pending = [...frames.values()]
    frames.clear()
    pending.forEach((callback) => callback(0))
  })
}
beforeEach(() => {
  fixtures.offsets = []
  fixtures.games = Array.from({ length: 900 }, (_, index) => ({
    workId: index + 1,
    title: `Game ${String(index + 1).padStart(4, '0')}`,
    bucket: 'never_played',
    playtimeMinutes: 0,
    entries: [
      {
        ownershipId: index + 1,
        releaseId: index + 1,
        workId: index + 1,
        title: `Game ${index + 1}`,
        store: 'steam',
        installed: true,
        playtimeMinutes: 0,
      },
    ],
  }))
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    const id = ++sequence
    frames.set(id, callback)
    return id
  })
  vi.stubGlobal('cancelAnimationFrame', (id: number) => frames.delete(id))
})
afterEach(() => {
  cleanup()
  frames.clear()
  vi.unstubAllGlobals()
  for (const mode of ['desktop', 'fullscreen']) {
    const prefix = `avalon:library:${mode}`
    for (const key of [
      'query',
      'bucket',
      'store',
      'list',
      'sort',
      'default-sort',
      'view',
      'density',
      'tools',
      'rows',
      'viewport',
      'selected',
      'selection',
      'rules',
      'filter-order',
    ])
      clearViewState(`${prefix}:${key}`)
    libraryScroll.delete(prefix)
  }
})

it.each([
  ['desktop', 'button'],
  ['desktop', 'escape'],
  ['fullscreen', 'button'],
  ['fullscreen', 'escape'],
] as const)(
  'restores the same deep library game and viewport after closing management in %s with %s',
  (mode, close) => {
    const context: ThemeContext = {
      mode,
      page: 'library',
      games: fixtures.games,
      loading: false,
      selectedWorkId: null,
      profile: selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon),
      feed: undefined,
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
    render(
      <QueryClientProvider client={new QueryClient()}>
        <AvalonLibrary {...context} />
      </QueryClientProvider>,
    )
    flushFrames()
    const viewport = document.querySelector<HTMLElement>('.avalon-library-scroll')!
    viewport.scrollTop = 24560
    fireEvent.scroll(viewport)
    const card = document.querySelectorAll<HTMLButtonElement>('[data-avalon-game]')[2]!
    const id = card.dataset.avalonGame
    act(() => card.focus())
    fireEvent.click(libraryRole('button', { name: 'Manage library' }))
    flushFrames()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Lists' }))
    if (close === 'button') fireEvent.click(screen.getByRole('button', { name: 'Close tools' }))
    else fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    flushFrames()
    flushFrames()
    expect(document.querySelector<HTMLElement>('.avalon-library-scroll')!.scrollTop).toBe(24560)
    expect((document.activeElement as HTMLElement).dataset.avalonGame).toBe(id)
    expect(fixtures.offsets.at(-1)).toBe(24560)
    expect(context.openGame).not.toHaveBeenCalled()
  },
)
