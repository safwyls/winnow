---
id: TASK-107
title: Journal notes can be written but never read back
status: To Do
assignee: []
created_date: '2026-09-05 02:49'
updated_date: '2026-09-05 02:49'
labels:
  - ui
dependencies: []
priority: medium
type: bug
ordinal: 134000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user asked where to view the journal. It is not a planned feature — it is a half-shipped one.

ROADMAP M3b lists "Launch + journal prompt" as shipped, and the write half exists: SessionJournalService, JournalPromptViewModel, the opt-in preference journal.prompt_after_play in the Display popover, and ISessionRepository.SetNoteAsync persisting a note and rating against a session. ISessionRepository.GetNoteAsync is declared and implemented in SessionRepository, but grep finds no caller anywhere in the application. Nothing displays a saved note.

So a user who turns the prompt on and writes notes after playing has no way to read any of them back. Give the notes a home.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A saved note and rating can be read back somewhere the user can reach
- [ ] #2 A game with notes shows them in its details modal, newest first, with the session date
- [ ] #3 A note can be edited or deleted after the fact
- [ ] #4 The surface distinguishes "no notes yet" from "the prompt is off", so a user who never enabled it is told why it is empty
- [ ] #5 Tests cover the read path that GetNoteAsync currently has no caller for
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
User confirmation, 2026-09-04: "the journal card shows up after playing but in the client. ideally it should be in a windows notification, but after that i dont see it in the client anywhere to review previous entries". So the write path works and the prompt is reaching the user — the gap is purely that nothing reads the notes back. Prompt delivery is tracked separately as its own task.
<!-- SECTION:NOTES:END -->
