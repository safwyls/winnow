# Details actions and metadata parity

Measured on Windows on 2026-09-29. These checks extend TASK-381's source-contract
inventory; they do not establish completion of the entire migration.

## Store actions and links

`parity-action-controls.test.tsx` exercises the Avalon details action in desktop
and fullscreen. The shared policy distinguishes installed, uninstalled and unknown
states, validates each store's identity, and prefers a viable copy before an installed
copy with Derelict evidence. An all-Derelict group still permits manual launch.
The lifecycle text uses the backend's grouped status, confidence, source reason and
user exemption; it does not infer whether a game can run.

`GameActionParityTests` ran 18 cases through the production HTTP endpoint and
`GameActionApplication` on temporary SQLite databases. Only the final OS dispatcher
and Epic launch-key source are replaced. Assertions include the literal measured
Steam, GOG and Epic play/install URLs, action kinds, management destinations and
refusal before dispatch for absent or malformed identities. All 18 passed. Results:
`.tmp/action-parity-results/`; build output: `.tmp/action-parity-artifacts/`.

The link tests cover IGDB numeric short IDs, grouped Steam identities, deduplication,
SteamGridDB and GOG Galaxy browsing. Read-only links use the external-link boundary;
play/install commands use the ownership-based backend action boundary.

## Installation folders

The preload exposes `openInstallFolder(ownershipId)`. Main re-reads the active
backend workspace, requires the current copy to be installed, and verifies that its
absolute saved path is an existing directory before calling the OS. The renderer
cannot supply a path or a file URL. The control appears only for an installed copy
with a recorded folder, reports errors inline and allows retry.

`parity-install-folder.test.tsx` covers invalid IDs, missing/changed ownership,
missing paths, files, inaccessible directories, OS refusal and renderer pending/error
states. The combined folder/action-control run passed 35 cases. A native gallery
fixture also exercises the real preload/main path in both modes; it replaces only
the final `shell.openPath` handoff, so verification never opens Explorer on the runner.
That native case passed in 1.4 seconds in the combined 85-case run; both gallery cases
also passed. The broader run still required correction of unrelated navigation failures.

## Metadata fields

`parity-metadata-editor.test.tsx` passed 26 cases. The six fields retain independent
sources and drafts. Successful writes adopt the stored value and refresh library
facts; refused writes retain the draft without reporting a save or refreshing an
unchanged library. Conflict and uncertain-write recovery retain revision checks.
Only user-owned fields offer a return to automatic sourcing. Blank artwork URLs
are refused locally, and every art outcome produces a distinct inline message.

The existing Details and metadata-refresh suites passed another 56 cases after these
changes, including both presentation modes, metadata drafts across navigation and
live-list updates. Native file selection is provided by Chromium; the old optional
Avalonia picker-registration test has no corresponding dependency to register.

The checkpoint integration run passed all 1,936 Electron component and live-backend
tests across 105 files, with no skips. All nine native Details cases passed, including
desktop 1200×640/2560×1440 and fullscreen 1280×720/2560×1440 layouts, 140% text,
120% interface scaling, and actual artwork IPC with a test-owned image. The coordinator
inspected both 1920×1080 Overview captures. Fullscreen previews retain the source
aspect ratio after capping their height; the desktop strip remains horizontal.
