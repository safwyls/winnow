# Gameplay statistics checkpoint — 2026-10-01

TASK-381.18 covers eleven frozen source contracts. It is the eighth task in the
authorized sequential batch through TASK-381.20.

## Source boundaries

The view-model fixture begins with two ownerships, including GOG or Xbox, and a clock
at December 1, 2026 at noon UTC. Hiding the second game must cancel an obsolete
statistics read and request only the remaining visible ownership. Linking the games
must preserve the selected store and map both ownerships to the first resolved game.
Removing the selected store must return to All stores. Search must not filter
statistics. A reader that ignores cancellation must never publish after deactivation.

Custom dates are inclusive local calendar dates. The Los Angeles transitions on
March 8 and November 1, 2026 span 23 and 25 hours respectively; each one-day query
must retain those exact UTC boundaries in its single time bin. The five invalid
source inputs cover malformed text, reversed dates, a future date, a pre-1900 date
and a range longer than ten years. None may query or retain obsolete results.

The UI preview retains all nine works and nine ownerships, representing eight
resolved games, together with the original chart formulas. Changing from 30 to
90 days does not change current library counts. Store selection and custom dates
survive switching between Gameplay and Spending. The Spending fixture retains its
one $20 Steam transaction. The Xbox source case changes an existing ownership from
Steam to Xbox and refreshes the open library; it verifies the post-import state,
without running a provider import job.

Desktop chart layouts use 1200px and 600px widths. Fullscreen uses 1920px and 1280px,
including 140% text. Full-shell typography preserves the 28px section label, 3px
active underline and 24px Recorded hours caption, with text sizes scaled by the
stored preference. Compact Spending views at 1024px and 600px preserve the original
170px and 220px maximum distance from the statistics view top to the figures.

## Implementation

Desktop Stats opens Gameplay first and retains the chosen section on return. Its
navigation highlight follows the entire statistics dashboard, including Spending.

Gameplay requests consume the query cancellation signal. Their identity includes the
visible ownership, resolved game and store mapping, while excluding presentation
search. Cancel and invalid Apply stop the current request; obsolete results cannot
replace the current view. Removing a selected store clears its persisted choice.
Failed refreshes do not restore cached figures. Invalid attempted dates remain
invalid across Gameplay/Spending navigation, with no replacement read on return.
The fourth chart includes both resolved-game bucket counts and overlapping store
ownership counts. Fullscreen uses the source store and period buttons and scaled
statistics typography. Spending shares its heading, section choices and actions in
a wrapping toolbar, with figures immediately after source and coverage copy.
Selected fullscreen store and period choices use the raised surface and accent
foreground/border independently of keyboard focus. Chart bins name both local
endpoints; coverage and session explanations retain the source distinctions between
recorded hours, lifetime playtime, overlapping sittings and full session lengths.

The first HTTP run revealed a backend defect: gameplay held an immediate SQLite
writer transaction across the aggregate read. A held obsolete reader therefore
blocked both hiding a game and a replacement read. Gameplay now uses an explicit
deferred read transaction through `IUnitOfWorkFactory.BeginRead`. It retains one
consistent scope/statistics snapshot without reserving the writer. Existing write
scopes retain their immediate transaction behavior; read and write scopes reject
nesting. The original failing ordering remains in the regression tests.

## Verification

The final complete component/live API gate passes all **3,822 cases across 188
files** in 95.16 seconds (`.tmp/task38118-components-final2.log`). The final
TypeScript/production build passes (`.tmp/task38118-build-final3.log`), as do
formatting, whitespace checks and the migration audit. All eleven assigned methods
are now ported: the inventory records 1,487 ported, 650 retained backend and 32
framework-specific methods, with 231 pending and 35 partial methods remaining.

All 23 original source cases pass unchanged: eleven view-model cases and twelve UI
cases. Logs are `.tmp/task38118-source-tests.log` and
`.tmp/task38118-source-ui-tests.log`, with TRX results under
`.tmp/task38118-dotnet-results/`.

The focused app navigation suite passes all 57 cases
(`.tmp/task38118-app-focused.log`).
The focused gameplay/activity/account matrix passes 72 cases
(`.tmp/task38118-renderer-focused.log`). Four new regression failures first reproduced
the cached-figures and invalid-date-remount bugs on both surfaces, then passed after
the corresponding fixes (`.tmp/task38118-refresh-before.log`).

The final focused HTTP gate passes all eleven cases in four seconds
(`.tmp/task38118-api-tests-final3.log`). The initial run passed seven and failed two
with the writer-lock defect described above. The final run preserves their ordering
and passes without releasing the old reader early. A further 63 transaction and
gameplay repository cases pass (`.tmp/task38118-transaction-tests.log`), including
the new proof that a read snapshot stays unchanged while another writer commits
and a new reader observes the updated value.

Desktop Spending places its figures 115.1875px below the dashboard top at 1024px
width and 206.375px below it at 600px, within the source's 170px and 220px limits.
Inspected captures include desktop 600px and fullscreen 1280px at 140% text;
selected choices, custom inputs and main-menu bumper hints remain visible.

Native verification passes 28 distinct cases: 26 new source-equivalent statistics
cases and two earlier desktop/fullscreen navigation regressions. The corrected
final run passes 24 cases (`.tmp/task38118-native-final2.log`); the two full-shell
cases pass after correcting a harness assertion that searched for SVG hint text
(`.tmp/task38118-native-final3.log`). The final assertions inspect visible LB/RB
glyphs flanking the main navigation. The earlier consumers pass in
`.tmp/task38118-native-consumers.log`. These runs use production bundle
`index-BIyID6oS.js`; the ledger records the final result for every case.

The first complete component/live API run passed 3,821 of 3,822 cases. Its remaining
account-capture case exposed a regression in the embedded fullscreen Spending view:
transaction and licence counts had been hidden along with the intended redundant
library count. Restoring that existing paragraph below the figures preserves the
compact header. All 59 focused account-capture/gameplay cases pass after the fix
(`.tmp/task38118-account-counts.log`).
A final fullscreen chart/Spending native recheck passes on bundle `index-CxNyF6F6.js`,
asserting and capturing the restored counts (`.tmp/task38118-native-final4.log`).
The ledger keeps the earlier 28-case provenance and this final changed-view recheck.

## Framework adaptations

Native tests keep the source date fixed at December 1 while allowing timers and
animation frames to run normally. Chromium timezone overrides exercise UTC and
Los Angeles independently. The displayed British-English September abbreviation
is `Sept` in Chromium and `Sep` in the frozen .NET test. Both spellings represent
the same independently asserted local dates and exact UTC request boundaries;
the production formatter remains locale-aware.

Library invalidation can start a statistics read before the refreshed library
projection arrives. The later projection cancels that intermediate query and starts
the final scope. Fixture plans match the final store and ownership mapping, and the
replacement value remains stable across repeated reads. The original obsolete read
stays held until its explicit release. A dedicated HTTP case proves that nonmatching
requests cannot consume the plan; native call ledgers record intermediate cancellation.
History also uses the statistics endpoint. Fixture classification distinguishes its
exact-time upper bound from Gameplay's inclusive local-calendar boundary. An initial
six-case diagnostic run was excluded after its gates proved capable of consuming
History reads; final verification requires the intended Gameplay loading state and
records the correctly classified request before releasing it.

Chromium rounds border widths after interface zoom and device scale. Typography
verification checks the authored 3px underline together with its computed rounded
width, while retaining exact computed text sizes at 100% and 140% text preferences.

Native checks use real Electron windows, the production preload/main bridge and
authenticated throwaway backends. Controlled readers retain the source injection
boundaries; the additional real-session HTTP case exercises the production aggregate
repository. Controller input uses standard Gamepad API frames and does not certify
physical controllers. These Windows checks do not replace Linux or release CI.
