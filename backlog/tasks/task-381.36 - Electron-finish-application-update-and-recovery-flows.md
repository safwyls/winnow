---
id: TASK-381.36
title: 'Electron: finish application update and recovery flows'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
updated_date: '2026-10-02 03:21'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies: []
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
documentation:
  - src/Winnow.Electron/README.md
  - design-system.md
parent_task_id: TASK-381
priority: high
type: task
ordinal: 454000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Downloading and restarting into a new Electron build must preserve the data directory and recover correctly after installation failure.

Owns 6 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Ui.Tests/UpdateInstallationTests.cs
- UpdateInstallationTests.LinuxAutomationUsesTheTestedDistribution [pending at split]
- UpdateInstallationTests.PortableRequiresMatchingManifestAppHostAndHelper [pending at split]
- UpdateInstallationTests.ManagedLinuxNeverUsesArchiveReplacement [pending at split]

tests/Winnow.Ui.Tests/WindowsUpdateInstallerTests.cs
- WindowsUpdateInstallerTests.RestartPreservesSelectedLibraryAndNoSyncWithoutRepeatingSeedOrLogin [pending at split]
- WindowsUpdateInstallerTests.PortableExecutableCannotClaimARegisteredInstallation [pending at split]
- WindowsUpdateInstallerTests.MissingInstallRegistrationIsUnsupported [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All original update download/install/cancel/restart and Windows installer contracts have equivalent Electron behavior and isolated failure/recovery tests.
- [x] #2 Desktop and fullscreen expose accurate progress and deliberate restart/cancel actions; packaged updates preserve user data, respect package-managed installations and recover interrupted upgrades.
- [x] #3 All 6 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Audit the six remaining original updater contracts, close concrete Electron policy or recovery gaps, execute focused updater/component/native checks, and record complete source mappings and milestone evidence. The user has authorized finishing TASK-381.36 through TASK-381.41 as one batch; proceed sequentially without the older per-task pause.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit found functional delivery gaps beyond the six policy mappings: Electron had only NSIS/AppImage paths and no existing archive recovery integration. Preserve the original Inno AppId and Windows ZIP/Ubuntu deb/tar formats. Added primary distribution selection, official digest-verified release downloads, existing helper staging/handoff and parent-bound frontend lease/readiness. Secondary NSIS/AppImage now obey registered path and Ubuntu/package-manager guards. Source inventory reaches 1688 ported, 706 retained-backend, 41 framework-specific, 0 pending/partial; all six exact source contracts have executed replacement assertions. Packaging smoke remains TASK-381.37/.38, not implied by source mapping.

Milestone commit 88627a7f. Final reviewed evidence: 233/233 Electron focused, 70/70 current .NET updater, 12/12 original source, 4/4 native; complete migration audit and build/typecheck pass. Continuing to TASK-381.37 under the latest six-task batch authorization.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Preserved all six original contracts and restored primary Inno/archive updater dispatch with verified downloads, registered installation checks, safe restart arguments, and a parent-bound portable lease/readiness handshake using the existing backup/recovery engine. Twelve original source cases, 233 focused Electron cases, 70 helper/recovery cases and four native checks pass; builds/typecheck and complete migration audit pass (1688 ported,706 retained-backend,41 framework-specific,0 pending/partial). Desktop/fullscreen screenshots reviewed; all owned processes closed. Actual installer/platform smoke remains the next two delivery tasks; no release published. See checkpoint-ninety-one.md and task38136 evidence ledgers.
<!-- SECTION:FINAL_SUMMARY:END -->
