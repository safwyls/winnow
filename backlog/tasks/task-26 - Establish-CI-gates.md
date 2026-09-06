---
id: TASK-26
title: Establish CI gates
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 21:34'
labels:
  - infra
milestone: m-4
dependencies:
  - TASK-31
priority: high
ordinal: 700
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
No CI pipeline exists. Restore, build, test, analyzers, migration-hash verification, and dependency advisories must gate every change before the next milestone boundary. Finding F43. Source: stabilization-2026-08-28.md Group 2. Trigger: before the next milestone boundary.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 CI runs `dotnet restore`, `dotnet build`, `dotnet test` on every push
- [x] #2 Roslyn analyzers are enabled and must pass
- [x] #3 A migration-hash verification step detects edits to shipped migrations
- [x] #4 Dependency advisory scanning is enabled
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add a Windows GitHub Actions gate for pushes and pull requests using the .NET 10 SDK. Run restore with transitive dependency auditing, build with explicit SDK analyzers and warnings as errors, test all projects, and verify immutable migrations against the event baseline. Validate the same commands locally and document checks; hosted execution requires a later push.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Workflow passes actionlint 1.7.12. Fresh dotnet restore --force-evaluate --no-cache -warnaserror passed with NuGet auditing enabled for direct/transitive packages; Release build with SDK analyzers and warnings-as-errors passed. Migration verifier and isolated mutation tests passed. Full Release test run follows ingest integration. No remote push or branch-protection change was made; first hosted workflow execution remains pending.

Final integrated Release build passed with zero warnings and errors; all 3813 tests passed across four projects. Corrected new Steam fixtures to the repository-approved fake account ID after the hygiene check caught an unapproved invented ID.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added Windows push/PR CI for audited restore, analyzer-enabled Release build, all tests, immutable migration checks, and retained TRX results. Workflow lint and equivalent local checks pass. First hosted execution awaits a push; branch protection remains an administrator setting.
<!-- SECTION:FINAL_SUMMARY:END -->
