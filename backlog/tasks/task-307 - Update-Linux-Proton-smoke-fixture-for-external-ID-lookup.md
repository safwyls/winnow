---
id: TASK-307
title: Update Linux Proton smoke fixture for external-ID lookup
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-16 05:01'
updated_date: '2026-09-16 05:02'
labels: []
dependencies: []
type: bug
ordinal: 349000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
PR 20 Linux Proton CI fails because the smoke release repository returns no external IDs after executable indexing switched from release identities to external IDs.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Linux smoke repository provides the Steam external ID consumed by executable indexing, and native plus Proton smoke tests pass on Linux.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Update the smoke repository external-ID fixture; run the Linux smoke suite under WSL if available and push the fix to PR 20 for CI verification.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Corrected SmokeReleases.GetAllExternalIdsAsync to return its Steam app ID. Release build of the Linux smoke project passed locally with zero warnings/errors. WSL Fedora has no dotnet installation; real Linux execution will be verified by the PR job.
<!-- SECTION:NOTES:END -->
