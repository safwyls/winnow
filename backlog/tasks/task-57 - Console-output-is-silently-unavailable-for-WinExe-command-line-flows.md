---
id: TASK-57
title: Console output is silently unavailable for WinExe command-line flows
status: Done
assignee: []
created_date: '2026-08-30 04:28'
updated_date: '2026-09-06 22:41'
labels:
  - infra
  - auth
milestone: m-4
dependencies: []
documentation:
  - docs/spikes/winexe-console-flow.md
priority: medium
ordinal: 2300
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Winnow.App is built as WinExe, so a launched process has no attached console and Console.WriteLine writes nowhere. ConsoleAuthPrompt's console-attach helper guards on Console.IsOutputRedirected, but a WinExe with no console has a null stdout handle that .NET reports as redirected, so the guard skips the attach in exactly the case the helper exists for. The Steam sign-in probe hit this and produced no output at all across two live runs before the cause was found. The shipped --epic-login and --epic-signin flows use the same helper and are likely equally silent, which would make a documented fallback path unusable without the user knowing why. Relates to code review finding F41, which called for rolling file diagnostics for this same reason.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The console-attach helper distinguishes a genuinely redirected stream from an absent console and attaches in the latter case
- [x] #2 The Epic console sign-in flows are verified to produce visible output when launched from a terminal, or their output is routed to a file whose path the user is told
- [x] #3 A test or documented manual procedure covers the WinExe no-console case so the regression cannot return silently
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect console detection and command-line sign-in paths. 2. Distinguish redirection from a missing WinExe console and provide an attach or explicit output-file path. 3. Add regression coverage and run an isolated terminal probe.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Updated the Windows console helper to inspect real standard handles, attach only when stdout is absent, and reopen cached WinExe streams after attaching.

Fixed cached null stdin after AttachConsole: the helper records whether stdin was absent before attach, restores it only in that case, and preserves an intentionally redirected input stream.

Validation: parent ran the Release focused suite (268 passing tests), including ConsoleAuthPromptTests. A native C:\\Temp\\winnow-beta-final\\Release\\net10.0\\Winnow.exe probe with redirected capture and a scratch --data-dir reached the Epic sign-in heading, URL instruction, and input prompt. Its stdin stayed open and the process was stopped before browser launch or any API request. docs/spikes/winexe-console-flow.md records the exact probe and the separate no-console manual procedure.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed WinExe console detection and stream reopening after attachment. Release tests cover null and invalid standard handles; a native executable probe confirmed redirected Epic console output; the spike documents the separate absent-handle regression procedure.
<!-- SECTION:FINAL_SUMMARY:END -->
