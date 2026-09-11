---
id: TASK-4
title: Validate fullscreen with physical controllers and the intended display
status: To Do
assignee:
  - codex
created_date: '2026-08-29 21:52'
updated_date: '2026-09-11 14:03'
labels:
  - ui
  - accessibility
  - needs-user
milestone: m-3
dependencies:
  - TASK-3
references:
  - tests/Winnow.Ui.Tests/FullscreenAccessibilityTests.cs
documentation:
  - design-system.md
  - docs/spikes/fullscreen-controller-verification.md
priority: low
ordinal: 259000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Validate the implemented fullscreen UI with a real controller at normal seating distance, including platform sign-in, file selection and launcher handoffs. Automated layout, focus and interaction coverage already exists. This task tracks the remaining device and live-flow evidence rather than rebuilding fullscreen. Winnow-owned operations must be controller-accessible; external provider challenges and launcher input requirements are recorded explicitly.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every Winnow-owned screen and operation is reachable using the tested physical controller; live provider and native-window handoffs have recorded results and explicit external input requirements.
- [ ] #2 Focus remains visible throughout the tested flows at normal seating distance and the supported display/text settings.
- [ ] #3 Winnow-owned operations complete without a mouse or keyboard; record any failing screen, control or handoff instead of inferring success from automated input tests.
- [x] #4 The full-screen surface shows a clock
- [x] #5 Connected controller battery level is shown when the platform reports it, and the absence of that reading is not treated as an error
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Use the intended controller and TV/display with a throwaway data directory. Record controller model, OS, resolution, seating distance and scaling. Walk navigation, search, filters, details, lists, identity review, journal, settings, text/file entry and provider handoffs. Record reproducible failures and verify fixes on the affected surfaces before closing hardware criteria.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: docs/spikes/fullscreen-controller-verification.md and the fullscreen accessibility, navigation, details and platform tests establish automated coverage. They do not establish physical-controller or seating-distance usability. Existing clock/battery completion is retained; no hardware or live provider pass occurred in this audit. Keep needs-user and the deferred queue position.
<!-- SECTION:NOTES:END -->
