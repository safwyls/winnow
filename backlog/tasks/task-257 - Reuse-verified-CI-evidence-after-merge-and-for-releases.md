---
id: TASK-257
title: Reuse verified CI evidence after merge and for releases
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-13 16:06'
updated_date: '2026-09-13 16:13'
labels: []
dependencies: []
type: enhancement
ordinal: 299000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Avoid repeating the full suite for unchanged verified source across PR, main and release while retaining fresh dependency audits, migration checks and final package smoke tests.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 PRs run full CI; main and release reuse only recent successful trusted evidence bound to the tested checkout and matching runtime/dependencies.
- [ ] #2 Missing, stale, mismatched or inaccessible evidence falls back to full tests without changing required check names.
- [ ] #3 Fresh audits and migration checks still gate success; final release packages retain all smoke checks.
- [ ] #4 Automated policy tests cover valid reuse and rejection cases; workflow and release documentation describe limits.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Record immutable per-platform full-test evidence. Compare complete Git tree, resolved SDK, runner image and restored dependency fingerprints; bind PR evidence to its actual merged commit through GitHub metadata. Consume only successful same-repository runs within 24 hours. Never republish reused evidence. Keep required jobs and fresh validation steps; update release permissions/dependencies, add policy tests, and validate in a PR.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented per-platform full-only artifacts and provenance checks with 24-hour age. Reuse requires exact target commit or GitHub-verified merged PR checkout tree/parents, same SDK/image/dependency fingerprint, trusted same-repository workflow and both successful platform jobs in the same attempt. Reused runs never refresh evidence. Fresh restore/audit and Windows migration checks remain; release version gates verification. Local policy tests: 47 passed; real repository dependency hash and PR resolver entrypoint passed; actionlint 1.7.12 passed both workflows. No desktop/fullscreen application behavior changes.
<!-- SECTION:NOTES:END -->
