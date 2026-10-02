---
id: TASK-381.37
title: 'Electron: verify the Windows Electron installer and portable build'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:50'
updated_date: '2026-10-02 04:32'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies:
  - TASK-381.24
  - TASK-381.23
  - TASK-381.36
  - TASK-381.29
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
documentation:
  - src/Winnow.Electron/README.md
  - design-system.md
parent_task_id: TASK-381
priority: high
type: task
ordinal: 455000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The primary Windows release still ships Avalonia; a working development Electron window does not establish installation or upgrade behavior.

This task owns a remaining delivery/validation gate beyond the source-method inventory. It does not reassign contracts from the feature tasks.

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Windows installer and portable packages contain the correct Electron UI, bundled backend/runtime, product identity, assets, provider files and notices, with no runtime Avalonia UI dependency.
- [x] #2 Disposable install/launch/upgrade/uninstall smoke checks pass, preserving existing/legacy data, protocol activation, shortcuts, single-instance behavior and update recovery.
- [x] #3 Build and smoke commands, artifacts and any signing limits are documented with reproducible evidence.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Build primary Electron Windows packages with existing Inno identity and ZIP layout, bundled independent backend/helper/providers/notices and version provenance. Preserve legacy upgrade/data behavior and protocol/shortcut/single-instance paths. Validate locally without installation, then execute actual install/upgrade/uninstall/recovery smoke on disposable GitHub runners, retaining artifacts and diagnostics. Do not publish a release; continue sequentially under the six-task batch authorization.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Primary Windows package built with no Avalonia UI assemblies: 695 payload files, self-contained backend/helper, provider, notices and exact ASAR/version/source identity. Local build identity20, startup/activation25, package mutation16, pure Windows packaging54 and native packaged11 checks pass with no native skips. Native desktop/fullscreen package uses its own backend and releases real update lease. Disposable GitHub install/upgrade/uninstall/recovery execution is the remaining gate; workflow prepared. Evidence: docs/spikes/2026-09-28-electron-parity/checkpoint-ninety-two.md. No software installed locally and no release published.

Second disposable CI36961972888 passed actual package integrity/native11 checks and the four installer rejection scenarios, but real upgrade relaunch was invisible. Bounded local reproduction confirmed WindowStyle Hidden leaves a responsive Electron window invisible with healthy backend for60s. Corrected shared Install-Update.ps1 frontend restart to Normal (background helpers remain Hidden), added responsive exact-PID/backend readiness and retained cleanup diagnostics. Normal native reproduction passes: ready2.71s, graceful exit2.86s, backend/locks/processes clean. Four focused smoke contracts pass. Third remote installer and independent portable recovery run follows; no claim of completed install gate yet.

Clean Windows CI36963975414 at aec9a0a3 passed publish/integrity/native11, all five actual installer scenarios including visible upgrade restart and uninstall, and all four actual portable recovery scenarios. Both older baselines are digest-verified v0.2.0-beta.3. Evidence retained as electron-windows-evidence and validated installer/ZIP as electron-packages-win-x64; local reports .tmp/task38137-ci-passed. Updated checkpoint92 and release commands/limits. Packages unsigned; no local installation, release publication or merge.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Primary Electron Inno installer and portable ZIP retain product/update identity, self-contained backend/helper/provider, notices and verified source metadata without Avalonia UI runtime. Clean CI36963975414 passes native11, installer5 and portable4 recovery gates; local61 Windows contracts and4 startup-window contracts pass. The real hidden-update-restart defect was fixed and checked with native visible-window/backend readiness. Evidence: checkpoint-ninety-two.md. Continue to Linux packaging under the explicit six-task batch authorization.
<!-- SECTION:FINAL_SUMMARY:END -->
