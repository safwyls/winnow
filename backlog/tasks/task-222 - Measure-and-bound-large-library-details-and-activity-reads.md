---
id: TASK-222
title: Measure and bound large-library details and activity reads
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:07'
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
- [ ] #1 A reproducible fixture measures latency, query count and UI-thread work for representative large libraries and long histories on both surfaces.
- [ ] #2 Date-scoped bulk projections/paging or background reads address measured unbounded paths against an explicitly recorded responsiveness budget.
- [ ] #3 The evidence record includes before/after measurements, navigation/cancellation behavior and remaining platform limits without treating source inspection as measured performance.
<!-- AC:END -->
