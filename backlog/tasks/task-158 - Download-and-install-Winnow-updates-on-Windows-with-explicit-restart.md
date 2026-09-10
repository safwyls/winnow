---
id: TASK-158
title: Download and install Winnow updates on Windows with explicit restart
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 04:59'
updated_date: '2026-09-10 00:46'
labels:
  - app-updates
dependencies:
  - TASK-157
references:
  - packaging/windows/Winnow.iss
  - src/Winnow.App/Program.cs
documentation:
  - docs/releases.md
priority: medium
type: feature
ordinal: 190000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Level 2 of in-app updating: users of the installed Windows build can download a release inside Winnow and explicitly restart to install it. Preserve existing installations and user data. Portable Windows and Linux retain the notification and download-link experience until Level 3. Packaging technology is selected during execution with existing Inno Setup installations accounted for.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 An available update can be downloaded with progress, cancellation, retry, and an explicit Restart to update action; downloading never forces an exit or installation.
- [x] #2 Only the intended platform and release package can be executed after integrity and authenticity verification; missing or invalid verification data prevents installation and produces a useful error.
- [x] #3 Update shutdown bypasses close-to-tray and waits for workers and database connections to close; successful installation relaunches Winnow with the intended data directory and supported launch settings.
- [x] #4 Upgrades preserve the existing install location, library, credentials, covers, themes, and settings; portable or unsupported installations are detected and offered a safe manual update path.
- [x] #5 Interrupted downloads, installer cancellation or failure, locked files, and restart failures leave a usable existing installation or a documented actionable recovery path.
- [x] #6 Disposable Windows upgrade smoke tests exercise an older installed release and custom install location, verify data preservation and relaunch, and cover failure handling; release documentation describes the shipped update and recovery behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Stage and verify the official Inno installer. Explicit restart uses normal shutdown and an external helper that waits for process exit and verifies again before installation and relaunch. Add disposable upgrade and failure tests; document manual paths and recovery limits.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented installed-Windows Inno handoff with registered-path detection, repeated SHA-256 verification, locked payload, readiness handshake, process-exit wait, non-forcing Setup, explicit restart/data-dir preservation and manual recovery logs. Local tests pass; actual older-to-newer installer and failure smoke is wired into release CI and not executed against this machine. Portable and Linux remain safe manual paths under TASK-159.

First remote Windows smoke run caught nested JSON-array handling in the baseline-fetch script before any installer ran. Fixed Invoke-RestMethod enumeration and added an offline fixture regression check to the release preflight; local preflight passes. Linux release packaging/startup and native/Proton CI passed. Windows upgrade smoke rerun pending.

Preserved CI diagnostics confirmed bad-digest, cancellation, shutdown-timeout, locked-file, installation and relaunch scenarios reached expected outcomes; the updated app then hung on normal close. Reproduced locally with an isolated seeded data directory (>45 seconds). Removed UI synchronization-context capture from updater I/O and StopAsync, added a stopped-dispatcher regression, and rechecked real app exit: 0.15 seconds, exit 0. Added a one-shot handoff guard against repeated Restart actions. 32 core updater tests pass. Final CI rerun pending.

Final local Release suite passes 4367 tests with zero failures (same as Debug). Final code reviewed after shutdown fix with no remaining must-fix findings. General Windows repository CI remains pending at handoff; installer CI is verified on c920893.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Installed Windows can stage a GitHub-digest-verified update in the background and explicitly restart through the existing Inno installation. The helper waits for normal process exit, locks the verified payload, rejects locked binaries, preserves installation/data locations and supported launch arguments, and records manual recovery instructions. Fixed dispatcher-dependent shutdown and prevented duplicate handoffs. CI release run 34421921570 passed beta.5 to ci.41 upgrade, checksum rejection, cancellation, shutdown timeout, locked-file refusal, user-file preservation, relaunch and clean shutdown; Linux package smoke also passes. Full local solution suite: 4367 passed, two Linux-only skips; separate Linux native CI passes. General Windows CI is still running. Portable/Linux in-place replacement remains TASK-159.
<!-- SECTION:FINAL_SUMMARY:END -->
