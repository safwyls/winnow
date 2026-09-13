---
id: TASK-24
title: Route stored secrets through a platform secret store
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 21:12'
labels:
  - security
  - auth
milestone: m-4
dependencies: []
priority: high
ordinal: 100
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Stored credentials (Epic tokens, API keys) are not routed through a platform secret store. The README's blanket DPAPI claim was corrected by F03/F40 (Epic tokens are protected, but keys are plaintext). The remainder of F40: migrate plaintext rows to a platform secret store on first read. Source: stabilization-2026-08-28.md Group 2, finding F40 (remainder). Trigger: next credential or settings work.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All stored secrets are read from and written to the platform secret store (DPAPI/Credential Manager on Windows)
- [x] #2 Existing plaintext rows are migrated on first read
- [x] #3 A test confirms that no secret is stored in plaintext after migration
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Audit persisted credentials. Protect and migrate the optional Epic client secret. Recover leftover plaintext rows after interrupted Steam and IGDB migrations. Verify refusal and DPAPI round trips with focused tests, then build and test the solution.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit: Steam API keys, Steam sessions, Epic sessions, IGDB secrets and Twitch tokens already had DPAPI support. Added protection and first-read migration for optional Epic client-secret settings. Steam/IGDB/Epic readers now retry leftover plaintext cleanup after interrupted migrations; Twitch clears incomplete legacy token rows and retries failed storage loads. 457 focused tests passed, including real DPAPI with temporary SQLite and production Epic DI. Legacy user-entered values are preserved but unused when encryption is unavailable; backups and residual disk bytes are outside logical row migration.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed the persisted credential audit and closed the remaining Epic client-secret plaintext path. First reads protect legacy secrets and recover interrupted cleanup; incomplete legacy Twitch token rows are removed. Real Windows DPAPI and temporary SQLite tests verify migration, restart reads, encrypted values and refusal behavior. All 457 focused tests and all 3767 solution tests passed. Solution build passed with zero warnings/errors; diff check passed. Logical row migration does not scrub historical backups or residual disk bytes.
<!-- SECTION:FINAL_SUMMARY:END -->
