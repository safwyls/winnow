# Platform accounts checkpoint — 2026-10-01

TASK-381.27 covers 25 frozen source methods and is the seventh task in the authorized
batch through TASK-381.30. All forty original cases pass without skips; the nine source
files retain their fixtures from `cf45d9f1127243a987d3cf6e664a32fc767ecb67`.

## Shared policy and HTTP evidence

Six confirmation methods retain the actual backend implementation. Their real SQLite
fixtures preserve candidate-winning and non-winning accounts, credential fingerprints,
confirmation clearing and fresh disclosure. The changed-key source case checks the
new fingerprint after the same account is disclosed again; the separate no-disclosure
case proves confirmation stays cleared.

Eleven new HTTP cases exercise the production application services and repositories.
Incomplete inventory keeps My game and Household game in Library and Feed; a complete
selected-account inventory narrows both, and a later failed attempt restores both.
Replacing the original key clears confirmed account 10001 before refresh publication.
Empty 2022–2025 history markers request only 2026 and never invent confirmation.
Epic session changes preserve an expired identity on cancellation and replace Account A
with Account B only after successful authentication. Controlled providers supply external
responses; the real account-attempt validation, persistence and projections still run.

Unknown Epic and GOG install observations preserve the same ownership and install path;
authoritative absence changes Play to Install. Selected account 10002 displays its 2024
gift acquisition, while all accounts display the 2020 acquisition without a conflicting
licence. Three five-dollar facts retain two known accounts and an unknown scope. The API
retains observed money; presentation withholds the ambiguous aggregate and percentages.

Two narrow host assertions are framework-specific: the Avalonia WinExe console-handle
test, and constructing the legacy optional StoreConnections with no Steam module.
The production backend requires its provider registrations. This does not waive missing
credentials or unavailable native sign-in: both actual Settings surfaces test a present
API key, absent session and missing sign-in bridge, retaining KEY SET, unavailable copy
and an enabled masked key editor.

Source logs are `.tmp/task38127-source-main.log` and `task38127-source-ui.log`, with TRX
under `task38127-source-results`. The final HTTP log is `task38127-api-final4.log`.
The fixture build has zero warnings or errors. Full source/HTTP details are recorded in
`.tmp/task38127-backend-evidence.json`; no production backend change was needed.

## Desktop and fullscreen presentation

Fullscreen Platforms now has refreshed summary rows, provider pages and separate key,
purchase-history and saved-page tools. Back restores the invoking control. Desktop keeps
its platform cards. Optional provider states have one named, visible status with neutral
treatment; GOG retains its no-sign-in-needed state. Both surfaces confirm Epic sign-out,
preserve identity on Cancel, and restore the new connection action after a delayed
successful refresh. Setup also passes its actual fullscreen mode to shared Steam controls.

Key drafts stay masked and local, clear on disposal, and reopen empty. Save and Remove
share their pending state. Vertical navigation follows the controls' physical order.
An actual native test caught controller Right falling through to spatial navigation
after physical caret movement was restored. Directional controller events are now
identified separately: controller focus stays in the field while physical keyboard
caret and selection movement retain their defaults.

Saved-page selection requires an explicit Read on both surfaces. Fullscreen uses the
existing controller file browser. Main issues opaque temporary handles and reads bytes
only after Read selected pages; cancellation and disposal revoke them and abort reads.
Repeated choices accumulate without duplicates, and cancelling an additional chooser
preserves earlier selections. Files are bounded individually and in total. Native checks
use real sanitized HTML and the actual importer, with no renderer response rewriting.

CSV saving preserves the UTF-8 BOM and backend content. Seven host checks cover exact
accented text and CRLF bytes, default filename, unchanged cancelled destinations,
deferred file reads, handle revocation, disposal and size limits. They pass in
`.tmp/task38127-host-files-final.log`.

The renderer ledger records 377 distinct platform cases across eleven suites, verified
through the broad 372-case run and focused additions/reruns. The last controller change
passes 28 tool/controller cases plus fourteen shared dispatch cases; twenty of those
are additional shared-controller coverage outside the 377. TypeScript, formatting and
the production build pass. See `.tmp/task38127-ui-ledger.json` and
`.tmp/task38127-build-final.log`; the final renderer bundle is `index-Dqdv37w6.js`.

## Native and visual evidence

All thirteen new native journeys pass across desktop and fullscreen, including
1280×720 at 140% text size. The tests exercise real API inventory, acquisition and
statistics projections, the controller file chooser, import and actual CSV file bytes.
Screenshots show main-menu LB/RB hints, local A/B hints, Y Keyboard when editing, and
masked on-screen keyboard input. Directional checks measure increasing control positions,
visible bounds and hit targets; Back closes one layer and restores focus.

Desktop and fullscreen captures were inspected. The scaled key and consent pages scroll
their larger text while keeping local hints visible. The Epic identity capture records
the brief post-completion refresh, while the subsequent action waits for its enabled state.
Synthetic controllers and offline provider documents do not establish physical-device
or live-service compatibility.

Early native failures corrected raw-storage versus API assertions, fullscreen-only refresh
expectations, dialog readiness, exact labels and nested routes. A separate two-case Escape
diagnostic passed: CDP keyboard input did not reach Electron's before-input hook, while
native Electron key events closed the actual provider owner on both surfaces. Cancellation
preserved identity and performed no token exchange. The diagnostic is recorded under
`.tmp/task38127-escape-diagnostic-results`.

Eleven directly affected native regressions also pass: saved-page imports on both
surfaces, manual forms on both surfaces, four platform/key accessibility routes and
three Epic workflows. Nested region and title-count selectors were updated to the actual
fullscreen pages without changing their count or validation assertions. The consolidated
24-case ledger is `.tmp/task38127-native-evidence.json`, with reports in the initial,
final2, final3 and final5 result directories. The separate Escape diagnostic adds two
supplementary checks. Native fixtures were closed before any other build or test lane ran.

All 25 assigned methods leave pending/partial: seventeen ported, six retained-backend and
two framework-specific. The audit retains 2,435 methods: 1,606 ported, 688 retained-backend,
35 framework-specific, 88 pending and 18 partial. The complete migration gate still fails;
the authorized batch continues with TASK-381.28 after this milestone.
