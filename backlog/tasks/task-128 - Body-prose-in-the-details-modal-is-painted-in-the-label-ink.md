---
id: TASK-128
title: Body prose in the details modal is painted in the label ink
status: Done
assignee:
  - '@codex'
created_date: '2026-09-05 22:32'
updated_date: '2026-09-06 17:43'
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
- [x] #1 Prose in the details modal takes the Text ink, per §2
- [x] #2 Value labels, status lines and metadata keep TextDim, and the distinction is stated rather than left to inspection
- [x] #3 Section headings visibly separate from the prose beneath them as a result
- [x] #4 Contrast figures over the art-backed card are measured and recorded for the changed runs
- [x] #5 design-system.md records which runs take which ink, so the next addition does not have to guess
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Review the implemented prose/status inventory and governing design-system.md 10.3 distinction. 2. Verify the rendered ABOUT heading/body treatment through the existing isolated headless harness and run the four-theme prose/art contrast tests. 3. Record verification and close if all criteria are demonstrated.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
2026-09-06 verification: the implementation and explicit prose/status inventory already exist in GameDetailsView.axaml, GameMetadataEditorView.axaml and design-system.md 10.3. The real compiled view was rendered with Skia in an isolated Avalonia.Headless 11.3.20 window at three sizes. ABOUT resolves to Text (#F0EDE7) at a measured 410px prose width; its heading and metadata resolve to the sage TextDim treatment. Inspected about-1280.png: the brighter two-line paragraph visibly separates from ABOUT and the identity labels. Existing The_modal_prose_takes_the_primary_ink and Art_behind_the_back_face_and_the_modal_keeps_text_over_AA tests passed as part of 192 targeted tests. Four-theme Text contrast already recorded in 10.3: 13.11/16.44/14.76/13.42 flat, 10.34/13.75/11.90/10.61 over brightest art. The exhaustive test walks 256 greys at every transparency position. No application change needed; verification captures live in C:/Temp/winnow-task105.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Already implemented. Verified the rendered heading/prose distinction, documented status-versus-prose inventory and four-theme art contrast; all 192 targeted checks passed.
<!-- SECTION:FINAL_SUMMARY:END -->
