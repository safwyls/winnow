---
id: TASK-214
title: Cover configuration and host construction with the startup error boundary
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 06:18'
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
- [x] #1 Configuration, service registration, host construction and framework/startup failures all reach an appropriate outer error boundary and safe cleanup.
- [x] #2 Bad data-dir refusal remains exit2; other startup failures use exit3 with an actionable console/message presentation and no secret leakage.
- [x] #3 Process-level tests use throwaway directories and cover malformed configuration and host-construction failure for both normal and fullscreen startup paths.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Put configuration and host creation inside an outer bootstrap boundary while retaining data-dir exit2 and startup exit3. 2. Ensure cleanup and error reporting cannot throw a second exception or assert unchanged data after work already ran. 3. Add process-level malformed-config/unsupported-schema tests in isolated directories for normal/fullscreen startup plus reporter failure tests; verify existing startup enforcement.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Program.Main now wraps complete bootstrap/host lifetime; failures in configuration/build/cleanup reach StartupFailure. Reporting redacts secrets, survives arbitrary logger-construction failure, and avoids claiming initialized data was unchanged. Seven real child-process tests pass with throwaway directories and actual stored desktop/fullscreen startup preferences: malformed JSON, invalid host logging options, unsupported schema and bad data directory. Exit3/exit2 and unchanged database bytes verified. Nine reporter unit cases also pass.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Covered full bootstrap with an outer error boundary and safe reporting/cleanup. Verified process-level failure paths on isolated desktop/fullscreen-preference libraries and reporter redaction/failure tests.
<!-- SECTION:FINAL_SUMMARY:END -->
