---
id: TASK-226
title: Validate accessibility of code-built fullscreen pages at runtime
status: To Do
assignee: []
created_date: '2026-09-11 05:01'
updated_date: '2026-09-11 05:07'
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
- [ ] #1 Runtime accessibility checks traverse representative code-built fullscreen pages and verify interactive names, focus reachability and back/modal restoration.
- [ ] #2 Coverage includes dynamic prompts, note editors, settings and disabled/error states, with failures tied to actual rendered controls.
- [ ] #3 Desktop enforcement remains intact and the documented validation distinguishes automated coverage from TASK-4 physical-controller/readability verification.
<!-- AC:END -->
