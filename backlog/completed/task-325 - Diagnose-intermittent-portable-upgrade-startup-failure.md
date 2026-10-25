---
id: TASK-325
title: Diagnose intermittent portable upgrade startup failure
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 06:25'
updated_date: '2026-09-17 06:44'
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
- [x] #1 Smoke failures identify the scenario and journal failure in CI output and retain startup diagnostics.
- [x] #2 Startup failures before host construction are persisted with existing privacy filtering without changing exit behavior on desktop or fullscreen.
- [x] #3 Investigate captured evidence and document whether the underlying intermittent failure is resolved or still requires reproduction.
- [x] #4 Windows journal replacement tolerates a temporary reader lock but preserves the original journal and fails when contention persists.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Compare failed CI journals and logs. 2. Preserve redacted pre-host startup diagnostics and expose journal failures in smoke output. 3. Capture a failing diagnostic build and reproduce journal contention in a regression test. 4. Retry only transient Windows atomic journal replacement failures, preserving the original journal and failing on persistent locks. 5. Verify focused tests and real CI upgrade/recovery smoke; record remaining limitations.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Compared release runs 125 (35168044152) and 132 (35188672942), attempt 1. Journals show updated process exits with code 3 before readiness; failing scenarios differ (internal versus external data). Both artifacts contain only previous-app diagnostics, so no evidence yet identifies the underlying exception. RC4 release run 35188781253 passed. Added bounded privacy-filtered startup-failure.log independent of host logger and smoke output with scenario, journal phase/failure and startup log tail. Desktop and fullscreen use the same Program/StartupFailure path; no UI interaction changed. 31 focused logging/startup tests passed. PowerShell parse and invocation against the actual failed-run journal passed, including expected-failure behavior. Underlying intermittent startup cause remains unconfirmed; no retry or timeout workaround added.

Diagnostic build reproduced the flake on Windows run 35189971933: Win32Exception at DurableFiles.Move -> PortableUpdateEngine.Write -> ValidateStartup before host construction. A regression test holding a Windows reader without delete sharing failed against the previous code. Journal-only atomic rename now retries native errors 5/32/33 for at most two seconds; it preserves the old journal and fails on persistent locks. All 39 update-engine tests pass, including transient and persistent real Windows file locks. Exact identity of the temporary reader in CI is not established. Added native error code to startup diagnostics for future evidence.

31 focused startup/logging tests passed again after the final diagnostics change. First push attempt hit a transient SSH connection reset; retry successfully pushed 2abbb9d. PR #23 now includes the journal retry fix and remains draft pending fresh CI.

Fresh PR release run 35190643909 at 2abbb9d passed Windows portable upgrade/recovery smoke, Windows installer checks, Linux package/startup/upgrade/recovery and plugin packaging. All targeted local checks passed (39 updater tests, 31 startup/logging tests). The deterministic contention regression failed before the fix and passed after it. Full PR Windows test job is still running; no merge or tag movement performed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Captured the intermittent pre-host startup failure in CI and traced it to Windows atomic journal replacement. Added a bounded two-second retry for native access/sharing/lock contention while retaining failure and recovery behavior for persistent errors. Added independent redacted startup logs and actionable smoke output. Verified by a before/after Windows lock regression, 39 updater tests, 31 startup/logging tests, and passing real Windows/Linux upgrade and recovery smoke in PR #23. The exact temporary CI reader is unknown.
<!-- SECTION:FINAL_SUMMARY:END -->
