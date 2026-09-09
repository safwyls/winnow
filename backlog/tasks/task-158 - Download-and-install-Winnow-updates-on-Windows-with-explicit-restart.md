---
id: TASK-158
title: Download and install Winnow updates on Windows with explicit restart
status: To Do
assignee: []
created_date: '2026-09-08 04:59'
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
- [ ] #1 An available update can be downloaded with progress, cancellation, retry, and an explicit Restart to update action; downloading never forces an exit or installation.
- [ ] #2 Only the intended platform and release package can be executed after integrity and authenticity verification; missing or invalid verification data prevents installation and produces a useful error.
- [ ] #3 Update shutdown bypasses close-to-tray and waits for workers and database connections to close; successful installation relaunches Winnow with the intended data directory and supported launch settings.
- [ ] #4 Upgrades preserve the existing install location, library, credentials, covers, themes, and settings; portable or unsupported installations are detected and offered a safe manual update path.
- [ ] #5 Interrupted downloads, installer cancellation or failure, locked files, and restart failures leave a usable existing installation or a documented actionable recovery path.
- [ ] #6 Disposable Windows upgrade smoke tests exercise an older installed release and custom install location, verify data preservation and relaunch, and cover failure handling; release documentation describes the shipped update and recovery behavior.
<!-- AC:END -->
