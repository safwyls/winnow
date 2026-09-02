---
id: TASK-72
title: Add a data-directory override so the app can run against a throwaway database
status: To Do
assignee: []
created_date: '2026-09-02 15:17'
labels: []
dependencies: []
ordinal: 99000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
There is no supported way to launch Winnow against a database other than the users real one. WinnowDataLocation resolves the data directory through Environment.GetFolderPath(SpecialFolder.LocalApplicationData); on Windows .NET resolves known folders through the shell API and ignores the LOCALAPPDATA environment variable, so setting that variable before launching does nothing and fails silently. --seed-sample is not an alternative: it only fills a database that is already empty.

The consequence is that any visual verification of a UI change - screenshotting the running app, which is how the TASK-71 defects were found in the first place - runs against real data, and any click that writes is a real write to the users library. That happened during TASK-71 (one same_game link, reversed through the products own Undo and verified restored), and it will happen again.

Wanted: an explicit override, honoured before GetFolderPath is consulted. A --data-dir argument or a Winnow-specific environment variable both work; the argument is easier to see in a launch command and harder to leave set by accident. It must cover the database, the sidecars and the cover cache, since WinnowDataLocation owns all of them, and it must not disturb the Hoard-to-Winnow migration path that the same class carries.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Launching with an explicit data-directory override creates and uses a database at that path, leaving the real one untouched
- [ ] #2 The override covers the database, its sidecars and the cover cache, not the database alone
- [ ] #3 The legacy Hoard directory migration still behaves correctly under an override and is not run against the real directory
- [ ] #4 An override pointing somewhere unusable fails loudly at startup rather than silently falling back to the real directory
<!-- AC:END -->
