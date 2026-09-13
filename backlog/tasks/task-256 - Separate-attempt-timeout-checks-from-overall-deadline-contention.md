---
id: TASK-256
title: Separate attempt timeout checks from overall deadline contention
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 14:54'
updated_date: '2026-09-13 14:56'
labels: []
dependencies: []
type: bug
ordinal: 298000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
PR #15 Windows CI passed paging but an HTTP conformance case hit the fixture five-second overall deadline before attempt retries exhausted on the busy runner.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Attempt timeout cases retain exact exception and retry/disposal checks with adequate overall scheduling headroom.
- [x] #2 Explicit overall timeout and caller cancellation conformance tests remain passing.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Use the production-sized overall budget for both attempt-timeout cases only, preserve the dedicated overall deadline test, run all provider conformance cases and the core Release suite, and update PR #15.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Only the two attempt-timeout fixture hosts use a 90-second overall budget, matching the production default. Attempts remain 25 ms, retries remain exactly three, and transport exception/body disposal assertions are unchanged. Dedicated overall-deadline and caller-cancellation cases retain their original settings. All 104 provider conformance cases and all 4,668 core Release tests passed; diff whitespace check passed. No production or desktop/fullscreen behavior changed. Paging test passed in hosted CI run 34744190426.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Separated attempt-timeout fixture scheduling headroom from the dedicated overall-budget checks. All 104 conformance cases and 4,668 core Release tests passed. Hosted rerun is pending.
<!-- SECTION:FINAL_SUMMARY:END -->
