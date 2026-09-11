---
id: TASK-200
title: Use one migration checksum manifest in tests and CI
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 06:18'
labels:
  - architecture
  - review
dependencies: []
references:
  - src/Winnow.Data/Migrations/hashes.json
  - src/Winnow.Data/Migrations/checksums.txt
  - 'tests/Winnow.Tests/Enforcement/SchemaDisciplineTests.cs:30'
  - scripts/Verify-Migrations.ps1
documentation:
  - docs/architecture-review-2026-09-10.md
priority: low
type: chore
ordinal: 231000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R12. Evidence: Source verified. The documented workflow and PowerShell integrity verifier use Migrations/hashes.json, while SchemaDisciplineTests independently require checksums.txt with separate parsing. Adding a migration exactly as AGENTS describes can fail tests because an undocumented second registry also needs updating. Two authorities and duplicate canonicalization invite drift. This is distinct from existing TASK-65, which concerns a handwritten migration list in backup rewind tests.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 xUnit and CI consume the same authoritative append-only hashes.json manifest; the redundant registry is removed or mechanically derived.
- [x] #2 Baseline immutability, manifest membership and canonical UTF-8/newline behavior remain enforced.
- [x] #3 Existing mutation scenarios still fail for altered, missing and unrecorded SQL; the documented migration workflow is sufficient to pass all checks.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Compare xUnit and PowerShell manifest semantics. 2. Make hashes.json the sole checked manifest while preserving newline normalization and migration membership tests. 3. Remove the redundant registry, update any applicable docs and run migration integrity plus mutation/enforcement checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
SchemaDisciplineTests now parses hashes.json with duplicate/hash validation and the same CRLF normalization and relative names as CI. Removed checksums.txt. Focused schema enforcement tests passed in the 65-test Release run. Verify-Migrations against a21753b verified all32 scripts; Test-MigrationHashes passed content/membership/baseline/CRLF mutation cases.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Consolidated migration integrity on hashes.json; xUnit and CI share the manifest and preserve append-only baseline and content checks. All focused enforcement and mutation checks passed.
<!-- SECTION:FINAL_SUMMARY:END -->
