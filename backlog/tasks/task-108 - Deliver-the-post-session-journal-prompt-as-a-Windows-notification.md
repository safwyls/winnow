---
id: TASK-108
title: Deliver the post-session journal prompt as a Windows notification
status: To Do
assignee: []
created_date: '2026-09-05 02:49'
labels:
  - ui
dependencies:
  - TASK-107
priority: medium
type: enhancement
ordinal: 135000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The journal prompt currently appears as an in-window card (JournalPromptViewModel, design-system.md §5.2). The user has just come out of a game, so Winnow is usually not the window they are looking at — a card inside the client is easy to miss and arrives after attention has already moved on.

The user asked for a Windows notification instead. That also fits the fact being reported: a session just ended, which is a moment in time rather than a state of the app.

Keep the in-window card as the fallback where a toast cannot be shown or the user has notifications off; a prompt that silently goes nowhere is worse than one in the wrong place.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A finished session raises a Windows notification offering the note and rating
- [ ] #2 Acting on the notification records the note against the right session
- [ ] #3 The in-window card remains as a fallback when a notification cannot be delivered
- [ ] #4 The prompt stays opt-in and off by default, as journal.prompt_after_play already is
- [ ] #5 Nothing is written when the user dismisses or ignores the notification
<!-- AC:END -->
