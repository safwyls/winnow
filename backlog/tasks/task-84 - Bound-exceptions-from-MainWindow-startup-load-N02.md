---
id: TASK-84
title: Bound exceptions from MainWindow startup load (N02)
status: Done
assignee:
  - '@safwyl'
created_date: '2026-09-04 00:40'
updated_date: '2026-09-04 00:53'
labels:
  - infra
dependencies: []
priority: high
ordinal: 111000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
MainWindow.OnOpened is async void and sequences load-bearing startup work (library load, merge queue, display load, feed load) with no try/catch. Any exception escapes onto the UI thread and kills the process at startup - the same class as fixed F36, one layer up. Remediation: wrap the body in a try/catch that logs and leaves the shell standing, mirroring the error boundary Program.cs puts around its startup task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Exceptions from the library load, merge queue load, display load, or feed load inside OnOpened are caught and logged, never escaping the async void
- [x] #2 The shell window stays open after a startup load failure, showing whatever last succeeded
- [x] #3 Build passes with dotnet build from the repository root
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Wrap the OnOpened body in a try/catch after base.OnOpened(e); 2. Log through the host's ILoggerFactory (Program.AppHost), with a Trace fallback when no host exists (previewer); 3. Leave the shell standing on whatever last succeeded - no rethrow, no process teardown; 4. Build and run tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The existing OnOpened body moved verbatim into a private LoadOnOpenAsync (no logic change, no reindentation churn) and OnOpened now wraps it: catch OperationCanceledException silently (window closed mid-load, same benign shutdown race Program.cs tolerates) and catch Exception through LogStartupLoadFailure, which logs via the host's ILoggerFactory and falls back to System.Diagnostics.Trace when there is no host (previewer) or the host answers nothing (shutdown race). Verified with a scripted e2e: a SQLite db whose SchemaVersions journal claims all 22 migrations applied but whose data tables are absent makes DatabaseInitializer pass and the window open, then the library load throws SqliteException 'no such table: ownerships'. With the boundary, the process STAYS ALIVE and logs 'Startup load failed; the shell stays standing on whatever last succeeded.'; zero unhandled exceptions. Side observation for a future finding: the same trap db kills the process EARLIER, in App.OnFrameworkInitializationCompleted via ThemeService.LoadAsync reading the settings table, before the window exists - a separate unhandled site outside this task's scope.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Bounded MainWindow.OnOpened's async void startup sequence (library load, merge queue, display load, feed) with a try/catch mirroring Program.cs's F36 boundary: OperationCanceledException treated as shutdown, everything else logged through the host logger (Trace fallback for the previewer and shutdown races) with the shell left standing on whatever last succeeded. Verified with a scripted failure injection - a journal-only database that passes migrations but has no data tables: the library load threw SqliteException, the boundary logged it, the process stayed alive, zero unhandled exceptions. Build clean (0 warnings), full suite green (2800 + 145).
<!-- SECTION:FINAL_SUMMARY:END -->
