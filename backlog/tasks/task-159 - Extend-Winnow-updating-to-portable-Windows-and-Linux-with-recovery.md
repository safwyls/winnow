---
id: TASK-159
title: Extend Winnow updating to portable Windows and Linux with recovery
status: To Do
assignee: []
created_date: '2026-09-08 04:59'
labels:
  - app-updates
dependencies:
  - TASK-157
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
Level 3 of in-app updating: extend the installed-Windows experience to supported portable Windows and Linux distributions, with tested recovery from interrupted replacement and failed startup. Distribution formats and updater framework remain execution-time decisions. Package-manager-owned Linux installations must have an explicit supported upgrade route rather than uncoordinated replacement of managed files.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Portable Windows users can download, verify, apply, and restart into an update while preserving the portable location and selected data directory.
- [ ] #2 Supported Linux distribution formats have a documented and tested update route; package-managed installations use their package-management boundary and any required authentication is explicit.
- [ ] #3 Existing Windows and Linux distribution users have a documented transition path if packaging formats or installation layout change; unsupported environments retain useful download links.
- [ ] #4 Interrupted replacement, insufficient permissions or disk space, verification failure, and failed new-version startup have tested recovery behavior without silently discarding user data.
- [ ] #5 Recovery defines database migration compatibility and backup or restore behavior; an older binary is never automatically reopened against an incompatible migrated database.
- [ ] #6 Release CI tests actual older-to-newer upgrades and recovery on supported Windows and Linux environments using disposable data; documentation specifies the support matrix and recovery limits.
<!-- AC:END -->
