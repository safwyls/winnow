---
id: TASK-307
title: Update Linux Proton smoke fixture for external-ID lookup
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 05:01'
updated_date: '2026-09-16 05:03'
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
- [x] #1 Linux smoke repository provides the Steam external ID consumed by executable indexing, and native plus Proton smoke tests pass on Linux.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Update the smoke repository external-ID fixture; run the Linux smoke suite under WSL if available and push the fix to PR 20 for CI verification.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Corrected SmokeReleases.GetAllExternalIdsAsync to return its Steam app ID. Release build of the Linux smoke project passed locally with zero warnings/errors. WSL Fedora has no dotnet installation; real Linux execution will be verified by the PR job.

Ubuntu CI run 35057943535 passed the native and synthetic Proton process smoke job on aef0539: https://github.com/safwyls/winnow/actions/runs/35057943535/job/104671949865. The previously failing attribution test now passes; Windows validation is still running.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed the stale Linux smoke repository fixture after the external-ID lookup change. Local Release build passed; Ubuntu CI confirmed native and Proton smoke success.
<!-- SECTION:FINAL_SUMMARY:END -->
