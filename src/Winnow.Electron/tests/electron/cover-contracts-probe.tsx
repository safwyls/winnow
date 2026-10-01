import '../../src/renderer/styles.css'
import { useLayoutEffect, useState, type CSSProperties } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AvalonCover } from '../../src/renderer/themes/avalon'
import { AvalonFeedCard } from '../../src/renderer/themes/avalon-feed-card'
import { AvalonCoverWorkspace } from '../../src/renderer/themes/avalon-desktop-cover'
import { FeedDeck } from '../../src/renderer/themes/avalon-feed-model'
import { AVALON_PALETTES, avalonPaletteStyle } from '../../src/renderer/themes/avalon-palettes'
import { useController } from '../../src/renderer/controller'
import type { ThemeContext } from '../../src/shared/theme'
import { coverGame, coverProfile, coverWorkspace } from '../cover-fixtures'
import '../../src/renderer/themes/avalon.css'

const parameters = new URLSearchParams(location.search)
const feed = parameters.get('kind') === 'feed'
const fullscreen = parameters.get('mode') === 'fullscreen'
const source = document.createElement('canvas')
source.width = 160
source.height = 240
source.getContext('2d')!.fillStyle = 'SlateBlue'
source.getContext('2d')!.fillRect(0, 0, 160, 240)
const pixels = source.toDataURL('image/png')
const game = {
  ...coverGame(1, false, false, true),
  title: 'Fixture',
  bucket: 'untouched',
  entries: [
    {
      ownershipId: 1,
      releaseId: 1,
      workId: 1,
      title: 'Fixture',
      store: 'steam',
      installed: false,
      playtimeMinutes: 0,
    },
  ],
}
const profile = coverProfile()
profile.appearance.reducedMotion = true
profile.appearance.scale = 100
const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
Object.assign(window, {
  winnow: {
    request: async () => ({ ok: true, status: 200, data: { ratings: [] } }),
    cancelRequest: async () => true,
  },
})
function Art() {
  return (
    <div className="artwork" data-state="ready">
      <img className="art-ready" src={pixels} alt="" />
    </div>
  )
}
const context = {
  mode: fullscreen ? 'fullscreen' : 'desktop',
  profile,
  components: { Artwork: Art },
  actions: { launch: async () => {} },
  openGame: () => {},
} as unknown as ThemeContext
const deck = new FeedDeck(
  async () => ({ saved: true, expiresAt: null }),
  async () => [],
)
deck.receive(
  [
    {
      id: 'source',
      title: 'Source',
      blurb: '',
      feedback: false,
      rows: [{ game, releaseId: 1, reason: 'A forgotten game' }],
      reserve: [],
    },
  ],
  'source',
  true,
)
function Probe() {
  const [selected, select] = useState(false)
  Object.assign(window, { coverContractProbe: { select } })
  useController({
    enabled: true,
    surface: fullscreen ? 'fullscreen' : 'desktop',
    menu() {},
    search() {},
    switchPage() {},
    keyboard() {},
    play() {},
  })
  const style = {
    ...avalonPaletteStyle(AVALON_PALETTES[0]!),
    '--font-body': '"Avalon Body"',
    '--font-display': '"Avalon Display"',
    '--font-mono': '"Avalon Data"',
    display: 'block',
    padding: 0,
    width: 800,
    height: 650,
  } as CSSProperties
  useLayoutEffect(() => {
    for (const [key, value] of Object.entries(style))
      if (key.startsWith('--')) document.body.style.setProperty(key, String(value))
  }, [])
  return (
    <QueryClientProvider client={client}>
      <div
        className={`avalon-shell ${fullscreen ? 'fullscreen' : 'desktop'}`}
        data-reduced-motion="true"
        style={style}
      >
        <div style={{ position: 'absolute', left: 0, top: 0, width: 220, height: feed ? undefined : 330 }}>
          <AvalonCoverWorkspace.Provider value={{ ...coverWorkspace(game), externalIds: [] }}>
            {feed ? (
              <AvalonFeedCard
                context={context}
                deck={deck}
                shelf={deck.shelves[0]}
                row={deck.shelves[0].rows[0]}
              />
            ) : (
              <AvalonCover context={context} game={game} selected={selected} />
            )}
          </AvalonCoverWorkspace.Provider>
        </div>
        <button id="elsewhere" style={{ position: 'absolute', left: 240, top: 0 }}>
          Elsewhere
        </button>
      </div>
    </QueryClientProvider>
  )
}
Object.assign(document.body.style, { margin: '0', zoom: '1' })
createRoot(document.getElementById('root')!).render(<Probe />)
