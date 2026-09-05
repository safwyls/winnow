---
id: TASK-128
title: Body prose in the details modal is painted in the label ink
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-05 22:32'
updated_date: '2026-09-05 22:37'
labels:
  - ui
dependencies: []
priority: medium
type: bug
ordinal: 155000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Found while investigating why the section headings did not separate from their content (TASK-127). The headings were already TextDim — every one of them, app-wide, from the .label style. What they fail to separate from is the body prose beside them, which is also TextDim: the IGDB note, the editor intro, the ABOUT summary, every status line.

design-system.md §2 gives TextDim the labels-and-metadata job and gives prose the primary Text ink. So the modal is painting prose in the wrong role colour, and the reported symptom — headings not reading as chrome — is a consequence of that rather than a fault in the headings.

The user chose brightening the prose over adding a rule under each heading, so this fixes the spec violation and separates the headings as a side effect.

Find every prose run in the details modal painted TextDim and decide each one against §2: a paragraph the user reads is prose and takes Text; a value label, a status line or a piece of metadata beside a value keeps TextDim. Do not sweep the file — the distinction is the point, and flattening it would trade one wrong role for another.

Check what this does to contrast: Text on the card is brighter than TextDim so it cannot fail AA where TextDim passed, but the modal sits over the dimmed key art (TASK-98) and the figures should be stated rather than assumed.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Prose in the details modal takes the Text ink, per §2
- [ ] #2 Value labels, status lines and metadata keep TextDim, and the distinction is stated rather than left to inspection
- [ ] #3 Section headings visibly separate from the prose beneath them as a result
- [ ] #4 Contrast figures over the art-backed card are measured and recorded for the changed runs
- [ ] #5 design-system.md records which runs take which ink, so the next addition does not have to guess
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inventory every TextDim run in the modal rather than sweeping. 27 in GameDetailsView.axaml, 11 in GameMetadataEditorView.axaml, plus the runs that take TextDim by style (.label, .data-s). Decide each against section 2: a paragraph the user reads, or a run standing in a prose slot, or a section blurb under a heading, is prose and takes Text; a value label, a status line, a confirmation, or metadata beside a value keeps TextDim.
2. Verify the previous agent list instead of trusting it. The editor Intro and the ABOUT summary are prose. The IGDB note is NOT: it is either PinnedNote (Matched by you.) - a standing state label on the pin - or a landed-act confirmation (AssignedNote, ClearedNote, LinkedNote). That is a status line and keeps TextDim, and so does the editor mirror of it.
3. Extend the list past the three named: the gap rail caption and record line and their no-rail counterparts (section 10.2 makes them the words that carry what the rail draws, so a user who cannot resolve a 7px dot reads these instead - section 8 redundancy carrier is primary text); the EXTENDS and EXPANSIONS section blurbs, which sit directly against the headings the user complained about; the LISTS empty state, which is section 7 direction; and the ABOUT empty-body line, which stands in the prose slot.
4. Leave, with the reason stated: the two runs design-system.md already pins TextDim by name (10.9 no-results and id-miss lines, 10.8 no-notes line); every status and confirmation; the identity line year and publisher; the chips; the candidate row platforms; the coverage total note; the art preview placeholder. Two genuinely ambiguous: the provisional-title note (a state report on the value above it, until metadata loads) and the two flag-control captions (drawn inline and repeated verbatim as the control own tooltip) - both left TextDim with the reasoning recorded.
5. Contrast from the repo own machinery, not new arithmetic: ThemeContrastTests.Art_behind_the_back_face_and_the_modal_keeps_text_over_AA already walks Text over 256 greys at every slider position on the art-backed card, so the brightened runs are pinned already. Add a case pinning the pair - Text at least as bright as TextDim over the brightest cover, both clearing AA - and state the per-theme figures.
6. JOB 2: the same InnerScrollGutter token and the same content-margin idiom in the two regions TASK-127 found and left. The IGDB candidate list ItemsControl gives up its local Margin 0,0,4,0. The left column content StackPanel takes the token; its 18px top moves onto the ScrollViewer, since a Thickness token cannot be composed with a second value in XAML, and a top margin on the ScrollViewer is not the inert Padding case. No double-count: the candidate list own bar is a different bar from the band own.
7. docs-writer authors every XAML comment, every XML doc comment and the design-system.md addition. Wait for every child before the final build.
8. dotnet build Winnow.slnx to C:\Temp\winnow-gg1, then dotnet test per project --no-build against the same path, from PowerShell.
<!-- SECTION:PLAN:END -->
