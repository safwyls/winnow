import type { LibraryGame, Workspace } from '../src/renderer/api/types'
import { DEFAULT_PROFILE, selectThemeProfile } from '../src/shared/theme'
import { avalon } from '../src/renderer/themes/avalon'

export function coverGame(workId = 1, installed = true, played = true, singleStore = false): LibraryGame {
  return {
    workId,
    title: 'A deliberately long game title occupying two lines',
    bucket: 'stale_but_patched',
    playtimeMinutes: played ? 740700 : 0,
    lastPlayedAt: played ? new Date(Date.now() - 3653 * 86400000).toISOString() : null,
    entries: (singleStore ? ['steam'] : ['steam', 'gog', 'epic']).map((store, index) => ({
      ownershipId: workId * 10 + index,
      releaseId: workId * 10 + index,
      workId,
      title: 'Fixture edition',
      store,
      installed: index === 0 && installed,
      playtimeMinutes: index === 0 && played ? 740700 : 0,
    })),
  }
}
export const coverWorkspace = (game: LibraryGame): Workspace => ({
  preferences: { showNonGameEntries: false, showExplicitContent: false, maturityCap: 'all' },
  works: [{ id: game.workId, name: game.title }],
  externalIds: game.entries.map((entry) => ({
    releaseId: entry.releaseId,
    provider: entry.store,
    providerId: '80',
  })),
  pluginActions: {},
  epicLaunchKeys: {},
})
export const coverProfile = () => selectThemeProfile(structuredClone(DEFAULT_PROFILE), 'avalon', avalon)
export function FixtureCoverArt({ workId }: { workId: number }) {
  return (
    <div
      className="artwork"
      data-art-for={workId}
      style={{ background: 'linear-gradient(145deg, #22535a, #193328 45%, #70442e)' }}
    />
  )
}
