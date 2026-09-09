---
id: TASK-167
title: Organize game details into Overview Activity and Library tabs
status: Done
assignee:
  - '@codex'
created_date: '2026-09-09 02:46'
updated_date: '2026-09-09 03:08'
labels: []
dependencies: []
priority: medium
type: enhancement
ordinal: 199000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the user-approved compact-header mockup in the existing game details modal. Separate rediscovery, play activity, and library management while preserving existing actions, data, optional integrations, and return to the library.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A compact persistent header exposes identity, Play or Install, Add to list, and More above stable Overview, Activity, and Library tabs.
- [x] #2 Overview shows a concise personal summary with an Activity shortcut, description, screenshots, reception, and related games; Activity shows history, updates, and journal; Library shows copies, lists, acquisition, and disclosed technical facts.
- [x] #3 Metadata editing and game matching use focused content with a way back; existing commands, draft state, and dismissal behavior continue to work.
- [x] #4 Keyboard navigation, focus, scrolling, populated and sparse content remain usable at supported modal sizes, with regression coverage.
- [x] #5 The visual spec describes the final layout, replaced statements are recorded in decisions, and build and relevant tests pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Restructure the existing Avalonia details surface around a compact header and Overview, Activity, and Library content while preserving integrations and commands. 2. Add presentation state, keyboard and focus routing, and focused metadata tools without losing drafts. 3. Update headless interaction and structure coverage for navigation, sparse data, and small window layouts. 4. Rewrite the governing details spec to match verified behavior and archive replaced statements in decisions. 5. Build and run relevant and full regression tests, review the integrated changes, and commit the completed milestone.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented compact persistent header, Overview/Activity/Library tabs, independent scroll positions, collapsed summary/expansions/technical facts, and focused metadata tools preserving drafts. Metadata-driven reopen preserves the selected tab; ordinary opens default Overview. Updated the governing visual spec and archived replaced passages. Verification: full solution build passed with zero warnings/errors. Full regression run passed 3785 core tests, 160 recommendation tests and 108 cover tests; after adapting store-link interactions to the real More menu, the entire UI assembly passed 96/96. Two Linux-only smoke tests skipped on Windows. Rich/sparse headless captures checked at 1200x640 and 1280x820, including long header, all tabs, bottom content, menus and focused tools. Reviewer found no remaining source defect; final spec corrections applied. git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Reworked game details into the approved compact header and three-tab layout, with focused correction tools and retained navigation state. Verified 4149 passing tests across the solution assemblies, two platform skips, a clean build, and rendered keyboard/pointer checks at supported sizes.
<!-- SECTION:FINAL_SUMMARY:END -->
