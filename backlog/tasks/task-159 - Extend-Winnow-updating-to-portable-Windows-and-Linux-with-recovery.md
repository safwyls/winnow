---
id: TASK-159
title: Extend Winnow updating to portable Windows and Linux with recovery
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-08 04:59'
updated_date: '2026-09-11 19:06'
labels:
  - app-updates
dependencies:
  - TASK-157
  - TASK-158
references:
  - packaging
documentation:
  - docs/releases.md
priority: low
type: feature
ordinal: 191000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Extend the existing release checks and installed-Windows update flow to supported portable Windows and Linux distributions with tested recovery. Current portable Windows, Debian and portable Linux builds offer release links and manual update routes. Preserve those routes where automated updating is unsupported, and preserve the package manager boundary for managed Linux installations. Packaging and recovery design are execution-time decisions.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Portable Windows users can download, verify, apply, and restart into an update while preserving the portable location and selected data directory.
- [ ] #2 Supported Linux distribution formats have a documented and tested update route; package-managed installations use their package-management boundary and any required authentication is explicit.
- [ ] #3 Existing Windows and Linux distribution users have a documented transition path if packaging formats or installation layout change; unsupported environments retain useful download links.
- [ ] #4 Interrupted replacement, insufficient permissions or disk space, verification failure, and failed new-version startup have tested recovery behavior without silently discarding user data.
- [ ] #5 Recovery defines database migration compatibility and backup or restore behavior; an older binary is never automatically reopened against an incompatible migrated database.
- [ ] #6 Release CI tests actual older-to-newer upgrades and recovery on supported Windows and Linux environments using disposable data; documentation specifies the support matrix and recovery limits.
- [ ] #7 Desktop and fullscreen share update preferences, progress, failure/recovery state and explicit restart behavior; verify both presentation paths for the supported installation types.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Retain current ZIP, tar.gz and Debian layouts. Add a separately packaged helper for Windows x64 and portable Ubuntu 24.04 x64; registered Windows installations retain Inno and package-managed Linux retains the package manager. Validate release metadata, digest, archive paths and writable owned installation boundaries before staging beside the existing directory. After explicit Restart, wait for the exact app process and all database handles to close, create a pre-migration SQLite backup, and durably journal directory replacement while retaining previous binaries. Preserve the selected data directory, including when it lies inside the portable directory, and preserve only supported restart arguments. Require a new-version ready handshake after migrations and host startup. Before that handshake, recover interrupted replacement from the journal; after any possible migration, never automatically reopen old binaries against the changed database. Expose recovery details and same-or-newer reinstall guidance; restoring the paired pre-upgrade database is an explicit recovery operation. Exercise digest, permissions, disk-space, interruption, startup and backup/restore failures with temporary files, plus actual previous-release upgrades on disposable Windows and Ubuntu runners; verify shared desktop/fullscreen state. This helper and recovery architecture is a material decision and awaits plan approval before implementation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: ApplicationUpdater gates staging on IUpdateInstaller.IsSupported; WindowsUpdateInstaller supports only a matching registered Windows installation. docs/releases.md documents manual portable/Linux routes and no automatic rollback. TASK-158 supplies the completed installed-Windows baseline; this task's recovery scope remains unimplemented.
<!-- SECTION:NOTES:END -->
