---
id: TASK-156
title: Version Winnow and complete GitHub release verification
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 03:19'
updated_date: '2026-09-08 04:38'
labels: []
dependencies: []
ordinal: 188000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Give installed and development builds traceable versions in Settings and complete the CI and draft release path on GitHub.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Settings displays application version and source build identity
- [x] #2 Development, CI and tagged packages use consistent version metadata
- [x] #3 GitHub CI and both installer smoke checks pass and a draft release contains verified assets
- [x] #4 Main requires up-to-date pull requests and passing Windows and Linux CI, while releases remain tag-triggered
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect current packaging and CI failures; add central version metadata and Settings display; fix observed gate failures; verify locally, push a branch and verify GitHub workflows; create a beta draft through the gated tag workflow.

Apply GitHub main branch protection with required Windows/Linux checks, enforce it for administrators, and retain tag-triggered draft releases as requested.

Investigate final PR CI recommendation-host hang from the captured dump and fix its root cause. Raise Windows job budget to 45 minutes because general tests were still progressing at the 30-minute cutoff; retain five-minute inactivity diagnostics. Reverify and merge through protected CI.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added Version.props, assembly-derived Settings build identity, package metadata validation and release version checks. Fixed GitHub failure caused by a navigation test racing its temporary database teardown; 45 related tests pass. Release solution build passes with zero warnings/errors; complete tests and GitHub release verification pending.

All 4,012 local tests passed after the accessibility correction; the two Linux-only tests correctly skip on Windows. Main branch protection applied and read back from GitHub: strict Windows/Linux checks, PR required, zero mandatory approvers, administrators included, force pushes/deletion disabled. Release candidate beta.2 is undergoing gated GitHub verification; beta.1 was stopped before any release was created.

Tagged Windows and Linux package/smoke jobs passed in run 34183493083. Downloaded both package artifacts and verified archive release-info.json values (0.1.0-beta.2, b648cb000dec96061c25a290bbd25beb1185742f); Windows assembly ProductVersion matches and FileVersion is 0.1.0.0. Computed hashes for all four assets; awaiting GitHub draft/checksum generation after the Windows test gate.

Final PR run 34184642522 hit the 30-minute job limit. Artifact includes recommendation-host hang diagnostics, while general tests continued passing until cancellation. Reopened task for CI stability follow-up before merge; the beta.2 draft and previous tagged CI remain verified.

The minidump locates the recommendation stall in CrowdedLibrary fixture initialization inside SQLite writes. 151 of 155 tests had completed; four were waiting for fixture setup. Batched the same 217-game fixture through the existing transaction helper. All 155 recommendation tests pass in 19 seconds with five-minute hang detection enabled. Windows CI now has a 45-minute overall budget for slower runners; no checks or inactivity guards were removed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented shared versioning and selectable version/source identity in Application settings. Fixed the navigation-test database teardown race and batched the crowded recommendation fixture after dump analysis identified slow setup writes. All 4,012 local tests passed; all 155 recommendation tests passed after batching. GitHub CI runs 34186677252 and 34186679987 pass with the 45-minute job budget and unchanged five-minute inactivity guard. Tagged run 34183493083 passed CI and both installer smoke checks, creating the beta.2 draft; all four assets match SHA256SUMS. Main requires up-to-date PRs with passing Windows/Linux CI, including administrators; releases remain tag-triggered.
<!-- SECTION:FINAL_SUMMARY:END -->
