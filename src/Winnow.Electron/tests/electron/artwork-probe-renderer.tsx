import '../../src/renderer/styles.css'
import '../../src/renderer/themes/avalon.css'
import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Artwork } from '../../src/renderer/components/Artwork'
import {
  artworkImages,
  loadArtworkImage,
  closeArtworkImages,
  type ArtworkKey,
} from '../../src/renderer/components/artwork-images'
import { AvalonCover, avalon } from '../../src/renderer/themes/avalon'
import { DEFAULT_PROFILE, selectThemeProfile, type ThemeContext } from '../../src/shared/theme'
import type { WinnowBridge } from '../../src/shared/bridge'

const canvas = document.createElement('canvas')
canvas.width = 600
canvas.height = 900
const paint = canvas.getContext('2d')!
paint.fillStyle = '#48988d'
paint.fillRect(0, 0, 600, 900)
paint.fillStyle = '#e9c989'
paint.fillRect(20, 20, 560, 860)
paint.fillStyle = '#13292c'
paint.fillRect(100, 100, 400, 700)
const encoded = canvas.toDataURL('image/png')
const client = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } })
const cache = artworkImages(client)
const key: ArtworkKey = ['artwork-image', 'fixture', 'portrait', 480, 'fixture']
const probe = {
  requests: [] as { width: number; requestId?: string }[],
  cancels: [] as string[],
  failNext: false,
  hold: false,
  release: () => {},
  warmSource: '',
  state: () => ({ live: cache.liveSlots, pending: cache.pendingCount, decoded: cache.decodedCount }),
}
Object.assign(window, { artworkProbe: probe })
window.winnow = {
  request: async (input) => ({
    ok: true,
    status: 200,
    data:
      input.route === 'artworkState'
        ? { revision: 'fixture', current: { previewKey: { provider: 'fixture', id: 'portrait' } } }
        : [],
  }),
  artwork: async (_provider, _id, width = 600, requestId) => {
    probe.requests.push({ width, requestId })
    if (probe.hold)
      await new Promise<void>((done) => {
        probe.release = done
      })
    if (probe.failNext) {
      probe.failNext = false
      return null
    }
    return encoded
  },
  cancelRequest: async (requestId) => {
    probe.cancels.push(requestId)
    return true
  },
} as WinnowBridge
window.addEventListener('pagehide', () => {
  void closeArtworkImages(client)
})

function Fixture() {
  const [mode, setMode] = useState<'desktop' | 'fullscreen'>('desktop')
  const [visible, setVisible] = useState(false),
    [warm, setWarm] = useState(false)
  const [recent, setRecent] = useState(false),
    [selected, setSelected] = useState(false)
  const game = {
    workId: 1,
    title: 'Hollow Knight',
    bucket: 'stale',
    playtimeMinutes: 200,
    lastPlayedAt: recent ? new Date().toISOString() : '2020-01-01T00:00:00Z',
    entries: [
      {
        workId: 1,
        releaseId: 1,
        ownershipId: 1,
        title: 'Hollow Knight',
        store: 'steam',
        installed: true,
        playtimeMinutes: 200,
      },
    ],
  }
  const context: ThemeContext = {
    mode,
    page: 'library',
    games: [game],
    loading: false,
    profile: selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon),
    selectedWorkId: null,
    setPage() {},
    toggleFullscreen() {},
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
    openGame() {},
  }
  return (
    <div className={`avalon-shell ${mode}`} style={{ display: 'block', padding: 40 }}>
      <div style={{ display: 'flex', gap: 12, marginBottom: 20 }}>
        <button onClick={() => setMode(mode === 'desktop' ? 'fullscreen' : 'desktop')}>Change surface</button>
        <button
          onClick={async () => {
            probe.warmSource = (await cache.get(JSON.stringify(key), (signal) =>
              loadArtworkImage(client, key, signal),
            ))!.source
            setWarm(true)
          }}
        >
          {warm ? 'Pixels warm' : 'Warm pixels'}
        </button>
        <button onClick={() => setVisible(!visible)}>{visible ? 'Detach cover' : 'Attach cover'}</button>
        <button onClick={() => document.documentElement.style.setProperty('--cover-art-fit', 'cover')}>
          Fill
        </button>
        <button onClick={() => document.documentElement.style.setProperty('--cover-art-fit', 'contain')}>
          Fit
        </button>
        <button
          onClick={() => {
            document.documentElement.dataset.dimDormant = String(recent)
            setRecent(!recent)
          }}
        >
          Change dormancy
        </button>
        <button onClick={() => setSelected(!selected)}>Change selection</button>
      </div>
      <div style={{ width: 400, height: 600 }}>
        {visible && <AvalonCover context={context} game={game} selected={selected} />}
      </div>
    </div>
  )
}
createRoot(document.getElementById('root')!).render(
  <QueryClientProvider client={client}>
    <Fixture />
  </QueryClientProvider>,
)
