---
id: TASK-212
title: Publish built-in recommendations without waiting for optional plugins
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 08:31'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/Services/FeedService.cs:88'
  - src/Winnow.App/Services/PluginFeedService.cs
  - src/Winnow.Plugins/PluginCatalog.cs
  - docs/plugins.md
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: enhancement
ordinal: 243000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R24. Evidence: Source-verified blocking path; latency not measured. FeedService computes the built-in feed and then awaits all plugin shelves before returning it. PluginFeedService invokes providers sequentially. PluginCatalog's per-invocation deadline starts after its gate is acquired, so queue time is additional. A slow optional provider can delay the whole feed for its timeout, and multiple providers add delay. Soft-failing plugins can still block the core product's useful output. Cancellation cannot forcibly terminate uncooperative in-process plugin code, so host publication must be isolated from that limitation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Built-in shelves become usable independently of optional provider completion, with coherent incremental or otherwise bounded plugin publication.
- [x] #2 An explicit aggregate latency/cancellation budget includes queued work and prevents stale provider results from replacing a newer feed generation.
- [x] #3 Tests use slow, failing and cancellation-ignoring providers to verify baseline availability, bounded host waiting and stable desktop/fullscreen focus/impression behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Return built-in shelves immediately with an optional bounded supplemental result. Run provider requests concurrently under a shared five-second aggregate deadline that starts before queue acquisition. Append supplements within the current feed generation without replacing existing cards, and verify slow/cancellation-ignoring providers plus focus/impression stability on desktop and fullscreen.

Integration correction: contain aggregate deadline expiry consistently during snapshot, feedback and facet reads while preserving caller cancellation. Replace wall-clock race assumptions in deadline regressions with a manually controlled TimeProvider; verify queue time and early-read cancellation separately without enlarging the production budget.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Built-in feed returns immediately with an observed optional supplement. Provider work runs concurrently under a five-second aggregate budget covering data reads and invocation queue time. Generation and verdict revisions reject obsolete supplements; appending preserves existing cards, focus and impression identity. Expected history cancellation during feed disposal is contained.

Final integration caught a pending backfill being lost when a newer verdict invalidated the in-flight answer. FeedViewModel now discards that answer while continuing the coalesced pending read; optional-provider failure also cannot discard a queued built-in read. The existing coalescing regression and a new controlled optional-failure case pass in the 59-test feed/view-model/activity projection slice (final-feed-backfill.trx). Provider deadline and final whole-suite checks are recorded separately.

Full-suite correction: the old 100 ms queue fixture could expire during SQLite snapshot loading, revealing that aggregate cancellation escaped shared input reads while provider cancellation was contained. PluginFeedService now treats its own aggregate deadline as an empty optional result at snapshot, feedback and facet stages; caller cancellation still propagates. Production budget remains five seconds. An optional TimeProvider enables manually fired deadlines in tests. Queue tests use preloaded real fixture projections so they reach the provider gate before expiry; the mixed-speed test waits behind the fast provider to establish completed output before firing. Six added cases distinguish deadline expiry and caller cancellation at all three shared-read stages. Windows Release PluginFeedServiceTests passed 19/19, evidence tests/Winnow.Tests/TestResults/plugin-feed-deadline-fix.trx. Existing desktop/fullscreen supplement behavior and earlier surface evidence remain unchanged; source compiles with the production host. Also corrected the recommendation document Active formula to include creation and future revocation, preserving the old sentence in decisions.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented independent optional shelves with bounded host waiting. Verified 47 feed/model/inventory tests and 6 desktop/fullscreen supplemental-focus and production-composition tests. Cooperative, cancellation-ignoring, queued and mixed-speed providers are covered. In-process code cannot be forcibly killed; the host stops waiting and quarantines providers through the existing catalog policy.

Integration deadline correction verified 19/19 plugin-feed service tests with deterministic cancellation-controlled stages and unchanged five-second production budget. Early shared-read timeout is now contained consistently; caller cancellation remains distinct.
<!-- SECTION:FINAL_SUMMARY:END -->
