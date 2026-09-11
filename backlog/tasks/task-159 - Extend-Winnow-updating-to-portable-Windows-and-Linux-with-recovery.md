---
id: TASK-159
title: Extend Winnow updating to portable Windows and Linux with recovery
status: To Do
assignee: []
created_date: '2026-09-08 04:59'
updated_date: '2026-09-11 13:58'
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: ApplicationUpdater gates staging on IUpdateInstaller.IsSupported; WindowsUpdateInstaller supports only a matching registered Windows installation. docs/releases.md documents manual portable/Linux routes and no automatic rollback. TASK-158 supplies the completed installed-Windows baseline; this task's recovery scope remains unimplemented.
<!-- SECTION:NOTES:END -->
