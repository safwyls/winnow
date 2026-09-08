---
id: TASK-156
title: Version Winnow and complete GitHub release verification
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-08 03:19'
updated_date: '2026-09-08 03:23'
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
- [ ] #1 Settings displays application version and source build identity
- [ ] #2 Development, CI and tagged packages use consistent version metadata
- [ ] #3 GitHub CI and both installer smoke checks pass and a draft release contains verified assets
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect current packaging and CI failures; add central version metadata and Settings display; fix observed gate failures; verify locally, push a branch and verify GitHub workflows; create a beta draft through the gated tag workflow.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added Version.props, assembly-derived Settings build identity, package metadata validation and release version checks. Fixed GitHub failure caused by a navigation test racing its temporary database teardown; 45 related tests pass. Release solution build passes with zero warnings/errors; complete tests and GitHub release verification pending.
<!-- SECTION:NOTES:END -->
