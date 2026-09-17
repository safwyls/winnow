---
id: TASK-333
title: Give the artwork browser a spacious desktop overlay
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 16:55'
updated_date: '2026-09-17 17:01'
labels: []
dependencies: []
ordinal: 375000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The browser is functional but cramped inside the details body, forcing frequent vertical scrolling while comparing artwork. Give it space above the whole details modal while preserving the current editing context.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop artwork browsing opens above the full details modal with a larger gallery and visible preview and actions at supported window sizes.
- [x] #2 Closing or pressing Escape returns to the invoking control and preserves details tabs, metadata drafts and selected artwork state; background controls cannot receive input while browsing.
- [x] #3 Gallery scrolling is limited to results; preview and apply/import controls remain accessible without scrolling the whole selector.
- [x] #4 Desktop resize/focus/layout tests and visual captures pass; fullscreen controller layout is assessed and verified separately; visual documentation matches the result.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Move desktop browser to a bounded large overlay sibling of the details card and isolate focus/input. 2. Expand gallery/preview proportions and fit previews without nested preview scrolling. 3. Verify desktop window sizes and context return plus fullscreen regression coverage, inspect rendered captures and update documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop browser now occupies a sibling overlay above the disabled details card, up to1440x1000 with24px insets. Gallery has3:2 width against a bounded non-scrolling preview; footer remains pinned. Focus cycles in the overlay and Back/Escape preserve editor drafts and return to the invoking control. Fullscreen retains its separate controller page; its regression tests pass independently. Verified15 artwork tests including1200x640,1280x820,1920x1080 all-slot layout with long attribution/source link, saved status and imports;387 existing details/fullscreen tests and4 automation reachability checks passed. Styled headless captures inspected at C:\Temp\winnow-artwork-overlay-captures using synthetic data; no production library touched. Solution build passed with0warnings/errors; diff check clean.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Moved desktop artwork selection into a larger modal above details, expanded the gallery and removed preview scrolling while keeping save/import controls visible. Preserved keyboard focus return and drafts. Verified15 browser tests,387 details/fullscreen regressions,4 accessibility-name checks and rendered short/standard/large-window captures; zero-warning solution build.
<!-- SECTION:FINAL_SUMMARY:END -->
