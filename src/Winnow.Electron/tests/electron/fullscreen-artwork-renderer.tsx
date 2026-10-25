import '../../src/renderer/styles.css'
import '../../src/renderer/themes/avalon.css'
import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Artwork } from '../../src/renderer/components/Artwork'
import { CoverPadding } from '../../src/renderer/components/CoverPadding'
import { AvalonBackdrop } from '../../src/renderer/themes/avalon-backdrop'
import { AvalonCover, avalon } from '../../src/renderer/themes/avalon'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeContext } from '../../src/shared/theme'
import type { ApiRequest, WinnowBridge } from '../../src/shared/bridge'

function picture(width: number, height: number, pixels?: number[]) {
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const context = canvas.getContext('2d')!
  if (pixels) context.putImageData(new ImageData(new Uint8ClampedArray(pixels), width, height), 0, 0)
  else {
    context.fillStyle = 'white'
    context.fillRect(0, 0, width, height)
  }
  return canvas.toDataURL('image/png')
}
const sources = {
  vertical: picture(2, 2, [240, 0, 0, 255, 240, 0, 0, 255, 0, 0, 220, 255, 0, 0, 220, 255]),
  horizontal: picture(2, 2, [0, 180, 0, 255, 240, 0, 0, 255, 0, 180, 0, 255, 240, 0, 0, 255]),
  hero: picture(384, 124),
  landscape: picture(160, 90),
}
const images = Object.fromEntries(
  await Promise.all(
    Object.entries(sources).map(async ([key, source]) => {
      const image = new Image()
      image.src = source
      image.style.objectFit = 'contain'
      image.hidden = true
      document.body.append(image)
      await image.decode()
      return [key, image] as const
    }),
  ),
)
const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
type Options = {
  view: 'padding' | 'cover' | 'backdrop' | 'empty'
  width: number
  height: number
  source: 'vertical' | 'horizontal'
  clear: boolean
  mode: 'desktop' | 'fullscreen'
  cinematic: boolean
  workId: number
  dimmed: boolean
}
const frames = new Map<number, FrameRequestCallback>(),
  requestFrame = requestAnimationFrame.bind(window),
  cancelFrame = cancelAnimationFrame.bind(window)
let frameId = 100000
const probe = {
  configure: (_value: Partial<Options>) => {},
  freeze: false,
  requests: [] as { id: string; width: number }[],
  step(time: number) {
    const callbacks = [...frames.values()]
    frames.clear()
    callbacks.forEach((callback) => callback(time))
  },
  get pendingFrames() {
    return frames.size
  },
  fit(value: boolean) {
    document.documentElement.style.setProperty('--cover-art-fit', value ? 'contain' : 'cover')
    for (const image of Object.values(images)) image.style.objectFit = value ? 'contain' : 'cover'
  },
}
window.requestAnimationFrame = (callback) => {
  if (probe.freeze) {
    const id = ++frameId
    frames.set(id, callback)
    return id
  }
  return requestFrame(callback)
}
window.cancelAnimationFrame = (id) => {
  if (!frames.delete(id)) cancelFrame(id)
}
Object.assign(window, { fullscreenArtwork: probe })
window.winnow = {
  request: async (request: ApiRequest) => ({
    ok: true,
    status: 200,
    data:
      request.route === 'artworkState'
        ? {
            revision: 'fixture',
            current: {
              previewKey: {
                provider: 'fixture',
                id: request.params?.workId === 2 ? 'horizontal' : 'vertical',
              },
            },
          }
        : request.route === 'preferences.presentation.get'
          ? []
          : {
              candidates: [
                {
                  key: {
                    provider: Number(request.params?.workId) % 2 ? 'steam-hero' : 'igdb-backdrop',
                    id: Number(request.params?.workId) % 2 ? 'hero' : 'landscape',
                  },
                  aspectRatio: Number(request.params?.workId) % 2 ? 384 / 124 : 16 / 9,
                  fitWholeHero: Boolean(Number(request.params?.workId) % 2),
                },
              ],
              coverKey: null,
            },
  }),
  artwork: async (_provider: string, id: string, width = 0) => {
    probe.requests.push({ id, width })
    return sources[id as keyof typeof sources]
  },
  cancelRequest: async () => true,
  onEvent: () => () => {},
} as unknown as WinnowBridge

function Fixture() {
  const [options, setOptions] = useState<Options>({
    view: 'padding',
    width: 20,
    height: 60,
    source: 'vertical',
    clear: false,
    mode: 'fullscreen',
    cinematic: false,
    workId: 1,
    dimmed: false,
  })
  probe.configure = (value) => setOptions((previous) => ({ ...previous, ...value }))
  const game = {
    workId: options.source === 'vertical' ? 1 : 2,
    title: 'Cover edge fixture',
    bucket: 'stale',
    playtimeMinutes: 200,
    lastPlayedAt: '2020-01-01T00:00:00Z',
    entries: [
      {
        workId: 1,
        releaseId: 1,
        ownershipId: 1,
        title: 'Cover edge fixture',
        store: 'steam',
        installed: true,
        playtimeMinutes: 200,
      },
    ],
  }
  const profile = selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon)
  profile.settings.avalon = { ...profile.settings.avalon, dimCovers: options.dimmed }
  const context = {
    mode: options.mode,
    page: 'library',
    games: [game],
    loading: false,
    profile,
    selectedWorkId: null,
    setPage() {},
    toggleFullscreen() {},
    openGame() {},
    feed: undefined,
    children: null,
    renderScreen: () => null,
    actions: { async launch() {} },
    components: {
      Artwork,
      GameCard: () => null,
      GamePreview: () => null,
      ArtworkEffects: () => null,
      Impression: ({ children }) => children,
    },
  } as ThemeContext
  return (
    <>
      {options.view === 'padding' && (
        <div
          data-padding-fixture
          style={{ position: 'relative', width: options.width, height: options.height }}
        >
          <CoverPadding image={options.clear ? null : images[options.source]} />
        </div>
      )}
      {options.view === 'cover' && (
        <div className={`avalon-shell ${options.mode}`} style={{ display: 'block', padding: 20 }}>
          <div data-cover-fixture style={{ width: options.width, height: options.height }}>
            <AvalonCover context={context} game={game} />
          </div>
        </div>
      )}
      {options.view === 'backdrop' && (
        <div data-backdrop-fixture style={{ position: 'absolute', inset: 0 }}>
          <AvalonBackdrop workId={options.workId} cinematic={options.cinematic} />
        </div>
      )}
    </>
  )
}
document.documentElement.style.setProperty('--cover-art-fit', 'contain')
document.documentElement.style.setProperty('--bg', '#071b1a')
createRoot(document.getElementById('root')!).render(
  <QueryClientProvider client={client}>
    <Fixture />
  </QueryClientProvider>,
)
