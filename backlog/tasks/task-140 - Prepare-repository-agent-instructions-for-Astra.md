---
id: TASK-140
title: Prepare repository agent instructions for Astra
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 16:40'
updated_date: '2026-09-06 16:44'
labels: []
dependencies: []
type: chore
ordinal: 167000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Remove exclusive prose authorship and audit active Claude and Codex agent instructions for use with Astra, preserving domain constraints and existing work.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Agents can write and complete their own documentation and comments without a docs-writer dependency
- [x] #2 Codex agent definitions parse and retain the six domain roles without model pins
- [x] #3 Shared instructions, frontend skill and Backlog hook fit the repository and Codex tool contracts
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Audit active instruction surfaces and official Codex schemas; centralize writing and delegation guidance; remove docs-writer definitions; correct portability and hook issues; validate configuration, hook behavior and instruction consistency.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified all six TOML roles with Python tomllib, required fields and exact body parity with Claude peers. Both docs-writer definitions and active prose delegation requirements are removed. The two skill copies are identical; their unchanged frontmatter was manually reviewed. The bundled skill validator could not run because PyYAML is absent. Exercised the configured Windows hook command in 12 cases covering blocked add/update/delete/move, absolute/relative/backslash/normalized paths, CRLF and nested cwd, plus permitted non-Backlog files. All passed. Scoped git diff --check passed. No application build was needed for instruction and hook changes.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed exclusive prose authorship, centralized concise writing guidance, retained six model-inheriting domain roles, scoped the design skill to Winnow and corrected the Codex Backlog hook. Validated TOML parsing, role and skill parity, and 12 hook behavior checks. Existing unrelated work was preserved.
<!-- SECTION:FINAL_SUMMARY:END -->
