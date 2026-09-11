---
id: TASK-108
title: Deliver the post-session journal prompt as a Windows notification
status: To Do
assignee: []
created_date: '2026-09-05 02:49'
updated_date: '2026-09-11 14:04'
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
- [ ] #1 A qualifying finished session raises a Windows notification offering note and rating entry.
- [ ] #2 Notification activation records an explicitly saved note/rating against the correct session.
- [ ] #3 The in-window prompt remains a usable fallback when notification delivery is unavailable.
- [ ] #4 The prompt stays opt-in and off by default through journal.prompt_after_play.
- [ ] #5 Dismissing or ignoring the notification writes nothing.
- [ ] #6 Verify activation, duplicate suppression and fallback across desktop and fullscreen; external Windows notification availability is distinguished from application errors.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: SessionJournalService raises SessionEnded and JournalPromptViewModel opens an in-window prompt; no Windows notification delivery exists. Existing SessionJournalService/JournalPrompt tests cover eligibility, off-by-default behavior and session association. Current journal visual guidance is design-system.md section 6.
<!-- SECTION:NOTES:END -->
