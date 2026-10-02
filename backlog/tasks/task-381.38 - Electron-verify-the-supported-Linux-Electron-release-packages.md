---
id: TASK-381.38
title: 'Electron: verify the supported Linux Electron release packages'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:50'
updated_date: '2026-10-02 06:18'
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

Retain all five portable cases, including actual copied previous-release helper, and establish the exact executable AppArmor profile before upgrading the stable portable path.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Preparation during Windows packaging identified a Linux startup risk to verify: BackendInstallationLease currently creates its adjacent lock for every release manifest, including /opt/winnow package-managed installs. Debian non-root launch cannot assume parent /opt is writable. Assess managed and read-only backend lease behavior before Linux smoke; do not bypass Chromium sandbox to make startup pass.

Implementation paused at safe boundary because37review found legacy embedded updater still uses hidden restart. Preserve prepared Linux baseline scripts/probe edits; no Linux completion claims.

Read-only optional macOS audit: secondary package.json has DMG target and backend publisher recognizes osx-x64/osx-arm64, but primary publisher/verifier and release matrix exclude macOS. No macOS CI, signing/notarization or device/package evidence. Secret persistence remains Windows DPAPI with no Keychain; in-memory sign-in paths do not establish device support. macOS updater remains release-page-only and package config has no URL association declaration. Document optional DMG as unverified/outside supported release matrix; no support expansion.

Resuming after37 completed with final prior-helper CI36967166296. Linux is now the sole active implementation task; actual install/upgrade/remove runs remain restricted to disposable Ubuntu GitHub runners.

Debian previous-release selector now checks official asset URL, positive size and SHA-256 before accepting a baseline; five offline contracts pass. Linux CI job drafted for restricted Ubuntu user namespaces, primary build/integrity, desktop/fullscreen native probes, actual previous Debian upgrade/removal, all five portable scenarios, and native/Proton sessions. Shared portable smoke establishes/removes an exact stable executable sandbox profile and probes both surfaces after all three successful upgrade cases. Seven existing cross-platform helper/evidence contract groups still pass. Actual Ubuntu run pending implementation integration.

Shared managed/read-only startup lease policy implemented; existing busy locks and no-lease journals fail closed. Windows focused Update.Tests87/87 passed,0skipped, including18new real-ACL/managed/journal/exclusion cases; logs .tmp/task38138-leases.log and .tmp/task38138-test-results/task38138-leases.trx. Known real copied-parent fixture remains excluded from this focused gate and assigned41. Packaged Linux probe syntax/format/full TypeScript checks pass; actual kernel/process/native/activation evidence pending UbuntuCI. Read-only integration review found no concrete issues in baseline/workflow/sharedportable wiring. Checkpoint93 records qualified scope and optionalmacOSaudit.

Linux packaging source complete: exact-path sandbox setup/managed hooks, 0755 chrome-sandbox normalization, ELF/hash/declared dependency-closure verification, real old-schema linked work/release/ownership comparisons, Xvfb/Openbox wrapper and native probes. Offline package20cases:17passed onWindows with3 explicitlyLinux-only (case-sensitive names,Unixmodes,symlinks); shell/Python syntaxpass. Actual UbuntuCI will execute all20 and installation/native/recovery checks. No local software installed.

First Ubuntu run36969598691 at136342b9 passed87/87 focused lease/recovery tests with0skips, primary publish691files,16package mutations,5Debian baseline contracts andall20Linux package contracts. It stopped at syntax-only AppArmor parsing because the parser tried its root-owned /var/cache/apparmor cache. No native Linux launch occurred. Fix is cache-free syntax validation; keep kernel/sandbox policy intact. Full log .tmp/task38138-linux-first.log. Companion Windows native11 passed aftersharedpolicychange; remainingWindowsinstallation stillrunning.

Second Ubuntu run36970126301 atc49782af passes cache-freepreflight and launches desktop/fullscreen under exact AppArmor profile. Reports establish visible native1920x1080fullscreen, ownbackend/libraryread, rendererNoNewPrivs1/Seccomp2 andcleanclose. Probe failed parsing Chromium rewritten /proc commandline (one combinedstring, type null); correcting parser and forbiddenflag detection without looseningkernelassertions. Actual WM_CLASS is lowercase winnow forbothinstance/class, so Electron desktopmetadata willmatchit. Reports .tmp/task38138-linux-second-evidence; log .tmp/task38138-linux-second.log. Fullscreenreadiness willrewaitaftermodechange; screenshotobserved transientPreparingfullscreenbeforeparserfailure.

Chromium title parser corrected with24 passing offline contracts; renderer/main forbidden switches and kernel sandbox assertions remain strict. Post-mode and pre-capture waits require startup presentation absent. Full TypeScript, syntax, formatting checks pass. Desktop entry now uses measured lowercase winnow; actual production writer regression preserves Avalonia uppercase. Package contracts18passWindows/3Linux-only (21total). Companion Windows job36970126301 now fully passed native11,installer5,portable5. Preparing corrected Ubuntu rerun.

Third Ubuntu run36971220965 atb677ec93 passes both native desktop/fullscreen probes, all21Linux contracts,24commandline cases and Debian/tar package creation. Root reviewed both full rendered screenshots. Actual beta.3 managed install and upgrade succeed before dependency closure rejects optional-looking .NET libcoreclrtraceptprovider.so missing liblttng-ust.so.0; investigating exact upstream/runtime policy rather than skipping ELF checks. Portable external-data helper stage/apply succeeds with journalReady phase5 and noFailure, then smoke exact child identity comparison refuses cleanup; investigating recorded versus observed process identity without unrelated process termination. Logs .tmp/task38138-linux-third.log; evidence .tmp/task38138-linux-third-evidence. Managed/remove/recovery/session qualification still pending.

Targeted corrections verified locally: optional LTTng classification has seven new negative/closure contracts; package suite25passWindows/3Linux-only (28total). Linux portable cleanup now witnesses exact helper-parent and child kernel birth/executable plus unchanged child-record bytes; Windows UTC comparison unchanged.33offline identity/diagnostic contracts and all7existing packaged helper/evidence groups pass; logs .tmp/task38138-portable-identity.log and .tmp/task38138-portable-existing-contracts.log. CI also runs actual own /proc identity check. Product/installer diagnostic retention excludes Chromium profile persistence and retains child comparison evidence. Third run companion Windows job fully passed again. No local installs or native launches; corrected Ubuntu rerun next.
<!-- SECTION:NOTES:END -->
