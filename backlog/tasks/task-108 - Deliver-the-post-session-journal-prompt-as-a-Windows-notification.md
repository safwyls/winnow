---
id: TASK-108
title: Deliver the post-session journal prompt as a Windows notification
status: Done
assignee:
  - '@codex-ui'
created_date: '2026-09-05 02:49'
updated_date: '2026-09-11 19:24'
labels:
  - ui
dependencies:
  - TASK-107
documentation:
  - design-system.md
priority: medium
type: enhancement
ordinal: 135000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Deliver the opt-in post-session journal prompt through a Windows notification offering note/rating entry. The existing in-window journal prompt remains the fallback when notifications cannot be delivered. Notification activation must target the finished session through Winnow's current desktop or fullscreen journal flow.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A qualifying finished session raises a Windows notification offering note and rating entry.
- [x] #2 Notification activation records an explicitly saved note/rating against the correct session.
- [x] #3 The in-window prompt remains a usable fallback when notification delivery is unavailable.
- [x] #4 The prompt stays opt-in and off by default through journal.prompt_after_play.
- [x] #5 Dismissing or ignoring the notification writes nothing.
- [x] #6 Verify activation, duplicate suppression and fallback across desktop and fullscreen; external Windows notification availability is distinguished from application errors.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add an injectable Windows notification adapter using the shell notification-area API and main-window callback hook; return unavailable on unsupported/suppressed desktop state. 2. Offer one notification per qualifying session, retain an in-window fallback and route activation to the existing shared desktop/fullscreen prompt without writing until Save. Preserve drafts and opt-in settings. 3. Verify native availability classification and injected activation/duplicate/fallback/save behavior on both surfaces, document limitations and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: SessionJournalService raises SessionEnded and JournalPromptViewModel opens an in-window prompt; no Windows notification delivery exists. Existing SessionJournalService/JournalPrompt tests cover eligibility, off-by-default behavior and session association. Current journal visual guidance is design-system.md section 6.

Implemented an injectable Windows shell notification adapter with quiet-time/suppression checks, real-time silent delivery, a five-second display-acknowledgement fallback, and process-local activation. Shared prompt opens only on activation or fallback; stale callbacks, duplicate session offers and in-progress drafts/saves are guarded. Ten headless tests passed across submitted/unavailable/suppressed/failed delivery on desktop/fullscreen, with correct-session note/rating persistence only after Save. Twenty-six existing JournalPrompt and AccountStatsViewModel regressions passed. Native isolated Avalonia harness reported Submitted, observed the real NIN_BALLOONSHOW callback and routed a synthetic USERCLICK through the real Win32 hook to activation; no production host/database was started. Reproducible harness and measured limitations are in scripts/JournalNotificationSmoke and docs/spikes/windows-journal-notification.md.

Full-suite Release follow-up: the journal assertions passed, but the desktop fixture could delete its temporary database while MainWindow async startup still loaded that library. The fixture now disposes LibraryViewModel, awaits its initial load, and attaches the preloaded shell after opening the desktop window so unrelated startup work cannot outlive the database. No production notification behavior changed.

Follow-up validation: all thirty Release JournalNotificationTests and StoresAccountContextTests passed together; the ten journal cases cover submitted and fallback outcomes on both surfaces.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Delivered the opt-in Windows journal notification through the shell API, with exact-session activation into the existing desktop or fullscreen editor. Save is the only journal write; duplicate, stale and ignored notifications are guarded, with an in-window fallback for suppressed, unavailable or unconfirmed delivery. Verified ten desktop/fullscreen notification tests and twenty-six existing regressions. An isolated native Windows harness observed a real notification SHOW callback and exercised activation through the real Win32 hook. Physical pointer activation and controller usability were not measured; evidence and reproduction steps are in docs/spikes/windows-journal-notification.md.
<!-- SECTION:FINAL_SUMMARY:END -->
