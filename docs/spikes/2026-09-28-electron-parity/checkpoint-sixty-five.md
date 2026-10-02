# Identity and expansion projections checkpoint — 2026-09-30

TASK-381.10 covers nineteen frozen source contracts. Desktop and fullscreen are assessed
separately. This checkpoint does not establish completion of the Electron migration.

## Implementation

Owned copies now show their original work titles, store labels, individual playtime and
last-played dates. Achievement rows retain the release title, counts and percentage;
Steam 100% and Epic 30% never become a blended percentage. Unsupported stores, unfetched
Steam progress, unavailable progress, empty schemas and known zero progress stay distinct.
Rows outside the current visible release scope are omitted.

Expansions and Extends are separate sections. Their rows show the counterpart's own stores,
playtime and date, with View and Separate actions. Expansion minutes never enter the base
game's total. Relationships are projected through the published visible library, so hidden
and account-scoped-away counterparts do not leak names, figures or actions. Separating a
relationship still targets its exact child and expected link, preserving sibling links and
all original records.

Native testing exposed an intermittent nested Escape dismissal. The parent Details dialog
now refuses Escape from another dialog, and the confirmation handles its own Escape before
or after its Radix layer becomes active. Cancel restores the opener without closing Details
or writing a relationship. Fullscreen confirmation uses larger type and controls, with
D-pad Choose, A Select and B Cancel hints inside the dialog. Its original SVG assets use the
same viewBox normalization as the keyboard and file chooser, preserving complete glyphs.

Both surfaces keep their existing themes and section placement. Expansion explanations use
the visual specification's primary text ink, and the desktop disclosure leaves space before
its section heading. The Electron README describes these behaviors in place. No backend
production changes were needed.

## Source equivalence

The inventory remains frozen at `cf45d9f1127243a987d3cf6e664a32fc767ecb67`.
`migration-identity-projections.json` records each method's evidence: eleven ported methods
and eight retained backend methods. The five reader-inventory guards still scan and classify
the actual tree, reject stale entries, and catch new SQL, repository and bulk-snapshot readers.
The three registration tests retain sweep execution, shared matcher/resolver lifetimes and
pre-registered threshold precedence through the production registration extension.

Component fixtures retain the original dates, titles, Steam IDs, years and minute values,
with source-derived retired, bounced, active and never-played buckets. They use prepared
API-shaped snapshots; they do not establish database behavior. The eleven new backend tests
and native fixtures establish actual authenticated HTTP projections and SQLite persistence.

Native fixtures preserve the original UTC instants using the same SQLite text representation
as Dapper. They use the real clock, so relative idle wording reflects the run date. Distinct
synthetic cached covers make the primary-art pixel comparison non-vacuous while retaining
Steam IDs 500001 and 500002. Account filtering includes a completed selected-account inventory;
membership rows alone intentionally cannot prove another account's game is absent.

The source unsupported-achievement comment describes no row, but its executed assertion
requires an Epic row saying Not supported. Evidence follows that assertion. Electron keeps
its existing count wording, such as “10 of 10 unlocked”, instead of slash notation.

## Verification

The final Electron production build and typecheck pass in `.tmp/task38110-build-final.log`.
All **3,555 component/live API cases across 178 files** pass without skips in **88.08 seconds**,
recorded in `.tmp/task38110-components-final.log`. Native runs finish before that suite,
which uses four workers. Changed Electron files pass Prettier and whitespace checks.

All **32 original cases** across ExpansionLinkTests, IdentityReadModelTests,
IdentityReadInventoryTests and SoftMatchRegistrationTests pass without skips. All **11 new
IdentityProjectionParityTests** pass through real HTTP and temporary SQLite databases.
TRX files are `.tmp/task38110-dotnet-results/identity-source-contracts.trx` and
`identity-api-projections.trx`; logs are `.tmp/task38110-source-tests.log` and
`.tmp/task38110-api-tests.log`.

The focused identity component matrix passes **36 cases**: eleven source methods on both
surfaces, omitted counterparts, five achievement states per surface, and immediate nested
Escape on both surfaces. `.tmp/task38110-projection-final.log` records that run. Existing
Details component fixtures now include the visible counterparts their relationship assertions
require; all 100 existing cases pass.

**Thirty-nine distinct native cases pass:** twenty-four identity cases in
`.tmp/task38110-native-final3-results/results.json` and all fifteen existing Details cases in
`.tmp/task38110-native-regression-final2-results/results.json`. The identity run took 3.7
minutes; the existing Details run took 1.1 minutes. The existing scale-boundary assertions
now use the established fullscreen base and viewport scale when checking effective zoom and
snapped border pixels. Their original dimensions, text preferences, geometry, action traversal
and focus assertions remain intact.

Six additional repetitions pass in `.tmp/task38110-native-repeat-final-results/results.json`:
three desktop and three fullscreen runs cancel immediately after safe autofocus, then reopen
the confirmation for settled cancellation and long-title presentation checks. They retain the
open Details view, restore its opener, preserve the link and reach both counterpart games with
a visible focused control.

Additional captures in `.tmp/task38110-native-capture-results/` and
`.tmp/task38110-native-confirmation-final-results/` verify the final fullscreen confirmation.
Checks cover real SVG drawing bounds within the viewBox, visible hint bounds, readable type,
safe focus, cancellation and the exact child-only delete. Relationship captures explicitly
scroll their rows into view and require the figures and actions inside the reading viewport.
Top and lower owned-copy captures show the independent title, hours and original date.

The inventory validates with **1,383 ported, 636 retained backend, 32 framework-specific,
319 pending and 65 partial** methods. **384 remain unresolved**, nineteen fewer than checkpoint
64. The complete migration gate still exits 1 because later checkpoints remain. Logs are
`.tmp/task38110-migration-report.log` and `.tmp/task38110-migration-gate.log`.

## Remaining validation

Native controller input uses the standard Gamepad API with simulated button states. Physical
devices, TV-distance readability, the complete .NET suite and packaged installers remain
outside this checkpoint. Native identity tests use the production preload/API/backend and
intercept only OS dispatch; the existing Details regression fixture controls selected API
replies. Every interactive run uses disposable data, and no game or launcher is started.

TASK-381.10 stops for review. TASK-381.11, names, editions and group-header preferences,
remains unstarted until the user prompts continuation.
