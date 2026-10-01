# Activity tracker and large-history checkpoint — 2026-10-01

TASK-381.16 covers eight frozen source contracts. It is the sixth task in the
authorized sequential batch through TASK-381.20.

## Source boundaries

The plot fixtures preserve the original January 1–September 8, 2026 interval. They
exercise February and July monthly bars, three sessions with the first two colliding,
thirty nearby updates, and first/last-day targets at 180px. Selection must retain the
complete labels and update dates through pointer and keyboard input. The range fixture
uses March–May 2022 cumulative readings of 360, 600 and 780 minutes and two completed
sessions, at the original 360px and 620px widths.

The large-history fixture retains 2,000 works, releases and ownerships, 125,000 sessions
and notes, 14,600 snapshots, 2,000 updates and 40,000 transactions. The opened game owns
5,060 sessions. No live library or account participates.

The original measurement records elapsed time, synchronous invocation, maximum timer
gap and repository leases. Its assertions require no UI-thread database leases and no
exhausted ten-second read guard; it does not impose a universal frame-time threshold.
Electron measurements distinguish backend request work from native renderer interaction.
They cannot establish physical-controller latency or performance on other machines.

Standalone plot and tracker probes import the production components with the exact
source props. They measure Chromium layout and real pointer/keyboard input; they do
not claim to exercise an API request. Separate isolated API-backed checks cover their
composition in desktop Details and fullscreen Play history, weekly Activity and
account statistics. The source tracker consumes classified updates, while the live
adapter retains Winnow's corroborated-update policy.

## Implementation

The tracker again states its unread-update count, retains the selected range after
acknowledgement, and uses compact duration text. Its ownership/API adapter continues
to classify updates before passing them to the shared presentation.
The total uses the specified 30px monospace size with saved text scaling. Axis dates
use the same UTC calendar as their series, preventing a midnight tick from displaying
the previous date in Pacific time. Last-played summary dates retain their local display.
Timeline update markers retain their 24px circle inside fullscreen's general 48px
button sizing, so their targets do not cover the date labels.

Timeline formatting reuses locale-aware UTC date/time and number formatters. Memoized
series avoid recalculating thousands of dated labels when selection changes. A paired
Node 24.19.0 projection experiment against `adef2a8e` used the source's 5,060 sessions
and 14,600 snapshots. Tracked projection took 468.94–485.58ms before and 24.20–82.40ms
after; all runs retained 5,000 distinct sittings and 166 monthly records. Overlapping
session observations account for that distinction. This measures projection CPU time,
not native rendering. The script and raw samples are
`.tmp/task38116-timeline-comparison.mjs` and `.tmp/task38116-timeline-comparison.json`.

Details now builds journal rows only when Journal is selected. Sorted session and note
arrays retain their identity between unrelated renders, and session rows reuse local
date and duration formatters. Recorded hours mounts its complete list when opened and
removes that hidden tree when closed. The history remains a continuous list with the
same editing actions; no pagination, record truncation or virtual scrolling was added.

## Verification

The final component/live API gate passes **3,741/3,741 cases in 185 files** in
95.72 seconds (`.tmp/task38116-components-final2.log`). The production build with the
final invalid-date guard passes (`.tmp/task38116-build-final3.log`), as do formatting,
TypeScript and `git diff --check`.

All **ten original source cases** pass unchanged: two tracker cases and eight UI cases.
Logs are `.tmp/task38116-source-tests.log` and `.tmp/task38116-source-ui-tests.log`;
TRX files are in `.tmp/task38116-dotnet-results/`. Their large-history measurements are:

| Source operation | Total / synchronous invocation / maximum UI timer gap, ms | Repository leases |
|---|---:|---:|
| Desktop Details | 122.27 / 0.17 / 42.50 | 5 |
| Desktop account summary | 501.13 / 0.06 / 1.61 | 1 |
| Fullscreen Details | 140.73 / 4.50 / 57.01 | 5 |
| Fullscreen weekly Activity | 80.07 / 80.04 / 79.97 | 1 |
| Fullscreen account summary | 416.03 / 0.63 / 9.44 | 1 |

Every source measurement reports zero UI-thread leases and no exhausted read guard.
One account-summary lease executes multiple aggregate statements; it is not one SQL
statement. The timings include current machine load and are not universal budgets.

All **five new HTTP cases** pass (`.tmp/task38116-api-tests.log`). They preserve the
single source announcement and separately exercise a corroborated build/announcement
pair through the production acknowledgement policy. The persisted range fixture uses
the schema's `process_watch` value for the source in-memory `process` label; dates,
durations and ownership remain identical. The large fixture returns all 5,060 sessions
and journal entries, all 14,600 snapshots and the 40,000-transaction account aggregate.

Measured HTTP reads took 241.05ms for Details (16 leases), 89.41ms for weekly Activity
(5 leases) and 400.33ms for account summary (1 lease). These API totals include current
visibility/identity reads that the source's direct-repository measurement does not.
Weekly Activity still performs one paged activity query. Every measured lease ran in
the backend process and none exhausted the read guard. No production backend code
changed for this checkpoint.

The initial **70 focused renderer cases** cover the unchanged activity suite, exact
source fixtures, formatter equivalence, cached projection behavior and UTC axis labels
under `TZ=America/Los_Angeles`. The performance changes pass **127 focused cases**,
including existing metadata and Details reading regressions. New checks verify that
all 5,000 tracked records appear when their disclosure opens, unchanged session rows
retain their DOM, and all journal notes remain reachable after deferred mounting.
Typechecking and the performance build pass
(`.tmp/task38116-renderer-performance-focused.log`,
`.tmp/task38116-build-performance.log`).

The first complete native-load measurements exposed work beyond timeline projection:
Details-to-history readiness took 3,869ms on desktop and 3,971ms in fullscreen, with
maximum renderer timer gaps of 2,662ms and 2,619ms. Tracked-range changes took 432ms
and 455ms, with gaps of 402ms and 419ms. These are observed UI stalls even though
backend leases remain in a separate process and the ten-second guard passes. Raw
baseline phases, API timings and process evidence are in
`.tmp/task38116-large-history-baseline.json`.

Those complete-load measurements supersede the initial harness timings, which ended
when the Details header appeared. The corrected harness waits for bound history and
both Activity events and statistics. It measures the desktop 30-day Activity surface
and fullscreen weekly Activity separately. It also checks the exact selected bar label
before stopping the selection phase.

The final affected native run passes **12/12 cases**, including both large-library
surfaces, four source-width range fixtures, both real API range and acknowledgement
flows, and both existing metadata-refresh regressions. Final measurements are:

| Electron operation | Desktop total / maximum timer gap, ms | Fullscreen total / maximum timer gap, ms |
|---|---:|---:|
| Details through usable play history | 816 / 378 | 832 / 409 |
| Switch to tracked sessions | 58 / 20 | 89 / 46 |
| Select a tracked mark | 58 / 20 | 62 / 21 |
| Activity through events and statistics | 1,173 / 222 | 1,127 / 224 |
| Account summary through figures and enabled actions | 859 / 16 | 863 / 14 |

Desktop Activity covers 30 days; fullscreen covers a week, so those rows describe
their respective product paths rather than identical queries. Compared with the
complete-load baseline, Details readiness improves by about 79%, and maximum renderer
gaps fall by about 84–86%. The remaining 378–409ms history gap is still measurable:
the full session list mounts together. This checkpoint does not claim frame-perfect
rendering or a general performance guarantee. All original-volume records remain
available, and every measured database lease runs in the separate backend process
without exhausting the source read guard.

The final performance run is recorded in `.tmp/task38116-native-performance.log` and
`.tmp/task38116-native-performance-results/results.json`. The invalid-date formatter
guard added afterward preserves the previous `Invalid Date` output; six focused
formatting/history cases and TypeScript pass. It does not affect these valid-date
fixture measurements.

Across the complete native matrix and affected reruns, **22 distinct cases pass**:
20 activity cases and two metadata-refresh consumers. Each latest result and its
report are listed in `.tmp/task38116-native-evidence.json`; final decoded measurements
are in `.tmp/task38116-large-history-final.json`. Ten reviewed screenshots cover
desktop and fullscreen history, Activity, account summary, source widths, update
circles and narrow geometry. Focus remains visible, controller navigation keeps its
bumper hints, and the long-history summary retains the selected collision's complete
dates and count. Controller input uses simulated Gamepad API frames, not a physical
device.

The migration audit records all eight assigned methods as ported: **1,458 ported,
650 retained-backend, 32 framework-specific, 244 pending and 51 partial**, across
2,435 frozen methods. The remaining 295 unresolved methods still prevent the overall
migration-completion gate from passing. The next authorized task is TASK-381.17,
activity paging, recovery and journal interactions.
