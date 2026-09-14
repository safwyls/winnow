---
id: TASK-289
title: Make fullscreen animation tests deterministic and audit CI pipeline
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-14 05:14'
updated_date: '2026-09-14 05:19'
labels: []
dependencies: []
ordinal: 331000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
PR CI still fails intermittently after activation fix. Latest run has one row navigation IsAnimating assertion failure under slow runner timing. Review all pipeline stages and eliminate timing-dependent verification without weakening behavior coverage.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Animation tests control frame time and retain intermediate motion and final-state assertions, including slow input dispatch.
- [ ] #2 Pipeline audit identifies actual failures and validates full Release suite and required PR checks.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Audit workflow/evidence/results and test scheduling; introduce injectable frame scheduling for row viewport tests, verify delayed dispatch deterministically, run Release tests, push and wait for CI.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audited all CI stages: only Search wheel IsAnimating failed; main 4725 passed on prior CI, other jobs passed. Added injected frame scheduler and controlled-frame tests retaining production callbacks, intermediate motion, stale generation and detach checks. Real-frame integration stays enabled. Local full Release build zero warnings/errors; 5972 passed, 2 Linux-only skipped. Evidence policy 53 checks, migration hashes/mutation checks and summary checks pass. Independent code review found no blockers. Pushing for full CI verification.
<!-- SECTION:NOTES:END -->
