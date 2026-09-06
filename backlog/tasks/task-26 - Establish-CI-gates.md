---
id: TASK-26
title: Establish CI gates
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 23:45'
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

Investigate hosted Windows Test-step duration using retained reports; batch expensive fixture setup without changing assertions, expose test progress in CI, and verify the hosted run.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Workflow passes actionlint 1.7.12. Fresh dotnet restore --force-evaluate --no-cache -warnaserror passed with NuGet auditing enabled for direct/transitive packages; Release build with SDK analyzers and warnings-as-errors passed. Migration verifier and isolated mutation tests passed. Full Release test run follows ingest integration. No remote push or branch-protection change was made; first hosted workflow execution remains pending.

Final integrated Release build passed with zero warnings and errors; all 3813 tests passed across four projects. Corrected new Steam fixtures to the repository-approved fake account ID after the hygiene check caught an unapproved invented ID.

Hosted reports show two passing recommendation tests taking 5m38 and 4m22; Windows main suite remains active. Reopened to diagnose and complete hosted verification rather than treating a quiet Test step as success.

Retained TRX identified four expensive fixture setups: 170-game maturity history, 200-game shortlist, 1200 cache writes, and 1000 identity-model games. Wrapped setup writes in existing unit-of-work scopes committed before measured reads/assertions; retained all counts/assertions and left the deliberate 2003-lease legacy benchmark unchanged. 28 affected tests pass locally. Added normal console test progress alongside TRX and five-minute hang diagnostics.

The cached-library comparison also paid for repeated WAL teardown because its test factory is unpooled while the app uses pooling. Holding one idle connection during that test preserves 1000 games and all 2003 legacy leases, without a read transaction or altered assertions. Two snapshot tests pass; the measured test is 1.915s locally (legacy1740.9ms/2003leases, bulk18.5ms/1lease), versus49.655s in the earlier full local report. Current hosted run is being allowed to finish before this follow-up is pushed.

Hosted CI run 34066965706 at d021b6e completed successfully: Windows 3932 passed, two Linux-only facts skipped there; Ubuntu two passed. Windows Test took13.8677minutes and now reports individual test progress. The former 5m38 maturity case completed in4s and former4m22 shortlist case in3s; cache bulk test1s, identity realistic-library test35s. The additional cached-read WAL keeper was separately verified with both snapshot tests passing locally; production code is unchanged.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Windows and Ubuntu CI passed at https://github.com/safwyls/winnow/actions/runs/34066965706. Batched four expensive fixture setups, preserved workload sizes and assertions, and enabled visible test results and hang diagnostics. All 3,932 Windows tests and both Ubuntu smoke tests passed. The Windows Test step took about 14 minutes. The final cached-read test-only adjustment also passed its two focused local tests.
<!-- SECTION:FINAL_SUMMARY:END -->
