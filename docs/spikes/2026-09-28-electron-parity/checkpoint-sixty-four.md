# Manual games from executables checkpoint — 2026-09-30

TASK-381.9 covers eight frozen source contracts. Desktop and fullscreen are assessed
separately. This checkpoint does not establish completion of the Electron migration.

## Implementation

The shared manual editor now ignores file inspections that finish after Cancel or
navigation. Entered fields survive navigation, while Cancel discards the draft. Save
waits for file inspection; metadata search remains optional and does not block a
hand-filled save. Correcting a title, saving or closing retires its pending search.

Browsing again replaces a previous automatic proposal without taking ownership of a
typed title merely because it matches the new file's title. This preserves the original
proposal rule even when the user types Tunic before browsing for Tunic, then Braid.
Searches trim surrounding whitespace, as the original does. Development StrictMode
effect replay retains one file selection and accepts its result.

The existing form layout and theme remain in use. Visual review found that the fullscreen
file chooser covered the shell hints without rendering its own. It now keeps D-pad Browse,
A Select and B Cancel glyphs inside its safe margins, adds Y Keyboard for filename entry,
and uses B Back on replacement confirmation. The Electron README and visual specification
describe these controls and draft behavior in place.

## Source equivalence

The inventory remains frozen at `cf45d9f1127243a987d3cf6e664a32fc767ecb67`.
`migration-manual-flows.json` records each method's evidence. Component checks retain
the literal original paths and metadata fixtures; native checks use disposable paths
with the same derivation behavior and inspect the real SQLite database.

The source comments for the CP and Iconoclasts fixtures describe no usable title, but
their stub inspectors call real path derivation. CP and Iconoclasts are usable folder
names and trigger automatic searches before the explicit searches. Evidence follows
the executed source, including the separate generic `C:\Games\game.exe` no-title case.

## Verification

Build and final typecheck pass in `.tmp/task3819-build-final.log` and
`.tmp/task3819-typecheck.log`. Changed Electron files pass Prettier and whitespace checks.
All **3,519 component/live API cases across 177 files** pass without skips in
**83.16 seconds**, recorded in `.tmp/task3819-components-final.log`. The focused manual
executable suite contains 42 cases: sixteen exact source rows across both surfaces and
twenty-six asynchronous, title-ownership, note and StrictMode regressions.

All six unchanged `ManualGameParityTests` pass with no skips in
`.tmp/task3819-dotnet-results/manual-game-parity.trx`. They verify real HTTP/SQLite
create/edit/delete persistence across restart, claimed identifiers and identity correction
conflicts. Interrupted-response reconciliation remains component simulation; this is not
evidence of losing a real response after a committed write.

**Twenty-two distinct native cases pass.** `.tmp/task3819-native-summary.json` deduplicates
the three executed reports: eighteen initial cases, six cases rerun with stricter source
fixtures and lower-form screenshots, and five chooser-hint regressions. These reports are
preserved in `.tmp/task3819-native-results/`, `.tmp/task3819-native-final-results/` and
`.tmp/task3819-picker-hints-results/`. Native runs are serial; the complete component/API
suite runs afterward with four workers.

| Native scope | Distinct cases |
|---|---:|
| Eight executable source flows, desktop and fullscreen separately | 16 |
| Manual form validation, identity conflicts, focus and confirmed removal | 2 |
| Shared fullscreen open/save/replace controls and desktop native-dialog regression | 4 |

Before Save, checks require zero works, releases, ownerships and manual entries in SQLite,
an empty real manual API result, and no write requests. After Save, the API and direct SQL
agree on the executable, its installation folder and the handwritten platform. Reopening
Tunic restores the selected paths. Every fixture retains its inert file bytes and records
zero shell dispatches. The source's literal drive paths remain in the component tests;
native tests relocate them into disposable folders without changing derivation semantics.

Inspected top and lower forms, metadata candidate rows, selected-match explanations and
fullscreen chooser open/save/replace screenshots. The chooser tests require visible SVG
glyphs and hint bounds wholly inside the viewport, with Y only during filename entry and
Back during replacement confirmation. Both modes retain their existing form typography.
An independent review found no remaining issue within the eight contracts.

The first typecheck caught a Playwright-only locator option in a Testing Library helper;
removing that unsupported option preserves its exact string matching. Stricter native
fixtures restore the CP auto-search and both Iconoclasts searches from the source, without
loosening assertions. The screenshot gap led to the visible chooser hints above.

The inventory validates with **1,372 ported, 628 retained backend, 32 framework-specific,
338 pending and 65 partial** methods. **403 remain unresolved**, eight fewer than checkpoint
63. The complete migration gate still fails because subsequent checkpoints remain. Logs
are `.tmp/task3819-migration-report.log` and `.tmp/task3819-migration-gate.log`.

## Remaining validation

Native controller input is simulated through the standard Gamepad API. Physical devices,
TV-distance readability, the complete .NET suite and packaged installers remain outside
this checkpoint. Interactive runs use disposable data; selected executable fixtures are
inert and never launched. Native metadata search replies are controlled at the readonly
API boundary; manual entry reads, writes and SQLite persistence use the real backend.

TASK-381.9 stops for review. TASK-381.10, identity projections and expansion relationships,
remains unstarted until the user prompts continuation.
