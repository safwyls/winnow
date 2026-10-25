import '../../src/renderer/styles.css'
import {
  useEffect,
  useLayoutEffect,
  useMemo,
  useState,
  useSyncExternalStore,
  type CSSProperties,
} from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AvalonFeedCard } from '../../src/renderer/themes/avalon-feed-card'
import { AvalonCoverWorkspace } from '../../src/renderer/themes/avalon-desktop-cover'
import { FeedDeck } from '../../src/renderer/themes/avalon-feed-model'
import { AVALON_PALETTES, avalonPaletteStyle } from '../../src/renderer/themes/avalon-palettes'
import type { ThemeContext } from '../../src/shared/theme'
import { coverGame, coverProfile, coverWorkspace, FixtureCoverArt } from '../cover-fixtures'

interface Options {
  width: number
  installed: boolean
  attached: boolean
  workId: number
  version: number
  x: number
  y: number
  second: boolean
  feedback: boolean
  deferDetails: boolean
}
const defaults: Options = {
  width: 220,
  installed: true,
  attached: true,
  workId: 1,
  version: 0,
  x: 40,
  y: 40,
  second: false,
  feedback: true,
  deferDetails: false,
}
let optionsNow = defaults
const requests = new Map<string, (value: unknown) => void>()
const probe = {
  opened: [] as number[],
  launches: [] as number[],
  feedback: [] as unknown[],
  requests: [] as string[],
  cancelled: [] as string[],
  heroes: 0,
  configure: (_value: Partial<Options>) => {},
  resolveDetails: () => {
    for (const resolve of requests.values()) resolve({ ok: true, status: 200, data: { ratings: [] } })
    requests.clear()
  },
}
Object.assign(window, {
  feedCardProbe: probe,
  winnow: {
    request: async ({ route, requestId }: { route: string; requestId?: string }) => {
      if (route === 'game.details') {
        probe.requests.push(requestId!)
        if (optionsNow.deferDetails) return new Promise((resolve) => requests.set(requestId!, resolve))
        return { ok: true, status: 200, data: { ratings: [] } }
      }
      if (route === 'library.get') return { ok: true, status: 200, data: { games: [], lists: [] } }
      return { ok: true, status: 200, data: {} }
    },
    cancelRequest: async (id: string) => {
      probe.cancelled.push(id)
    },
  },
})
function Art({ workId, hero }: { workId: number; hero?: boolean }) {
  useEffect(() => {
    if (hero) probe.heroes++
    return () => {
      if (hero) probe.heroes--
    }
  }, [workId, hero])
  return <FixtureCoverArt workId={workId} />
}
const profile = coverProfile()
profile.appearance.reducedMotion = true
const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
function Probe() {
  const [options, setOptions] = useState(defaults)
  optionsNow = options
  probe.configure = (patch) => setOptions((state) => ({ ...state, ...patch }))
  const games = useMemo(
    () =>
      [options.workId, options.workId + 1].map((id) => ({
        ...coverGame(id, options.installed, true, true),
        title: id === 1 ? 'Aloft with a deliberately long game title' : `Second game ${id}`,
        summary:
          'A recorded summary that stays below the ratings and needs several lines when the preview is narrow. '.repeat(
            8,
          ),
      })),
    [options.workId, options.installed, options.version],
  )
  const [deck] = useState(
    () =>
      new FeedDeck(
        async (releaseId, kind, undo) => {
          probe.feedback.push({ releaseId, kind, undo })
          return { saved: true, expiresAt: kind === 1 ? '2026-10-29T12:00:00Z' : null }
        },
        async () => [],
      ),
  )
  useSyncExternalStore(deck.subscribe, deck.snapshot)
  useLayoutEffect(() => {
    deck.receive(
      [
        {
          id: 'for-you',
          title: 'For you',
          blurb: 'Waiting for you',
          feedback: options.feedback,
          rows: games.map((game) => ({
            game,
            releaseId: game.entries[0].releaseId,
            reason: 'Last played on 7 Sep 2026. A reason kept beneath the cover.',
          })),
          reserve: [{ game: coverGame(99), releaseId: 990, reason: 'Reserve' }],
        },
      ],
      String(options.version),
      true,
    )
  }, [deck, games, options.feedback, options.version])
  const style = {
    ...avalonPaletteStyle(AVALON_PALETTES[0]!),
    '--font-body': '"Avalon Body"',
    '--font-display': '"Avalon Display"',
    '--font-mono': '"Avalon Data"',
  } as CSSProperties
  useLayoutEffect(() => {
    for (const [key, value] of Object.entries(style))
      if (key.startsWith('--')) document.body.style.setProperty(key, String(value))
  }, [])
  const context = {
    mode: 'desktop',
    profile,
    components: { Artwork: Art },
    actions: {
      launch: async (id: number) => {
        probe.launches.push(id)
      },
    },
    openGame: (id: number) => {
      probe.opened.push(id)
    },
  } as unknown as ThemeContext
  const shelf = deck.shelves[0]
  return (
    <QueryClientProvider client={client}>
      <div className="avalon-shell desktop" data-reduced-motion="true" style={{ ...style, display: 'block' }}>
        <button id="outside" style={{ position: 'absolute', right: 20, top: 20 }}>
          Outside
        </button>
        {options.attached &&
          shelf &&
          shelf.rows.slice(0, options.second ? 2 : 1).map((row, index) => (
            <div
              className="probe-slot"
              key={row.game.workId}
              style={{
                position: 'absolute',
                left: options.x + index * 660,
                top: options.y,
                width: options.width,
              }}
            >
              <AvalonCoverWorkspace.Provider value={coverWorkspace(row.game)}>
                <AvalonFeedCard context={context} deck={deck} shelf={shelf} row={row} />
              </AvalonCoverWorkspace.Provider>
            </div>
          ))}
      </div>
    </QueryClientProvider>
  )
}
createRoot(document.getElementById('root')!).render(<Probe />)
