# Large-history reads — 2026-09-11

> **Dated evidence.** Findings describe the builds and services observed on the dates below.
> Current implementation choices are in [the build spec](../../game-library-design.md);
> current interactions and layout are in [the visual spec](../../design-system.md).
> This record is optional background.

TASK-222 measures the review's scaling concern using synthetic data. The measurements below
come from the local Windows Release headless test host, .NET 10.0.11, SDK 10.0.400. They do
not establish native Linux performance, physical-controller latency or a screen refresh rate.

## Fixture and method

`tests/Winnow.Ui.Tests/LargeHistoryResponsivenessTests.cs` creates a temporary SQLite database:

- 2,000 games, releases and ownerships;
- 125,000 sessions and notes, including 5,060 sessions for the opened game;
- 14,600 playtime snapshots for that game;
- 2,000 update events and 40,000 account transactions.

Both presentation paths use the actual library/detail models and SQLite repositories. The
fullscreen run additionally attaches the real weekly Activity page. No production host, live
library, store account or network service is used. Every invocation reports total elapsed
time, synchronous invocation time, repository leases, and leases acquired on the UI thread.
The revised fixture also samples dispatcher availability with a one-millisecond input-priority
timer; its maximum gap includes scheduling, view attachment, layout and model publication.
It is an observation of this test process, not a precise frame-time measurement.

For details and Activity, each measured repository lease executes one read statement. The
account summary uses one lease for eleven aggregate SELECTs; lease count is therefore not
presented as its SQL statement count. Connection initialization pragmas are excluded.

Run from the repository root:

```powershell
dotnet test tests/Winnow.Ui.Tests/Winnow.Ui.Tests.csproj -c Release `
  --filter FullyQualifiedName~LargeHistoryResponsivenessTests `
  --logger 'console;verbosity=normal'
```

Use an isolated `BaseOutputPath` when Winnow holds the normal output assemblies. Timing is
reported instead of asserted against CI machine speed. Regression assertions require zero UI
thread repository leases, no exhausted measurement deadline and the paged repository's fixed
row/read bounds. The fixture stops further repository reads after ten seconds so a return of
the old N+1 path cannot occupy the test host indefinitely.

## Before the change

The baseline was measured after the earlier detail snapshot and refresh-order fixes, before
moving detail/account reads to workers or replacing Activity's query loop. SQLite's async API
completed each detail/account operation synchronously. Activity read all ownerships, every
ownership's complete sessions, each session's note separately, and each release's updates,
then filtered that result to one week in the view.

| Operation | Total / synchronous invocation | Repository reads on UI |
|---|---:|---:|
| Desktop details | 81.05 / 81.05 ms | 5 |
| Fullscreen details | 138.53 / 138.53 ms | 5 |
| Desktop account summary | 109.37 / 109.37 ms | 11 SQL statements in 1 lease |
| Fullscreen account summary | 124.26 / 124.26 ms | 11 SQL statements in 1 lease |
| Fullscreen weekly Activity | More than 10 seconds / 251.14 ms | 0 |

Activity had acquired 5,074 repository leases when the ten-second measurement guard stopped
it. This is a lower bound on completion time, not a ten-second successful load. An earlier
uncapped attempt was stopped after several minutes and supplies no completed timing result.
The guarded baseline is retained locally in `large-history-before.trx`.

## Changes and observed result

`LibraryViewModel` captures the detail identity context on the dispatcher, reads its history
snapshot on a worker with cancellation, and publishes only for the current request. Closing
details cancels its reader. The existing full-history timeline remains available. Account
statistics perform their aggregates on a worker and check cancellation before publishing.

`IActivityRepository` reads only the requested half-open UTC week and visible ownership IDs.
It joins notes in the same query, filters Journal before paging, deduplicates updates by release
and returns at most 50 rows plus a continuation cursor. Existing ownership/date indexes serve
the read. Keyset paging orders equal timestamps by ID, so older entries remain reachable without
an offset or a silently truncated history. Raw update payloads are not loaded for these rows.
The fullscreen page translates local calendar-week boundaries to UTC, and cancels or discards
obsolete period reads. Shared detail/account behavior applies to both presentation paths.

Two local after-change runs observed:

| Operation | Total range | Synchronous invocation range | Maximum dispatcher gap range |
|---|---:|---:|---:|
| Desktop details | 100.68–146.93 ms | 0.11–0.12 ms | 25.99–38.98 ms |
| Fullscreen details | 119.84–149.76 ms | 0.27–4.50 ms | 54.24–55.63 ms |
| Desktop account summary | 101.38–166.13 ms | 0.03–0.04 ms | 1.06–1.47 ms |
| Fullscreen account summary | 129.72–161.38 ms | 0.09–1.02 ms | 11.24–11.91 ms |
| Fullscreen weekly Activity | 68.38–119.32 ms | 23.34–119.21 ms | 41.84–119.15 ms |

All measured reads now run off the UI thread. Weekly Activity uses one SQL query instead of
the unfinished thousands of reads. Details still use five scoped reads in this fixture, and
account summary still uses eleven aggregates; their total cost is not claimed to disappear.
Activity's synchronous interval includes attaching the page and replacing the details view.

The working responsiveness budget for this fixture is: no SQLite work on the dispatcher,
detail/account read invocations yielding within 16 ms, at most one read and 50 initial rows per
Activity page, and first-page/detail/account completion within 250 ms on this measured host.
These runs meet that budget. They do not meet or claim a universal 16 ms layout budget:
fullscreen attachment still produced a 119 ms gap, and very long detail timelines still have
model/layout work. These observations justify the targeted query/thread fixes, not a wholesale
timeline or rendering rewrite. Final integration checks should repeat structural assertions;
absolute timing will vary with machine load and hardware.

`ActivityRepositoryTests` exercises boundary instants, same-time pagination, rating-only journal
entries, per-release update deduplication, hidden ownership exclusion and 40,000 visible IDs.
`ActivityPagingInteractionTests` verifies delayed responses, week changes, cancellation and
dispatcher availability. Existing detail refresh/order tests cover draft preservation and
closing a now-hidden game. The final repair evidence records the complete verification runs.

Independent interaction review added seven recovery cases: failed first-page and continuation
reads, retry with retained rows, editor return after loading older sessions, saved-note preview
updates, delayed-read focus, and account-summary loading/error recovery. The final focused run
passed 51 UI cases in `ui222-independent-review.trx`. Its repeated large-history measurements
again recorded zero UI-thread repository leases and one weekly Activity query. Weekly Activity
completed in 68.27 ms; details completed in 116.85 ms on desktop and 117.80 ms in fullscreen;
account summary completed in 122.51 ms and 128.48 ms respectively. This is additional local
evidence, with the same platform and layout limits above.
