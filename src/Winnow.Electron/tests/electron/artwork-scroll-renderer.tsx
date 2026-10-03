import '../../src/renderer/styles.css'
import '../../src/renderer/themes/avalon.css'
import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Artwork } from '../../src/renderer/components/Artwork'
import { artworkImages, closeArtworkImages } from '../../src/renderer/components/artwork-images'
import { AvalonLibrary, avalon } from '../../src/renderer/themes/avalon'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeContext } from '../../src/shared/theme'
import type { LibraryGame, Mode } from '../../src/renderer/api/types'
import type { ApiRequest, WinnowBridge } from '../../src/shared/bridge'

const games: LibraryGame[] = Array.from({ length: 1045 }, (_, index) => ({
  workId: index + 1,
  title: `Game ${String(index + 1).padStart(4, '0')}`,
  bucket: 'unplayed',
  playtimeMinutes: 0,
  entries: [
    {
      workId: index + 1,
      releaseId: index + 1,
      ownershipId: index + 1,
      title: `Game ${String(index + 1).padStart(4, '0')}`,
      store: 'steam',
      installed: true,
      playtimeMinutes: 0,
    },
  ],
}))
const client = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } })
const cache = artworkImages(client)
const canvas = document.createElement('canvas')
canvas.width = 240
canvas.height = 360
const context = canvas.getContext('2d')!
const pixels = context.createImageData(canvas.width, canvas.height)
let random = 12345
for (let offset = 0; offset < pixels.data.length; offset += 4) {
  random = (Math.imul(random, 1664525) + 1013904223) | 0
  pixels.data[offset] = random & 255
  pixels.data[offset + 1] = (random >>> 8) & 255
  pixels.data[offset + 2] = (random >>> 16) & 255
  pixels.data[offset + 3] = 255
}
context.putImageData(pixels, 0, 0)
const binary = atob(canvas.toDataURL('image/png').split(',')[1])
const png = Uint8Array.from(binary, (byte) => byte.charCodeAt(0))
const selected = new Set<number>(),
  requested = new Set<string>()
const pending = new Map<
  string,
  { timer?: ReturnType<typeof setTimeout>; finish(): void; canceled: boolean }
>()
let canceled = 0,
  imageReads = 0,
  deliveredLate = 0,
  decodeStarted = 0,
  decodeRetired = 0,
  decodeActive = 0
let maxPending = 0,
  maxLive = 0
const originalDecode = HTMLImageElement.prototype.decode
const activeDecodes = new Map<
  number,
  {
    image: HTMLImageElement
    source: string
    sourceRevoked: boolean
    startedAt: number
    nativeResult: 'pending' | 'resolved' | 'rejected'
    error?: string
    delayStartedAt?: number
    delayFinished: boolean
  }
>()
const completedSources = new Set<string>()
let completedRevoked = 0
const originalRevoke = URL.revokeObjectURL.bind(URL)
URL.revokeObjectURL = (source) => {
  if (completedSources.has(source)) completedRevoked++
  for (const tracked of activeDecodes.values()) if (tracked.source === source) tracked.sourceRevoked = true
  originalRevoke(source)
}
HTMLImageElement.prototype.decode = function () {
  const image = this
  decodeStarted++
  decodeActive++
  const id = decodeStarted
  const tracked = {
    image,
    source: image.src,
    sourceRevoked: false,
    startedAt: Date.now(),
    nativeResult: 'pending' as 'pending' | 'resolved' | 'rejected',
    error: undefined as string | undefined,
    delayStartedAt: undefined as number | undefined,
    delayFinished: false,
  }
  activeDecodes.set(id, tracked)
  // Run Chromium's real PNG decoder, then hold retirement briefly so scrolling
  // deterministically exercises cancellation during the owned decode lifetime.
  return originalDecode
    .call(image)
    .then(
      () => {
        tracked.nativeResult = 'resolved'
        tracked.delayStartedAt = Date.now()
        return new Promise<void>((resolve) =>
          setTimeout(() => {
            tracked.delayFinished = true
            resolve()
          }, 40),
        )
      },
      (error: unknown) => {
        tracked.nativeResult = 'rejected'
        tracked.error = String(error)
        return Promise.reject(error)
      },
    )
    .finally(() => {
      decodeActive--
      if (!image.getAttribute('src')) decodeRetired++
      else completedSources.add(image.src)
      activeDecodes.delete(id)
    })
}
const probe = {
  start: (_mode: Mode) => {},
  refreshSelections: () => client.invalidateQueries({ queryKey: ['artwork'] }),
  state() {
    maxPending = Math.max(maxPending, cache.pendingCount)
    maxLive = Math.max(maxLive, cache.liveSlots)
    const viewport = document.querySelector('.avalon-library-scroll')?.getBoundingClientRect()
    const covers = [...document.querySelectorAll<HTMLElement>('.avalon-library [data-avalon-game]')]
    const visible = covers
      .filter((cover) => {
        const bounds = cover.getBoundingClientRect()
        return (
          viewport &&
          !cover.closest('[aria-hidden="true"]') &&
          bounds.bottom > viewport.top + 4 &&
          bounds.top < viewport.bottom - 4 &&
          bounds.right > viewport.left &&
          bounds.left < viewport.right
        )
      })
      .map((cover) => {
        const artwork = cover.querySelector<HTMLElement>('.artwork')
        const image = artwork?.querySelector('img')
        return {
          id: Number(cover.dataset.avalonGame),
          state: artwork?.dataset.state,
          decoded: !!image && image.complete && image.naturalWidth > 0,
          source: image?.getAttribute('src') ?? null,
          complete: image?.complete ?? false,
          width: artwork?.getBoundingClientRect().width ?? 0,
        }
      })
    return {
      selected: selected.size,
      requested: requested.size,
      imageReads,
      imageQueries: client.getQueryCache().findAll({ queryKey: ['artwork-image'] }).length,
      canceled,
      deliveredLate,
      decodeStarted,
      decodeRetired,
      completedDecodedBytes: completedSources.size * canvas.width * canvas.height * 4,
      completedRevoked,
      decodeActive,
      activeDecodes: [...activeDecodes].map(([id, tracked]) => ({
        id,
        source: tracked.source,
        sourceRevoked: tracked.sourceRevoked,
        currentSource: tracked.image.src,
        sourceAttribute: tracked.image.getAttribute('src'),
        complete: tracked.image.complete,
        naturalWidth: tracked.image.naturalWidth,
        naturalHeight: tracked.image.naturalHeight,
        elapsedMs: Date.now() - tracked.startedAt,
        nativeResult: tracked.nativeResult,
        error: tracked.error,
        delayStartedAt: tracked.delayStartedAt,
        delayElapsedMs:
          tracked.delayStartedAt === undefined ? undefined : Date.now() - tracked.delayStartedAt,
        delayFinished: tracked.delayFinished,
      })),
      bridgePending: pending.size,
      live: cache.liveSlots,
      pending: cache.pendingCount,
      decodedBytes: cache.decodedBytes,
      maxPending,
      maxLive,
      mounted: covers.length,
      visible,
    }
  },
}
Object.assign(window, { artworkScrollProbe: probe })
window.winnow = {
  request: async (input: ApiRequest) => {
    let data: unknown = []
    if (input.route === 'artworkState') {
      const id = Number(input.params?.workId)
      selected.add(id)
      data = { revision: 'scroll-fixture', current: { previewKey: { provider: 'fixture', id: String(id) } } }
    } else if (input.route === 'library.get') data = { games, lists: [] }
    else if (input.route === 'library.workspace')
      data = {
        works: games.map((game) => ({ id: game.workId, name: game.title })),
        externalIds: [],
        ownerships: [],
        releases: [],
        identityLinks: [],
        epicLaunchKeys: {},
        pluginActions: {},
      }
    else if (input.route === 'preferences.presentation.get')
      data = [{ preference: 'DefaultSort', value: 'name' }]
    else if (input.route === 'game.details') data = { ratings: [] }
    return { ok: true, status: 200, data }
  },
  artworkBytes: (_provider: string, id: string, _width: number, requestId: string) => {
    imageReads++
    // Keep the first six reads pending until scrolling detaches them, so late
    // cancellation coverage does not depend on renderer or test-runner speed.
    const hold = Number(id) <= 6 && !requested.has(id)
    requested.add(id)
    return new Promise<Uint8Array>((resolve) => {
      const entry = {
        timer: undefined as ReturnType<typeof setTimeout> | undefined,
        canceled: false,
        finish() {
          if (entry.canceled) deliveredLate++
          pending.delete(requestId)
          resolve(png.slice())
        },
      }
      if (!hold) entry.timer = setTimeout(entry.finish, 55)
      pending.set(requestId, entry)
    })
  },
  cancelRequest: async (requestId: string) => {
    const entry = pending.get(requestId)
    if (!entry || entry.canceled) return false
    canceled++
    entry.canceled = true
    clearTimeout(entry.timer)
    entry.timer = setTimeout(entry.finish, 120)
    return true
  },
} as unknown as WinnowBridge
window.addEventListener('pagehide', () => {
  void closeArtworkImages(client)
})

function Fixture() {
  const [mode, setMode] = useState<Mode | null>(null)
  probe.start = setMode
  if (!mode) return <p>Artwork scroll fixture ready</p>
  const profile = selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon)
  profile.appearance.reducedMotion = true
  const theme: ThemeContext = {
    mode,
    page: 'library',
    games,
    loading: false,
    profile,
    selectedWorkId: null,
    setPage() {},
    toggleFullscreen() {},
    feed: undefined,
    children: null,
    renderScreen: () => null,
    actions: { async launch() {} },
    openGame() {
      throw Error('The scroll fixture must not open Details')
    },
    components: {
      Artwork,
      GameCard: () => null,
      GamePreview: () => null,
      ArtworkEffects: () => null,
      Impression: ({ children }) => children,
    },
  }
  return (
    <div className={`avalon-shell ${mode}`} style={{ display: 'block', height: '100vh', padding: 24 }}>
      <AvalonLibrary {...theme} />
    </div>
  )
}
createRoot(document.getElementById('root')!).render(
  <QueryClientProvider client={client}>
    <Fixture />
  </QueryClientProvider>,
)
