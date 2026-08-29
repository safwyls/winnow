# Winnow full repository code review — 2026-08-28

## Executive verdict

Winnow has unusually deliberate design documents and several strong implementation seams, but the
current working tree is **not release-ready**. The highest-risk problems are not cosmetic:

- the solution does not build;
- the Hoard-to-Winnow data migration has paths that can select an empty/partial database or attach
  legacy SQLite sidecars to a different database;
- established enrichment facts are overwritten despite a documented fill-only contract;
- configured network sources run synchronously before the first window and every 15-minute snapshot
  tick;
- the embedded OAuth bridge does not bind messages to an expected origin or a per-attempt state;
- several background algorithms can starve work forever or record facts the user never observed.

This review found no evidence of intentional storefront writes, fuzzy auto-merges, stored derived
buckets, known-vulnerable NuGet dependencies, or secret-bearing HTTP logs. Those safeguards are real
and worth preserving.

The review targets the **current dirty working tree**, not merely `HEAD`. At review time Git reported
many modified and untracked files, largely associated with the Hoard-to-Winnow rename and update
acknowledgements. Findings therefore describe the code a user would build from the workspace on
2026-08-28.

## Severity model

| Level | Meaning |
|---|---|
| P0 | Active or near-certain unrecoverable loss/security compromise. None confirmed. |
| P1 | Release blocker: broken build, plausible data loss/security issue, core promise violated, or severe correctness failure. |
| P2 | Important defect/debt likely to cause wrong results, poor reliability, or major scaling/accessibility trouble. |
| P3 | Lower-risk hardening, maintainability, consistency, or future-regression concern. |

## Release gates

Do not ship the current tree until these are complete:

1. Restore a green solution build and full test run.
2. Replace the directory rename/copy flow with staged, validated, atomic promotion and test every
   partial-state combination.
3. Restore fill-only enrichment semantics.
4. Remove Steam Web and Epic network calls from the pre-window and snapshot paths.
5. Bind embedded OAuth to exact origins, redirect ports, and cryptographic state.
6. Make soft-match truncation resumable and reconcile stale pending pairs.
7. Separate UI cover ownership from the shared library tile objects.
8. Stop treating missing update coverage as negative evidence in recommendations.

## Findings summary

| ID | Severity | Finding |
|---|---:|---|
| F01 | P1 | Main test project does not compile; non-game soft-match change is incomplete |
| F02 | P1 | Rename migration can choose partial data and can mix a database with foreign WAL/SHM files |
| F03 | P1 | Enrichment overwrites established facts instead of filling nulls |
| F04 | P1 | Network ownership calls block first paint and repeat in the snapshot scheduler |
| F05 | P1 | WebView OAuth bridge lacks origin binding and OAuth state |
| F06 | P1 | Truncated soft-match sweeps permanently starve the same tail |
| F07 | P1 | Pending soft matches survive when metadata makes them ineligible |
| F08 | P1 | Soft matching can run 250,000 SQL lookups inside one writer transaction |
| F09 | P1 | “Same game” records intent but never merges identity |
| F10 | P1 | Candidate coalescing can manufacture a play observation |
| F11 | P1 | Update polling can starve every game behind persistent failures |
| F12 | P1 | Raw build history is not polled independently of announcements |
| F13 | P1 | Startup/library refresh performs UI-thread N+1 database work |
| F14 | P1 | One mutable cover state is shared by wall, feed, and recycled views |
| F15 | P1 | Recommender treats missing update coverage as proof of no updates |
| F16 | P1 | Feed impressions are stored before cards are actually shown |
| F17 | P1 | Journal notes can be closed and lost before their write succeeds |
| F18 | P2 | Logical repository batches are not atomic |
| F19 | P2 | Out-of-order play observations are not idempotent |
| F20 | P2 | Merge-candidate canonicality is enforced only by callers |
| F21 | P2 | Bucket threshold invariants are not validated |
| F22 | P2 | Release matching uses a Work-level year for every edition |
| F23 | P2 | The specified `Dead` bucket has no raw facts or query branch |
| F24 | P2 | Achievement support stops at schema creation |
| F25 | P2 | Steam manifest paths can escape the library root; a bad root can abort startup |
| F26 | P2 | Cover negative cache does not reopen when source capability changes |
| F27 | P2 | Shared cover work inherits the first caller's cancellation |
| F28 | P2 | Cover responses and decoded dimensions are unbounded |
| F29 | P2 | Metadata cache has no payload version and corrupt entries can poison repeated runs |
| F30 | P2 | Session collection is Windows-only despite the product flywheel depending on it |
| F31 | P2 | Epic owned-library cache is process-only |
| F32 | P2 | Recommendation shortlist pruning is not score-bound safe |
| F33 | P2 | Recommendation maturity tier is inferred from a biased subset |
| F34 | P2 | Feed invalidation events are dropped during an active load |
| F35 | P2 | Accessibility names and control hierarchy are incomplete |
| F36 | P2 | Startup `async void` initialization has no error boundary |
| F37 | P2 | Recommendation explanations violate the one-sentence contract |
| F38 | P2 | Duplicate store ownerships consume shortlist capacity before Work collapse |
| F39 | P2 | Multiple app instances can duplicate session/scheduler work |
| F40 | P2 | Steam/IGDB secrets are plaintext while README claims credentials are encrypted |
| F41 | P2 | The specified rolling diagnostic log is absent |
| F42 | P2 | The sole local database is migrated without an automatic recovery copy |
| F43 | P2 | No CI gate exists despite the current compile regression |
| F44 | P3 | Storefront parser inputs have weak size/depth bounds |
| F45 | P3 | `DateTimeKind.Unspecified` is silently relabelled UTC |
| F46 | P3 | Append-only migrations have no immutable hash verification |
| F47 | P3 | Reduced-motion coverage is incomplete |
| F48 | P3 | Unread-update accessible copy omits counts |
| F49 | P3 | Sync naming and comments materially misdescribe runtime behavior |
| F50 | P2 | Dormancy brightness has conflicting authority values (`0.60` versus `0.68`) |

## Detailed findings

### F01 — Main test project does not compile; non-game soft-match change is incomplete (P1)

**Evidence.** `LibrarySoftMatchSweepTests` references `SoftMatchSweepReport.ExcludedWithdrawn`
at [line 327](../tests/Winnow.Tests/LibrarySoftMatchSweepTests.cs#L327) and
[line 352](../tests/Winnow.Tests/LibrarySoftMatchSweepTests.cs#L352), but the report at
[LibrarySoftMatchSweep.cs:42](../src/Winnow.Resolve/LibrarySoftMatchSweep.cs#L42) has no such member.
`ReleaseRepository` projects `SteamAppType` and `EpicCategories` at
[lines 107–108](../src/Winnow.Data/Repositories/ReleaseRepository.cs#L107), but
`ReleaseIdentity` does not expose them. Admission at
[LibrarySoftMatchSweep.cs:130](../src/Winnow.Resolve/LibrarySoftMatchSweep.cs#L130) still excludes
only provisional or empty names.

**Impact.** `dotnet build Winnow.slnx` fails. The intended protection against tools, DLC-like
entries, and asset packs polluting the merge queue is also absent.

**Remediation.** Complete the projection and DTO, apply the existing non-game classification during
admission, reconcile already-pending excluded pairs, and add `ExcludedWithdrawn` to the report.
Keep confirmed/rejected decisions immutable. The full solution build and test suite must be the
acceptance test.

### F02 — Rename migration can choose partial data and mix SQLite files (P1)

**Evidence.** When both directories exist, `WinnowDataLocation` unconditionally selects the new
directory at [lines 148–159](../src/Winnow.App/Services/WinnowDataLocation.cs#L148), without proving
that it contains a database or a complete copy. A failed copy can leave a partial destination if
cleanup fails at [lines 365–379](../src/Winnow.App/Services/WinnowDataLocation.cs#L365); the next
launch then takes that partial destination. In a directory containing both `hoard.db` and
`winnow.db`, `RenameDatabase` skips an existing destination database but independently moves any
missing sidecars at [lines 315–325](../src/Winnow.App/Services/WinnowDataLocation.cs#L315).

**Impact.** The app can silently open/create an empty new database while the complete legacy library
still exists. Worse, a legacy `hoard.db-wal` can be renamed to `winnow.db-wal` beside a different
database, risking SQLite rejection or corruption. This violates the explicit compatibility rule:
never point at an empty new directory.

**Remediation.** Copy to a unique staging sibling, validate the database (`quick_check`, expected
schema/journal, and sidecar set), fsync as practical, then atomically rename the staging directory
into place. If both final directories exist, select only after validating both and prefer legacy
when the new directory has no valid database. Rename the database and its sidecars as one declared
set only when the destination database is absent; never mix sets. Add tests for empty new directory,
partial copy plus failed cleanup, both database names, destination DB plus legacy sidecars, and
interrupted promotion.

### F03 — Enrichment overwrites established facts instead of filling nulls (P1)

**Evidence.** [WorkRepository.cs:191–201](../src/Winnow.Data/Repositories/WorkRepository.cs#L191)
documents a fill-only rule but assigns `COALESCE(@IncomingValue, stored_value)` for year, summary,
cover, publisher, Steam type, and Epic categories.

**Impact.** Any later non-null provider response replaces an established value, allowing a weaker or
changed source to rewrite identity, matching, filters, and presentation.

**Remediation.** Reverse the operands: `COALESCE(stored_value, @IncomingValue)`. Add a conflicting
non-null regression test for every column; the existing null-input tests are insufficient.

### F04 — Network ownership calls block first paint and repeat in the snapshot scheduler (P1)

**Evidence.** [Program.cs:145–163](../src/Winnow.App/Program.cs#L145) synchronously waits for
`SteamSyncService` before starting Avalonia. The service awaits Steam Web and Epic API work at
[SteamSyncService.cs:74–75](../src/Winnow.App/Services/SteamSyncService.cs#L74) and
[120–122](../src/Winnow.App/Services/SteamSyncService.cs#L120). The same service is the scheduler's
`ISteamSync`, despite `SnapshotSchedulerService` claiming the path has no network at
[lines 20–24](../src/Winnow.App/Services/SnapshotSchedulerService.cs#L20).

**Impact.** Configured users can see no window while offline, rate-limited, refreshing an Epic token,
or waiting on the default HTTP timeout. The calls repeat every 15 minutes and multiple Steam
accounts multiply the delay and traffic.

**Remediation.** Split `ILocalLibrarySource` scanning/resolution from remote entitlement backfills.
Show the shell from the existing database immediately, run local sync and remote sync as separate
cancellable background jobs, and publish refresh events. Rename the coordinator to
`LibrarySyncService`, as already recorded in `ROADMAP.md`.

### F05 — WebView OAuth bridge lacks origin binding and OAuth state (P1)

**Evidence.** Script injection runs for every document/iframe at
[WebView2AuthPrompt.cs:459–466](../src/Winnow.Auth.WebView/WebView2AuthPrompt.cs#L459), and the message
handler accepts shaped `exchange`/`harvest` messages at
[lines 519–546](../src/Winnow.Auth.WebView/WebView2AuthPrompt.cs#L519) without validating the
message source. Popups are redirected into the same browser at
[lines 507–516](../src/Winnow.Auth.WebView/WebView2AuthPrompt.cs#L507). Redirect matching omits the
port at [lines 1113–1124](../src/Winnow.Auth.WebView/WebView2AuthPrompt.cs#L1113), and
`AuthPromptRequest` has no expected OAuth state at
[IInteractiveAuthPrompt.cs:70–107](../src/Winnow.Core/Auth/IInteractiveAuthPrompt.cs#L70).

**Impact.** A provider subpage, social-login page, iframe, or compromised navigation can submit an
attacker-controlled authorization code. That creates login-CSRF/account-confusion risk.

**Remediation.** Generate a cryptographically random state per attempt, send it to Epic, and require
constant-time equality before spending a code. Allowlist exact HTTPS origins per capture strategy,
validate `e.Source`, enable the bridge only on approved pages, reject unapproved navigation/popup
destinations, and match redirect scheme/host/port/path. Add origin, iframe, popup, wrong-port, and
wrong-state tests through a testable navigation/message seam.

### F06 — Truncated soft-match sweeps permanently starve the same tail (P1)

**Evidence.** The ceiling is described as rerunnable at
[LibrarySoftMatchSweep.cs:27](../src/Winnow.Resolve/LibrarySoftMatchSweep.cs#L27), but the algorithm
always stops at the same deterministic prefix at
[lines 245–269](../src/Winnow.Resolve/LibrarySoftMatchSweep.cs#L245). It records a completion time
even when truncated at [line 111](../src/Winnow.Resolve/LibrarySoftMatchSweep.cs#L111).

**Impact.** Pairs after the cap are never examined, no matter how many launches occur, while the UI
can claim the sweep completed.

**Remediation.** Persist a block/pair cursor, rotate the deterministic start, or process every
partition in bounded transactions. Do not stamp full completion while truncated. Extend the cap
test to prove a later run reaches previously omitted pairs.

### F07 — Pending soft matches survive when metadata makes them ineligible (P1)

**Evidence.** Only pairs produced by the current blocking pass reach the resolver at
[LibrarySoftMatchSweep.cs:104](../src/Winnow.Resolve/LibrarySoftMatchSweep.cs#L104). Withdrawal occurs
only while rescoring a submitted pair at
[SoftMatchResolver.cs:135](../src/Winnow.Resolve/SoftMatchResolver.cs#L135).

**Impact.** If enrichment changes a title/blocking key or classifies a release as a non-game, an old
pending question can remain forever because it is never submitted for withdrawal.

**Remediation.** Load all pending pairs and reconcile them against the current admitted release set
and current proposal set. Withdraw/delete only pending proposals; preserve terminal user decisions.

### F08 — Soft matching can run 250,000 SQL lookups inside one writer transaction (P1)

**Evidence.** `SoftMatchResolver` opens a transaction at
[line 104](../src/Winnow.Resolve/SoftMatchResolver.cs#L104), but performs a database lookup for each
remaining pair at [line 184](../src/Winnow.Resolve/SoftMatchResolver.cs#L184). The configured ceiling
is 250,000 comparisons.

**Impact.** One background sweep can hold SQLite's writer while issuing hundreds of thousands of
round trips, delaying acknowledgements, journal notes, feed feedback, and session writes.

**Remediation.** Preload every candidate status into a canonical pair dictionary or stage proposed
pairs in a temp table and join once. Batch writes in bounded transactions. Add query-count and
duration budgets against a realistic 1,000–3,000 release fixture.

### F09 — “Same game” records intent but never merges identity (P1)

**Evidence.** The command at [MergeQueueViewModel.cs:121](../src/Winnow.App/ViewModels/MergeQueueViewModel.cs#L121)
is presented as “Same game,” while [lines 195–199](../src/Winnow.App/ViewModels/MergeQueueViewModel.cs#L195)
explicitly only set the candidate status.

**Impact.** Confirmed duplicates remain separate Works/Releases, so unified cross-store identity—the
core product promise—does not change. This debt is acknowledged in `ROADMAP.md`, but the UI wording
still reads as an executed operation.

**Remediation.** Until merge execution exists, label the action as a recorded decision and surface
its pending-application state. Then implement a dedicated transactional merge service that repoints
external IDs, ownerships, updates, facets, lists, feedback, and achievements; resolves same-store
ownership collisions without losing history; preserves distinct editions; and removes only the
redundant identity rows. Add rollback and collision-heavy tests.

### F10 — Candidate coalescing can manufacture a play observation (P1)

**Evidence.** [CandidateOwnershipMerge.cs:20](../src/Winnow.Core/Ingest/CandidateOwnershipMerge.cs#L20)
does not include `AccountRef` in the merge key. Its merge independently chooses maximum playtime,
latest last-played, first account, observation time, and a source associated with only part of that
selection at [lines 50–72](../src/Winnow.Core/Ingest/CandidateOwnershipMerge.cs#L50).

**Impact.** The stored record can combine account A's minutes, account B's date, source A, and source
B's observation time—a fact no source reported. Buckets and longitudinal analysis then trust fiction.

**Remediation.** Select one coherent winning play tuple and only fill fields that are genuinely
missing. Merge install state separately. If multiple accounts are supported, include account in
ownership identity or model account-level observations explicitly.

### F11 — Update polling can starve work behind persistent failures (P1)

**Evidence.** Due rows are ordered by successful `LastPolledAt` and capped at
[UpdateSignalPoller.cs:58–72](../src/Winnow.Enrich.Updates/UpdateSignalPoller.cs#L58). An unavailable
news request returns without recording an attempt at
[lines 117–124](../src/Winnow.Enrich.Updates/UpdateSignalPoller.cs#L117).

**Impact.** The same first failing batch remains oldest forever; healthy releases behind it may never
be polled.

**Remediation.** Persist `last_attempt_at`, failure count, and bounded `next_attempt_at` separately
from last success. Schedule fairly with backoff and prove with a test containing more than one batch
where the first batch repeatedly fails.

### F12 — Raw build history is not polled independently (P1)

**Evidence.** The poller always fetches news first at
[UpdateSignalPoller.cs:101–124](../src/Winnow.Enrich.Updates/UpdateSignalPoller.cs#L101), and only
fetches build info after new/recent news or an active watch at
[lines 142–211](../src/Winnow.Enrich.Updates/UpdateSignalPoller.cs#L142).

**Impact.** Releases with no feed, no items, or unchanged news never collect raw build pushes. This
violates the requirement to retain two independent signals and prevents later heuristic retuning.

**Remediation.** Add a lower-frequency independent build baseline/poll, preferably local SteamCMD
with mirror fallback. Store each changed raw build event, and correlate only in queries. Add
no-news/unchanged-news build-change tests.

### F13 — Startup/library refresh performs UI-thread N+1 database work (P1)

**Evidence.** After the synchronous pre-window sync, `MainWindow.OnOpened` serially loads surfaces at
[MainWindow.axaml.cs:244–281](../src/Winnow.App/Views/MainWindow.axaml.cs#L244).
`LibraryViewModel` then awaits release and external-ID queries per work at
[lines 580–621](../src/Winnow.App/ViewModels/LibraryViewModel.cs#L580), on the captured UI context.
The enrichment refresh invokes the load on the dispatcher at
[Program.cs:219–229](../src/Winnow.App/Program.cs#L219).

**Impact.** The 1,000-game target pays roughly 2N database round trips and can freeze the shell during
initialization and refresh.

**Remediation.** Add one bulk library read-model query covering work, releases, IDs, ownerships,
buckets, and facets. Execute I/O off the dispatcher, build an immutable projection, then publish it
atomically. Version/cancel superseded loads. Add a realistic query-count/performance test.

### F14 — Mutable cover state is shared across wall, feed, and recycled views (P1)

**Evidence.** `FeedCardViewModel` borrows a library tile at
[lines 33–37](../src/Winnow.App/ViewModels/FeedCardViewModel.cs#L33). `GameTileViewModel` owns a single
cover-wanted flag and image pair and applies fire-and-forget results without generation/size checks at
[lines 297–343](../src/Winnow.App/ViewModels/GameTileViewModel.cs#L297). Recycling can clear that
state at [lines 319–324](../src/Winnow.App/ViewModels/GameTileViewModel.cs#L319).

**Impact.** Recycling a wall tile can blank a visible feed image; a small feed request can overwrite
larger wall art; stale requests can win after realization changes.

**Remediation.** Make cover state view-local through reference-counted `CoverLease` objects keyed by
cover and size bucket, with generation/cancellation. Keep identity in the shared tile model, not
mutable presentation state. Add a two-consumer recycling/size race test.

### F15 — Missing update coverage is treated as proof of no updates (P1)

**Evidence.** [RecommendationScorer.cs:112–123](../src/Winnow.Recommend/RecommendationScorer.cs#L112)
penalizes an old bounced title outside the stale bucket and explains that nothing major changed.
The design record states build coverage begins when polling begins and requires “No updates
recorded,” not proof that none shipped.

**Impact.** Incomplete observation suppresses potentially good recommendations and emits a false,
categorical explanation.

**Remediation.** Persist/query coverage intervals for successful dual-signal observation. Apply
negative evidence only when coverage proves it; otherwise omit the penalty or explicitly say Winnow
has not recorded an update.

### F16 — Feed impressions are stored before cards are actually shown (P1)

**Evidence.** [FeedService.cs:245–268](../src/Winnow.App/Services/FeedService.cs#L245) records every
engine result immediately. `FeedViewModel` later drops candidates without matching tiles at
[lines 178–190](../src/Winnow.App/ViewModels/FeedViewModel.cs#L178), and a navigation/load race can
abandon the result.

**Impact.** Rotation and endorsement learn from recommendations the user never saw.

**Remediation.** Make scoring side-effect free. Record an idempotent impression only after the UI
maps and accepts the card set, or after actual realization if impression semantics require it.

### F17 — Journal notes can close before the write succeeds (P1)

**Evidence.** [JournalPromptViewModel.cs:152–164](../src/Winnow.App/ViewModels/JournalPromptViewModel.cs#L152)
starts `SaveAsync`, closes immediately, and exposes no error/retry state. The task is retained only
for tests at [lines 97–98](../src/Winnow.App/ViewModels/JournalPromptViewModel.cs#L97).

**Impact.** User-authored notes can disappear on database failure or process exit with no visible
indication.

**Remediation.** Use an awaited async command, keep the prompt open while busy, show retryable failure,
and drain tracked journal writes during host shutdown. Add write-failure and immediate-shutdown tests.

### F18 — Logical repository batches are not atomic (P2)

**Evidence.** Facet replacement reads/deletes/upserts without opening a transaction at
[FacetRepository.cs:144–179](../src/Winnow.Data/Repositories/FacetRepository.cs#L144). List positions
are rewritten row-by-row at [GameListRepository.cs:154](../src/Winnow.Data/Repositories/GameListRepository.cs#L154),
and feed surfacings are recorded row-by-row at
[FeedFeedbackRepository.cs:84](../src/Winnow.Data/Repositories/FeedFeedbackRepository.cs#L84).

**Impact.** Cancellation, a constraint failure, or concurrent writes can leave partial assignments,
orders, or history.

**Remediation.** Add a helper that opens a local unit of work when no ambient one exists, then wrap
each logical batch. Add fault injection after deletion and mid-insert.

### F19 — Out-of-order observations are not idempotent (P2)

**Evidence.** `ExternalIdResolver` compares only with the newest play record at
[lines 217–232](../src/Winnow.Resolve/ExternalIdResolver.cs#L217) and snapshot at
[lines 239–250](../src/Winnow.Resolve/ExternalIdResolver.cs#L239). Older replays remain non-latest and
are appended repeatedly. The schema has no natural observation uniqueness constraint.

**Impact.** Imports, delayed sources, and cache replay can grow duplicate history indefinitely.

**Remediation.** Define observation identity (ownership/source/observed time plus any needed sequence)
and reject exact replays. Test older-after-newer replay.

### F20 — Merge-candidate canonicality is enforced only by callers (P2)

**Evidence.** [0001_initial.sql:151–159](../src/Winnow.Data/Migrations/0001_initial.sql#L151) permits
self-pairs and mirror duplicates; the unique constraint protects only literal orientation.

**Impact.** Future/import/concurrent writers can create `(A,B)`, `(B,A)`, or `(A,A)`, duplicating or
invalidating human review.

**Remediation.** Add an append-only migration with `CHECK(left_release_id < right_release_id)` and a
canonical unique key, cleaning mirrors without discarding terminal decisions.

### F21 — Bucket threshold invariants are not validated (P2)

**Evidence.** [Buckets.cs:40](../src/Winnow.Core/Queries/Buckets.cs#L40) accepts negative windows and
contradictory floors without validation.

**Impact.** Misconfiguration silently produces nonsensical classifications.

**Remediation.** Validate `0 <= bounced < retired`, non-negative windows, and reasonable upper bounds;
add invalid-configuration tests.

### F22 — Release matching uses a Work-level year (P2)

**Evidence.** [ReleaseIdentity.cs:23](../src/Winnow.Core/Queries/ReleaseIdentity.cs#L23) supplies
`works.first_release_year`; `Release` has no release-specific date/year.

**Impact.** Remasters and editions inherit the original Work's year, weakening the ±1-year signal
exactly where edition identity matters.

**Remediation.** Add a release date/year in a new migration and populate it from version/store data.
Keep the Work's first-release year as a separate conceptual fact.

### F23 — The `Dead` bucket has no implementation (P2)

**Evidence.** [Buckets.cs:7](../src/Winnow.Core/Queries/Buckets.cs#L7) and the query CASE at
[LibraryQueryRepository.cs:192](../src/Winnow.Data/Repositories/LibraryQueryRepository.cs#L192) omit
`Dead`; the schema has no provider viability, delisting, or launch-failure events.

**Impact.** Unlaunchable titles remain in actionable recommendation piles.

**Remediation.** Store raw viability/delisting/launch-failure facts, then derive `dead` in the bucket
query with explicit precedence. Do not store a bucket column.

### F24 — Achievement support stops at schema creation (P2)

**Evidence.** The schema correctly keys achievements per release at
[0001_initial.sql:95–112](../src/Winnow.Data/Migrations/0001_initial.sql#L95), but there are no domain
records, repositories, projections, or rule-specific tests.

**Impact.** The feature is not executable, and no API/test prevents a future blended percentage.

**Remediation.** Add per-release repository/query surfaces and a two-release 100%/30% fixture that
asserts two separate rows and no aggregate percentage.

### F25 — Steam paths can escape the library root; one bad root can abort startup (P2)

**Evidence.** `CollectManifests` performs full-path conversion and enumeration without per-root
exception isolation at [SteamLibrarySource.cs:208–246](../src/Winnow.Ingest.Steam/SteamLibrarySource.cs#L208).
Raw `installdir` is combined directly at [lines 119–125](../src/Winnow.Ingest.Steam/SteamLibrarySource.cs#L119).

**Impact.** An offline drive or malformed path can abort the whole startup scan. Rooted or traversing
manifest input can store an arbitrary directory that the process monitor later scans.

**Remediation.** Canonicalize root and candidate, reject rooted/traversing values, and require the
candidate to equal or remain beneath the root. Isolate IO/authorization/argument failures per library
and each storefront scan. Add malicious manifest and unavailable-drive tests.

### F26 — Cover negatives do not reopen when capability changes (P2)

**Evidence.** [CoverPipeline.cs:52–76](../src/Winnow.Covers/CoverPipeline.cs#L52) returns from an
in-memory missing set without comparing the current source-set identity; misses store only a boolean
at [lines 107–112](../src/Winnow.Covers/CoverPipeline.cs#L107).

**Impact.** Adding IGDB credentials during a session does not retry previously missing covers until
restart.

**Remediation.** Store `CoverKey -> sourceSetId`, compare it on every lookup, and evict when capability
changes. Test the same pipeline instance across a configuration change.

### F27 — Shared cover work inherits the first caller's cancellation (P2)

**Evidence.** [CoverCache.cs:52–69](../src/Winnow.Covers/CoverCache.cs#L52) places
`LoadAsync(..., firstCallerToken)` in `_inFlight`; the pipeline turns cancellation into `null` at
[CoverPipeline.cs:119–122](../src/Winnow.Covers/CoverPipeline.cs#L119).

**Impact.** One recycled/scrolling tile can cancel the fetch for every waiter and make cancellation
look like a missing cover.

**Remediation.** Run shared work with a cache-lifetime token and let each waiter use
`shared.WaitAsync(callerToken)`. Propagate cancellation rather than negative-caching it. Test two
waiters where only the first cancels.

### F28 — Cover responses and decoded dimensions are unbounded (P2)

**Evidence.** Steam and IGDB sources buffer complete response bodies at
[SteamCapsuleSource.cs:48–75](../src/Winnow.Covers/SteamCapsuleSource.cs#L48) and
[IgdbCoverSource.cs:143–159](../src/Winnow.Covers.Igdb/IgdbCoverSource.cs#L143), then decode
server-controlled image dimensions.

**Impact.** A malformed/compromised CDN response can cause high memory use, decompression bombs, and
unbounded disk growth.

**Remediation.** Use `ResponseHeadersRead`, validate content type/length, enforce a streaming byte
cap, inspect dimensions before full decode, cap total pixels, and reject over-limit responses without
marking a durable negative.

### F29 — Metadata cache lacks payload version/integrity (P2)

**Evidence.** IGDB deserializes cached payloads without version/recovery at
[IgdbClient.cs:153–165](../src/Winnow.Enrich.Igdb/IgdbClient.cs#L153) and
[256–265](../src/Winnow.Enrich.Igdb/IgdbClient.cs#L256). GamesDB does likewise at
[GamesDbClient.cs:64–73](../src/Winnow.Enrich.GamesDb/GamesDbClient.cs#L64). The roadmap already
records the missing IGDB payload version.

**Impact.** One corrupt or old cache row can break enrichment every launch for the full TTL.

**Remediation.** Add a versioned cache envelope/key namespace, validate shapes, catch deserialization
errors, invalidate only the bad row, and refetch. Test corrupt and prior-version rows.

### F30 — Session collection is Windows-only (P2)

**Evidence.** [GameExecutableIndexBuilder.cs:193–221](../src/Winnow.Monitor/GameExecutableIndexBuilder.cs#L193)
searches only `*.exe` and [lines 256–268](../src/Winnow.Monitor/GameExecutableIndexBuilder.cs#L256)
warn that off-Windows no sessions will be recorded. README now admits this, but the architecture and
core product loop are still framed as cross-platform.

**Impact.** The launch → session data → better feed flywheel does not function on Linux/macOS.

**Remediation.** Introduce a platform attribution abstraction. On Linux use `/proc/<pid>/environ` and
`STEAM_COMPAT_DATA_PATH` for Proton plus pidfd exit tracking. Gate and label unsupported platforms
until implemented; add platform fixtures/integration tests.

### F31 — Epic owned-library cache is process-only (P2)

**Evidence.** [ServiceCollectionExtensions.Web.cs:64–67](../src/Winnow.Ingest.Epic/Web/ServiceCollectionExtensions.Web.cs#L64)
registers `InMemoryEpicLibraryCache`; only the catalog cache is replaced persistently in the App.

**Impact.** Every signed-in restart requires network and has no stale cross-process ownership fallback,
amplifying F04.

**Remediation.** Add a privacy-reviewed persistent cache that excludes account/token data and use
stale-while-revalidate in the background.

### F32 — Recommendation shortlist pruning is not score-bound safe (P2)

**Evidence.** [RecommendationEngine.cs:50–89](../src/Winnow.Recommend/RecommendationEngine.cs#L50)
probes history only for the preliminary top 3× candidates and assumes history can only add. Hidden
tried-to-like history can add enough to let an excluded candidate cross the final cutoff. Shelves
repeat the assumption at [lines 130–186](../src/Winnow.Recommend/RecommendationEngine.cs#L130).

**Impact.** Valid winners can never be considered, making rankings depend on an unsafe optimization.

**Remediation.** Bulk-fetch aggregates for all candidates, or retain every candidate whose
preliminary score plus maximum possible hidden bonus can beat the kth bound. Add an adversarial
outside-shortlist leapfrog test.

### F33 — Recommendation maturity tier uses a biased subset (P2)

**Evidence.** [RecommendationEngine.cs:169–179](../src/Winnow.Recommend/RecommendationEngine.cs#L169)
examines only 25 recent ownerships plus shelf-union candidates; tier logic at
[lines 417–429](../src/Winnow.Recommend/RecommendationEngine.cs#L417) treats that sample as global.

**Impact.** A user with many sessions distributed across more than 25 titles can remain in a lower
tier and receive the wrong model behavior.

**Remediation.** Query one global aggregate across all sessions/snapshots for tier; keep per-candidate
history separate. Test distributed sessions.

### F34 — Feed invalidation events are dropped during a load (P2)

**Evidence.** [FeedViewModel.cs:241–247](../src/Winnow.App/ViewModels/FeedViewModel.cs#L241)
ignores `TilesChanged` while `LoadCommand` is running and sets no pending/dirty flag.

**Impact.** Enrichment or bucket changes during scoring can leave cards stale indefinitely.

**Remediation.** Use a serialized coalescing reload loop (`dirty = true`, rerun until clean) or a
versioned latest-wins load with cancellation. Add an invalidation-during-load test.

### F35 — Accessibility names and hierarchy are incomplete (P2)

**Evidence.** No `AutomationProperties` usage exists. Glyph-only caption buttons at
[MainWindow.axaml:826–846](../src/Winnow.App/Views/MainWindow.axaml#L826) and rating dots at
[lines 1652–1667](../src/Winnow.App/Views/MainWindow.axaml#L1652) rely on tooltips. The complex feed
card is a `Button` at [FeedCardView.axaml:138–142](../src/Winnow.App/Views/FeedCardView.axaml#L138)
and nests Play/feedback/Undo buttons later in the same control.

**Impact.** Screen readers receive empty/ambiguous names and a confusing nested control tree.

**Remediation.** Use a non-button card container with a dedicated open-details peer. Add explicit
automation names/help text for cards, glyphs, badges, and ratings. Add automation-tree smoke tests.

### F36 — Startup initialization has no error boundary (P2)

**Evidence.** [MainWindow.axaml.cs:244–281](../src/Winnow.App/Views/MainWindow.axaml.cs#L244) is an
unguarded `async void OnOpened`; `LibraryViewModel.LoadAsync` has no result/error boundary.

**Impact.** A corrupt DB or transient IO failure can become an unhandled dispatcher exception rather
than a visible, diagnosable retry state.

**Remediation.** Add an initialization coordinator with cancellation, structured per-surface results,
logging, and visible retry. Keep event handlers as thin await-and-report adapters.

### F37 — Recommendation explanations violate the one-sentence contract (P2)

**Evidence.** [ReasonBuilder.cs:23–60](../src/Winnow.Recommend/ReasonBuilder.cs#L23) concatenates a
lead, secondary reason, probably-done statement, and mode mismatch into as many as four sentences.

**Impact.** Cards become prose-heavy and obscure the primary reason; the output violates the module's
explicit explainability constraint.

**Remediation.** Return structured primary/secondary signals and render one bounded sentence, with
details on demand. Assert sentence and character budgets.

### F38 — Duplicate ownerships consume capacity before Work collapse (P2)

**Evidence.** The flat feed collapses after scoring at
[RecommendationEngine.cs:92–99](../src/Winnow.Recommend/RecommendationEngine.cs#L92); shelf union
deduplicates by ownership at [lines 145–165](../src/Winnow.Recommend/RecommendationEngine.cs#L145).

**Impact.** A twice-owned game can crowd out distinct Works and underfill shelves/feed.

**Remediation.** Collapse by Work before shortlist probing using a safe best-upper-bound ownership,
while retaining store-choice and bought-twice signals.

### F39 — Multiple app instances can duplicate background facts (P2)

**Evidence.** `Program.Main` starts hosted snapshot and session services without a per-user
single-instance lease at [Program.cs:102–145](../src/Winnow.App/Program.cs#L102). The sessions schema
has no uniqueness constraint at [0001_initial.sql:76–85](../src/Winnow.Data/Migrations/0001_initial.sql#L76),
and [SessionRepository.cs:23–32](../src/Winnow.Data/Repositories/SessionRepository.cs#L23) inserts
unconditionally.

**Impact.** Two Winnow processes can independently watch the same game, write duplicate sessions,
poll APIs twice, and compete for SQLite writes.

**Remediation.** Acquire a named per-user mutex/file lease before starting hosted services and route a
second launch to the first instance. Also add a defensible session observation identity where
possible so crashes/retries remain idempotent.

### F40 — Steam/IGDB secrets are plaintext while README claims encryption (P2)

**Evidence.** Steam API keys and IGDB client secrets are read directly from the generic `settings`
table at [SteamApiKeySources.cs:16–28](../src/Winnow.Enrich.SteamWeb/Credentials/SteamApiKeySources.cs#L16)
and [CredentialSources.cs:11–29](../src/Winnow.Enrich.Igdb/Credentials/CredentialSources.cs#L11).
The repository stores settings as plain text at
[SettingsRepository.cs:34–46](../src/Winnow.Data/Repositories/SettingsRepository.cs#L34). README
states without qualification that credentials are encrypted with DPAPI at
[README.md:73–75](../README.md#L73).

**Impact.** Anyone who obtains `winnow.db` gets reusable API credentials; documentation gives users a
false security expectation. Epic refresh tokens are protected correctly, but the statement is broader.

**Remediation.** Route all stored secrets through a platform secret-protector/keychain abstraction,
refusing persistence when secure storage is unavailable. Migrate existing plaintext rows on first
read and delete the old values only after verification. Until then, correct README to distinguish
Epic token protection from plaintext developer/user keys.

### F41 — The specified rolling diagnostic log is absent (P2)

**Evidence.** The design requires Serilog with a rolling file sink. `Program` uses the default generic
host and only creates a temporary console logger for migration at
[lines 56–90](../src/Winnow.App/Program.cs#L56); the app project has no Serilog/file-sink dependency.
As a `WinExe`, production console output is generally unavailable.

**Impact.** The many intentionally soft-failed ingest/enrichment paths are effectively undiagnosable
after the fact.

**Remediation.** Add structured rolling logs beneath the data directory, retention/size caps, secret
redaction tests, and a UI action to open/export diagnostics. Preserve the existing redacting HTTP
loggers.

### F42 — The sole database is migrated without an automatic recovery copy (P2)

**Evidence.** [DatabaseInitializer.cs:28–49](../src/Winnow.Data/DatabaseInitializer.cs#L28) applies
DbUp scripts directly to the live database, transaction-per-script, with no `quick_check` or backup.

**Impact.** A bad migration, disk fault, or application bug can strand the only copy of the product's
core asset. Per-script transactions prevent partial scripts, not bad committed transformations.

**Remediation.** Before a schema version changes, checkpoint WAL, run `quick_check`, create a bounded
SQLite online backup with the previous schema version in its name, and retain a small rotation.
Validate the upgraded DB before pruning. Add restore-path documentation and tests.

### F43 — No CI gate exists despite the compile regression (P2)

**Evidence.** The repository contains no CI workflow/configuration, and the current workspace reached
a state where production projects compile but `Winnow.Tests` does not.

**Impact.** The declared `dotnet build`/`dotnet test` contract is not continuously enforced. Platform
and migration regressions can land unnoticed.

**Remediation.** Add CI for Windows and at least compile/test on Linux, running restore, build,
tests, formatting/analyzers, migration-hash verification, and a dependency advisory check. Protect
the main branch on those checks. Pin the .NET SDK with `global.json`; consider NuGet lock files for
reproducible restores.

### F44 — Storefront parser inputs have weak size/depth bounds (P3)

**Evidence.** KeyValues and JSON readers parse storefront-owned files without explicit byte/depth
ceilings, for example [KeyValues1.cs:26–46](../src/Winnow.Ingest.Steam/KeyValues1.cs#L26) and
[GogGameInfoReader.cs:73–109](../src/Winnow.Ingest.Gog/GogGameInfoReader.cs#L73).

**Impact.** Torn, hostile, or unexpectedly huge files can spike startup CPU/RAM.

**Remediation.** Apply format-specific file-size ceilings, bounded streams, JSON depth limits, and one
redacted diagnostic per rejected input. Add oversize/deep fixtures.

### F45 — `DateTimeKind.Unspecified` is silently relabelled UTC (P3)

**Evidence.** [DapperConfig.cs:37–45](../src/Winnow.Data/DapperConfig.cs#L37) converts Local values but
writes Unspecified unchanged, then labels parsed values UTC.

**Impact.** A local unspecified time can be stored as the wrong instant while looking valid.

**Remediation.** Reject Unspecified at persistence boundaries or standardize domain timestamps on
`DateTimeOffset`. Test Local/UTC/Unspecified round trips.

### F46 — Append-only migrations have no immutable hash verification (P3)

**Evidence.** DbUp journals resource names at
[DatabaseInitializer.cs:34–43](../src/Winnow.Data/DatabaseInitializer.cs#L34). Migration tests derive
their expected set from current resources, so editing a shipped script changes fresh databases while
upgraded databases skip the changed content.

**Impact.** Fresh and upgraded installations can silently diverge.

**Remediation.** Commit a migration hash manifest and verify it in CI. Add checked-in historical DB
upgrade fixtures rather than relying primarily on rewinding the current schema.

### F47 — Reduced-motion coverage is incomplete (P3)

**Evidence.** `GameTileView` keeps hover/shadow/placeholder transitions while reduced-motion styles
cover only some art/flip/badge paths; the scrim has local transitions at
[GameTileView.axaml:301–307](../src/Winnow.App/Views/GameTileView.axaml#L301). Feed card background
animation is unconditional at [FeedCardView.axaml:30–39](../src/Winnow.App/Views/FeedCardView.axaml#L30).

**Impact.** Users requesting reduced motion still receive nonessential animation.

**Remediation.** Apply a root reduced-motion class that disables every nonessential transition,
remove locally overriding transitions, and test computed transition sets.

### F48 — Unread-update accessible copy omits counts (P3)

**Evidence.** The list marker always says “Patched since you played” at
[MainWindow.axaml:1533–1544](../src/Winnow.App/Views/MainWindow.axaml#L1533); tile/feed dots do not
carry the specified singular/plural update count.

**Impact.** Pointer and assistive-technology users lose information the design system promises.

**Remediation.** Include unread count in the tile projection and expose exact pluralized tooltip and
automation help text.

### F49 — Sync naming/comments materially misdescribe behavior (P3)

**Evidence.** `SteamSyncService` ingests three stores and two network APIs, yet its type/docs claim a
local Steam/filesystem-only path at [SteamSyncService.cs:13–34](../src/Winnow.App/Services/SteamSyncService.cs#L13).
`SnapshotSchedulerService` repeats the no-network claim at
[lines 20–24](../src/Winnow.App/Services/SnapshotSchedulerService.cs#L20).

**Impact.** Maintainers make scheduling/startup decisions from false contracts; F04 is already the
result.

**Remediation.** Split responsibilities first, then name interfaces after actual guarantees
(`ILocalLibrarySync`, `IRemoteOwnershipSync`). Add architecture tests on project references and
small contract tests that prove the local sync never resolves network clients.

### F50 — Dormancy brightness has conflicting sources of truth (P2)

**Evidence.** The repository instructions describe the deepest dormancy stop as `0.60`, while the
amended design system and implementation use `0.68` in
[Dormancy.cs](../src/Winnow.App/Services/Dormancy.cs),
[CoverCacheOptions.cs](../src/Winnow.Covers/CoverCacheOptions.cs), and
[tokens.axaml](../src/Winnow.App/Themes/tokens.axaml).

**Impact.** The running application is internally consistent, but two governing documents disagree.
A future maintenance pass can reasonably treat either number as authoritative and introduce a visual
regression.

**Remediation.** Choose the intended value through visual review, update every authority document to
match, and add an invariant test asserting that UI, cover processing, and token values share the same
ramp. If `0.68` is the accepted amendment, update `AGENTS.md` explicitly rather than relying on
precedence knowledge.

## Architecture assessment

### What is structurally sound

- The four-layer Work → Release → Ownership → PlayRecord model remains distinct, with foreign keys
  directed correctly.
- `Winnow.Core` is BCL-only; `Winnow.Resolve` depends on Core abstractions rather than Data.
- Ingest sources emit `CandidateOwnership` and do not write Works/Releases directly.
- Fuzzy matching queues human review and has no structural auto-merge path.
- Derived buckets remain SQL queries with explicit precedence and tunable thresholds.
- Achievements are at least schematically per-release rather than blended.
- External-ID hard joins are globally unique by provider/id.
- External resolution has rollback-by-default unit-of-work semantics.
- SQLite foreign keys and WAL are enabled on opened connections.
- DbUp uses embedded, versioned scripts and transaction-per-script.
- HTTP integrations use typed `HttpClient`, shared Polly retry/rate limiting, canned test handlers,
  and explicit request redaction. Steam's required owned-game flags are present.
- IGDB uses text/plain Apicalypse, shared 4 rps limiting, persistent long-lived tokens, and one 401
  refresh.
- Epic refresh tokens use DPAPI CurrentUser with no plaintext fallback.
- Steam parsing uses ValveKeyValue; storefront readers are read-only.
- Raw update bucket semantics require both signals, even though collection coverage is incomplete.
- The recommendation engine stores no scores as truth, exposes contribution breakdowns, has cold-start
  shelves, and supports reversible feedback.
- The custom `CoverWall` virtualizes with closed-form geometry instead of reintroducing the known
  ItemsRepeater defect.
- Flare usage remains confined to unread-update meaning/theme preview; bundled fonts and numeric
  `tnum` styles are present.
- Keyboard grid navigation, modal focus cycling, display-sized cover requests, and placeholder art
  are substantive rather than aspirational.

### Architectural pressure points

The code is a modular monolith with 17 production projects and a single 550-line composition root.
That is defensible, but boundaries are currently enforced mostly by comments and constructor shape.
The most important next architectural move is not more projects: it is explicit read models and job
boundaries. In particular:

1. Separate local discovery, remote ownership, enrichment, update polling, and UI projection into
   independently scheduled jobs with observable state.
2. Add bulk read-model repositories so UI and recommendation code do not synthesize views through
   thousands of calls.
3. Treat user events (notes, dismissals, impressions, launches) as durable writes with explicit
   acknowledgement, not fire-and-forget UI effects.
4. Add platform capability contracts instead of compiling Windows-shaped implementations everywhere
   and discovering their absence at runtime.
5. Put data safety—single instance, backup, staged rename, cache versioning—around the local database,
   because it is the product's only durable asset.

## Verification performed

| Check | Result |
|---|---|
| `dotnet build Winnow.slnx` | **Failed**: two `CS1061` errors for missing `ExcludedWithdrawn` |
| `dotnet test tests/Winnow.Recommend.Tests/... --no-restore` | Passed 74/74 |
| `dotnet test tests/Winnow.Covers.Tests/... --no-restore` | Passed 65/65 |
| Main `Winnow.Tests` | Could not run because the project does not compile |
| NuGet advisory audit, direct + transitive | No known vulnerable packages from nuget.org |
| Storefront write scan | No Steam/Epic/GOG write path found |
| Secret/logging review | HTTP redaction is strong; plaintext settings/documentation issue remains |

The test suite is broad, especially around parser fixtures, HTTP contracts, bucket boundaries,
recommender scoring, cover cache behavior, migrations, and compatibility shims. The most valuable
missing tests are adversarial state-transition tests: interrupted data moves, same-process capability
changes, two-consumer cancellation, invalidation during loads, impression timing, journal write
failure, large-library query budgets, and cross-platform/session behavior.

## Recommended remediation sequence

### Phase 0 — Stop-the-line fixes

1. Fix F01 and restore a green full build/test run.
2. Fix F02 before any renamed build touches user data; add pre-upgrade backup from F42 in the same
   work package.
3. Fix F03 with conflict tests.
4. Correct README's credential claim immediately, even if secret-store work follows later.

### Phase 1 — Trust and startup

1. Split sync jobs (F04/F49), show the shell first, and replace library N+1 reads (F13).
2. Harden OAuth (F05).
3. Add rolling diagnostics, startup error states, and single-instance enforcement (F36/F39/F41).
4. Make journal writes acknowledged (F17).

### Phase 2 — Correctness of the differentiators

1. Make soft matching resumable, reconciling, and batched (F06–F08), then build real merge execution
   (F09).
2. Preserve coherent play observations and idempotency (F10/F19).
3. Make update collection fair and independent (F11/F12), then fix recommendation coverage semantics
   (F15).
4. Correct impression timing, shortlist bounds, tier aggregation, and Work collapse (F16/F32/F33/F38).

### Phase 3 — Reliability and platform completion

Address transactional batches, path containment, cover/cache hardening, accessibility, Linux session
attribution, CI/reproducibility, and the remaining P2/P3 items. Each should land with the acceptance
test named in its finding; otherwise the repository will retain excellent prose but no executable
guarantee.
