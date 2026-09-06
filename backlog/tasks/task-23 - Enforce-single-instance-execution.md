---
id: TASK-23
title: Enforce single-instance execution
status: Done
assignee:
  - '@safwyl'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-04 00:53'
labels:
  - infra
dependencies: []
priority: high
ordinal: 23000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
No single-instance guard exists. Running two copies of Winnow simultaneously duplicates session recording, scheduler work, and can corrupt the database. Finding F39. Source: stabilization-2026-08-28.md Group 2. Trigger: next startup composition change. Lands with F36 in the same startup pass.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Session and scheduler work is never duplicated
- [x] #2 A test or manual procedure demonstrates the guard
- [x] #3 A second launch against the SAME data directory detects the running instance, shows a sentence, and exits instead of starting a second copy (deviation from the original wording: the N03 remediation decided sentence-and-exit rather than activating the first instance)
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add Winnow.App/Services/SingleInstanceGuard: a named Local mutex keyed on the resolved data-directory path (SHA-256), so two copies on the SAME data dir are blocked while a dev --data-dir instance is not; 2. Acquire in Program.Main right after WinnowDataLocation.ResolveFrom, before the host is built; hold for process lifetime; 3. Second instance: AttachConsoleIfNeeded + Console.Error sentence when a console exists, native MessageBox when it does not (WinExe launched from Explorer), then exit; 4. Unit test: second acquire on the same path fails, a different path succeeds; 5. Update AC #1 to match the decided remediation (sentence-and-exit, not activate) and note the deviation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Guard is a named Local mutex keyed on the SHA-256 of the NORMALIZED data-directory path, held in a Program.SingleInstance static field for the process lifetime. First TryAcquire draft used Mutex.WaitOne, which was wrong: mutex ownership is per THREAD, so a same-thread reacquire (exactly what the unit test does) would succeed against a held mutex. Switched to the createdNew check from the Mutex constructor, which is per-object and answers the actual question (does the name already exist); it also needs no AbandonedMutexException handling, since a crashed process takes the named mutex with it. ConsoleAuthPrompt.HasConsole widened private -> internal so the refusal reuses it. Verified e2e: two Winnow.exe launches against the same --data-dir; the second printed the refusal sentence on stderr and exited before any host output, while the first kept running.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added Winnow.App/Services/SingleInstanceGuard: a named Local mutex keyed on the resolved data-directory path, acquired in Program.Main right after WinnowDataLocation.ResolveFrom and held for the process lifetime in a static field. A second copy against the same data directory shows a sentence (console when launched from a terminal via AttachConsoleIfNeeded, native MessageBox when launched from Explorer) and exits before the host exists, so the session watcher, the snapshot/ownership schedulers and the update poller are never doubled. A second copy against a different --data-dir is deliberately allowed. Verified with 4 new unit tests in SingleInstanceGuardTests (refusal, release, different directory, path-spelling normalization) and a two-process e2e run: second instance refused with exit sentence, first unaffected. Full suite green: 2800 + 145 tests.
<!-- SECTION:FINAL_SUMMARY:END -->
