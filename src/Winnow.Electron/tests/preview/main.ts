import type { WinnowBridge } from '../../src/shared/bridge'
import { DEFAULT_PROFILE } from '../../src/shared/theme'
import samples from './games.json'

// Explicit visual-test bridge. No database, credentials, filesystem writes or launch actions.
// This entry is separate from the shipped renderer and only serves local review fixtures.
const games = samples.map((sample, index) => ({
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
      store: sample.store,
      installed: Boolean(sample.installed),
      playtimeMinutes: 0,
    },
  ],
}))
let fullscreen = new URLSearchParams(location.search).get('mode') === 'fullscreen'
let onFullscreen = (_value: boolean) => {}
const images = import.meta.glob('../../../../docs/spikes/2026-09-27-afterglow-artwork-mock/assets/*.jpg', {
  eager: true,
  query: '?url',
  import: 'default',
})
const profile = structuredClone(DEFAULT_PROFILE)
profile.appearance.artwork!.finish = 'foil'
const bridge = {
  request: async ({ route, params }: { route: string; params?: Record<string, string | number> }) => {
    let data: unknown
    if (route === 'library.get') data = { games, lists: [] }
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
                reason: samples[game.workId - 1].reason,
              })),
          },
        ],
        candidateCount: games.length,
        confidence: 1,
        failed: false,
      }
    else if (route === 'library.workspace') data = { externalIds: [], pluginActions: {}, epicLaunchKeys: {} }
    else if (route === 'artworkState') {
      const sample = samples[Number(params?.workId) - 1]
      const hero = params?.slot === 'Hero'
      const id = hero ? (sample.id === 'hades' ? 'hades-hero' : 'outer-hero') : sample.id
      data = { current: { previewKey: { provider: 'fixture', id } }, revision: 'fixture-1' }
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
  savePreferences: async () => {},
  listThemes: async () => [],
  installTheme: async () => null,
  importProfile: async () => null,
  exportProfile: async () => true,
  openExternal: async () => {},
}
window.winnow = bridge as WinnowBridge
void import('../../src/renderer/src/main')
