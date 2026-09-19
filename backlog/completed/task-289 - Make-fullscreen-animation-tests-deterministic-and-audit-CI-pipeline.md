---
id: TASK-289
title: Make fullscreen animation tests deterministic and audit CI pipeline
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 05:14'
updated_date: '2026-09-14 05:33'
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
- [x] #1 Animation tests control frame time and retain intermediate motion and final-state assertions, including slow input dispatch.
- [x] #2 Pipeline audit identifies actual failures and validates full Release suite and required PR checks.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Audit workflow/evidence/results and test scheduling; introduce injectable frame scheduling for row viewport tests, verify delayed dispatch deterministically, run Release tests, push and wait for CI.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audited all CI stages: only Search wheel IsAnimating failed; main 4725 passed on prior CI, other jobs passed. Added injected frame scheduler and controlled-frame tests retaining production callbacks, intermediate motion, stale generation and detach checks. Real-frame integration stays enabled. Local full Release build zero warnings/errors; 5972 passed, 2 Linux-only skipped. Evidence policy 53 checks, migration hashes/mutation checks and summary checks pass. Independent code review found no blockers. Pushing for full CI verification.

CI passed for code commit 644618c933c4264f34b91d27fc8cf2796decf7cc: https://github.com/safwyls/winnow/actions/runs/34809200276 . Windows build/tests/migration integrity and Linux native/Proton checks succeeded. Promo-site and both Windows/Linux release builds also passed. No retries, timeout increases, skips or weakened gates were introduced.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed wall-clock races from fullscreen row animation tests using controlled frame scheduling while retaining production callbacks and a real-frame integration test. Reviewed all pipeline stages and documented the actual timing failure. Verified full local Release suite (5972 passed, 2 Linux-only skips), zero-warning build, evidence/migration/reporting scripts, and successful complete PR CI at 644618c.
<!-- SECTION:FINAL_SUMMARY:END -->
