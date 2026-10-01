# Tile actions and installed store links checkpoint — 2026-09-30

TASK-381.8 closes fourteen frozen source contracts. Desktop and fullscreen are verified
separately. This checkpoint does not establish completion of the Electron migration.

## Implementation

Desktop tiles, fullscreen X and Details now share one primary-action command for each
ownership. Concurrent callers join the same pending request. An interrupted response
retains its original operation and action across views; completion permits a new intent.
Management and store actions retain their separate typed backend routes. The backend
still checks current ownership and provider observations before dispatch.

Library tiles describe their collection using the rail's vocabulary. Feed tiles retain
their recommendation reason. Details displays plugin inclusion evidence in its header
and per-copy Library section, including after offline refresh or provider unload.
Fullscreen copy facts, source explanations and primary-action feedback use scaled 24px
body text. An independent review found no issues in pending coalescing, uncertain retries,
current ownership resolution or dismissal during dispatch.

Pending primary buttons retain Play or Install, expose busy state and disable duplicate
input. Completion, error and retry feedback occupies a separate row, keeping More and
its store destination stationary. The fullscreen action panel now applies viewport
scaling once, retaining reference proportions at 4K and honoring interface scale, text
size and percentage safe margins.

## Source equivalence

The inventory remains frozen at `cf45d9f1127243a987d3cf6e664a32fc767ecb67`.
`migration-tile-actions-install-links.json` records thirteen new dispositions;
`migration-routing.json` completes the remaining router method. Twelve methods are
ported and two retain their backend tests. Per-method reasons preserve source scope:

- Anvil retains work/release/ownership 1, Steam 700001, zero playtime and the August 26
  observation. Installed Fez retains Steam 620 and GOG 1971477531. Component reference
  and callback assertions plus native operation identity replace Avalonia command and
  view-model properties. Fullscreen X remains installed Play only; Anvil installs through
  Details. Merely opening Details never dispatches an action. Chromium accessibility
  checks require the literal Never played description on Library tiles.
- Moonlighter retains both original Epic launch tuples and their exact cached store URLs.
  Refresh changes Install to Play and back, updates the current folder and removes Steam
  uninstall, while retaining mounted Details, the metadata input and its unsaved draft.
  Pointer center clicks verify store text, destination, hit ownership, live DOM identity
  and one-pixel bounds tolerance during and after dispatch. All four original window sizes
  run on both surfaces: 1200×640, 1280×820, 1920×1080 and 3840×2160.
- The unchanged SDK plugin fixture runs through the real catalog, importer, repositories,
  action API and launch-intent service. Tests retain the stable source ID, provisional
  title promotion, null offline observation, exact Play/OpenStore calls, wrong ownership,
  forged links, duplicate suppression, stale action rejection and catalog unload. Two
  wholly backend methods remain retained; the mixed UI/backend method includes actual
  tile/controller dispatch and visible source evidence in Electron.
- Seventeen original routing rows run through the reading policy with exact dispatch
  counts, destination schemes and fallback checks. The eighteenth, `steam://run/440`,
  uses authenticated Play through the real backend on both surfaces. Its intercepted
  dispatcher receives that exact URI once, with launch attribution, no reader and no
  browser fallback. The generic URL bridge continues to reject executable launcher URLs.

## Verification

Build and typecheck pass in `.tmp/task3818-build-final.log`. The separate plugin fixture
build has zero warnings or errors. All three unchanged `PluginGameActionIntegrationTests`
pass without skips in `.tmp/task3818-plugin-dotnet-results/plugin-game-actions.trx`.

All **3,490 component/live API cases across 177 files** pass without skips in
**86.65 seconds**, recorded in `.tmp/task3818-components-final.log`.
Native suites run serially; the complete component/API suite runs separately with four
workers. Changed Electron files pass Prettier and whitespace checks.

**Forty-nine distinct native cases pass.** `.tmp/task3818-native-summary.json` records
the deduplicated case list and executed attempts. The final broad run and targeted retry
are preserved in `.tmp/task3818-final-native-results/` and
`.tmp/task3818-native-retry-results/`.

| Suite | Cases |
|---|---:|
| Tile commands, shared pending operations and accessible collection names | 6 |
| Install refresh, drafts, store hit targets, 4K scaling and saved display settings | 15 |
| Real plugin actions, offline observations, unload and fullscreen copy typography | 7 |
| Original Steam launch-router row through the authenticated action API | 2 |
| Saved reading destinations and fallback notices | 8 |
| Launch feedback, selected copies, controller routes and summary type | 9 |
| Production desktop cover and fullscreen Details action regressions | 2 |

Inspected desktop and fullscreen drafts, store links, settled More panels, plugin evidence
and launch feedback. Fullscreen copy facts measure 24px at normal text and 33.6px at 140%
text before interface zoom. The 4K action panel preserves its authored 28px action text
and 620px width; physical dimensions scale by two from 1080p. Simulated D-pad navigation
reaches every row through Hide game. A separate case verifies 80% interface scale, 140%
text and 8% safe margins together.

Earlier failures remain alongside the final evidence. Native measurements found the
moving More trigger, undersized feedback/copy text and compounded 4K scaling; production
fixes preserve the original assertions. Fixture repairs correct a built-output import,
single-store accessible-name expectations, complete plugin workspace identities and
waiting for all separately published display preferences. The full component suite also
caught changed feed descriptions; feed reasons now retain their previous semantics while
Library tiles receive bucket names. No test tolerance or source case was removed.

The inventory has **1,364 ported, 628 retained backend, 32 framework-specific, 338 pending
and 73 partial** methods. **411 remain unresolved**, fourteen fewer than checkpoint 62.
The report validates; the complete migration gate still fails because subsequent
checkpoints remain. Logs are `.tmp/task3818-migration-report.log` and
`.tmp/task3818-migration-gate.log`.

## Remaining validation

Controller input is simulated through the standard Gamepad API. Physical devices,
TV-distance readability, the complete .NET suite and packaged installers remain outside
this checkpoint. All interactive runs use disposable data. OS calls are intercepted;
no game, launcher, browser or user folder is actually opened. Install-state presentation
uses controlled HTTP snapshots; plugin and Steam routing cases use real backend services.

TASK-381.8 stops for review. TASK-381.9, adding manual games from executables, remains
unstarted until the user prompts continuation.
