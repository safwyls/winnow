---
id: TASK-31
title: Add immutable hash verification for shipped migrations
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:54'
updated_date: '2026-09-06 21:25'
labels:
  - data
  - infra
milestone: m-4
dependencies: []
priority: high
ordinal: 600
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Shipped SQL migrations are append-only by convention, but nothing enforces immutability. A silent edit to a shipped migration can corrupt an existing database on upgrade. Finding F46. Source: stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Each shipped migration has a recorded hash
- [x] #2 Startup or CI verifies that no shipped migration's content has changed
- [x] #3 A test demonstrates that altering a shipped migration triggers a failure
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Record SHA-256 hashes for all current SQL migrations with normalized line endings. Add a PowerShell verifier that checks exact file membership and contents, and optionally compares the manifest with a git baseline to reject edits to previously recorded hashes. Add isolated mutation tests and document adding migrations. TASK-26 will wire verification into CI.
<!-- SECTION:PLAN:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Recorded normalized SHA-256 hashes for all 28 SQL migrations. Verifier checks exact membership/content and compares existing hashes to a git baseline, including bootstrap from a pre-manifest revision. Isolated tests reject changed SQL, missing/unrecorded files and simultaneous SQL/hash edits, while accepting matching files and CRLF. Current checkout and HEAD-baseline verification pass. CI invokes the verifier and mutation tests on push/PR; workflow passed actionlint. Hosted execution awaits push.
<!-- SECTION:FINAL_SUMMARY:END -->
