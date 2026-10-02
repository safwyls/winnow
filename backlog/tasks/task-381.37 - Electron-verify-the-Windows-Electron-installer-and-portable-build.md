---
id: TASK-381.37
title: 'Electron: verify the Windows Electron installer and portable build'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:50'
updated_date: '2026-10-02 05:16'
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
- [x] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Build primary Electron Windows packages with existing Inno identity and ZIP layout, bundled independent backend/helper/providers/notices and version provenance. Preserve legacy upgrade/data behavior and protocol/shortcut/single-instance paths. Validate locally without installation, then execute actual install/upgrade/uninstall/recovery smoke on disposable GitHub runners, retaining artifacts and diagnostics. Do not publish a release; continue sequentially under the six-task batch authorization.

Qualify one successful portable upgrade using the exact copied helper from the selected published baseline, alongside the existing four current-helper recovery scenarios; retain baseline helper hashes and version.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Primary Windows package built with no Avalonia UI assemblies: 695 payload files, self-contained backend/helper, provider, notices and exact ASAR/version/source identity. Local build identity20, startup/activation25, package mutation16, pure Windows packaging54 and native packaged11 checks pass with no native skips. Native desktop/fullscreen package uses its own backend and releases real update lease. Disposable GitHub install/upgrade/uninstall/recovery execution is the remaining gate; workflow prepared. Evidence: docs/spikes/2026-09-28-electron-parity/checkpoint-ninety-two.md. No software installed locally and no release published.

Second disposable CI36961972888 passed actual package integrity/native11 checks and the four installer rejection scenarios, but real upgrade relaunch was invisible. Bounded local reproduction confirmed WindowStyle Hidden leaves a responsive Electron window invisible with healthy backend for60s. Corrected shared Install-Update.ps1 frontend restart to Normal (background helpers remain Hidden), added responsive exact-PID/backend readiness and retained cleanup diagnostics. Normal native reproduction passes: ready2.71s, graceful exit2.86s, backend/locks/processes clean. Four focused smoke contracts pass. Third remote installer and independent portable recovery run follows; no claim of completed install gate yet.

Clean Windows CI36963975414 at aec9a0a3 passed publish/integrity/native11, all five actual installer scenarios including visible upgrade restart and uninstall, and all four actual portable recovery scenarios. Both older baselines are digest-verified v0.2.0-beta.3. Evidence retained as electron-windows-evidence and validated installer/ZIP as electron-packages-win-x64; local reports .tmp/task38137-ci-passed. Updated checkpoint92 and release commands/limits. Packages unsigned; no local installation, release publication or merge.

Reviewable milestone210477dd records the passed Windows gate. Earlier implementation milestone aec9a0a3 is the exact successful CI source. Per-task pause is superseded by Finish the remaining six.

Post-gate review found material legacy-updater compatibility gap: CI uses repaired current checkout Install-Update.ps1 against old installed files, but published beta.3 embeds old Hidden restart script. Reopened37 to make new Electron startup visible under legacy SW_HIDE and execute actual baseline embedded helper. Strengthen installed seeded database comparison and portable recovery sentinel checks. Prior run remains valid current-helper evidence only; completion revoked until genuine prior-helper path passes.

Legacy compatibility corrected: Windows first ShowWindow is suppressed by SW_HIDE, bounded second explicit show succeeds; background launch stays hidden.21tray tests+typecheck+buildpass. Actual production devbundle legacy-hidden launch visible/healthy2238ms and gracefulexit2307ms,no forcedcleanup. Actual baseline embedded helper now extracted without assembly execution; digest/version evidence and installed seeded library comparison added. Five offline extraction/library/sentinel contract groupspass. NewCI will verify exact released helper upgrade.

Run36965801651 at7b60ee68 has passed the actual beta.3 embedded-helper installer upgrade/uninstall step, including visible restart, both presentation probes and seeded linked identity/ownership preservation. Final portable recovery step is still running. Native background startup2/2 also passed after bounded show retry. Exact beta.3 schema42migrations plus current3migrations retain fixture facts with integrity ok.

Bounded review confirms beta.3 PortableUpdateInstaller copies and runs its own old bundled helper. Current smoke runs new publish helper. Journal/readiness protocol is unchanged and old portable restart already Normal; no concrete defect found, but adding one actual prior-helper successful upgrade before completion.

Fifth portable case implemented with exact full beta helper bundle copied outside replacement tree; every copied file verified and no current-helper fallback allowed. Stage/apply executable path, PID, exit codes and host/DLL hashes retained. Seven offline evidence groups pass (both runtime copy shapes, forbidden destinations/missing bundle, actual invocation binding); all four existing recovery cases preserved. Installer embedded baseline gate36965801651 and native11 are green; added portable prior-helper execution requires next disposable run.

Final CI36967166296 at c847ebba5da4762ce51a98579f4613c6806ca640 passed native11, installer5 and portable5. Fifth portable case runs all199 exact beta.3 helper files outside replacement tree: bad-digest exit1, stage/apply exit0, journal ready5/no failure, native replacement probe passed and closed. Actual previous embedded installer helper also passed. Evidence .tmp/task38137-ci-final-passed and checkpoint92; implementation milestone c847ebba. User batch authorization supersedes per-task pause.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Primary Windows Electron installer and ZIP are verified, including bundled backend/runtime/provider/notices, actual previous-release updater compatibility, desktop/fullscreen launch and activation, preserved library data, uninstall and recovery. CI36967166296 passed11 native checks,5 installer scenarios and5 portable scenarios. Packages remain unsigned; no local installation or release publication.
<!-- SECTION:FINAL_SUMMARY:END -->
