// Establish the same CSS layer order as the production entry before importing a theme.
import '../../src/renderer/styles.css'
import type { WinnowBridge } from '../../src/shared/bridge'
import { DEFAULT_PROFILE, parseThemeProfile, selectThemeProfile } from '../../src/shared/theme'
import { rift } from '../../src/renderer/themes/rift'
import samples from './games.json'

// Explicit visual-test bridge. No database, credentials, filesystem writes or launch actions.
// This entry is separate from the shipped renderer and only serves local review fixtures.
const parameters = new URLSearchParams(location.search)
const count = Math.max(0, Math.min(2000, Number(parameters.get('count') ?? samples.length)))
const games = Array.from({ length: Number.isFinite(count) ? count : samples.length }, (_, index) => {
  const sample = samples[index % samples.length]
  return {
    workId: index + 1,
    title: sample.title,
    summary: sample.description,
    firstReleaseYear: 2020,
    publisher: 'Fixture publisher',
    bucket: sample.time === 'Unplayed' ? 'never_played' : 'dormant',
    playtimeMinutes: sample.time === 'Unplayed' ? 0 : Math.round(Number.parseFloat(sample.time) * 60),
    lastPlayedAt: sample.time === 'Unplayed' ? null : '2026-08-01T12:00:00Z',
    entries: [
      {
        ownershipId: index + 1,
        releaseId: index + 1,
        workId: index + 1,
        title: sample.title,
        store: sample.store.toLowerCase(),
        installed: Boolean(sample.installed),
        playtimeMinutes: 0,
      },
    ],
  }
})
let fullscreen = new URLSearchParams(location.search).get('mode') === 'fullscreen'
let onFullscreen = (_value: boolean) => {}
const images = import.meta.glob('../../../../docs/spikes/2026-09-27-afterglow-artwork-mock/assets/*.jpg', {
  eager: true,
  query: '?url',
  import: 'default',
})
let profile =
  parameters.get('theme') === 'rift'
    ? selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'rift', rift)
    : structuredClone(DEFAULT_PROFILE)
profile.appearance.artwork!.finish = 'foil'
const preferenceKey = `winnow-visual-fixture:${parameters.get('theme') ?? 'afterglow'}`
try {
  const saved = sessionStorage.getItem(preferenceKey)
  if (saved) profile = parseThemeProfile(JSON.parse(saved))
} catch {
  /* Invalid fixture preferences start with the chosen design. */
}
const bridge = {
  request: async ({ route, params }: { route: string; params?: Record<string, string | number> }) => {
    let data: unknown
    if (route === 'library.get')
      data = {
        games,
        lists: [
          {
            id: 1,
            name: 'Weekend worlds',
            releaseIds: games.filter((_, i) => i % 2 === 0).map((g) => g.workId),
            isLive: false,
            revision: 'fixture-1',
          },
        ],
      }
    else if (route === 'feed.get')
      data = {
        shelves: [
          {
            id: 'forgotten',
            title: 'Worth another look.',
            blurb: 'There is more to these worlds.',
            supportsFeedback: false,
            items: games
              .slice()
              .reverse()
              .map((game) => ({
                ownershipId: game.workId,
                releaseId: game.workId,
                title: game.title,
                reason: samples[(game.workId - 1) % samples.length].reason,
              })),
          },
        ],
        candidateCount: games.length,
        confidence: 1,
        failed: false,
      }
    else if (route === 'library.workspace')
      data = {
        works: games.map((g) => ({ id: g.workId, name: g.title })),
        externalIds: [],
        pluginActions: {},
        epicLaunchKeys: {},
      }
    else if (route === 'game.details')
      data = {
        workId: params?.workId,
        events: [],
        sessions: {},
        journalEntries: [],
        ratings: [],
        achievements: [],
      }
    else if (route === 'activity.query') data = { rows: [], next: null }
    else if (route === 'statistics.gameplay')
      data = {
        recordedSeconds: 0,
        gamesPlayedCount: 0,
        startedSessionCount: 0,
        overlappingSessionCount: 0,
        excludedSessionCount: 0,
        periods: [],
        topGames: [],
      }
    else if (route === 'preferences.library.get')
      data = { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' }
    else if (route === 'connections.get')
      data = { steam: { hasUsableCredential: false, hasSession: false, hasApiKey: false } }
    else if (route === 'connections.igdb.get') data = { clientId: '', hasSavedCredentials: false }
    else if (
      [
        'plugins.get',
        'operations.get',
        'feedFeedback.get',
        'hidden.get',
        'identity.candidates',
        'manual.get',
      ].includes(route)
    )
      data = []
    else if (route === 'artworkState') {
      if (parameters.get('art') === 'loading') return new Promise<never>(() => {})
      const sample = samples[(Number(params?.workId) - 1) % samples.length]
      const hero = params?.slot === 'Hero'
      const id = hero ? (sample.id === 'hades' ? 'hades-hero' : 'outer-hero') : sample.id
      data = {
        current: parameters.get('art') === 'missing' ? null : { previewKey: { provider: 'fixture', id } },
        revision: 'fixture-1',
      }
    } else if (route === 'feedImpression') data = {}
    else return { ok: false, status: 400, message: 'This screen or action is outside the visual fixture.' }
    return { ok: true, status: 200, data }
  },
  artwork: async (_provider: string, id: string) =>
    Object.entries(images).find(([path]) => path.endsWith(`/${id}.jpg`))?.[1] ?? null,
  connection: async () => ({ connected: true, message: 'Visual fixtures' }),
  onConnection: () => () => {},
  onEvent: () => () => {},
  isFullscreen: async () => fullscreen,
  onFullscreen: (callback: (value: boolean) => void) => {
    onFullscreen = callback
    return () => {}
  },
  setFullscreen: async (value: boolean) => {
    fullscreen = value
    onFullscreen(value)
  },
  loadPreferences: async () => profile,
  savePreferences: async (value: unknown) => {
    profile = parseThemeProfile(value)
    sessionStorage.setItem(preferenceKey, JSON.stringify(profile))
  },
  listThemes: async () => [],
  installTheme: async () => null,
  importProfile: async () => null,
  exportProfile: async () => true,
  openExternal: async () => ({ opened: true }),
}
window.winnow = bridge as WinnowBridge
void import('../../src/renderer/src/main')
