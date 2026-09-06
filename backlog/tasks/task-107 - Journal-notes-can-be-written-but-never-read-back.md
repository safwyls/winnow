---
id: TASK-107
title: Journal notes can be written but never read back
status: Done
assignee:
  - '@beta_ui'
created_date: '2026-09-05 02:49'
updated_date: '2026-09-06 22:32'
labels:
  - ui
milestone: m-4
dependencies: []
priority: medium
type: bug
ordinal: 2000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user asked where to view the journal. It is not a planned feature — it is a half-shipped one.

ROADMAP M3b lists "Launch + journal prompt" as shipped, and the write half exists: SessionJournalService, JournalPromptViewModel, the opt-in preference journal.prompt_after_play in the Display popover, and ISessionRepository.SetNoteAsync persisting a note and rating against a session. ISessionRepository.GetNoteAsync is declared and implemented in SessionRepository, but grep finds no caller anywhere in the application. Nothing displays a saved note.

So a user who turns the prompt on and writes notes after playing has no way to read any of them back. Give the notes a home.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A saved note and rating can be read back somewhere the user can reach
- [x] #2 A game with notes shows them in its details modal, newest first, with the session date
- [x] #3 A note can be edited or deleted after the fact
- [x] #4 The surface distinguishes "no notes yet" from "the prompt is off", so a user who never enabled it is told why it is empty
- [x] #5 Tests cover the read path that GetNoteAsync currently has no caller for
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add an ownership-scoped journal read model and repository operations for list, edit, and delete. 2. Load the journal into the game details modal as a newest-first inline section with session dates, rating, edit and delete actions, and the required distinct empty states. 3. Cover repository reads and the details view model plus a headless modal interaction path; run focused Release tests in the shared build slot.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
User confirmation, 2026-09-04: "the journal card shows up after playing but in the client. ideally it should be in a windows notification, but after that i dont see it in the client anywhere to review previous entries". So the write path works and the prompt is reaching the user — the gap is purely that nothing reads the notes back. Prompt delivery is tracked separately as its own task.

Validation: focused Release journal/repository/view-model tests passed 18/18; headless JournalDetailsInteractionTests passed 2/2. The interaction test opens GameDetailsView, edits a saved note, confirms deletion, and checks the prompt-off empty state. A broader RepositoryRoundTrip filter also exposed an unrelated ownership acquisition-date failure already reported to the coordinator.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a newest-first journal read model and inline details section with date, rating, edit, and confirmed delete. Verified repository reads plus view-model and headless modal interaction paths.
<!-- SECTION:FINAL_SUMMARY:END -->
