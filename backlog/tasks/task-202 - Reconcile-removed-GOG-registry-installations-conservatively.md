---
id: TASK-202
title: Reconcile removed GOG registry installations conservatively
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Ingest.Gog/GogLibrarySource.cs:147'
  - 'src/Winnow.Ingest.Gog/GogInstalledGameRegistry.cs:51'
  - 'src/Winnow.App/Services/LibrarySyncService.cs:202'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 233000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R14. Evidence: Source verified. GOG registry-only installations emit candidates while present. Removing a registry entry emits no candidate, and LibrarySyncService has no corresponding complete-inventory absence reconciliation for that source. The ownership can remain installed indefinitely. The registry interface also cannot distinguish a complete empty scan from an unreadable registry. Launch actions and Ready to play eligibility can remain stale after uninstall on machines without usable Galaxy data.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A complete registry inventory reconciles missing installations by clearing only install-specific facts while preserving ownership and history.
- [ ] #2 Unreadable or partial registry scans cannot produce false uninstalls.
- [ ] #3 Tests cover registry-only install/uninstall, restart, absent Galaxy data and inaccessible registry state; desktop/fullscreen actions refresh consistently.
<!-- AC:END -->
