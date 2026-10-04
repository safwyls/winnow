import '../../src/renderer/styles.css'
import '../../src/renderer/themes/avalon.css'
import '../../src/renderer/themes/rift/rift.css'
import '../../src/renderer/features/details-layout.css'
import { useMemo, useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Artwork } from '../../src/renderer/components/Artwork'
import { ArtworkEffects } from '../../src/renderer/components/artwork-effects'
import { GamePreview } from '../../src/renderer/components/GamePreview'
import { GameCard, Impression } from '../../src/renderer/components/primitives'
import { artworkImages, closeArtworkImages } from '../../src/renderer/components/artwork-images'
import { AvalonLibrary, AvalonShell, avalon } from '../../src/renderer/themes/avalon'
import { AfterglowLibrary, afterglow } from '../../src/renderer/themes/afterglow'
import { catalogue } from '../../src/renderer/themes/catalogue'
import { rift } from '../../src/renderer/themes/rift'
import { RiftLibrary } from '../../src/renderer/themes/rift/Library'
import { JourneyContext, type JourneyOrigin } from '../../src/renderer/themes/rift/journey'
import { AvalonBackdrop } from '../../src/renderer/themes/avalon-backdrop'
import { applyThemeProfile } from '../../src/renderer/theming/runtime'
import {
  DEFAULT_PROFILE,
  selectThemeProfile,
  type ThemeContext,
  type ThemeMode,
} from '../../src/shared/theme'
import type { ApiRequest, WinnowBridge } from '../../src/shared/bridge'
import type { LibraryGame } from '../../src/renderer/api/types'

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

function png(width: number, height: number) {
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const drawing = canvas.getContext('2d')!
  const gradient = drawing.createLinearGradient(0, 0, width, height)
  gradient.addColorStop(0, '#264c57')
  gradient.addColorStop(1, '#be8757')
  drawing.fillStyle = gradient
  drawing.fillRect(0, 0, width, height)
  const binary = atob(canvas.toDataURL('image/png').split(',')[1])
  return Uint8Array.from(binary, (byte) => byte.charCodeAt(0))
}
const coverBytes = png(96, 144),
  heroBytes = png(640, 360)
const viewportSelector = '.avalon-library-scroll, .library-scroll, .rift-gallery'
const cardSelector = '[data-work-id], [data-rift-game]'
function cards() {
  const viewport = document.querySelector<HTMLElement>(viewportSelector)
  if (!viewport) return { visible: [] as number[], mounted: [] as number[], readyVisible: [] as number[] }
  const clip = viewport.getBoundingClientRect()
  const mounted = new Set<number>(),
    visible = new Set<number>(),
    readyVisible = new Set<number>()
  for (const card of viewport.querySelectorAll<HTMLElement>(cardSelector)) {
    const id = Number(card.dataset.workId ?? card.dataset.riftGame)
    mounted.add(id)
    const box = card.getBoundingClientRect()
    if (
      card.closest('[inert], [hidden], [aria-hidden="true"]') ||
      !card.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true })
    )
      continue
    if (
      box.bottom > Math.max(0, clip.top) &&
      box.top < Math.min(innerHeight, clip.bottom) &&
      box.right > Math.max(0, clip.left) &&
      box.left < Math.min(innerWidth, clip.right)
    ) {
      visible.add(id)
      const image = card.querySelector('img')
      if (image?.complete && image.naturalWidth > 0) readyVisible.add(id)
    }
  }
  return { visible: [...visible], mounted: [...mounted], readyVisible: [...readyVisible] }
}
type Read = {
  id: number
  kind: 'selection' | 'bytes'
  width?: number
  phase: 'library' | 'details'
  visible: number[]
  started: number
  finished: boolean
  canceled: boolean
}
const background: Read[] = []
const pending = new Map<string, { background: boolean; finish(canceled?: boolean): void }>()
let phase: Read['phase'] = 'library',
  hold = false,
  maxBackgroundPending = 0
let holdNextOverscan = false
let heldCover: { id: number; width: number; requestId: string; finished: boolean; canceled: boolean } | null =
  null
function log(id: number, kind: Read['kind'], width?: number) {
  const entry: Read = {
    id,
    kind,
    width,
    phase,
    visible: cards().visible,
    started: Date.now(),
    finished: false,
    canceled: false,
  }
  background.push(entry)
  return entry
}
function later<T>(
  requestId: string | undefined,
  data: T,
  milliseconds: number,
  record?: Read,
  held = false,
): Promise<T | null> {
  return new Promise((resolve) => {
    let timer: ReturnType<typeof setTimeout> | undefined
    const finish = (canceled = false) => {
      clearTimeout(timer)
      if (requestId) pending.delete(requestId)
      if (record) {
        record.finished = true
        record.canceled = canceled
      }
      resolve(canceled ? null : data)
    }
    if (requestId) pending.set(requestId, { background: !!record, finish })
    if (!held) timer = setTimeout(finish, milliseconds)
    maxBackgroundPending = Math.max(
      maxBackgroundPending,
      [...pending.values()].filter((entry) => entry.background).length,
    )
  })
}
window.winnow = {
  request: async (input: ApiRequest) => {
    let data: unknown = []
    const id = Number(input.params?.workId)
    if (input.route === 'artworkState' || input.route === 'artwork.get') {
      const hero = input.params?.slot === 'Hero'
      const value = {
        revision: 'preload-fixture',
        current: { previewKey: { provider: hero ? 'fixture-background' : 'fixture-cover', id: String(id) } },
      }
      data = hero ? await later(input.requestId, value, 30, log(id, 'selection')) : value
    } else if (input.route === 'artwork.backdrop') {
      data = await later(
        input.requestId,
        {
          candidates: [
            {
              key: { provider: 'fixture-background', id: String(id) },
              aspectRatio: 16 / 9,
              fitWholeHero: false,
            },
          ],
          coverKey: { provider: 'fixture-cover', id: String(id) },
        },
        30,
        log(id, 'selection'),
      )
    } else if (input.route === 'library.get') data = { games, lists: [] }
    else if (input.route === 'library.workspace')
      data = {
        works: games.map((game) => ({ id: game.workId, name: game.title })),
        externalIds: [],
        ownerships: [],
        releases: [],
        identityLinks: [],
        buckets: [],
        epicLaunchKeys: {},
        pluginActions: {},
      }
    else if (input.route === 'preferences.presentation.get')
      data = [{ preference: 'DefaultSort', value: 'name' }]
    else if (input.route === 'game.details') data = { ratings: [], sessions: {}, screenshots: [] }
    return { ok: true, status: 200, data }
  },
  artworkBytes: (provider: string, id: string, width: number, requestId: string) => {
    const isBackground = provider === 'fixture-background'
    if (!isBackground && holdNextOverscan && !cards().visible.includes(Number(id))) {
      holdNextOverscan = false
      const record = { id: Number(id), width, requestId, finished: false, canceled: false }
      heldCover = record
      return later(requestId, coverBytes.slice(), 0, undefined, true).then((bytes) => {
        record.finished = true
        record.canceled = bytes === null
        return bytes
      })
    }
    return later(
      requestId,
      (isBackground ? heroBytes : coverBytes).slice(),
      isBackground ? 120 : 8,
      isBackground ? log(Number(id), 'bytes', width) : undefined,
      isBackground && hold,
    )
  },
  cancelRequest: async (requestId: string) => {
    const request = pending.get(requestId)
    if (!request) return false
    request.finish(true)
    return true
  },
  onEvent: () => () => {},
} as unknown as WinnowBridge
window.addEventListener('pagehide', () => void closeArtworkImages(client))

let changeFilter: (query: string) => void = () => {}
let changeHidden: (hidden: boolean) => void = () => {}
function activity() {
  const viewport = document.querySelector<HTMLElement>(viewportSelector)
  if (!viewport) throw Error('No library viewport')
  const target =
    viewport.querySelector<HTMLElement>('[data-row-active="true"] [data-avalon-game]') ?? viewport
  target.dispatchEvent(new WheelEvent('wheel', { deltaY: 800, bubbles: true, cancelable: true }))
  if (target === viewport) viewport.scrollTop += 800
}
type FixtureOptions = { normalMotion?: boolean; fullShell?: boolean }
const probe = {
  start: (_theme: string, _mode: ThemeMode, _options: FixtureOptions = {}) => {},
  holdOneOverscanCover() {
    holdNextOverscan = true
  },
  scrollOnce: activity,
  hold(value: boolean) {
    hold = value
  },
  filter(query: string) {
    // External library filtering changes the same context collection in every composition,
    // including fullscreen Avalon where the search field is in a separate actions pane.
    document.dispatchEvent(new Event('input', { bubbles: true }))
    changeFilter(query)
  },
  hidden(value: boolean) {
    changeHidden(value)
  },
  async churn(kind: 'scroll' | 'filter') {
    const first = background.length
    for (let index = 0; index < 12; index++) {
      if (kind === 'scroll') activity()
      else probe.filter(index % 2 ? 'Game 08' : 'Game 07')
      await new Promise((resolve) => setTimeout(resolve, 90))
    }
    if (kind === 'filter') probe.filter('Game 09')
    return { first, last: background.length, ...cards() }
  },
  state() {
    return {
      ...cards(),
      background,
      phase,
      hold,
      heldCover,
      maxBackgroundPending,
      retainedWorkIds: [
        ...new Set(
          client
            .getQueryCache()
            .findAll({ queryKey: ['artwork-image', 'fixture-background'] })
            .filter(
              (query) =>
                !query.state.isInvalidated &&
                !!(query.state.data as { source?: unknown } | undefined)?.source,
            )
            .map((query) => Number(query.queryKey[2])),
        ),
      ],
      pending: [...pending.values()].filter((entry) => entry.background).length,
      artworkPending: cache.pendingCount,
      coverPixels: cache.decodedBytes,
    }
  },
}
Object.assign(window, { backgroundPreloadProbe: probe })

function Background({ workId, theme, mode }: { workId: number; theme: string; mode: ThemeMode }) {
  if (theme === 'avalon')
    return (
      <section className={`avalon-details ${mode}`} data-fixture-details={workId}>
        <AvalonBackdrop
          workId={workId}
          fullscreen={mode === 'fullscreen'}
          reducedMotion
          className="avalon-detail-backdrop"
        />
        <header className="avalon-details-header">
          <h1>{games[workId - 1].title}</h1>
        </header>
      </section>
    )
  const details = (
    <section className={`feature-page details-page mode-${mode}`} data-fixture-details={workId}>
      <Artwork workId={workId} hero eager className="detail-hero" />
      <header className="feature-heading">
        <h1>{games[workId - 1].title}</h1>
      </header>
    </section>
  )
  return theme === 'rift' ? (
    <div className="rift-details" data-expanded>
      <div className="rift-details-scroll">{details}</div>
    </div>
  ) : (
    details
  )
}
function LibraryFixture({
  theme: themeId,
  mode,
  normalMotion = false,
  fullShell = false,
}: { theme: string; mode: ThemeMode } & FixtureOptions) {
  const [selected, setSelected] = useState<number | null>(null)
  const [filter, setFilter] = useState('')
  const [hidden, setHidden] = useState(false)
  changeFilter = setFilter
  changeHidden = setHidden
  const filtered = useMemo(() => games.filter((game) => game.title.includes(filter)), [filter])
  const definition = [avalon, afterglow, catalogue, rift].find((theme) => theme.id === themeId)!
  const profile = useMemo(() => {
    const profile = selectThemeProfile(structuredClone(DEFAULT_PROFILE), themeId, definition)
    profile.appearance.reducedMotion = !normalMotion
    profile.layout.cardStyle = 'poster'
    applyThemeProfile(profile)
    document.documentElement.dataset.mode = mode
    document.documentElement.style.setProperty('--effective-interface-scale', '1')
    return profile
  }, [themeId, mode, definition, normalMotion])
  const openGame = (id: number) => {
    phase = 'details'
    setSelected(id)
  }
  const origin = useRef<JourneyOrigin | null>(null)
  const context: ThemeContext = {
    mode,
    page: selected === null ? 'library' : 'details',
    games: filtered,
    loading: false,
    profile,
    selectedWorkId: selected,
    openGame,
    setPage() {},
    toggleFullscreen() {},
    feed: undefined,
    children: null,
    renderScreen: () => null,
    actions: { async launch() {} },
    components: { Artwork, GameCard, GamePreview, ArtworkEffects, Impression },
  }
  const library =
    themeId === 'avalon' ? (
      <AvalonLibrary {...context} />
    ) : themeId === 'rift' ? (
      <RiftLibrary {...context} />
    ) : (
      <AfterglowLibrary {...context} quietCards={themeId === 'afterglow'} />
    )
  if (fullShell && themeId === 'avalon')
    return (
      <AvalonShell {...context}>
        <div className={`screen-view screen-${context.page}`}>
          {selected !== null ? (
            <Background workId={selected} theme={themeId} mode={mode} />
          ) : (
            <div
              className="background-probe-library"
              style={{ height: '100%', visibility: hidden ? 'hidden' : 'visible' }}
            >
              {library}
            </div>
          )}
        </div>
      </AvalonShell>
    )
  return (
    <JourneyContext.Provider
      value={{
        origin,
        open: openGame,
        finish() {},
        options: { roundness: 0, waviness: 0, activity: 0 },
        reducedMotion: true,
        coverSize: 'balanced',
      }}
    >
      <div
        className={`${themeId === 'avalon' ? 'avalon-shell' : themeId === 'rift' ? 'rift-shell' : `app-shell ${themeId}-shell`} ${mode} background-probe-shell`}
      >
        <main
          id="main-content"
          className={
            themeId === 'avalon' ? 'avalon-content' : themeId === 'rift' ? 'rift-content' : 'page-content'
          }
        >
          {selected !== null ? (
            <Background workId={selected} theme={themeId} mode={mode} />
          ) : (
            <div className="background-probe-library" style={{ visibility: hidden ? 'hidden' : 'visible' }}>
              {library}
            </div>
          )}
        </main>
      </div>
      <style>{`.background-probe-shell { display:block; height:100%; width:100%; } .background-probe-shell > main, .background-probe-library { height:100%; min-height:0; }`}</style>
    </JourneyContext.Provider>
  )
}
function Fixture() {
  const [configuration, setConfiguration] = useState<
    ({ theme: string; mode: ThemeMode } & FixtureOptions) | null
  >(null)
  probe.start = (theme, mode, options) => setConfiguration({ theme, mode, ...options })
  return configuration ? <LibraryFixture {...configuration} /> : <p>Background preload fixture ready</p>
}
createRoot(document.getElementById('root')!).render(
  <QueryClientProvider client={client}>
    <Fixture />
  </QueryClientProvider>,
)
