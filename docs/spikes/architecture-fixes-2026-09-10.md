# Architecture review correction checks

This records verification of the corrections to the September architecture review.
Backlog TASK-227 owns delivery status; the original review remains the finding record.
All checks below use temporary databases or isolated build/publish directories. No
launcher files, real library data, accounts or installed application were modified.

## Consolidated software gate

The final Windows Release solution run passed all 5,285 runnable tests in one clean pass:

| Assembly | Passed | Skipped on Windows |
|---|---:|---:|
| Winnow.Tests | 4,402 | 0 |
| Winnow.Ui.Tests | 462 | 0 |
| Winnow.Recommend.Tests | 185 | 0 |
| Winnow.Covers.Tests | 159 | 0 |
| Winnow.Plugins.Tests | 35 | 0 |
| Winnow.Plugin.SteamGridDb.Tests | 42 | 0 |
| Winnow.Monitor.Linux.Tests | 0 | 2 |

The two platform-specific cases separately passed on actual Linux processes, as described
below. Fresh restore used `--force-evaluate --no-cache -warnaserror`; the solution build used
Release, analyzers and warnings as errors and completed with zero warnings/errors. The full
test pass used `--no-build`, the shared scratch output, TRX, and a five-minute hang diagnostic
timeout. Its results are under `artifacts/review-fixes/final-results-v2/`.

The preceding integrated run exposed four failures, all corrected before this complete pass:
aggregate plugin timeout escaping a shared input read; a newer verdict discarding a queued
feed backfill; an Activity assertion running before its asynchronous section load; and
fullscreen context disposal releasing a borrowed preview feed. Controlled deadline, backfill
and independent-model ownership tests now cover those paths. The earlier failed run remains
under `final-results/`; it is not counted as successful evidence.

All 38 migration hashes passed against review commit `a21753b`, including the six appended
migrations. Migration mutation tests, test-result-summary checks, release-version checks and
bundled-plugin mutation checks passed. `git diff --check` passed. The original unrelated
ApplicationSettingsView.axaml edit was present during the Windows workspace tests and remains
outside the review commits; it removes duplicate toggle content while retaining accessible names.

### Clean publish verification

Both self-contained publish targets succeeded from a clean `git archive` of implementation
commit `e84ba077504d96aa18a95f7b4018efd61e97da23`, using `packaging/Publish.ps1` and diagnostic
version `0.1.0-ci.227`. Windows used the normal ReadyToRun setting without an override; Linux
used its normal non-ReadyToRun configuration. Assembly version, commit identity, required
runtime files and absence of local secrets/databases passed the script's checks.

Each output contains the bundled SteamGridDB manifest, matching assembly and declared entry
type. Byte comparisons use the archived source manifest; comparing it with a separate CRLF
working-tree copy would correctly fail the byte-identity check. The clean outputs are under
`artifacts/review-fixes/publish/win-x64-final/` and `linux-x64-final/`. No installer was run and
these diagnostic builds were not released or installed over the user's app.

Implementation milestones are `bde7ca4` and `e84ba07` on `codex/architecture-review-fixes`.
The completion record and task dispositions follow in a documentation-only commit. All
TASK-189–226, TASK-135 and TASK-27 are Done; TASK-4 remains at ordinal 259000 with `needs-user`
for physical controller/TV verification. Its remaining criteria are not claimed by headless
tests. [The resolution record](../architecture-review-resolution-2026-09-11.md) maps every
finding to its correction and explains user-visible effects and explicitly deferred scope.

## Foundation checkpoint

The first checkpoint covers identity undo, atomic metadata writes, manual corrections,
credential-scoped Steam/Epic caches, incomplete Epic scans, coherent Galaxy snapshots,
update acknowledgement, schema refusal, migration checksums, recommendation evidence and
cold-start shelves, startup failure handling, shared prompts/year/note validation and
bundled-plugin packaging. TASK-192, TASK-193 and TASK-195 still require their explicit
Stores/action presentation checks; their backend regression coverage is recorded below.

### Integrated checks

Windows, .NET SDK 10.0.400 / runtime 10.0.11, Release configuration:

| Assembly | Passed | Skipped |
|---|---:|---:|
| Winnow.Tests | 4,102 | 0 |
| Winnow.Ui.Tests | 340 | 0 |
| Winnow.Recommend.Tests | 166 | 0 |
| Winnow.Covers.Tests | 153 | 0 |
| Winnow.Plugins.Tests | 35 | 0 |
| Winnow.Plugin.SteamGridDb.Tests | 42 | 0 |
| Winnow.Monitor.Linux.Tests | 0 | 2 |

The complete pass initially found three stale expectations: two announcement-count
assertions and legacy-brand prose rejected by repository naming enforcement. After the
corrections, all 46 LibraryViewModel tests and all eight naming-enforcement tests passed.
The table combines that focused recheck with the other results from the complete pass;
it is not a claim that the first run was clean. No product test failure remains at this
checkpoint. The solution build finished with zero warnings and zero errors.

The shared scratch output made `dotnet test` at solution scope build one assembly while
another testhost held the same dependency DLL. Building first and testing the affected
assembly with `--no-build` avoided that verification-only collision. Future full checks
should build first and then use `dotnet test --no-build` with the same output property.

Migration verification against review commit `a21753b` passed all 33 hashes. The checksum
mutation checks passed when consolidating the manifest; only migration 0033 was appended.
`git diff --check` passed.

### Focused evidence

- Identity, atomic metadata, manual corrections and read inventory: 217 focused tests;
  26 related headless cases, including eight new desktop/fullscreen correction cases.
- Steam credential/history/account seams: 391 tests. Epic caching and scan completeness:
  269 tests, including account changes and partial/failed filesystem reads.
- Galaxy snapshots: 50 tests using native SQLite writers and WAL rollover. A writer
  denied by the held read guard could not interleave a mixed copy; a later snapshot
  included the committed update. Non-Windows live-copy refusal was inspected in source,
  not executed on a Linux host.
- Update acknowledgement: 123 focused repository/model cases, seven actual production
  composition cases and the final 46-test library regression recheck.
- Recommendations: all 166 engine cases plus four production desktop/fullscreen cases
  proving cold shelf rendering, installed-sibling action choice and original-release
  impression/verdict/undo. Plugin candidate and feedback regressions also passed.
- Prompt parity: eight new headless cases and 58 existing list/feed regressions. Year
  parsing and shared journal editors have dedicated desktop/fullscreen tests; the
  integrated 340-test UI pass includes them.
- Startup: seven real child processes selected desktop/fullscreen via the persisted
  setting and used only temporary data. Bad configuration and unsupported schemas exit
  with code 3; an unusable explicit data directory exits with code 2. Reporter tests
  cover secret scrubbing and a logger that itself fails.
- Packaging: isolated self-contained Windows and Linux publishes both contained a
  matching SteamGridDB manifest, assembly and declared entry type. The verifier rejected
  a missing DLL, wrong assembly, altered manifest and nonexistent entry type. Local
  Windows verification disabled ReadyToRun; release CI retains its normal setting.

TRX outputs live under each test project's ignored `TestResults` directory. The complete
pass uses the `foundation` prefix; focused commands and outcomes also appear in the
corresponding Backlog tasks. No installer, live external API or physical gamepad/TV check
is inferred from these results. Linux process checks remain a separate required gate.

## IGDB mapping revision isolation — TASK-196

The focused Release run passed 281 tests, selected by `IgdbObservation`,
`MaturityEnrichment`, `Lifecycle`, `GameRefetch`, `FacetSync`, `WorkImageMetadataWriter`,
`WorkReception`, `Enrichment`, `IgdbMatch`, `IgdbAssignment`, `ReceptionImageRefresh` and
`IdentityReadInventory`. A separate Avalonia run passed all 18 cases selected by
`IgdbMappingRefresh`, `ManualEntryForm` and `DetailsRefreshParity`. Both used the shared
`artifacts/review-fixes/Invoke-Dotnet.ps1` helper and the isolated Release output directory.
Migration verification passed 34 hashes at that checkpoint; TASK-196 adds no migration.

Fourteen delayed-response cases pause fake providers after capturing the current mapping,
pin a different ID, optionally return to the original ID, clear the pin and then release
the response. Facets, maturity, reception, lifecycle, automatic enrichment, refetch and
assignment retain no stale writes. Repository fixtures inject failures or cancellation
inside callbacks, including an outer transaction that catches the exception and commits
unrelated work. A second writer attempting to pin during a paused callback waits until
that callback commits, then retires its IGDB projections. Four SQLite delete triggers prove
projection invalidation and mapping changes roll back together. Separate cases distinguish
successful empty ratings from unavailable responses, retain independent provider evidence,
and verify immediate polling/refetch eligibility after a mapping change.

On both rendered surfaces, an open details view loses the old screenshots and reception
figures, displays the corrected summary, and the corrected game stays visible with explicit
content disabled after its old IGDB maturity evidence is retired. These are headless
keyboard/controller presentation checks against temporary databases, not live provider or
physical display measurements.

## Account inventory evidence — TASK-198

A focused Release run passed 123 cases selected by `AccountScopeTests`,
`OwnershipInventory`, `SteamInventory`, `RemoteOwnershipSync`, `OwnedAccountConfirmation`,
`CrossJobPlaytime`, `SteamWebJson`, `SteamWebApiClient` and `IdentityReadInventory`.
Eight Avalonia cases selected by `AccountInventoryComposition` and
`RecommendationComposition` passed. Migration verification passed 37 hashes at this
checkpoint, including new 0034 and the parallel provider migrations. Both test commands
used the shared helper and isolated Release output path.

Parser/client cases distinguish an explicit matching count, explicit zero, missing counts,
partial arrays, duplicate/invalid IDs, fresh cache and stale fallback. Repository cases
verify account/store/source separation, original response time, games discovered after
that response, caller transaction rollback and rejection of an older completion after a
newer attempt. Remote composition uses real resolver/repositories and fake HTTP answers:
complete, empty, partial, stale, unanswered, exception, cancellation and a SQLite trigger
that fails resolution. No failed attempt publishes completeness. The confirmed account is
queried even when the reusable local scan contains no games.

The desktop and fullscreen composition tests seed two local play observations, select one
account and render the production feed. Incomplete evidence leaves both library tiles and
feed cards; complete inventory evidence narrows both. Starting a new attempt without
completing it restores conservative visibility and a zero hidden-game count. These tests
use sanitized accounts and temporary SQLite files; no live account or launcher is changed.

The final consumer check expanded to 164 passing unit tests by adding account membership,
plugin feed and account-page provenance coverage, and to 34 passing UI tests by adding
library refresh ordering and store account context. Older plugin/refresh fixtures now
establish explicit complete inventory evidence when they intend to test filtering.

## Optional feed publication and artwork foundations

TASK-212 returns built-in shelves before optional providers finish. Its 47 focused model,
provider and inventory tests passed, including cooperative and cancellation-ignoring
providers, time spent behind an existing invocation, and mixed fast/slow providers.
Six headless desktop/fullscreen cases passed for incremental focus, stable cards,
impression identity and production feed composition. The host uses a five-second aggregate
budget; it does not claim to terminate arbitrary in-process plugin code.

TASK-210, TASK-211 and TASK-225 passed all 157 cover tests, 149 selected cover/library/merge
model tests and 19 headless artwork cases. The latter include ten production composition
combinations across desktop/fullscreen and nine lifecycle/retry cases. Controlled gates
exercise 100 concurrent direct cache callers, 100 shared leases, 80 navigation requests
against a four-slot limit, last-consumer cancellation, and shutdown during both fetch and
bitmap conversion. An outstanding lease retains pixels after cache shutdown; the final
release disposes them exactly once. The production admission default is 128 running or
queued slots. Excess requests remain retryable placeholders; no FPS or real-library scroll
latency is inferred from these deterministic tests.

Clock-driven tests warm disk and memory negative caches, advance to their original expiry,
and fetch newly available art in the same pipeline. Null and cancelled loads retry under a
retained lease without duplicate concurrent loads. Actual desktop/fullscreen cover controls
recover after a transport failure, and detail cover requests retry at the same display width.
TRX files: `optional-feed-unit`, `optional-feed-ui`, `covers-lifetime`,
`cover-selection-models`, and `cover-lifetime-selection-ui` under ignored `TestResults`.

## Ownership refresh parity — TASK-204

Startup, six-hour scheduling and successful account actions now share OwnershipRefreshCoordinator. Its ordered LibraryRefreshPipeline publishes committed acquisitions before metadata, keeps independent steps running after a provider failure, and reuses the same IGDB subset for credential refresh. LibraryChangePublisher uses the existing desktop TilesChanged/fullscreen active-or-pending bridge; it adds no ingest-to-UI dependency. TASK-198 supplies confirmed remote-only Steam targets and conservative inventory cancellation.

Verification: 40 Release checks in ownership-refresh.trx cover phase ordering, partial commits/failures, publication retry, cancellation, serialization, credential changes and remote-only account inventory. Four production-composition headless cases in ownership-refresh-ui.trx insert an acquisition while the actual desktop/fullscreen surface stays open, hold metadata to observe early publication, then verify the enriched title after success or partial failure. They also verify deferred fullscreen refresh on entry. The scheduler and production publisher are real; remote responses and downstream metadata are canned, with no external calls.

## Durable monitored sittings — TASK-213

`SessionRestartTests` drives the real executable index, watcher and SQLite repositories with
sanitized fake processes and a controlled UTC clock. It disposes the watcher without a flush
to model a crash, or flushes before recreating it to model normal shutdown. The same OS
process identity is rediscovered through a fresh handle. Both paths preserve one row, start,
monitor key, launch attribution and attached note/rating, then complete that row after exit.
A child observed shortly before restart resumes its older sitting before the duration floor.
A missing process leaves the old end/duration null; the same PID with a newer OS creation
time opens a separate sitting. Failed recovery reads defer finalization. Responses lost after
checkpoint or completion commits retry without duplicates. Unchanged live polls make no
additional writes, and the 59/60-second boundary retains the existing debounce policy.

`MonitoredSessionRepositoryTests` checks exact ownership/PID/creation-time/name matching,
legacy/manual preservation, ambiguous-match refusal, concurrent independent writers,
original-start preservation, caller rollback, and rejection of another ownership's key.
SQLite ABORT triggers fail process-ledger insertion or retirement after earlier writes;
both standalone transactions and caller savepoints restore the entire prior state even if
the caller subsequently commits. A connection-local function cancels after inserting a
session and verifies that session, alias and process rows roll back together.

`SessionRecoveryParityTests` opens actual desktop activity/journal controls and the actual
fullscreen Activity page against a temporary database. A new repository instance recovers
the open sitting, then completion preserves its selected session ID and note. The desktop
plot adds one ten-minute bar only after completion; fullscreen replaces unknown-duration
copy with ten minutes on refresh. The aggregate used for recommendation maturity counts
one sitting throughout; a real RecommendationEngine with a two-session diagnostic tier
threshold stays Settling after watcher restart instead of being promoted by duplicate rows.

Release checks: 77 main tests passed (SessionWatcherTests, SessionRestartTests,
MonitoredSessionRepositoryTests, JournalPromptTests, ActivityTimeline and LibraryHistoryStats),
19 headless tests passed (SessionRecoveryParityTests, SessionNoteParityTests,
JournalDetailsInteractionTests and ActivityTrackerInteractionTests), and all four
MaturityTierTests passed. The Linux smoke assembly compiled and explicitly skipped its two
native-process tests on Windows; this is not evidence of native Linux execution. All runs
used `artifacts/review-fixes/Invoke-Dotnet.ps1` and the shared scratch BaseOutputPath.
`Verify-Migrations.ps1 -BaselineRef HEAD` verified 38 hashes, including migration0038.

Limitations are intentional: no legacy duplicates are guessed into a merge, no end time is
inferred while Winnow is absent, no below-floor crash observation is promoted, and a child
not observed before a crash cannot establish continuity. These tests use controlled process
identities and temporary data; they do not claim real-game or hardware smoke coverage.

## Shared dormancy endpoint — TASK-27

Winnow.Covers.DormancyStyle now defines the saturation, brightness and hue endpoint once. CoverCacheOptions, CoverImaging, the application ramp and procedural placeholders reference it. Avalonia token resources expose those same doubles through DormancyTokenExtension. The endpoint remains 0.22 / 0.68 / -6 degrees, so existing disk variants remain valid and appearance is preserved. The visual spec records why the former 0.60 gradient calibration was too dark on real capsules and requires a disk-variant version change with any future transform retune. TASK223 retires the older mock as a fidelity target.

Verification: 16 CoverImaging/dormancy checks pass in dormancy-rendering.trx, including per-channel equivalence of procedural and Skia transformations. The actual tokens resource dictionary resolves all three shared values in dormancy-token.trx. The combined UI run passed 43 other history/dormancy/refresh cases; its one test-only resource lookup failure was corrected by loading the merged token dictionary explicitly, then rechecked successfully. No rendering values changed.

## Active-document reconciliation — TASK-223

The review's statements were checked against current source before editing their owning
documents. This was static contract verification, not another rendering calibration.

| Topic | Source evidence | Resolution |
|---|---|---|
| Dormancy in desktop merge rows | MergeQueueView.axaml floor/vivid images and DormancyAlpha; MergeSideViewModel's CoverPresenter | Corrected design-system §14.7; kept opaque card and dormancy as separate concerns. |
| Fullscreen identity review | FullscreenIdentityPage in FullscreenLibraryToolsPage.cs renders text proposals and opens member details | Documented that presentation explicitly without promising desktop thumbnails. |
| IGDB cache versions | IgdbClient.GamePayloadVersion=5 and GetGamesAsync's envelope/legacy fallback paths | Corrected facet provenance, distinguished version-4 size measurements and version-5 fields, removed obsolete TTL-only/manual deletion advice. GamesDb's CachedRelease.Version is1, so the review's provider name was corrected. |
| Refresh scheduling | Program's LibraryRefreshPipeline steps and OwnershipRefreshCoordinator | Replaced once-per-launch claims with startup/scheduled/account/IGDB refresh behavior. |
| Identity delivery | IdentityLinkRepository.LinkAsync and MergeQueueViewModel.LinkAsync; TASK-70 Done and TASK-64's supersession record | Removed obsolete merge-execution and destructive-model roadmap debts. |
| Recommendation grouping | RecommendationGame.Build, RecommendationEngine.AssemblePoolAsync and ScoreBounds.CollapseByWork | Distinguished grouped evidence/action selection from the defensive upper-bound duplicate pass. |
| Steam collections | SteamLibrarySource constructor/Scan and complete ingest file inventory; Backlog search found no implementation task | Removed the current-input claim, annotated TASK-92's historical claim, and created DRAFT-1 as an explicitly deferred post-beta scope decision. |

The mock now has a retirement notice before its app DOM, a historical browser title and
links to the governing spec and tokens. Static checks confirmed the notice's placement and
that its local links exist. Codex and Claude Avalonia charter descriptions and instruction
bodies were compared after newline normalization and are identical. Searches found no
remaining active visual-target, version-4, TTL-only, once-per-launch or obsolete merge-debt
claim in the edited owning documents. Replaced wording is retained in docs/decisions.md.
Scoped git diff --check passed. No rendering constants changed; TASK-27 owns brightness.

## Large-history reads — TASK-222

The reproducible fixture and before/after measurements are in docs/spikes/large-history-read-responsiveness.md. A 2,000-game library with 125,000 sessions/notes made the old weekly Activity path exceed ten seconds after 5,074 repository reads. The paged date-scoped projection used one query and completed its first page in 68–119 ms in two local runs. Detail/account SQL now runs off the UI thread; total aggregation cost remains documented. Both presentations and cancellation are covered; no physical-frame-rate or Linux claim is made.

## Native Linux session verification

Both `Winnow.Monitor.Linux.Tests` cases passed on Fedora 44 under WSL2, kernel
`6.18.33.2-microsoft-standard-WSL2`, x86-64. The tests discovered and recorded a copied native
`sleep` executable under its install root and a process attributed through a synthetic
`STEAM_COMPAT_DATA_PATH`. This exercises actual Linux `/proc` process discovery and exit events;
it does not establish compatibility with a real Wine/Proton game or physical controller.

The SDK was installed only in the ignored verification directory. SDK 10.0.400 and runtime
10.0.11 match the Windows checks. Its Linux archive's SHA-512 matched Microsoft's release
metadata before extraction. Core, Monitor and the Linux test project were copied into an
isolated source tree so Linux restore/build did not change the active Windows `obj` files.

```sh
dotnet test tests/Winnow.Monitor.Linux.Tests/Winnow.Monitor.Linux.Tests.csproj \
  -c Release --logger 'trx;LogFileName=linux-native-review.trx'
```

The local result is retained under
`artifacts/review-fixes/linux-source/tests/Winnow.Monitor.Linux.Tests/TestResults/`.
Both tests passed, with no skips, in a 0.74-second test run after restore/build. The normal
Ubuntu CI gate remains required for a pull request.

## Final artwork lifetime review — TASK-210, TASK-225

An independent review exercised the shared cache, lease pool, native pipeline and application
LeasedCover ownership path. These checks use controlled callbacks and temporary artwork,
without network access or the production host. Five assertions failed before their fixes:

| Boundary | Reproduction and observed failure | Correction |
|---|---|---|
| Eviction between shared waiters | Two live leases await the same art A. Release its cache hold, then complete their source with inline continuations. The first resuming waiter obtains and retains B; the late waiter previously returned A with zero holds. | Return the exact bitmap instance already retained by the slot. |
| Shutdown callback failure | Warm one decoded entry, start another source and register a callback that throws on cancellation. DisposeAsync previously faulted with AggregateException before clearing the warm entry. | Log callback faults, drain remaining loads and release resources; repeated shutdown stays idempotent. |
| Final waiter callback failure | Cancel the last direct caller while its source has that throwing callback. Its task previously faulted with AggregateException instead of OperationCanceledException. | Asynchronous callback notification outside the cache lock preserves caller cancellation and contains callback faults. |
| Conversion concurrency | Configure one decode slot, block its conversion and request another key. The second conversion previously entered immediately while the first still held native pixels. | Retain the decode slot through native-to-Avalonia conversion. The second request remains queued until release. |
| Native partial decode | Prepopulate source and floor paths; inject a decoder that returns a native vivid bitmap then throws for the floor. GetAsync returned null while the vivid bitmap still had a nonzero native handle. | Release partially constructed native layers before propagating the decoder failure to the soft-failure boundary. |

The corresponding tests are CoverLeaseInterleavingTests, CoverCancellationFailureTests,
CoverConversionBoundaryTests and CoverDecodeFailureTests. A sixth check injects failure in
the second Avalonia conversion and verifies that the already converted vivid bitmap is
disposed exactly once; that cleanup was already correct. LeasedCover's generation and
release ordering needed no further change. The existing desktop and fullscreen tests also
verify reattachment after transient failure and retained pixels across cache shutdown.

Release verification through the serialized helper passed all 159 Winnow.Covers.Tests cases
and 24 headless cases selected by CoverCancellationFailureTests, CoverConversionBoundaryTests,
CoverLifetimeTests, CoverSelectionTests and DormancyTokenTests. There were no failures or skips.
The converter-bound check uses a 300 ms exclusion interval and then explicitly releases the
first converter, verifying that both results complete; other asynchronous waits are bounded
at three seconds. No brightness constants, token values or source ordering changed.
