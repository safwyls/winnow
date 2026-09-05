---
id: TASK-126
title: Menu rows should keep their name; the disclosed section closes itself
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 21:13'
updated_date: '2026-09-05 21:43'
labels:
  - ui
dependencies:
  - TASK-125
priority: medium
type: enhancement
ordinal: 153000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User request: "instead of changing the entry name to close when we open those more options we should just put a close button on the section that gets added to the details modal and leave the text in the context menu the same".

GameIgdbMatchViewModel.ToggleLabel and GameMetadataEditorViewModel.ToggleLabel both read IsOpen ? CloseLabel : OpenLabel, so the More menu rows rename themselves to "Close" while their section is disclosed.

That was defensible when the trigger was a button sitting permanently on the action band — the label was visible, so it could carry state. It is not defensible in a menu: the row is only visible while the menu is open, "Close" does not say what it closes, and the user has to reopen a menu to dismiss a section they are looking at. The close control belongs on the section, where the thing being closed actually is.

So: the menu rows keep their names, and each disclosed section carries its own close control.

Note the More trigger itself already keeps one constant face after TASK-125, and there is a test pinning that (GameDetailsViewModelTests.The_action_band_trigger_keeps_one_face). This is the same principle applied one level down, so the two should end up consistent.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The Wrong game? and Edit details menu rows keep the same text whether their section is open or closed
- [ ] #2 Each disclosed section carries its own close control, positioned so it is evidently part of that section
- [x] #3 Choosing an already-open row from the menu does not close the section by surprise — decide what it does and say so
- [ ] #4 Closing returns focus somewhere sensible rather than dropping it
- [x] #5 The now-unused Close label constants are removed rather than left orphaned
- [x] #6 design-system.md sections 10.9 and 10.10 are updated and the superseded behaviour recorded in docs/decisions.md
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Menu rows become one-way OPEN commands. ToggleCommand -> OpenCommand on both GameIgdbMatchViewModel and GameMetadataEditorViewModel; each returns early when IsOpen is already true, so choosing an open row never closes it and never discards standing state (a same-game offer, an Amber refusal, loaded rows). The code-behind BringIntoView then always runs, so an already-open row scrolls its section back into view - which is what the row name promises.
2. Add a CloseCommand to each view model. It is the only route that sets IsOpen false by user action; the two existing IsOpen = false sites (assignment landed, same-game link landed) stay as they are, because those rebuild the modal.
3. ToggleLabel -> OpenLabel on both view models, now a constant. Drop the NotifyPropertyChangedFor on IsOpen. Only GameDetailsView.axaml binds it.
4. Delete GameIgdbMatchCopy.CloseLabel and GameMetadataEditorCopy.CloseLabel. Add per-section heading, close tooltip and close accessible name constants, authored by docs-writer.
5. Each disclosed section gains a header row - Grid ColumnDefinitions=*,Auto - with the section heading in Classes=label on the left and the close control in the trailing Auto column, VerticalAlignment Top. The control copies the modal own close affordance verbatim: Button.quiet, a U+00D7 glyph in body-l over TextDim. The heading is not decoration: the section used to be named by the trigger sitting directly above it, and that trigger is now a menu row that has closed itself, so nothing else names the surface.
6. Focus on close goes to the More trigger, the control the section was opened from - the same destination TASK-125 measured for Escape and for activating a row. It is in Band 3, outside the rest band scroll region, so it is always on screen. Wired in code-behind: the IGDB close button is in GameDetailsView own tree; GameMetadataEditorView raises CloseRequested, which GameDetailsView handles, because that section is a separate UserControl.
7. Tests: rename the ToggleCommand call sites; pin that the row label is constant across the open state (the sibling of The_action_band_trigger_keeps_one_face); pin that Open is idempotent and preserves a standing offer; pin that Close folds the section.
8. docs-writer authors every string, the XAML and XML doc prose, design-system.md 10.3 / 10.9 / 10.10, and the docs/decisions.md appends.
9. dotnet build Winnow.slnx to the scratch output path, then dotnet test per project --no-build against the same path, from PowerShell.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
── WHAT CHOOSING AN ALREADY-OPEN ROW DOES, AND WHY ──

It scrolls the section back into view and changes nothing else. The command is one-way:
ToggleCommand became OpenCommand on both view models and returns early when IsOpen is
already true. Re-opening was rejected as a no-op that leaves the user staring at an
unchanged screen, and closing was rejected because it puts the close affordance back in the
menu under a name that reads `Wrong game?` - which is the whole complaint this task answers.

Scrolling is the answer the row name already promises: choosing `Wrong game?` takes you to
the wrong-game surface. Both sections open below the fold in the rest band bounded scroll
region and both already called BringIntoView on open; the code-behind gate that suppressed
that scroll on a closing press is gone, so the scroll now always runs. The IGDB row also
puts the caret back in the search field, as it already did on a first open.

The early return is not just tidiness. It preserves what stands in the section: the
same-game offer the user may be part-way through answering, the Amber refusal that belongs
to the attempt that caused it, and the editor six drafts (which a reload would have
discarded). Two tests pin exactly that.

── WHERE FOCUS GOES ON CLOSE ──

To the More trigger - the control the section was opened from. It is the destination
TASK-125 already measured for Escape and for an activated menu row, so the section round
trip starts and ends in one place, and it sits on the strip in Band 3, OUTSIDE the rest
band scroll region, so it is always drawn and focus is never left below the fold.

CloseCommand is the only user route that sets IsOpen false. The two other IsOpen = false
sites in GameIgdbMatchViewModel - a landed assignment and a landed same-game link - are
untouched, because those reload the library and rebuild the modal; focus there belongs to
the reopened modal, not to a trigger that no longer exists. That is why the focus move is
wired to the close BUTTON rather than to an IsOpen transition.

Wiring: the IGDB close button is in GameDetailsView own tree and calls OnSectionClosePressed
directly. The editor is a separate UserControl whose close button vanishes with the section,
so GameMetadataEditorView raises CloseRequested and GameDetailsView subscribes in its
constructor. Both land on the same one-line focus call.

── WHERE THE CLOSE CONTROL SITS ──

In the trailing Auto column of a new header row at the top of each disclosed section, beside
a heading naming the section. It is the modal own close affordance verbatim, one level down:
Button.quiet, a U+00D7 glyph in body-l over TextDim, VerticalAlignment Top, in the Auto
column of a star-then-Auto Grid - exactly the shape of the card own close beside the title.
Consistency with the affordance the modal already ships beat inventing a second one. The
tooltips are `Close IGDB match` and `Close editor`, and deliberately do NOT carry (Esc),
because Escape closes the whole modal.

The headings (IGDB MATCH, EDIT DETAILS) are not decoration and not scope creep. The section
used to be named by the trigger standing directly above it; that trigger is now a menu row
which has closed itself by the time the section appears, so without a heading nothing on
screen identifies the surface - and the close glyph would have nothing to sit beside.

── THE PRINCIPLE GENERALISES ──

GameDetailsViewModelTests.The_action_band_trigger_keeps_one_face pinned that the More
trigger keeps one face because the menu owns its open state. That reasoning does generalise:
a label may carry state only when it is on screen for the whole of that state. A menu row is
visible only while the menu is open, so it is strictly worse at it than the trigger was.
Both view models now have a sibling test of the same name, The_menu_row_keeps_one_face, and
design-system.md 10.3 states the rule once, at both levels.

── VERIFICATION ──

dotnet build Winnow.slnx to a scratch output path: 0 warnings, 0 errors, with
TreatWarningsAsErrors on and Avalonia compiled bindings, so every binding in the two changed
views is compile-checked. Then per project against the same path: Winnow.Tests 3271 passed,
Winnow.Recommend.Tests 152, Winnow.Covers.Tests 78. Baseline was 3265 / 152 / 78; the six
new tests are the three sibling pairs added here.

One flake seen and dismissed: Igdb.IgdbResilienceTests.Rate_limiter_caps_the_initial_burst_
and_spaces_the_rest_at_4_per_second failed once on wall-clock timing and passed on both a
filtered re-run and a full re-run. It touches nothing this task changed.

── NOT VERIFIED ──

Appearance and focus behaviour in a running window. The close control placement, the heading
beside it, and MoreActionsButton.Focus() actually landing are unverified: this ran without
launching the app and no headless Avalonia probe was taken. Everything else is pinned by a
test or by the compiler.

── NOTICED, NOT CHANGED ──

GameMetadataEditorViewModel and GameIgdbMatchViewModel still carry class-level summaries
saying the surface is disclosed from a link in the action band. TASK-125 made that stale
(it is a menu row now), not this task, so it was left alone rather than silently widening
scope.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The two menu rows now keep one name and each disclosed section closes itself, at the user request.

ToggleCommand became a one-way OpenCommand on GameIgdbMatchViewModel and GameMetadataEditorViewModel, and each returns early when already open. Choosing an already-open row therefore scrolls the section back into view rather than folding it: that is what the row name promises, and the early return preserves what stands in the section - a same-game offer the user may be part-way through answering, a standing Amber refusal, and the editor six drafts. ToggleLabel became a constant OpenLabel, and both CloseLabel constants are gone.

Each section gained a header row carrying a heading and a close control. The control is the modal own close affordance one level down - Button.quiet, a U+00D7 glyph in body-l over TextDim, in the trailing Auto column, VerticalAlignment Top - rather than a second idiom. The headings (IGDB MATCH, EDIT DETAILS) exist because the section used to be named by the trigger above it, and that trigger is now a menu row which has closed itself by the time the section appears.

Closing hands focus to the More trigger, the same destination TASK-125 measured for Escape and for an activated row; it sits outside the rest band scroll region so focus is never left below the fold. CloseCommand is the only user route that folds a section - the two success paths that fold the IGDB section rebuild the modal, so focus there is the reopened modal business.

This is TASK-125 trigger rule one level down, and the reasoning generalises: a label may carry state only while it is on screen for the whole of that state, and a menu row is visible only while the menu is open. Both view models now have a sibling of The_action_band_trigger_keeps_one_face.

design-system.md 10.3 states the rule once at both levels; 10.9 and 10.10 describe each section heading, close control and Tab order, and the two sentences they replaced are quoted verbatim in docs/decisions.md.

Verified by build and test only. Build clean (0 warnings, 0 errors, TreatWarningsAsErrors on, compiled bindings). 3271 / 152 / 78 pass, six above the 3265 / 152 / 78 baseline. AC 2 and AC 4 are left unchecked: the close control looking like part of its section, and the focus call actually landing, need a running window or a headless Avalonia probe, and neither was taken.
<!-- SECTION:FINAL_SUMMARY:END -->
