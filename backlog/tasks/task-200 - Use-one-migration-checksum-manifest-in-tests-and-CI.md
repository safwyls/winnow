---
id: TASK-200
title: Use one migration checksum manifest in tests and CI
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 xUnit and CI consume the same authoritative append-only hashes.json manifest; the redundant registry is removed or mechanically derived.
- [ ] #2 Baseline immutability, manifest membership and canonical UTF-8/newline behavior remain enforced.
- [ ] #3 Existing mutation scenarios still fail for altered, missing and unrecorded SQL; the documented migration workflow is sufficient to pass all checks.
<!-- AC:END -->
