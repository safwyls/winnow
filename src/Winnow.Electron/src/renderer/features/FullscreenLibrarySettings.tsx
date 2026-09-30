import { useApiQuery } from '../api/hooks'
import type { LibraryPreferences } from '../api/types'
import {
  FullscreenAdjustment,
  FullscreenSettingsAction,
  FullscreenSwitch,
  useFullscreenSettingsEntry,
} from './FullscreenSettingRows'
import { AccountVisibility, JournalPromptPreference, usePresentationPreferences } from './SettingsPreferences'
import { RatingCapPreference, useLibraryPreferenceChange } from './RatingCap'
import { parseExpansionGrouping } from './parity-library-grain'
import { Notice } from './shared'

export const librarySortChoices = [
  ['DormantLongest', 'Dormant longest'],
  ['RecentlyPlayed', 'Recently played'],
  ['PlaytimeHighToLow', 'Playtime high to low'],
  ['PlaytimeLowToHigh', 'Playtime low to high'],
  ['NameAscending', 'Name A–Z'],
  ['NameDescending', 'Name Z–A'],
] as const

export function FullscreenLibrarySettings({
  onTools,
  onSpending,
  onRecommendations,
}: {
  onTools(): void
  onSpending(): void
  onRecommendations(): void
}) {
  const presentation = usePresentationPreferences()
  const library = useApiQuery<LibraryPreferences>('preferences.library.get')
  const change = useLibraryPreferenceChange()
  const entry = useFullscreenSettingsEntry(presentation.loaded)
  const sortIndex = Math.max(
    0,
    librarySortChoices.findIndex(([value]) => value === presentation.values.DefaultSort),
  )
  const platforms = [
    ['', 'No preference'],
    ['steam', 'Steam'],
    ['epic', 'Epic Games'],
    ['gog', 'GOG'],
  ] as const
  const platformIndex = Math.max(
    0,
    platforms.findIndex(([value]) => value === presentation.values.PreferredMergePlatform),
  )
  return (
    <section ref={entry} className="fullscreen-settings-content" aria-label="Library preferences">
      <h2 className="fullscreen-settings-group">Library preferences</h2>
      <FullscreenAdjustment
        label="Default library sort"
        description="The order used when Winnow starts."
        value={librarySortChoices[sortIndex]![1]}
        disabled={!presentation.loaded || presentation.pending}
        change={(direction) => {
          const next = Math.max(0, Math.min(librarySortChoices.length - 1, sortIndex + direction))
          if (next !== sortIndex) presentation.set('DefaultSort', librarySortChoices[next]![0])
        }}
      />
      <JournalPromptPreference mode="fullscreen" />
      <FullscreenSwitch
        label="Non-game entries"
        description="Include tools and other library entries."
        value={library.data?.showNonGameEntries ?? false}
        disabled={!library.data || change.pending}
        change={(value) => change.apply('showNonGameEntries', value)}
      />
      <FullscreenSwitch
        label="Group expansions"
        description="Show expansions with their base game."
        value={parseExpansionGrouping(presentation.values.GroupExpansions)}
        disabled={!presentation.loaded || presentation.pending}
        change={(value) => presentation.set('GroupExpansions', String(value))}
      />
      <FullscreenSwitch
        label="Explicit content"
        description="Include adults-only content. Unrated games remain visible."
        value={library.data?.showExplicitContent ?? false}
        disabled={!library.data || change.pending}
        change={(value) => change.apply('showExplicitContent', value)}
      />
      <RatingCapPreference mode="fullscreen" presentation="row" />
      <FullscreenAdjustment
        label="Preferred platform for grouped games"
        description="Choose which installed edition to prefer when games are grouped."
        value={platforms[platformIndex]![1]}
        disabled={!presentation.loaded || presentation.pending}
        change={(direction) =>
          presentation.set(
            'PreferredMergePlatform',
            platforms[(platformIndex + direction + platforms.length) % platforms.length]![0],
          )
        }
      />
      <AccountVisibility mode="fullscreen" />
      <Notice error={library.error || change.error || presentation.error} />
      <h2 className="fullscreen-settings-group">Manage library</h2>
      <FullscreenSettingsAction label="Library tools" onClick={onTools} />
      <FullscreenSettingsAction label="Spending" onClick={onSpending} />
      <FullscreenSettingsAction label="Recommendations" onClick={onRecommendations} />
    </section>
  )
}
