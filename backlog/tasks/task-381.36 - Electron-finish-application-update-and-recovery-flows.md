---
id: TASK-381.36
title: 'Electron: finish application update and recovery flows'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
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
- [ ] #1 All original update download/install/cancel/restart and Windows installer contracts have equivalent Electron behavior and isolated failure/recovery tests.
- [ ] #2 Desktop and fullscreen expose accurate progress and deliberate restart/cancel actions; packaged updates preserve user data, respect package-managed installations and recover interrupted upgrades.
- [ ] #3 All 6 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
