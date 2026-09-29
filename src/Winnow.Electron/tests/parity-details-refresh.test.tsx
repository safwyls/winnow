// @vitest-environment jsdom
import { useState } from 'react'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Details } from '../src/renderer/features/Details'
import { AvalonLibrary, AvalonShell, avalon } from '../src/renderer/themes/avalon'
import { useLibrary } from '../src/renderer/api/hooks'
import { clearViewState } from '../src/renderer/viewState'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeContext } from '../src/shared/theme'
import type { ApiRequest } from '../src/shared/bridge'
import type { LibraryGame, Mode } from '../src/renderer/api/types'

vi.mock('../src/renderer/features/SettingsPreferences', () => ({
  usePresentationPreferences: () => ({ values: { DefaultSort: 'NameAscending' } }),
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
    scrollToIndex: vi.fn(),
    scrollToOffset: vi.fn(),
  }),
}))
function Fixture({ mode }: { mode: Mode }) {
  const [details, setDetails] = useState(false),
    library = useLibrary()
  const context: ThemeContext = {
    mode,
    page: 'library',
    games: library.data?.games ?? [],
    loading: false,
    selectedWorkId: null,
    profile: selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon),
    feed: undefined,
    children: null,
    setPage: vi.fn(),
    openGame: () => setDetails(true),
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
    <AvalonShell {...context}>
      {details ? (
        <Details workId={1} mode={mode} onClose={() => setDetails(false)} />
      ) : (
        <AvalonLibrary {...context} />
      )}
    </AvalonShell>
  )
}
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  clearViewState('draft:metadata-fields:1')
  clearViewState('metadata-fields:1:sending')
  for (const mode of ['desktop', 'fullscreen']) {
    for (const key of ['tab', 'previous-section', 'editing']) clearViewState(`${mode}:details:1:${key}`)
    for (const key of [
      'query',
      'bucket',
      'store',
      'list',
      'sort',
      'sort-before-list',
      'list-base',
      'selected',
      'selection',
      'rules',
    ])
      clearViewState(`avalon:library:${mode}:${key}`)
  }
})
describe.each(['desktop', 'fullscreen'] as const)('metadata refresh in %s', (mode) => {
  it.each([
    ['summary', 'About', 'A saved summary.'],
    ['publisher', 'Publisher', 'A saved publisher'],
    ['first_release_year', 'Release year', '2017'],
  ])(
    'refreshes saved %s facts and year-filtered lists while preserving another field draft',
    async (field, label, value) => {
      let revision = 'm1'
      const values: Record<string, string> = {
        name: 'Original title',
        summary: 'Original summary',
        publisher: 'Original publisher',
        first_release_year: '2006',
      }
      const game = (): LibraryGame => ({
        workId: 1,
        title: values.name!,
        summary: values.summary,
        publisher: values.publisher,
        firstReleaseYear: Number(values.first_release_year),
        bucket: 'never_played',
        playtimeMinutes: 0,
        entries: [
          {
            ownershipId: 10,
            releaseId: 100,
            workId: 1,
            title: values.name!,
            store: 'steam',
            installed: false,
            playtimeMinutes: 0,
          },
        ],
      })
      const library = () => ({
        games: [game()],
        lists: [
          {
            id: 30,
            name: '2006 only',
            isLive: true,
            revision: 'l1',
            filter: { yearFrom: 2006, yearTo: 2006 },
            releaseIds: values.first_release_year === '2006' ? [100] : [],
          },
        ],
      })
      const request = vi.fn(async (input: ApiRequest) => {
        let data: unknown = {}
        if (input.route === 'library.get') data = library()
        else if (input.route === 'library.workspace')
          data = { works: [], externalIds: [], epicLaunchKeys: {}, pluginActions: {} }
        else if (input.route === 'game.details')
          data = {
            workId: 1,
            readAtUtc: '2026-09-28T00:00:00Z',
            sessions: {},
            events: [],
            journalEntries: [],
            ratings: [],
            achievements: [],
          }
        else if (input.route === 'metadata.get')
          data = {
            workId: 1,
            title: values.name,
            revision,
            isPinned: false,
            fields: Object.entries(values).map(([field, value]) => ({ field, value, source: 'igdb' })),
          }
        else if (input.route === 'metadata.put') {
          const body = input.body as { field: string; value: string; expectedRevision: string }
          expect(body.expectedRevision).toBe('m1')
          values[body.field] = body.value
          revision = 'm2'
          data = { outcome: 'Applied' }
        }
        return { ok: true, status: 200, data }
      })
      Object.defineProperty(window, 'winnow', {
        configurable: true,
        value: { request, artwork: vi.fn().mockResolvedValue(null) },
      })
      vi.stubGlobal(
        'ResizeObserver',
        class {
          observe() {}
          disconnect() {}
        },
      )
      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      client.setQueryData(['api', 'library.get'], library())
      render(
        <QueryClientProvider client={client}>
          <Fixture mode={mode} />
        </QueryClientProvider>,
      )
      fireEvent.change(screen.getByLabelText('My lists'), { target: { value: '30' } })
      fireEvent.click(screen.getByRole('button', { name: 'View Original title' }))
      fireEvent.click(screen.getByRole('button', { name: 'Metadata' }))
      fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'An unfinished title' } })
      const edited = screen.getByLabelText(label)
      fireEvent.change(edited, { target: { value } })
      fireEvent.click(
        within(edited.closest('form')!).getByRole('button', { name: `Save ${label.toLowerCase()}` }),
      )
      await screen.findByText('Saved.')
      expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('An unfinished title')
      expect((screen.getByLabelText(label) as HTMLInputElement).value).toBe(value)
      fireEvent.click(screen.getByRole('button', { name: 'Overview' }))
      await screen.findByText(values.summary!)
      expect(screen.getByText(`${values.first_release_year} · ${values.publisher}`)).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Back to your library' }))
      if (field === 'first_release_year') await screen.findByText('No games match these filters.')
      else expect(screen.getByRole('button', { name: 'View Original title' })).toBeTruthy()
      expect((screen.getByLabelText('My lists') as HTMLSelectElement).value).toBe('30')
      expect(
        client.getQueryData<{ lists: { releaseIds: number[] }[] }>(['api', 'library.get'])!.lists[0]!
          .releaseIds,
      ).toHaveLength(field === 'first_release_year' ? 0 : 1)
      expect(request.mock.calls.filter(([input]) => input.route === 'metadata.put')).toHaveLength(1)
    },
  )
})
