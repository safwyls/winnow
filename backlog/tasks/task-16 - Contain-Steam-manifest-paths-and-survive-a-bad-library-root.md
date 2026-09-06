---
id: TASK-16
title: Contain Steam manifest paths and survive a bad library root
status: Done
assignee:
  - '@steam-ingest'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 21:28'
labels:
  - ingest
milestone: m-4
dependencies: []
priority: high
ordinal: 300
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Steam manifest path resolution does not contain paths to the library root, and a bad library root can abort startup entirely instead of degrading gracefully. Finding F25. Source: stabilization-2026-08-28.md Group 2. Trigger: next Steam ingest change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Manifest paths are validated against and contained within the library root
- [x] #2 A malformed or inaccessible library root logs a warning and skips that root, not abort startup
- [x] #3 A test with a nonexistent library root demonstrates graceful degradation
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Validate canonical install paths beneath steamapps/common; isolate root validation and enumeration failures, preserving unknown install state on incomplete scans. Add fixture-based traversal and bad-root regression tests and run Steam tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Review follow-up: canonicalize the primary root before constructing paths; expose root-list read completeness so malformed files and entries missing path cannot imply an uninstall. Added two fallback-root regression cases. Re-ran 67 focused parser/source tests, all passing.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Canonical install directories must stay beneath steamapps/common; rooted paths, traversal and the common root itself are rejected with a warning. Each library root is validated and enumerated inside its own failure boundary. Missing or unreadable roots retain unknown install state for playtime-only candidates. All 21 SteamLibrarySourceTests passed, including five fixture-derived path attacks and a missing secondary root.
<!-- SECTION:FINAL_SUMMARY:END -->
