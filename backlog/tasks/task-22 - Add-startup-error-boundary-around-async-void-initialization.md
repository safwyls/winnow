---
id: TASK-22
title: Add startup error boundary around async void initialization
status: Done
assignee:
  - '@claude'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-04 03:22'
labels:
  - infra
dependencies: []
priority: high
ordinal: 22000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The startup path contains `async void` initialization that can throw unobserved. An exception during initialization crashes without diagnostics. Finding F36. Source: stabilization-2026-08-28.md Group 2. Lands with F39 in the same startup pass.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All `async void` initialization paths are wrapped in an error boundary
- [x] #2 An exception during initialization is caught, logged, and surfaced to the user
- [x] #3 The app does not crash silently on a startup fault
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Survey what is already bounded. MainWindow.OnOpened (TASK-84/N02) and the Program.cs startup sync task both already carry catch-and-log boundaries; F36's original evidence is closed. The remaining hole is the synchronous startup spine: Program.Main wraps DatabaseInitializer.Initialize, host.Start, the Epic console/sign-in flows, the sample seeder and StartWithClassicDesktopLifetime in try/finally with NO catch, so a migration failure or a corrupt database dies as an unhandled exception in a WinExe -- no console, no message, no diagnostics.
2. Factor the console-or-message-box channel out of SingleInstanceGuard into a reusable StartupAlert service, so a fatal startup fault reaches the user by the same route the single-instance refusal already uses.
3. Wrap Program.Main's startup spine in a catch that logs through the host logger when it exists (bootstrap console logger when it does not), surfaces one sentence through StartupAlert, and sets a nonzero exit code. Keep OperationCanceledException as shutdown, not failure.
4. Bound App.ApplyStartupTheme's synchronous theme load so a corrupt stored preference or an unreadable settings table falls back to the default palette instead of killing the framework-initialization callback.
5. Tests: unit tests over the boundary's decision (fault surfaced, exit code set, cancellation not a failure) plus an enforcement source scan asserting the initialization async void paths still carry a try. Verify with a scripted failure injection against a deliberately broken database, as TASK-84 did.
6. Update AGENTS.md/docs only where the change makes existing text wrong. Do not build the rolling diagnostic log -- that is TASK-25.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Surveyed first: MainWindow.OnOpened (TASK-84/N02) and the Program.cs startup sync task were already bounded, so F36's original evidence was closed. The hole left was the SYNCHRONOUS startup spine - Program.Main wrapped DatabaseInitializer.Initialize, host.Start, the Epic flows, the seeder and StartWithClassicDesktopLifetime (and, through it, Avalonia's framework initialization, the theme read and the composition root) in try/finally with no catch. In a WinExe that is a process that ends with nothing written anywhere.

Landed: StartupAlert (the shared console-or-message-box channel, factored out of SingleInstanceGuard), StartupFailure (the boundary's decision: shutdown vs fault, the sentence, exit code 3 - distinct from the --data-dir refusal's 2), a catch around the startup spine in Program.Main with LoggerFactoryOrNull so a half-built host cannot make the boundary the second exception, and a non-fatal try/catch around the synchronous theme load in App.ApplyStartupTheme (tokens.axaml already carries every authored value, so a caught failure opens in the default palette rather than not opening).

MEASURED DEFECT FOUND EN ROUTE, and fixed for this channel only. In a console-less launch - Explorer double-click, reproduced with a wscript parent - Console.IsOutputRedirected is TRUE while the standard error handle is 0 and GetFileType returns FILE_TYPE_UNKNOWN. ConsoleAuthPrompt.HasConsole() therefore answers 'yes, there is a console' in exactly the launch that has none, so the sentence went to a dead handle and the message box was never reached. Proven with a throwaway WinExe probe. TASK-57 already tracks this for the shared helper and the Epic sign-in flows and still owns it; TASK-22 did not touch the helper, it gave StartupAlert its own narrower predicate (WrittenLineWouldBeRead: a console window, or a standard error handle that is a real file or pipe). Without that, AC2 would have been unmet for the ordinary launch.

Side effect worth knowing: routing SingleInstanceGuard.RefuseToStart through the same channel repaired TASK-23's second-copy refusal on the double-click path, which had the same dead-handle problem. Verified live - first copy up, second copy shows a blocking 'Winnow is already running' message box. That second copy still exits 0; that is TASK-23's pre-existing choice and was left alone.

VERIFICATION.

Scripted failure injection - a 4 KB file of garbage bytes as winnow.db in a throwaway --data-dir, which makes DatabaseInitializer.Initialize throw SqliteException 26 ('file is not a database') on the synchronous spine.

1. Terminal launch (dotnet run -- --data-dir C:\Temp\winnow-fault --no-sync): the boundary caught it, LogCritical wrote the full stack through the host logger, stderr carried 'Winnow could not start. / InvalidOperationException: Refusing to migrate ... / Your library is at C:\Temp\winnow-fault and has not been changed by this run.', process exited 3.
2. Console-less launch (Winnow.exe run from a wscript parent, i.e. the Explorer double-click path): a message box titled 'Winnow could not start' appeared and BLOCKED - observed alive across 29 polls - and the process exited 3 once dismissed. Before the WrittenLineWouldBeRead fix the same run exited 3 in ~200 ms showing nothing at all, which is the silent crash AC3 forbids.
3. Second-copy refusal after the refactor: first copy running against a good throwaway directory, second copy launched console-less, blocking 'Winnow is already running' box observed.
4. Console-detection measurement, throwaway WinExe probe: console-less parent gives outRedir=True, hErr=0, GetFileType=0; a terminal or a redirect gives a valid handle with FILE_TYPE_CHAR/DISK/PIPE.

Automated: dotnet build clean, 0 warnings (TreatWarningsAsErrors). dotnet test - Winnow.Tests 2824 passed, Winnow.Recommend.Tests 145 passed, Winnow.Covers.Tests 70 passed, 0 failed. The three test projects had to be run against separate BaseOutputPath values; they race over a shared one and the file-lock error is unrelated to this change.

New tests: StartupErrorBoundaryTests (7) over the boundary's decision - fault surfaced with the startup exit code, the sentence naming exception type, message and data directory, an unresolved data directory still readable, cancellation treated as shutdown and not surfaced, a fault too early to have a logger still surfaced, the boundary surviving both a throwing logger and a throwing channel, and the exit code being distinct from 2. Enforcement/StartupBoundaryTests (4) - a source scan requiring every 'protected override async void' in Winnow.App to carry a catch (with a non-vacuity assertion so a broken pattern cannot pass silently), plus three markers holding the spine's boundaries in Program.cs, MainWindow.axaml.cs and App.axaml.cs.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Closed the startup error boundary. F36's original evidence (MainWindow.OnOpened) was already fixed by TASK-84; the hole left was the synchronous startup spine, which Program.Main wrapped in try/finally with no catch - so a failed migration, a broken host or a fault in Avalonia's framework initialization ended a WinExe with nothing written anywhere. Added StartupAlert (the shared console-or-message-box channel, factored out of SingleInstanceGuard), StartupFailure (shutdown vs fault, the sentence, exit code 3 - distinct from the --data-dir refusal's 2), a catch around the spine that logs through the host logger and cannot itself throw, and a deliberately non-fatal boundary around the synchronous theme load so an unreadable palette preference opens in the default rather than not opening.

En route, measured and fixed a defect that would have left AC2 unmet: in a console-less launch Console.IsOutputRedirected is true while the standard error handle is null, so ConsoleAuthPrompt.HasConsole() claims a console in exactly the launch that has none. StartupAlert now asks its own narrower question; the shared helper and the sign-in flows that trust it remain TASK-57's.

Verified by scripted failure injection against a corrupt database: from a terminal the fault is logged with its stack and printed, exiting 3; from a console-less parent (the Explorer double-click path) a blocking 'Winnow could not start' message box appears and the process exits 3, where the same run previously exited in 200 ms showing nothing. The refactor also repaired TASK-23's second-copy refusal on that path, confirmed live. Build clean with 0 warnings; 2824 + 145 + 70 tests pass, including 11 new ones.
<!-- SECTION:FINAL_SUMMARY:END -->
