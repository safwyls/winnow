---
id: TASK-212
title: Publish built-in recommendations without waiting for optional plugins
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 06:40'
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
- [ ] #1 Built-in shelves become usable independently of optional provider completion, with coherent incremental or otherwise bounded plugin publication.
- [ ] #2 An explicit aggregate latency/cancellation budget includes queued work and prevents stale provider results from replacing a newer feed generation.
- [ ] #3 Tests use slow, failing and cancellation-ignoring providers to verify baseline availability, bounded host waiting and stable desktop/fullscreen focus/impression behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Return built-in shelves immediately with an optional bounded supplemental result. Run provider requests concurrently under a shared five-second aggregate deadline that starts before queue acquisition. Append supplements within the current feed generation without replacing existing cards, and verify slow/cancellation-ignoring providers plus focus/impression stability on desktop and fullscreen.
<!-- SECTION:PLAN:END -->
