---
id: TASK-57
title: Console output is silently unavailable for WinExe command-line flows
status: To Do
assignee: []
created_date: '2026-08-30 04:28'
labels:
  - infra
  - auth
dependencies: []
priority: medium
ordinal: 59000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Winnow.App is built as WinExe, so a launched process has no attached console and Console.WriteLine writes nowhere. ConsoleAuthPrompt's console-attach helper guards on Console.IsOutputRedirected, but a WinExe with no console has a null stdout handle that .NET reports as redirected, so the guard skips the attach in exactly the case the helper exists for. The Steam sign-in probe hit this and produced no output at all across two live runs before the cause was found. The shipped --epic-login and --epic-signin flows use the same helper and are likely equally silent, which would make a documented fallback path unusable without the user knowing why. Relates to code review finding F41, which called for rolling file diagnostics for this same reason.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The console-attach helper distinguishes a genuinely redirected stream from an absent console and attaches in the latter case
- [ ] #2 The Epic console sign-in flows are verified to produce visible output when launched from a terminal, or their output is routed to a file whose path the user is told
- [ ] #3 A test or documented manual procedure covers the WinExe no-console case so the regression cannot return silently
<!-- AC:END -->
