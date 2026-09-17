---
id: TASK-325
title: Diagnose intermittent portable upgrade startup failure
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-17 06:25'
updated_date: '2026-09-17 06:28'
labels: []
dependencies: []
type: bug
ordinal: 367000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Windows release smoke runs 125 and 132 intermittently fail apply because the updated app exits with code 3 before host logging. Existing artifacts contain only old-app logs and a generic helper error, preventing identification of the startup exception.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Smoke failures identify the scenario and journal failure in CI output and retain startup diagnostics.
- [ ] #2 Startup failures before host construction are persisted with existing privacy filtering without changing exit behavior on desktop or fullscreen.
- [ ] #3 Investigate captured evidence and document whether the underlying intermittent failure is resolved or still requires reproduction.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Compare failed CI journals and logs. 2. Preserve redacted pre-host startup diagnostics and expose journal failures in smoke output. 3. Add focused regression tests, run checks, and collect CI evidence without masking failures with retries.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Compared release runs 125 (35168044152) and 132 (35188672942), attempt 1. Journals show updated process exits with code 3 before readiness; failing scenarios differ (internal versus external data). Both artifacts contain only previous-app diagnostics, so no evidence yet identifies the underlying exception. RC4 release run 35188781253 passed. Added bounded privacy-filtered startup-failure.log independent of host logger and smoke output with scenario, journal phase/failure and startup log tail. Desktop and fullscreen use the same Program/StartupFailure path; no UI interaction changed. 31 focused logging/startup tests passed. PowerShell parse and invocation against the actual failed-run journal passed, including expected-failure behavior. Underlying intermittent startup cause remains unconfirmed; no retry or timeout workaround added.
<!-- SECTION:NOTES:END -->
