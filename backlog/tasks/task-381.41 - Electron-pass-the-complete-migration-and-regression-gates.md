---
id: TASK-381.41
title: 'Electron: pass the complete migration and regression gates'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:50'
updated_date: '2026-10-02 04:26'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies:
  - TASK-381.2
  - TASK-381.3
  - TASK-381.4
  - TASK-381.5
  - TASK-381.6
  - TASK-381.7
  - TASK-381.8
  - TASK-381.9
  - TASK-381.10
  - TASK-381.11
  - TASK-381.12
  - TASK-381.13
  - TASK-381.14
  - TASK-381.15
  - TASK-381.16
  - TASK-381.17
  - TASK-381.18
  - TASK-381.19
  - TASK-381.20
  - TASK-381.21
  - TASK-381.22
  - TASK-381.23
  - TASK-381.24
  - TASK-381.25
  - TASK-381.26
  - TASK-381.27
  - TASK-381.28
  - TASK-381.29
  - TASK-381.30
  - TASK-381.31
  - TASK-381.32
  - TASK-381.33
  - TASK-381.34
  - TASK-381.35
  - TASK-381.36
  - TASK-381.37
  - TASK-381.38
  - TASK-381.39
  - TASK-381.40
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
documentation:
  - src/Winnow.Electron/README.md
  - design-system.md
parent_task_id: TASK-381
priority: high
type: task
ordinal: 459000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Passing focused batches does not prove the final combined tree. The migration needs one reviewed set of complete checks after feature and release integration.

This task owns a remaining delivery/validation gate beyond the source-method inventory. It does not reassign contracts from the feature tasks.

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The source inventory has no pending/partial methods and every mapping has reviewed assertion scope; all Electron component/live API/native and required backend/.NET/Linux suites pass against the final tree without unexplained skips.
- [ ] #2 Complete Windows/Linux packaging smoke and required CI gates pass for the final composition; native results include retained reports and failure diagnostics rather than only counts.
- [ ] #3 Cross-surface visual/workflow smoke evidence and agreed device limitations are recorded, the parent acceptance criteria are assessed against this evidence, and a reviewable final commit is reported without publishing a release.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
First clean CI frontend regression run 36961277764 at 20bceb58: 4420 passed,4 failed of4424 across231files. Preserve and resolve before final completion: themeFile canonical path equality rejects Windows runner TEMP aliases (shell.test.ts), theme-packages cleanup compares canonical child to uncanonical tmpdir and refuses two tests, parity-startup-boundaries mocked spawn hangs at5s under CI (investigate race; do not blindly raise timeout). Failure log .tmp/task38137-ci-frontend-failure.log. Clean native Windows packaged11 tests passed in38.9s independently. These findings remain open for the final regression task.

Follow-up diagnosis of CI spawn-boundary timeout: scrubStartupDiagnostic unanchored email regex rescans every suffix of a long non-email word, quadratic on bounded32KB stderr. Local isolated3-call benchmark: current pattern1231ms versus equivalent negative-local-part-boundary pattern0.29ms. Test calls diagnostic3times. Fix privacy scrubber complexity with preserved account-redaction cases before considering timeout changes. Theme failures arise from canonical vs lexical Windows TEMP path equality; normalize trusted base and compare secure containment while retaining link refusal, and canonicalize fixture cleanup root.

Second clean CI frontend run36961972740:4418passed/6failed of4424. Same four reproducible failures plus two parity-setup-settings tests finding Back to setup before accessibility/transition completion (full traces .tmp/task38137-ci-frontend-second-failure.log). Preserve assertions and diagnose asynchronous transition/focus timing; final CI should use explicit bounded worker count matching local evidence. This is not a completed full gate.

Completed clean .NET CI36961972732 found11failures: Backend FrontendActivationParityTests.Loss_of_the_real_parent... timeout10s; BackendOwnershipTests.OwnerCanSecureModifyOnlyDataDirectoryWithoutTakeOwnershipPermission unauthorized ACL (Windows runner account); Update HelperProtocolTests.RealCopiedHelperAcknowledgesHandoffAndReleasesCommandPipesWhileItsParentLives timeout30s; UI FullscreenBrowse visiblefeed1280x720 expected4 actual2; all6 DerelictOverrideComposition cases have empty grouped library; core IdentityReadInventory lacks DetailsApplication.GetMetadataAsync reader classification. Detailed log .tmp/task38137-ci-dotnet-failure.log; retained GitHub TRX artifacts must be inspected. Counts by affected assembly: Core4999/5000, UI892/899, Backend323/325, Update69/70. Linux native/Proton CI job passed. Diagnose all before final full gate; do not weaken expectations or substitute migrated UI passes for failed originals.

Read-only root-cause triage: six Derelict failures come from Sep 1 observations expiring after 30 days on Oct 2; inject a consistent fixed fixture clock, including reopen and later evidence. Fullscreen exposure test waits 150ms for a 220ms transition; use controlled frames plus committed rendering, retain exact 2-to-4 exposure assertions and separate real-frame coverage. Setup helper clicks Appearance while disabled by initial profile persistence; await enabled before one click and await provider entry. Theme roots must be canonicalized with strict child containment and link rejection retained. DetailsApplication.GetMetadataAsync fallback reads the exact editor target and belongs in DoNotResolve inventory with the same rationale as WorkMetadataEditService. Backend ACL fixture must establish current-SID ownership before applying Modify-only permissions. Activation and copied-helper failures both occur before first readiness, their PowerShell wrappers started 41ms apart; capture bounded phase/stderr/child cleanup evidence before changing implementation or deadlines. TRX downloaded to .tmp/task38141-ci-dotnet-results. No regression fixes implemented yet; task remains subsequent to package/device/default gates.
<!-- SECTION:NOTES:END -->
