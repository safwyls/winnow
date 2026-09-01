---
id: TASK-65
title: >-
  Derive the migration list in DatabaseBackupTests.Rewind instead of
  hand-maintaining it
status: To Do
assignee: []
created_date: '2026-09-01 04:07'
labels:
  - infra
  - data
dependencies: []
priority: low
ordinal: 82000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Rewind hard-codes the migrations it undoes, so every new migration breaks a passing test until someone adds a line. This has happened three times in a row now, with 0015, 0016 and 0017. The agent that hit it third suggested two fixes. The cheap one derives the LIKE list from the migration folder, which removes the SchemaVersions half of the maintenance. The fuller one reads SchemaVersions for every script after 0011 and drops every table and index in sqlite_master that is not in a captured post-0011 baseline, leaving only the restoration of a table a migration rebuilt as irreducibly hand-written; 0016 and 0017 both rebuild merge_candidates, so that case is real and not hypothetical.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Adding a new migration does not require editing Rewind for the SchemaVersions half
- [ ] #2 The test still fails loudly if a migration rebuilds a table Rewind cannot restore, rather than passing silently
<!-- AC:END -->
