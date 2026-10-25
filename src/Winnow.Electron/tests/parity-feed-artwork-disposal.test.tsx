// @vitest-environment jsdom
import { useSyncExternalStore, type ReactNode } from 'react'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, expect, it, vi } from 'vitest'
import type { ApiRequest } from '../src/shared/bridge'
import type { ThemeContext } from '../src/shared/theme'
import type { LibraryGame } from '../src/renderer/api/types'
import { Artwork } from '../src/renderer/components/Artwork'
import { artworkImages, closeArtworkImages } from '../src/renderer/components/artwork-images'
import { AvalonFeedCard } from '../src/renderer/themes/avalon-feed-card'
import { AvalonCoverWorkspace } from '../src/renderer/themes/avalon-desktop-cover'
import { FeedDeck, receiptDuration } from '../src/renderer/themes/avalon-feed-model'
import { coverProfile } from './cover-fixtures'

const clients: QueryClient[] = []
afterEach(async () => {
  cleanup()
  for (const client of clients.splice(0)) {
    await closeArtworkImages(client)
    client.clear()
  }
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it.each(['decoded', 'pending'] as const)(
  'receipt replacement releases the outgoing %s artwork consumer without rebuilding surviving cards',
  async (phase) => {
    const NativeBlob = Blob
    const sources = new WeakMap<Blob, string>()
    vi.stubGlobal(
      'Blob',
      class extends NativeBlob {
        constructor(parts: BlobPart[], options?: BlobPropertyBag) {
          super(parts, options)
          sources.set(this, `blob:${String.fromCharCode(...(parts[0] as Uint8Array))}`)
        }
      },
    )
    URL.createObjectURL = vi.fn((blob: Blob) => sources.get(blob)!)
    URL.revokeObjectURL = vi.fn()
    vi.stubGlobal(
      'Image',
      class {
        src = ''
        naturalWidth = 160
        naturalHeight = 240
        async decode() {}
      },
    )
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    )
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
      x: 0,
      y: 0,
      left: 0,
      top: 0,
      width: 148,
      height: 222,
      right: 148,
      bottom: 222,
      toJSON() {},
    })
    let releaseOld!: (value: string) => void
    const pending = new Promise<string>((resolve) => {
      releaseOld = resolve
    })
    const pixels = (id: string) => `data:image/png;base64,${btoa(`cover-${id}`)}`
    const artwork = vi.fn(async (_provider: string, id: string, _width: number, _requestId: string) =>
      id === '1' && phase === 'pending' ? pending : pixels(id),
    )
    const cancelRequest = vi.fn(async () => true)
    Object.defineProperty(window, 'winnow', {
      configurable: true,
      value: {
        artwork,
        cancelRequest,
        request: vi.fn(async (input: ApiRequest) => ({
          ok: true,
          status: 200,
          data:
            input.route === 'artworkState'
              ? {
                  current: { previewKey: { provider: 'steam', id: String(input.params?.workId) } },
                  revision: 'source',
                }
              : [],
        })),
      },
    })
    const game = (id: number): LibraryGame => ({
      workId: id,
      title: id === 101 ? 'Held 101' : `Shown ${id}`,
      bucket: 'never_played',
      playtimeMinutes: 0,
      entries: [
        {
          workId: id,
          ownershipId: id,
          releaseId: id,
          title: `Game ${id}`,
          store: 'steam',
          installed: false,
          playtimeMinutes: 0,
        },
      ],
    })
    const games = [1, 2, 3, 4, 5, 101].map(game)
    const row = (item: LibraryGame) => ({
      game: item,
      releaseId: item.workId,
      reason: `Never opened since it joined your library. (${item.title})`,
    })
    const deck = new FeedDeck(
      async () => ({ saved: true }),
      async () => [],
      () => {},
    )
    deck.receive(
      [
        {
          id: 'ready_to_play',
          title: 'Installed and waiting',
          blurb: 'Already on your disk, nothing sunk.',
          feedback: true,
          rows: games.slice(0, 5).map(row),
          reserve: games.slice(5).map(row),
        },
      ],
      'source-library',
    )
    const context = {
      mode: 'desktop',
      page: 'discover',
      selectedWorkId: null,
      setPage: vi.fn(),
      openGame: vi.fn(),
      toggleFullscreen: vi.fn(),
      games,
      loading: false,
      profile: coverProfile(),
      children: null,
      renderScreen: () => null,
      actions: { launch: vi.fn() },
      components: {
        Artwork,
        ArtworkEffects: ({ children }: { children: ReactNode }) => <>{children}</>,
        GamePreview: () => null,
      },
    } as unknown as ThemeContext
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    clients.push(client)
    const cache = artworkImages(client)
    function Cards() {
      useSyncExternalStore(deck.subscribe, deck.snapshot)
      const shelf = deck.shelves[0]
      return (
        <>
          {shelf.rows.map((item) => (
            <AvalonFeedCard key={item.game.workId} context={context} deck={deck} shelf={shelf} row={item} />
          ))}
        </>
      )
    }
    const view = render(
      <QueryClientProvider client={client}>
        <AvalonCoverWorkspace.Provider
          value={{
            preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
            works: [],
            externalIds: [],
            pluginActions: {},
            epicLaunchKeys: {},
          }}
        >
          <Cards />
        </AvalonCoverWorkspace.Provider>
      </QueryClientProvider>,
    )
    try {
      await waitFor(() => expect(artwork).toHaveBeenCalledTimes(5))
      await waitFor(() =>
        expect(view.container.querySelectorAll('img')).toHaveLength(phase === 'decoded' ? 5 : 4),
      )
      expect(cache.liveSlots).toBe(5)
      const original = screen.getByRole('button', { name: 'View Shown 1' })
      const oldImage = original.querySelector('img')
      const survivors = [2, 3, 4, 5].map((id) => screen.getByRole('button', { name: `View Shown ${id}` }))
      if (phase === 'decoded') {
        // Eviction must retain pixels still leased by the visible card, then free them at removal.
        cache.invalidate(JSON.stringify(['artwork-image', 'steam', '1', 160, 'source']))
        expect(URL.revokeObjectURL).not.toHaveBeenCalledWith('blob:cover-1')
      }
      const outgoing = deck.shelves[0].rows[0]
      await act(async () => {
        await deck.respond(outgoing, 0)
      })
      act(() => deck.tick(receiptDuration))
      await screen.findByRole('button', { name: 'View Held 101' })
      await waitFor(() => expect(artwork).toHaveBeenCalledTimes(6))
      await waitFor(() => expect(cache.liveSlots).toBe(5))
      expect(original.isConnected).toBe(false)
      expect(oldImage?.getAttribute('src') ?? null).toBeNull()
      survivors.forEach((element, index) => {
        expect(screen.getByRole('button', { name: `View Shown ${index + 2}` })).toBe(element)
      })
      expect(deck.shelves[0].rows.map((item) => item.releaseId)).toEqual([101, 2, 3, 4, 5])
      if (phase === 'decoded') {
        expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:cover-1')
      } else {
        expect(cancelRequest).toHaveBeenCalledWith(artwork.mock.calls.find((call) => call[1] === '1')![3])
        await act(async () => releaseOld(pixels('1')))
        expect(client.getQueryData(['artwork-image', 'steam', '1', 160, 'source'])).toBeUndefined()
        expect(
          [...view.container.querySelectorAll('img')].some(
            (img) => img.getAttribute('src') === 'blob:cover-1',
          ),
        ).toBe(false)
        expect(URL.createObjectURL).toHaveBeenCalledTimes(5)
      }
      view.unmount()
      expect(cache.liveSlots).toBe(0)
    } finally {
      releaseOld(pixels('1'))
      deck.dispose()
    }
  },
)
