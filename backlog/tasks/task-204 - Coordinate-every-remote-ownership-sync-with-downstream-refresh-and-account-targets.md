---
id: TASK-204
title: >-
  Coordinate every remote ownership sync with downstream refresh and account
  targets
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 08:31'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/Services/RemoteOwnershipSchedulerService.cs:118'
  - 'src/Winnow.App/Program.cs:305'
  - 'src/Winnow.App/Program.cs:517'
  - 'src/Winnow.App/Services/LibrarySyncService.cs:489'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 235000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R16. Evidence: Source verified. The six-hour RemoteOwnershipSchedulerService resolves ownership and logs completion but does not run the library reload/enrichment chain used at startup. New acquisitions can stay invisible until an unrelated reload and incompletely enriched until another trigger. Steam targets are also drawn from local candidate account refs, so a known signed-in account with no local candidates may receive no remote ownership fetch. Background ownership sync does not fulfill its end-to-end application contract. The copied startup pipeline and scheduler have drifted.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Startup, scheduled and explicit ownership sync use one application coordinator that schedules dependent metadata work and publishes committed library changes.
- [x] #2 Authorized known account identities remain eligible sync targets even without local installations; account changes and cancellation are handled conservatively.
- [x] #3 Tests add a remote acquisition while the app remains open and cover remote-only Steam startup, partial failures and both presentation surfaces without calling UI from ingest modules.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Use one ownership-refresh coordinator for startup, scheduler and explicit account actions; publish through the existing desktop TilesChanged/fullscreen active-or-pending bridge, then run the ordered shared downstream pipeline with independent failure boundaries. Reuse TASK198 confirmed remote-only account targets and verify cancellation, account triggers and both surfaces.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented OwnershipRefreshCoordinator, LibraryRefreshPipeline, LibraryChangePublisher and coalesced account-action requests; removed copied startup/IGDB chains. TASK198 provides confirmed remote-only Steam targets. Verified40 focused Release checks plus4 desktop/fullscreen production-publisher scheduler cases, including failure after commit and inactive fullscreen entry. Evidence: docs/spikes/architecture-fixes-2026-09-10.md.

Independent follow-up review: verifying coordinator and pipeline serialization, account-request coalescing, shutdown cancellation, and production UI publication. Add targeted cross-service regression tests only; preserve completed behavior unless a reproduced defect requires a narrow correction.

Independent follow-up review found no additional defect in OwnershipRefreshCoordinator, LibraryRefreshPipeline, LibraryChangePublisher, OwnershipRefreshRequests or Program account-change wiring. Added OwnershipRefreshLifecycleTests covering request coalescing behind startup and during a full operation, shutdown during active metadata with queued work discarded and both gates released, and IGDB/ownership downstream serialization while committed ownership publication remains immediate. Focused main suite passed 26/26 in tests/Winnow.Tests/TestResults/ui204-independent-lifecycle.trx. Production DI and both-surface publication retested with lifetime/preview/activity regressions: 51/51 in tests/Winnow.Ui.Tests/TestResults/ui204-lifetime-publication.trx. No live host or network account operation was started.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
All ownership refresh entry points now publish committed acquisitions and run the same downstream metadata pipeline. Desktop and fullscreen stay current without restart; failure/cancellation and remote-only account cases are covered.
<!-- SECTION:FINAL_SUMMARY:END -->
