---
id: TASK-72
title: Add a data-directory override so the app can run against a throwaway database
status: Done
assignee:
  - '@claude'
created_date: '2026-09-02 15:17'
updated_date: '2026-09-02 17:50'
labels: []
dependencies: []
ordinal: 99000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
There is no supported way to launch Winnow against a database other than the users real one. WinnowDataLocation resolves the data directory through Environment.GetFolderPath(SpecialFolder.LocalApplicationData); on Windows .NET resolves known folders through the shell API and ignores the LOCALAPPDATA environment variable, so setting that variable before launching does nothing and fails silently. --seed-sample is not an alternative: it only fills a database that is already empty.

The consequence is that any visual verification of a UI change - screenshotting the running app, which is how the TASK-71 defects were found in the first place - runs against real data, and any click that writes is a real write to the users library. That happened during TASK-71 (one same_game link, reversed through the products own Undo and verified restored), and it will happen again.

Wanted: an explicit override, honoured before GetFolderPath is consulted. A --data-dir argument or a Winnow-specific environment variable both work; the argument is easier to see in a launch command and harder to leave set by accident. It must cover the database, the sidecars and the cover cache, since WinnowDataLocation owns all of them, and it must not disturb the Hoard-to-Winnow migration path that the same class carries.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Launching with an explicit data-directory override creates and uses a database at that path, leaving the real one untouched
- [x] #2 The override covers the database, its sidecars and the cover cache, not the database alone
- [x] #3 The legacy Hoard directory migration still behaves correctly under an override and is not run against the real directory
- [x] #4 An override pointing somewhere unusable fails loudly at startup rather than silently falling back to the real directory
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a --data-dir flag, accepted as --data-dir <path> and --data-dir=<path>, following the same parsing shape as --code in EpicLoginConsole.cs. An argument rather than an environment variable because it is visible in the launch command and cannot be left set by accident. Environment.GetFolderPath resolves known folders through the Windows shell API and ignores %LOCALAPPDATA%, so repointing that variable is the silent failure this task exists to remove.

2. Parsing and override resolution go on WinnowDataLocation, which already owns the data directory. Four new members: an OverrideArgument constant, an OverrideFrom(args) parser, a ResolveOverride(path) resolver, and a ResolveFrom(args) entry point that picks between the override path and the existing %LOCALAPPDATA% path.

3. ResolveOverride never calls Migrate. Under an override the Hoard-to-Winnow move does not run at all, and the real %LOCALAPPDATA%\Hoard is never read, moved or copied (AC 3). The existing three-argument Resolve method and every existing test over it are untouched.

4. ResolveOverride still calls the existing DatabaseIn lookup, so an override pointed at a copy of an old Hoard folder opens the hoard.db it finds, in place, without renaming anything.

5. Validation is the loud failure (AC 4). Each of these is a thrown DataDirectoryOverrideException naming the path and the reason: the flag present with no path, a path that is not a legal path, a path that is an existing file, a directory that cannot be created, and a directory that cannot be written to. There is no fallback branch to the real directory.

6. Unlike the normal path, ResolveOverride creates the directory and writes and deletes a probe file inside it. Creating the directory is what proves it is usable, and the probe is the only thing that catches a directory that exists but is read-only.

7. Program.Main parses the flag before it resolves anything, catches DataDirectoryOverrideException, attaches to the parent console the way the Epic flows do, prints the message to standard error, sets a non-zero exit code, and returns before the host is built and before any database is opened.

8. AC 2 needs no new plumbing. Every consumer of the data directory already derives its path from DataLocation.Root, which ConfigureServices passes on to the SQLite connection factory, the cover cache, the theme folder and the WebView2 profile. SQLite writes its own -wal and -shm sidecars beside the database file. ConfigureServices becomes internal so a test can assert on the real composition root instead of a copy of it.

9. Tests go in tests/Winnow.Tests in a new file for the override behaviour. Coverage: argument parsing in both spellings and the missing-value case, the resolved root and database path, that no legacy migration runs and a seeded legacy directory beside the override is left untouched, and one test per unusable-path shape. A container test builds the real ConfigureServices against an override root and asserts the connection factory, the cover cache directory and the theme directory all fall inside it. A database test runs DatabaseInitializer against the resolved path and asserts the database and its WAL sidecar appear in the override directory and nowhere else.

10. Build and test with dotnet --artifacts-path pointed at a scratch directory to avoid fighting file locks held by the running build output.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
`WinnowDataLocation` gained a `--data-dir` override path, resolved before the host is built. The new surface is: `OverrideArgument` (the flag name), `OverrideFrom(args)` (parses both `--data-dir <path>` and `--data-dir=<path>`, matching the two spellings `EpicLoginConsole.CodeFrom` already accepts), `ResolveOverride(path)` and `ResolveFrom(args)`. A new `DataMigrationOutcome.Overridden` enum member distinguishes the override from `None` ("no legacy directory to move"), which would have been semantically false. A `DataDirectoryOverrideException` carries the refusal reason.

`Program.Main` calls `ResolveFrom` early. On refusal it attaches the parent console, prints the exception message to stderr, sets exit code 2 and returns before the generic host is built. `Program.ConfigureServices` was widened to `internal` so the tests can call the real composition root. Nothing downstream changed; every data-directory consumer already derived its path from `DataLocation.Root`.

Decisions taken rather than derived from the task description:

- An argument rather than an environment variable, as the task preferred. `%LOCALAPPDATA%` cannot work anyway; `Environment.GetFolderPath` goes through the Windows shell API and ignores overrides to the variable.
- `ResolveOverride` creates the target directory and writes then deletes a probe file (`.winnow-write-probe`), where the ordinary `Resolve` deliberately creates nothing. Creating the directory is the usability check; the probe is the only thing that catches an existing but read-only directory. The fixed probe name keeps the branch testable.
- A relative `--data-dir` value is accepted and expanded via `Path.GetFullPath`; the resolved absolute path is what appears in the startup log.
- Failure is a caught exception printed to stderr with exit code 2, not an unhandled crash. Because this is a WinExe, the console attach that the Epic login flows already use is what makes the message visible.

Validation: 14 new tests in `tests/Winnow.Tests/DataDirectoryOverrideTests.cs`, all passing. Full suite green: 2678 Winnow.Tests, 115 Winnow.Recommend.Tests, 70 Winnow.Covers.Tests, zero warnings under TreatWarningsAsErrors. Built and tested in a throwaway git worktree at HEAD (the working tree was concurrently mid-edit on another task and did not compile). Ran the built binary twice: `--data-dir <a file>` exited 2 with the message on stderr and opened no window; `--data-dir <scratch> --no-sync` created `winnow.db`, WAL/SHM files and `themes/` in the scratch directory and opened on an empty library. The real `%LOCALAPPDATA%\Winnow\winnow.db` was byte-size and mtime identical before and after, and nothing in the app was clicked.

Known gap: the read-only-directory branch is exercised by blocking the write probe rather than by an ACL, because the ACL APIs are not available under this `net10.0` target without a Windows-specific target framework.

Also updated the Conventions bullet in CLAUDE.md so the flag is used rather than merely available.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
`WinnowDataLocation` now accepts `--data-dir <path>` to redirect the database, sidecars, covers, themes and WebView2 profile to a throwaway directory, so visual-verification runs cannot write to the real library. An unusable path is refused at startup with exit code 2 and never falls back silently. Verified with 14 new tests in `tests/Winnow.Tests/DataDirectoryOverrideTests.cs` (full suite green: 2678 + 115 + 70 tests, zero warnings) and two runs of the built binary: the refusal run exited 2 with the message on stderr and opened no window, the override run created `winnow.db` and its sidecars in the scratch directory while the real `%LOCALAPPDATA%\Winnow\winnow.db` stayed identical in size and mtime.
<!-- SECTION:FINAL_SUMMARY:END -->
