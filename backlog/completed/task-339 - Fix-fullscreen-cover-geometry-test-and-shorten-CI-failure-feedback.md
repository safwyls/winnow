---
id: TASK-339
title: Fix fullscreen cover geometry test and shorten CI failure feedback
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 19:20'
updated_date: '2026-09-17 19:26'
labels: []
dependencies: []
type: bug
ordinal: 381000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
PR 25 failed because the Home geometry assertion included an asynchronously attached backdrop. The failure appeared early but the Windows job remained running for the rest of the suite.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Home geometry assertions measure the three game covers independently of backdrop loading.
- [x] #2 Focused Release tests and CI workflow policy checks pass; desktop behavior is unchanged.
- [x] #3 Home layout failures stop the Windows gate in a short preflight before the full suite; full coverage and evidence checks remain required.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Scope the geometry regression to Home content and require exactly three covers. 2. Add a focused Home layout preflight before the unchanged full solution test run; the full UI suite itself took 12 minutes and must not be serialized ahead of the other suites. 3. Verify Release layout tests, CI evidence and summary checks, then push and inspect CI.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
CI run 35261952892 had one failure: the description geometry test included a 2259x1271 backdrop among three 252x377 shelf covers. Scoped both samples to CurrentPage and require exactly three covers. Release Home layout tests: 19 passed in 3 seconds. Full Release UI suite: 864 passed in 2m55s locally. CI evidence policy: 53 checks passed; test-result summary checks passed; git diff --check passed. Added the verified layout command as a success-gated preflight; full solution command and evidence publication remain unchanged. Desktop application behavior is unchanged; the full UI suite covers both surfaces. Remote CI after push remains to be observed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed backdrop-dependent Home geometry assertion and added a short layout preflight before the full Windows suite. Verified 19 focused and 864 full Release UI tests plus CI evidence and timing-summary checks. This improves feedback for Home layout failures; it does not shorten the full successful suite.
<!-- SECTION:FINAL_SUMMARY:END -->
