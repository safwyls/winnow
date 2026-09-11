---
id: TASK-226
title: Validate accessibility of code-built fullscreen pages at runtime
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-11 05:01'
updated_date: '2026-09-11 08:05'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'tests/Winnow.Tests/Enforcement/InteractiveControlNameTests.cs:39'
  - 'tests/Winnow.Tests/Enforcement/AutomationNameReachabilityTests.cs:71'
  - src/Winnow.App/Views/Fullscreen/FullscreenActivityPage.cs
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: enhancement
ordinal: 257000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R38. Evidence: Source-verified enforcement gap. InteractiveControlNameTests and AutomationNameReachabilityTests inspect AXAML, while much of fullscreen is built in C#. These checks cannot cover that surface's unnamed controls or dynamic focus paths; the Activity note editor is one omission visible in source. Strong desktop enforcement can give a false sense of full-surface accessibility. Headless runtime coverage complements, but cannot replace, physical-controller and ten-foot checks in TASK-4.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Runtime accessibility checks traverse representative code-built fullscreen pages and verify interactive names, focus reachability and back/modal restoration.
- [x] #2 Coverage includes dynamic prompts, note editors, settings and disabled/error states, with failures tied to actual rendered controls.
- [x] #3 Desktop enforcement remains intact and the documented validation distinguishes automated coverage from TASK-4 physical-controller/readability verification.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Traverse real fullscreen controls and automation peers for dynamic prompts, journal note editors, settings, and unavailable or failed states. 2. Assert directional focus reachability plus modal back restoration against rendered pages, and correct verified naming or focus gaps in shared helpers and affected controls. 3. Run the representative headless suite and existing desktop name enforcement, document the automated scope separately from TASK-4 physical-controller and ten-foot verification.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added FullscreenAccessibilityTests with14 runtime cases over six Settings sections, search and filter pages, Steam key configuration, combined list prompts, both session note editors, action modal restoration and controller keyboard return. The test walks actual automation peers and checks exposed names and effective enabled states; a directional focus graph proves each enabled action is reachable. Verified omissions: Steam key and search TextBoxes had no automation name; both now use their existing field labels. Shared preview state was replaced with isolated model/settings graphs so the traversal can exercise adjustment controls without contaminating other tests. Final UI run54/54 passed in tests/Winnow.Ui.Tests/TestResults/ui226-runtime-accessibility.trx; existing desktop AXAML name/reachability and document checks11/11 passed in tests/Winnow.Tests/TestResults/ui226-desktop-enforcement.trx. Scoped git diff check passed. Design-system8 records the automated scope; native screen-reader behavior and physical-controller/readability checks were not claimed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Runtime automation-tree and directional-navigation tests now cover code-built fullscreen pages, dynamic prompts, note editors, disabled/error states, and modal/keyboard return. Corrected the verified unnamed search and Steam key fields. All54 focused UI regressions and11 desktop enforcement/document checks pass. Hardware and seating-distance verification remain in TASK-4.
<!-- SECTION:FINAL_SUMMARY:END -->
