---
id: TASK-214
title: Cover configuration and host construction with the startup error boundary
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/Program.cs:69'
  - 'src/Winnow.App/Program.cs:155'
  - 'src/Winnow.App/Program.cs:170'
  - AGENTS.md
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 245000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R26. Evidence: Reproduced. Program calls Host.CreateApplicationBuilder, adds JSON configuration, configures services and builds the host before entering its startup try/catch. An isolated launch from a directory containing malformed appsettings.json produced an unhandled JSON configuration exception ending at Program.Main:69, before data-directory selection or the documented failure presenter. Real bootstrap failures bypass the promised logged/displayed startup failure and exit-code3 contract. The reproduction used throwaway data and was terminated after crash reporting; no graceful exit code was observed.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Configuration, service registration, host construction and framework/startup failures all reach an appropriate outer error boundary and safe cleanup.
- [ ] #2 Bad data-dir refusal remains exit2; other startup failures use exit3 with an actionable console/message presentation and no secret leakage.
- [ ] #3 Process-level tests use throwaway directories and cover malformed configuration and host-construction failure for both normal and fullscreen startup paths.
<!-- AC:END -->
