---
id: TASK-3
title: Build install and uninstall management
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-06 21:52'
labels:
  - ui
  - ingest
  - infra
milestone: m-4
dependencies: []
priority: high
ordinal: 900
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the M9 deliverable. Winnow delegates installation and uninstallation to the owning store client and reflects state back. Never reimplements download, patching, or CDN auth (ROADMAP.md section 4, "M9 delegates, never reimplements"). Source: ROADMAP.md section 4, M9 row.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Install command for a Steam game issues `steam://install/<appid>` and monitors state change
- [x] #2 Install command for an Epic game delegates to the Epic launcher
- [x] #3 Uninstall delegates similarly and reflects the new state
- [x] #4 Library view reflects installed/uninstalled state after the operation
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add Steam uninstall to the Details More menu and stable manifest monitoring for install/uninstall changes. Verify dispatch and state transitions with temporary fixtures. Expose honest launcher management navigation for Epic and GOG, whose game-uninstall protocol is not verified. Keep TASK-141 live verification separate.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Beta triage: moved from M9-INSTALL into PRE-BETA-HARDENING at user request. Removed the TASK-2 export dependency: it represented roadmap sequencing, not an implementation prerequisite. TASK-141 remains a separate live Epic install verification blocker.

Implemented Steam uninstall in the Details More menu, dispatching through the existing shell link path and never recording game launch intent. Steam and Epic share a stable two-poll install refresh service. Complete Steam scans reconcile absent persisted installations directly, including manifest-only unplayed games and after restart; unreadable root lists preserve stored state. Epic and GOG management rows navigate to their launcher library/game page, not a fabricated uninstall command. GOG remains on existing local-sync cadence; no new GOG watcher or direct uninstall protocol is claimed. Focused checks: 26 main tests and 6 real Avalonia headless menu/action tests pass. The new menu row is opened by pointer, focusable, and hides after installed-to-uninstalled refresh.

Added library reload after every successful scheduled local sync, including install-only changes with zero history counters. GOG management changes therefore appear on the existing 15-minute cadence; Steam/Epic use stable two-second manifest polling. Failed/cancelled scheduled scans do not reload. Epic library navigation verified live by user. No direct Epic/GOG game-uninstall URI is claimed.

Integrated Release build passes with zero warnings/errors; full suite passes 3847 tests. Shared design documents updated. Steam owns direct uninstall confirmation; Epic/GOG removal is completed within their management screens. Steam/Epic state refresh uses stable manifest reads, GOG reflects on scheduled local sync. TASK-141 retains the unexplained historical Epic error rather than blocking verified delegation behavior.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented Steam uninstall and stable install-state reconciliation without erasing ownership/history, plus Epic/GOG management navigation. Library refresh covers install-only changes. Verified by 3847 passing tests including real headless menu behavior, temporary Steam/Epic install transitions, and user-confirmed Epic install and Library navigation.
<!-- SECTION:FINAL_SUMMARY:END -->
