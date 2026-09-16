---
id: TASK-299
title: Present a responsive fullscreen startup transition
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 00:57'
updated_date: '2026-09-16 01:12'
labels: []
dependencies: []
type: bug
ordinal: 341000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
After the layout repair, the user still sees one initial feed flicker and a few seconds of apparent unresponsiveness. Provide a brief branded loading presentation that paints before initialization and reveals the initial usable fullscreen UI after its layout is ready.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Cold fullscreen entry paints an accessible loading state before initialization and reveals the initial library/feed only after layout is ready, without a fixed artificial delay or indefinite network wait.
- [x] #2 Loading remains responsive to exit; failures provide recovery; rapid exit/re-entry and disposed views cannot reveal stale content.
- [x] #3 A brief themed animation and reveal respect reduced motion; warm re-entry remains fast; desktop behavior remains intact.
- [x] #4 Startup and readiness regressions, relevant fullscreen and desktop tests, visual checks and documentation are complete.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Trace initialization, first layout and UI-thread work. 2. Add branded readiness-driven fullscreen loading presentation with cancellation/lifetime and failure recovery. 3. Remove or yield avoidable long UI-thread work during library preparation. 4. Validate cold/warm startup, failures, exit/re-entry, reduced motion and desktop; document and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Startup currently attaches the full view before context preferences/library/feed finish, exposing initial geometry and data changes. The new view preparation gate paints loading first, retains it through initial load and layout opportunities, and gives explicit exit/retry paths. Library tile preparation now yields before publication after roughly 8ms or 128 items; cancellation/disposal/generation guards run around yields. New input callbacks during a 512-title preparation verify no partial publication and cancel/dispose/newer-load safety. All 18 preparation and refresh-ordering tests passed. Fullscreen presentation tests and reduced-motion/lifetime review in progress.

Presentation now waits for visual attachment and a render opportunity before loading, then initial load and settled frames before a 180ms reveal. Pulse waits for saved motion preference and stops before fade; status/actions respect text scale. Settings IO is off the UI thread. Back/Escape/F11/Space and retry work while hidden navigation is disabled. Early exit cancels pending attachment/frame waits; warm entry avoids repeat splash. Timing/failures use rolling diagnostics. Independent review found no remaining blocker; hit testing prevents unseen feed impressions. Normal, large-text and failure renders inspected. First full UI run:737 passed, one existing controller test required awaiting new readiness boundary; adapted it and rerunning.157 core library/filter/list/identity/documentation/architecture tests passed.

Final validation: all 738 UI tests passed with zero failures/skips, including cold/warm entry, pre-attachment exit, reduced motion, delayed settings, recovery and existing desktop/controller paths. An additional 157 core tests passed. Build and whitespace checks pass; independent final review found no blockers. Updated visual and architecture specs plus docs/spikes/fullscreen-startup-readiness.md. Production app/data untouched; unrelated desktop hover changes remain excluded.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a themed first-entry loading presentation that paints before initialization and reveals the initial library/feed after layout readiness, with a preference-aware pulse and 180ms fade. Back and retry remain usable; canceled presentations cannot reveal late, and warm re-entry skips the splash. Moved fullscreen settings reads off the UI thread and added bounded cooperative yields during library preparation while retaining atomic publication. Validated 738 UI tests, 157 core tests, normal/large/error captures and independent review.
<!-- SECTION:FINAL_SUMMARY:END -->
