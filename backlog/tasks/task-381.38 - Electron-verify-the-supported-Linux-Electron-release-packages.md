---
id: TASK-381.38
title: 'Electron: verify the supported Linux Electron release packages'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:50'
updated_date: '2026-10-02 04:47'
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
ordinal: 456000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Existing users have supported Linux desktop packages; replacing the frontend must preserve those installation paths and session behavior.

This task owns a remaining delivery/validation gate beyond the source-method inventory. It does not reassign contracts from the feature tasks.

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Supported Linux package/portable outputs contain the Electron UI and bundled backend/runtime with correct desktop integration and required native dependencies.
- [ ] #2 Disposable Linux install/start/upgrade/uninstall and session-tracking checks pass, preserving data and package-manager update behavior; unsupported environments are explicitly distinguished.
- [ ] #3 Existing optional macOS packaging is audited for build claims; any support expansion beyond current Winnow release scope remains explicitly out of scope.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Preserve Ubuntu24.04 x64 Debian and tar.gz release names, launcher, protocol and update routes. Bundle primary Electron/backend/helper/provider/notices and verify native dependencies and executable modes. Establish exact-path AppArmor user-namespace profiles for managed installation and explicit portable setup without disabling the Chromium sandbox or weakening global policy. Fix backend installation leasing for managed and genuinely read-only copies while retaining fail-closed pending-update behavior. Extend native package probes to Linux desktop/fullscreen, real own backend, activation, renderer sandbox and cleanup; retain all four portable recovery scenarios and actual managed install/upgrade/uninstall data checks on disposable Ubuntu runners. Verify native/Proton session suite. Audit optional macOS configuration without claiming support or publishing. Record evidence and complete before defaults/device/final tasks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Preparation during Windows packaging identified a Linux startup risk to verify: BackendInstallationLease currently creates its adjacent lock for every release manifest, including /opt/winnow package-managed installs. Debian non-root launch cannot assume parent /opt is writable. Assess managed and read-only backend lease behavior before Linux smoke; do not bypass Chromium sandbox to make startup pass.

Implementation paused at safe boundary because37review found legacy embedded updater still uses hidden restart. Preserve prepared Linux baseline scripts/probe edits; no Linux completion claims.

Read-only optional macOS audit: secondary package.json has DMG target and backend publisher recognizes osx-x64/osx-arm64, but primary publisher/verifier and release matrix exclude macOS. No macOS CI, signing/notarization or device/package evidence. Secret persistence remains Windows DPAPI with no Keychain; in-memory sign-in paths do not establish device support. macOS updater remains release-page-only and package config has no URL association declaration. Document optional DMG as unverified/outside supported release matrix; no support expansion.
<!-- SECTION:NOTES:END -->
