---
id: TASK-222
title: Measure and bound large-library details and activity reads
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 08:32'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:1367'
  - 'src/Winnow.App/Views/Fullscreen/FullscreenActivityPage.cs:84'
  - src/Winnow.App/ViewModels/AccountStatsViewModel.cs
documentation:
  - docs/architecture-review-2026-09-10.md
priority: low
type: spike
ordinal: 253000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R34. Evidence: Source-verified scaling risk; performance unmeasured. The main library query uses a worker and bulk snapshot, but details serially reads per-release/per-ownership histories on the UI continuation. Fullscreen Activity already uses Task.Run, but loads ownership histories and per-session notes despite displaying a bounded period; its concern is query count and loading latency. Account summaries also invoke repository loads directly. Large libraries and years of session history may cause visible stalls. Query shape is a justified measurement target, but this review did not measure a freeze and does not justify a speculative wholesale rewrite.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A reproducible fixture measures latency, query count and UI-thread work for representative large libraries and long histories on both surfaces.
- [x] #2 Date-scoped bulk projections/paging or background reads address measured unbounded paths against an explicitly recorded responsiveness budget.
- [x] #3 The evidence record includes before/after measurements, navigation/cancellation behavior and remaining platform limits without treating source inspection as measured performance.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Measure a deterministic 2,000-game library with long sessions, snapshots and account history. Record actual repository read counts and UI-thread synchronous intervals before editing. Move measured detail/account reads off the dispatcher; replace fullscreen all-history per-row activity reads with a date-scoped paged projection, preserving visibility, notes, navigation and cancellation. Verify both surfaces and record before/after budgets and platform limits.

Independent UI review: add regression cases for failed pagination retry, focus retained while a delayed Activity read completes, and returning from a journal editor after loading older rows. Correct confirmed regressions in Activity/summary lifecycle while preserving the measured bounded query contract; rerun cancellation, draft and performance checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Independent UI review fixed Activity pagination failures hidden behind retained rows, loss of loaded older-session pages when returning from note editors, and focus stealing after delayed reads. Stable session/update control IDs and focus restoration preserve navigation; completed pages survive editor detachment, cancelled reads resume, and saved notes refresh their badge/preview without fetching page one. Added summary reading/error/retry state and focus preservation, including a fast-retry layout fallback. New ActivityRecoveryInteractionTests cover7 scenarios; StoresAccountContextTests now awaits the actual asynchronous fullscreen summary. Final51/51 UI cases passed in tests/Winnow.Ui.Tests/TestResults/ui222-independent-review.trx, including paging cancellation, details draft/generation checks and the two large-history measurements. Latest desktop details116.85ms/5leases/0UIleases/maxUIgap36.22ms; desktop summary122.51ms/1lease/0UIleases/maxUIgap1.51ms; fullscreen details117.80ms/5leases/0UIleases/maxUIgap52.85ms; weekly Activity68.27ms/1lease/0UIleases/maxUIgap68.10ms including attach; fullscreen summary128.48ms/1lease/0UIleases/maxUIgap13.00ms. No additional defect found in the worker detail context/cancellation wrapper. Scoped diff check passed. Root retains task completion and governing documentation.

Root measurement and repository verification: docs/spikes/large-history-read-responsiveness.md records a reproducible 2,000-game, 125,000-session fixture, before/after timing, query counts, UI-thread checks and explicit local budgets. The old weekly path exceeded ten seconds after 5,074 reads; the replacement reads one bounded page. All nine ActivityRepository and identity-read inventory checks passed; 51 final UI checks passed, including seven independently added recovery cases. Governing architecture and visual specifications now describe paging, cancellation, retained selection, loading and retry. No physical controller or native Linux performance claim is made.

Full-suite follow-up corrected FullscreenActivityTests to await PendingRefresh after each week or section change before asserting empty copy and navigation state. The production asynchronous contract is unchanged. Focused UI rerun passed 51/51 in tests/Winnow.Ui.Tests/TestResults/ui204-lifetime-publication.trx, including Activity empty-state, recovery, draft-preservation and preview lifetime cases.

Final contract inspection also retained session attribution and monitor identity in the paged Activity projection. Same-time paging tests now prove those facts survive alongside notes and unknown duration; all 59 feed/view-model/activity projection checks pass in final-feed-backfill.trx.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Measured and removed the weekly history N+1 path with date-scoped keyset paging; moved shared detail/account reads off the dispatcher. Both surfaces preserve current-request publication and drafts. Verified nine repository/inventory and 51 UI checks, including long-history measurements and recovery interactions; documented budgets and platform limits.
<!-- SECTION:FINAL_SUMMARY:END -->
