---
id: TASK-26
title: Establish CI gates
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - infra
milestone: m-4
dependencies: []
priority: high
ordinal: 26000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
No CI pipeline exists. Restore, build, test, analyzers, migration-hash verification, and dependency advisories must gate every change before the next milestone boundary. Finding F43. Source: stabilization-2026-08-28.md Group 2. Trigger: before the next milestone boundary.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 CI runs `dotnet restore`, `dotnet build`, `dotnet test` on every push
- [ ] #2 Roslyn analyzers are enabled and must pass
- [ ] #3 A migration-hash verification step detects edits to shipped migrations
- [ ] #4 Dependency advisory scanning is enabled
<!-- AC:END -->
