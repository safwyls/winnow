---
id: TASK-325
title: Diagnose intermittent portable upgrade startup failure
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-17 06:25'
updated_date: '2026-09-17 06:36'
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

4. Reproduced on run 35189971933: Win32Exception in DurableFiles.Move -> Write -> ValidateStartup. Add bounded Windows journal replacement retries for sharing/access contention, preserve errors on exhaustion, and test transient/persistent locks. Verify real CI smoke again.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Compared release runs 125 (35168044152) and 132 (35188672942), attempt 1. Journals show updated process exits with code 3 before readiness; failing scenarios differ (internal versus external data). Both artifacts contain only previous-app diagnostics, so no evidence yet identifies the underlying exception. RC4 release run 35188781253 passed. Added bounded privacy-filtered startup-failure.log independent of host logger and smoke output with scenario, journal phase/failure and startup log tail. Desktop and fullscreen use the same Program/StartupFailure path; no UI interaction changed. 31 focused logging/startup tests passed. PowerShell parse and invocation against the actual failed-run journal passed, including expected-failure behavior. Underlying intermittent startup cause remains unconfirmed; no retry or timeout workaround added.

Diagnostic build reproduced the flake on Windows run 35189971933: Win32Exception at DurableFiles.Move -> PortableUpdateEngine.Write -> ValidateStartup before host construction. A regression test holding a Windows reader without delete sharing failed against the previous code. Journal-only atomic rename now retries native errors 5/32/33 for at most two seconds; it preserves the old journal and fails on persistent locks. All 39 update-engine tests pass, including transient and persistent real Windows file locks. Exact identity of the temporary reader in CI is not established. Added native error code to startup diagnostics for future evidence.
<!-- SECTION:NOTES:END -->
