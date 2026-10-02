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

## Game matching

The shared IGDB editor now marks and deduplicates numeric ID matches, preserves
candidate details and drafts, and distinguishes every assignment refusal from a
successful write. A collision offer names the existing game and shows its cover and
year. Declining restores the refusal without writing; accepting links under the
existing holder and never pins the child. A failed clear retains the pin and its
control. Revision conflicts require an explicit refresh before retrying.

The original 28 `IgdbMatchViewModelTests` methods have replacement evidence in
`migration-igdb-match.json`. The focused run passed 105 cases across the IGDB editor,
Details and App suites. Both Avalon modes verify returning from assignment, clearing
and linking to refreshed Details with the named confirmation and More-button focus.
These are component checks through the named API boundary; they do not contact IGDB
or establish behavior of a live external account. Existing native Details geometry
checks remain separate evidence for the presentation.

## Remaining Details source assertions

The identity line omits missing year/publisher fields, provisional names carry their
explicit workspace flag, and Overview distinguishes zero play from a missing session
date. More keeps its label and tooltip, starts with outbound destinations, and includes
validated launcher management, folder, refresh, correction and Hide controls. A malformed
Steam ID exposes neither a launch nor an uninstall action. New component matrices cover
both modes; native revalidation is recorded with the next integration checkpoint.

Five older `GameDetailsViewModelTests` methods assert `RailMarks` and `GapCaption`,
including normalized gap positions and a 14-mark cap. Source inspection found no binding
to either property in the current desktop or fullscreen views. The active activity
timeline and visual specification instead group colliding update marks without a cap.
Those five tests are recorded as retired view-model contracts with replacement timeline
evidence, not claimed as literal ports. The original nullable popup Hide-command test
also checks Avalonia construction rather than the always-available authenticated HTTP
capability. The source reduced-motion assertion still awaits explicit panel evidence.

The remaining nine `LibraryViewModelTests` Details contracts are now reviewed. Both
surfaces compose recorded playtime readings, retain readable historical updates
without an unread claim, and order update headlines newest first with build/title
fallbacks. Missing metadata produces absent identity fields and the original compact
playtime dash; live enrichment updates publisher, install path, playtime and combined
year/month idle text. Steam identity reaches the real install/store/news destinations.
The focused Details/action/facts run passed 204 cases. Eight methods are mapped to
these component or production HTTP checks; the ninth asserts only an unbound singular
gap-caption string and is classified with the other retired caption contracts above.

## Independent metadata navigation

The desktop editor now has a separate 1440 × 1000 dialog with a 24px inset, fixed Back
button and bounded field scrolling. Fullscreen retains the original six-row menu and
per-field navigation. Back discards the active field's changes; successful save/reset
returns to its newly attributed row. Reset asks for confirmation and defaults to Cancel.
The editor shares one busy state, and the existing keyboard handles controller A/Y/B.

All five original desktop dimensions pass native checks, including 940 × 820 at 120%
text. The checks traverse 30 Tab steps in each direction, measure every field within
the scroll body, retain Back during scrolling and test invalid-year placement. Opening
artwork preserves the desktop title draft and restores Browse focus. The fullscreen
native check verifies ordering, keyboard entry, validation, draft restoration and row
focus through the production controller sampler. Six new component cases cover saved
attribution, refused reset, default-cancel confirmation and busy navigation.

The first native pass exposed the inherited 700px generic-dialog width limit, which
was removed. The second pass exposed a test locator that included a textarea's current
text; using its accessible textbox name preserves the original geometry assertions.
The coordinator inspected desktop and fullscreen captures after the corrections.
