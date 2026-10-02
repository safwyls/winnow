// CSS uses the shared application and OS suppression rules. JS owners require
// separate behavior tests: CSS cannot suppress a WAAPI, Motion or canvas frame.
export const cssMotionOwners = [
  'components/SortMenu.css',
  'components/artwork-effects/artwork-effects.css',
  'components/game-preview.css',
  'features/launch-feedback.css',
  'features/parity-library-projection.css',
  'features/parity-merge.css',
  'styles.css',
  'themes/avalon-actions.css',
  'themes/avalon-browse-spine.css',
  'themes/avalon-collection-lists.css',
  'themes/avalon-desktop-cover.css',
  'themes/avalon.css',
  'themes/rift/rift.css',
  'theming/studio.css',
].sort()

// Each tuple is [scheduling sites, reviewed responsibility]. Counts make added
// sites fail closed; nonvisual clocks, IO, focus and measurement remain enabled.
export const scheduledOwners: Record<string, [number, string]> = {
  'App.tsx': [2, 'Focus restoration and search focus; no interpolated visual values.'],
  'api/prepare-library.ts': [1, 'Bounded API request timeout.'],
  'components/FullscreenHints.tsx': [1, 'Controller presence polling.'],
  'components/FullscreenStatus.tsx': [2, 'Clock and controller presence polling.'],
  'components/GamePreview.tsx': [2, 'Hover open/close delays, not interpolation.'],
  'components/artwork-effects/interaction.ts': [
    2,
    'Reduced-motion guard cancels tilt/parallax RAF; artwork-effects tests.',
  ],
  'components/deck-shuffle.ts': [1, 'WAAPI starts only when reduction is false; deck-shuffle tests.'],
  'components/feed-impressions.ts': [2, 'Visibility sampling and midnight date rollover, not interpolation.'],
  'components/portal-effects/controller.ts': [
    1,
    'Reduced motion prevents GPU acquisition and stops frames; portal-effects tests.',
  ],
  'controller.ts': [2, 'Gamepad sampling, required for navigation in reduced motion.'],
  'features/ControllerOverlays.tsx': [2, 'Editable focus inspection, not interpolation.'],
  'features/FullscreenAppearance.tsx': [1, 'Focus restoration after mounted controls.'],
  'features/FullscreenPlatforms.tsx': [1, 'Focus restoration after mounted controls.'],
  'features/FullscreenSettingRows.tsx': [1, 'Focus restoration after mounted controls.'],
  'features/ManualGames.tsx': [1, 'Focus return after editor closes.'],
  'features/PluginAccount.tsx': [2, 'Focus restoration and sign-in expiry.'],
  'features/SessionNotifications.tsx': [1, 'Dismissal timeout.'],
  'features/Settings.tsx': [2, 'Restoring active field and section focus.'],
  'features/details-layout.tsx': [3, 'Dialog and menu focus restoration.'],
  'features/launch-feedback.ts': [1, 'Status expiry; visual transition belongs to shared CSS.'],
  'features/metadata-sync.ts': [1, 'API retry delay.'],
  'features/parity-details.tsx': [4, 'Screenshot measurement and editor focus return.'],
  'features/parity-merge.tsx': [
    5,
    'Focus/scroll restoration and grace/refusal expiry; CSS motion is suppressed.',
  ],
  'features/plugin-installation.ts': [1, 'API polling interval.'],
  'features/restore-focus.ts': [1, 'Restoring focus after the closing dialog unmounts.'],
  'startup/dragon-renderer.ts': [
    1,
    'Startup canvas policy suppresses animation and retains static frame; startup tests.',
  ],
  'startup/preparation.ts': [2, 'Two-frame first-render readiness measurement.'],
  'themes/afterglow.tsx': [
    2,
    'Search focus plus hero Motion initial=false/duration=0 when reduced; afterglow-cards tests.',
  ],
  'themes/avalon-backdrop.tsx': [
    1,
    'Backdrop controller cancels and snaps when reduced; avalon-backdrop-renderer tests.',
  ],
  'themes/avalon-feed.tsx': [
    1,
    'Receipt countdown uses one-second discrete updates when reduced; parity-feed-ui tests.',
  ],
  'themes/avalon-row-viewport.tsx': [
    1,
    'Row controller snaps and clears motion when reduced; avalon-row-motion tests.',
  ],
  'themes/avalon-search.tsx': [1, 'Search-field focus after mount.'],
  'themes/avalon.tsx': [11, 'Library/Home retained selection, measurement and focus restoration.'],
  'themes/rift.tsx': [4, 'Mounted page and search focus.'],
  'themes/rift/Discover.tsx': [1, 'Recommendation focus after explicit selection.'],
  'themes/rift/Library.tsx': [3, 'Hover preview delay and retained scroll/focus restoration.'],
  'theming/runtime.tsx': [1, 'Developer stylesheet loading timeout.'],
  'useHeroRotation.ts': [1, 'Reduced-motion branch disables rotation; hero-rotation tests.'],
}
