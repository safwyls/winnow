---
id: TASK-294
title: Fix transient blank covers while browsing fullscreen
status: Done
assignee:
  - '@codex'
created_date: '2026-09-15 01:35'
updated_date: '2026-09-15 01:39'
labels: []
dependencies: []
type: bug
ordinal: 336000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Fullscreen library cards frequently show only title placeholders temporarily, even for games whose artwork has been seen before.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Reproduce the cover-loading failure and add a regression check for the identified cause.
- [x] #2 Fullscreen covers recover without unnecessary blank states; verify shared desktop behavior and resource ownership.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Trace fullscreen card attachment and shared cover loading, reproduce the lifecycle or scheduling failure, fix the cause, and run focused UI and cache tests.

Reproduce immediate reattachment while cancelled loads are still unwinding. Wait for the retiring same-key cache load before retrying; remove redundant fullscreen collection rebuilds and verify both desktop/fullscreen lifecycle behavior.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced two failures before fixes: fullscreen collection changes replaced their newly attached viewport again when queued notifications ran; immediate desktop/fullscreen reattachment while the prior source cancellation unwound settled on null artwork. The new regressions failed against original code. Fullscreen Rebuild now consumes pending notifications. CoverCache waits for a retiring same-slot load and then retries through normal admission/deduplication, respecting caller cancellation and shutdown. This preserves concurrency limits and avoids treating lifecycle cancellation as missing art. Verified 62 targeted UI tests (cover lifetime/cancellation/conversion, fullscreen browse/home/row navigation/cover request) and all 189 Winnow.Covers.Tests, plus git diff --check. Shared desktop recovery and lease release are covered by the same deterministic reattachment test. Verification used isolated headless fixtures, not the live library. Real first-time downloads can still show placeholders while loading.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed duplicate fullscreen collection rebuilds and the shared cover-cache cancellation/reacquisition race that left replacement cards blank. Added regressions proven to fail before the fixes; 62 UI tests and 189 cover tests pass. Updated architecture documentation.
<!-- SECTION:FINAL_SUMMARY:END -->
