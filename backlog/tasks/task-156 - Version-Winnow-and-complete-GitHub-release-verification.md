---
id: TASK-156
title: Version Winnow and complete GitHub release verification
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 03:19'
updated_date: '2026-09-08 03:45'
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
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added Version.props, assembly-derived Settings build identity, package metadata validation and release version checks. Fixed GitHub failure caused by a navigation test racing its temporary database teardown; 45 related tests pass. Release solution build passes with zero warnings/errors; complete tests and GitHub release verification pending.

All 4,012 local tests passed after the accessibility correction; the two Linux-only tests correctly skip on Windows. Main branch protection applied and read back from GitHub: strict Windows/Linux checks, PR required, zero mandatory approvers, administrators included, force pushes/deletion disabled. Release candidate beta.2 is undergoing gated GitHub verification; beta.1 was stopped before any release was created.

Tagged Windows and Linux package/smoke jobs passed in run 34183493083. Downloaded both package artifacts and verified archive release-info.json values (0.1.0-beta.2, b648cb000dec96061c25a290bbd25beb1185742f); Windows assembly ProductVersion matches and FileVersion is 0.1.0.0. Computed hashes for all four assets; awaiting GitHub draft/checksum generation after the Windows test gate.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added shared application versioning and selectable version/source identity in Application settings. Fixed the CI database teardown race. All 4,012 local tests pass, and GitHub tagged release run 34183493083 passed Windows CI, Linux sessions and both installer smoke checks. The beta.2 draft contains Windows/Linux installers and portable archives plus SHA256SUMS; downloaded assets match every checksum. Main protection requires up-to-date PRs and Windows/Linux CI, including for administrators; releases remain tag-triggered.
<!-- SECTION:FINAL_SUMMARY:END -->
