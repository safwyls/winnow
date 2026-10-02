import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { App } from '../../src/renderer/App'
import { useFeed, useLibrary, useWorkspace } from '../../src/renderer/api/hooks'
import type { ThemeContext } from '../../src/shared/theme'
import { Artwork } from '../../src/renderer/components/Artwork'
import { GameCard, Impression } from '../../src/renderer/components/primitives'
import { GamePreview } from '../../src/renderer/components/GamePreview'
import { ArtworkEffects, ArtworkEffectsProvider } from '../../src/renderer/components/artwork-effects'
import { PortalSurface } from '../../src/renderer/components/portal-effects'
import { avalon, AvalonCover, AvalonDiscover, AvalonLibrary } from '../../src/renderer/themes/avalon'
import { AvalonFilterPanel } from '../../src/renderer/themes/avalon-filter-panel'
import { avalonFacts, type AvalonWorkspace } from '../../src/renderer/themes/avalon-filters'
import { AvalonFeedCard } from '../../src/renderer/themes/avalon-feed-card'
import { FeedDeck } from '../../src/renderer/themes/avalon-feed-model'
import { Details } from '../../src/renderer/features/Details'
import { AccountStatistics } from '../../src/renderer/features/Accounts'
import { GameplayDashboard } from '../../src/renderer/features/activity-gameplay'
import { Merges } from '../../src/renderer/features/Merges'
import { Platforms } from '../../src/renderer/features/Platforms'
import { FullscreenPlatforms } from '../../src/renderer/features/FullscreenPlatforms'
import {
  SteamConnectionCard,
  EpicConnectionCard,
  LibraryPreferenceForm,
} from '../../src/renderer/features/Settings'
import {
  ApplicationPreferences,
  LibraryPresentationPreferences,
} from '../../src/renderer/features/SettingsPreferences'
import { FullscreenLibrarySettings } from '../../src/renderer/features/FullscreenLibrarySettings'
import { SetupAppearance } from '../../src/renderer/features/SetupAppearance'
import { useThemeRuntime } from '../../src/renderer/theming/runtime'
import type { DesignFixture } from './design-fixture'

export const DESIGN_SURFACES = [
  'GameDetailsView',
  'GameTileView',
  'RowCoverView',
  'FeedCardView',
  'FeedView',
  'ActionBarView',
  'FilterPanelView',
  'StoresView',
  'AppearanceView',
  'MergeQueueView',
  'AccountStatsView',
  'StatsView',
  'GameplayStatsView',
  'LibrarySettingsView',
  'ApplicationSettingsView',
] as const
export type DesignSurface = 'Shell' | (typeof DESIGN_SURFACES)[number]
const builtins = [avalon]

function Leaf({ fixture, surface }: { fixture: DesignFixture; surface: Exclude<DesignSurface, 'Shell'> }) {
  const library = useLibrary(),
    feed = useFeed(),
    workspace = useWorkspace(),
    runtime = useThemeRuntime(builtins)
  const [opened, setOpened] = useState<number | null>(null)
  const context: ThemeContext = {
    mode: fixture.mode,
    page: surface === 'FeedView' ? 'discover' : 'library',
    selectedWorkId: null,
    setPage: () => {},
    openGame: setOpened,
    closeGame: () => setOpened(null),
    toggleFullscreen: () => {},
    games: library.data?.games ?? [],
    feed: feed.data,
    loading: library.isPending,
    profile: runtime.profile,
    profileHydrated: !runtime.loading,
    children: null,
    renderScreen: () => null,
    actions: {
      launch: async (ownershipId) => {
        await fixture.bridge.request({ route: 'launch', params: { ownershipId } })
      },
    },
    components: { Artwork, GameCard, Impression, GamePreview, ArtworkEffects, PortalSurface },
  }
  const hollow = fixture.games.find((game) => game.workId === 1)!
  const deck = useMemo(() => {
    const value = new FeedDeck(
      async () => ({ saved: false }),
      async () => [],
    )
    value.receive(
      [
        {
          id: 'preview',
          title: 'Preview card',
          blurb: '',
          feedback: false,
          rows: [
            { game: hollow, reason: 'Patched 10 days ago, six weeks after you last played', releaseId: 101 },
          ],
          reserve: [],
        },
      ],
      'preview',
    )
    return value
  }, [fixture])
  useEffect(() => () => deck.dispose(), [deck])
  useEffect(() => {
    document.documentElement.dataset.mode = fixture.mode
    document.documentElement.style.setProperty('--fullscreen-text-scale', '1')
  }, [fixture.mode])
  const mode = fixture.mode
  let content: ReactNode
  switch (surface) {
    case 'GameDetailsView':
      content = <Details workId={4} mode={mode} presentation="avalon" onClose={() => {}} />
      break
    case 'GameTileView':
      content = (
        <div style={{ width: 200 }}>
          <AvalonCover context={context} game={hollow} />
        </div>
      )
      break
    case 'RowCoverView':
      content = (
        <div className="merge-cover">
          <Artwork workId={hollow.workId} className="artwork-edge-padding" />
        </div>
      )
      break
    case 'FeedCardView':
      content = (
        <div style={{ width: 240 }}>
          {mode === 'desktop' ? (
            <AvalonFeedCard
              context={context}
              deck={deck}
              shelf={deck.shelves[0]}
              row={deck.shelves[0].rows[0]}
            />
          ) : (
            <AvalonCover
              context={context}
              game={hollow}
              reason="Patched 10 days ago, six weeks after you last played"
            />
          )}
        </div>
      )
      break
    case 'FeedView':
      content = <AvalonDiscover {...context} />
      break
    case 'ActionBarView':
      content = <AvalonLibrary {...context} />
      break
    case 'FilterPanelView':
      content = (
        <AvalonFilterPanel
          filter={{}}
          games={fixture.games}
          facts={avalonFacts(fixture.games, fixture.workspace as AvalonWorkspace)}
          workspace={fixture.workspace as AvalonWorkspace}
          fullscreen={mode === 'fullscreen'}
          apply={() => {}}
          close={() => {}}
        />
      )
      break
    case 'StoresView': {
      const snapshot = {
        steam: {
          hasUsableCredential: false,
          hasSession: false,
          hasApiKey: false,
          apiKeyIsAppManaged: false,
          sessionUsable: false,
        },
      }
      content =
        mode === 'fullscreen' ? (
          <FullscreenPlatforms onChildChange={() => {}} />
        ) : (
          <Platforms
            snapshot={snapshot}
            steam={(count) => <SteamConnectionCard snapshot={snapshot} mode={mode} titleCount={count} />}
            epic={<EpicConnectionCard snapshot={snapshot} mode={mode} />}
          />
        )
      break
    }
    case 'AppearanceView':
      content = <SetupAppearance runtime={runtime} mode={mode} />
      break
    case 'MergeQueueView':
      content = <Merges mode={mode} onOpenGame={setOpened} />
      break
    case 'AccountStatsView':
      content = <AccountStatistics mode={mode} />
      break
    case 'StatsView':
    case 'GameplayStatsView':
      content = <GameplayDashboard mode={mode} onOpenGame={setOpened} />
      break
    case 'LibrarySettingsView':
      content =
        mode === 'fullscreen' ? (
          <FullscreenLibrarySettings />
        ) : (
          <>
            <LibraryPreferenceForm initial={fixture.workspace.preferences} />
            <LibraryPresentationPreferences />
          </>
        )
      break
    case 'ApplicationSettingsView':
      content = <ApplicationPreferences mode={mode} />
      break
  }
  return (
    <ArtworkEffectsProvider options={runtime.profile.appearance.artwork!} reducedMotion>
      <div className={`avalon-shell ${mode}`} style={{ display: 'block', height: '100vh', overflow: 'auto' }}>
        <div
          data-design-surface={surface}
          className={`feature-page mode-${mode}`}
          style={surface === 'FeedView' ? { height: '100%', minHeight: 0 } : undefined}
        >
          {content}
          {opened !== null && (
            <Details workId={opened} mode={mode} presentation="avalon" onClose={() => setOpened(null)} />
          )}
        </div>
      </div>
    </ArtworkEffectsProvider>
  )
}

/** The browser/native preview and component tests use the same production graph. */
export function DesignPreview({
  fixture,
  surface = 'Shell',
}: {
  fixture: DesignFixture
  surface?: DesignSurface
}) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: { retry: false, staleTime: 30_000, refetchOnWindowFocus: false },
          mutations: { retry: false },
        },
      }),
  )
  useEffect(() => () => client.clear(), [client])
  return (
    <QueryClientProvider client={client}>
      {surface === 'Shell' ? <App /> : <Leaf fixture={fixture} surface={surface} />}
    </QueryClientProvider>
  )
}
