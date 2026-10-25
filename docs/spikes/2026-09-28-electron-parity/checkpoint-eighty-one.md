# Ownership and snapshot checkpoint — 2026-10-01

TASK-381.26 covers 23 frozen source methods and is the sixth task in the authorized
sequential batch through TASK-381.30.

## Shared behavior and source evidence

All thirty assigned source cases pass without skips. The original nine source files
retain their frozen fixtures. Evidence is `.tmp/task38126-source.log` and
`.tmp/task38126-source-results/task38126-source.trx`.

The shared backend retains lifecycle batch limits and persisted attempts, mapping
revision invalidation, original cached observation timestamps, ownership refresh
coalescing and publication before enrichment. Exact original gates verify cancellation,
failure recovery and reuse after shutdown. Remote inventory evidence distinguishes
complete, explicitly empty, partial, stale, unanswered and failed reads; cancellation
and resolver failure never publish completeness. Confirming an account enables the
choice independently of whether an authoritative complete inventory can hide games.

Local Steam reads retain installation evidence across repeated remote sync, incomplete
library scans and restart. The two-poll stability rule handles install and uninstall;
launcher management remains navigation, with no false game-start declaration. Snapshot
tests preserve the 244→281-minute delta at fifteen-minute intervals and deduplicate
unchanged scans. The source cancellation fixture parks before entering the resolver;
it proves no later write and a readable factory, not rollback of a partly written
transaction. The install test does not seed play-history rows, despite its method name.

The C# Program regex scanner is a narrow framework-specific assertion. Electron has
no frontend C# Program or blocking Task APIs. The original scanner still passes;
actual refresh lifetimes remain in BackendStartupService's background operation.

## Presentation and integration

Both surfaces restore the exact **Show only your account** face, separate positive
count, singular/plural units and own-account caveat. The figure uses the data font
with tabular numbers. Reading a default choice performs no preference write; one user
change causes one explicit command invalidation. SSE invalidation is separate, so this
does not claim exactly one HTTP library read.

The selected GOG release now exposes its cached notes in Updates. Desktop uses the
GOG patch notes expander; fullscreen opens a reading page with Up/Down and Back hints,
restoring the invoking action on return. Notes are text, never rendered HTML. Empty
notes are omitted; another release's cache and absent Epic URLs are not borrowed.
Avalon and the shared Details path both have component coverage.

Ten HTTP cases pass: six new ownership/storefront cases and four existing launcher
management cases. The new fixture proves confirmation without an inventory, absent
defaults, exact hidden counts of one and 1,234, one PUT, an independent change event
and persistence after restarting the backend. The storefront test retains both exact
provider responses, owned IDs and the unowned GOG99 exclusion; repeated sync and
workspace reads issue no extra provider requests. A second projection retains safe
Panzer notes and missing Epic link until the actual Hades cache entry arrives. Fixture
controls and account routes reject unauthenticated access.

All 307 focused renderer cases pass in eight files; the final source typography
correction also passes thirteen targeted cases. TypeScript, formatting and production
build pass. API evidence is `.tmp/task38126-api-final.log`; renderer evidence is
`.tmp/task38126-ui-focused.log` and `.tmp/task38126-ui-final-leaf.log`; the build is
`.tmp/task38126-build.log`. The isolated fixture build has zero warnings or errors.

Eight distinct native cases pass: both account-scope journeys, both cached-storefront
journeys, and four existing ownership/enrichment regressions. Account fixtures use
1,234 genuine other-account ownerships and a complete selected-account inventory;
the visible library narrows from 1,235 titles to My game, with the saved choice retained
on reopening. The zero/default and exact singular-one paths also execute in HTTP and
component fixtures.

Desktop and fullscreen captures were inspected. Fullscreen retains main-menu bumper
hints and uses a 32-pixel reading title, 24-pixel note text and local Read/Back glyphs;
desktop notes retain 13-pixel text. Back returns focus to Patch notes. The initial two
store-link checks selected the default in-app destination while observing the system
browser boundary. Their corrected fixture saves and verifies the browser preference;
both reruns pass without a production change. Provider and shell requests are captured;
no actual external application or physical controller was exercised.

The consolidated ledger is `.tmp/task38126-native-evidence.json`, with initial and
final reports and screenshots under the matching native-results directories. The
renderer bundle is `index-Cfq_DyaD.js`. Retained refresh checks demonstrate interactive
committed ownership while downstream metadata is held; they do not claim a held
initial-startup experiment.

All 23 methods leave pending: five ported, seventeen retained-backend and one narrow
framework-specific scanner. The audit explicitly accepts the reviewed
BackendStartupService composition file. Totals are 1,589 ported, 682 retained-backend,
33 framework-specific, 111 pending and 20 partial, across the unchanged 2,435 methods.
The complete migration gate still fails; TASK-381.27 follows this checkpoint.
