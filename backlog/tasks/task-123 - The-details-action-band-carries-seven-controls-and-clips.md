---
id: TASK-123
title: The details action band carries seven controls and clips
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 16:46'
updated_date: '2026-09-05 17:48'
labels:
  - ui
dependencies: []
priority: high
type: bug
ordinal: 150000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The details modal action band (Band 3 of GameDetailsView.axaml) now carries the primary action, Store page, All patch notes, Open folder, Wrong game?, Edit details and Hide — seven controls in a horizontal StackPanel with Spacing=10 that does not wrap.

Estimated by the TASK-119 agent from Button.link chrome (24px), Button.launch chrome (40px) and 12px Jakarta: the installed-game set is roughly 652-690px and the not-installed set roughly 574-605px, against a right column running 422px (card MinWidth 700) to 582px (MaxWidth 860). That overruns at every card width, and it was already near the edge before Edit details was added.

This is an ESTIMATE, not a measurement — the app was not run. Confirm it before designing, since the remedy depends on how much it actually overruns.

The remedy is a design decision and was deliberately not taken: wrapping, a second row, moving a control elsewhere, or folding the rarer actions behind a disclosure. Note the band has already absorbed several additions today (Hide in TASK-87, Wrong game in TASK-102, Edit details in TASK-119) and will attract more, so the answer should accommodate growth rather than buy back one control width.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The actual overrun is measured in a running app before a remedy is chosen
- [x] #2 Every action in the band is reachable at every card width from MinWidth 700 to MaxWidth 860
- [x] #3 Nothing clips, and no control is silently unreachable
- [x] #4 The chosen arrangement accommodates a further control without another redesign
- [x] #5 Keyboard traversal still reaches every action in a sensible order
- [x] #6 design-system.md records the arrangement and the rule for what may join the band
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. MEASURE, before any design. Build a throwaway headless-Avalonia harness outside the repo at the app's own version (Avalonia 11.3.20, Skia text shaping, Fluent, and the repo's real Plus Jakarta Sans TTFs), replicate the card geometry and the Button.link / Button.launch style declarations verbatim, and read DesiredSize off a real layout pass. Record: the right column's true width at card MinWidth 700 and MaxWidth 860, each control's own width, the whole band's width, and where the last child is actually arranged when the band is given the column it has.
2. Choose what stays on the band, against design-system.md 10.1 / 10.3 / 16.2: the band is GET ME IN. Keep the primary action and the two links that answer what this is and what changed before launching (Store page, All patch notes). Fold the filesystem errand (Open folder), the two corrections (Wrong game?, Edit details) and the dismissal (Hide).
3. Disclosure form: one trigger on the band opening an inline vertical list, drawn in the modal's own tree directly under the band inside Band 3, above the divider. Not a flyout, per 10.7 and 12.3: Avalonia's FocusAdorner does not render inside a popup. Vertical, so an eighth control costs one row and no horizontal budget at all (acceptance criterion 4).
4. View model: disclosure open state, label pair and tooltip on GameDetailsViewModel, strings in a copy class beside GameIgdbMatchCopy and GameMetadataEditorCopy. Every string authored by docs-writer.
5. XAML: move the four folded controls out of the horizontal StackPanel into the disclosure panel, keeping their classes, bindings, commands and tooltips exactly. Declaration order is Tab order (10.7), so the trigger is declared last on the band and the folded four follow it in the tree.
6. Re-measure the new band with the authored label and confirm it fits the 420px column, and that the folded panel fits too.
7. docs-writer authors design-system.md 10.3, 10.9, 10.10 and 16.2, the rule for what may join the band, and appends every superseded sentence to docs/decisions.md.
8. Build and test to the scratch output path, per project, and report verbatim.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
DECIDED BY THE USER, 2026-09-05: "fold the rarer actions behind a disclosure". Do not wrap the band and do not add a second row. Keep the actions a user reaches often on the band itself and put the rest behind one disclosure control. Which actions count as rare is a design judgement to make against the design system and to record — the band currently holds the primary action, Store page, All patch notes, Open folder, Wrong game?, Edit details and Hide.

── DONE 2026-09-05 ──

MEASURED FIRST. The app was not run; the substitute was a throwaway headless Avalonia console
project outside the repository at the app's own 11.3.20, with UseSkia + UseHeadless(UseHeadlessDrawing=false)
so text is shaped by Skia rather than the headless stub, the repository's own Plus Jakarta Sans
files, and the Button.link / Button.launch style declarations and the card's geometry copied
verbatim out of GameDetailsView.axaml. Every figure is DesiredSize or Bounds off a real measure
and arrange pass. Evidence: docs/spikes/details-action-band-width.md.

The right column is 420px at card MinWidth 700 and 580px at MaxWidth 860 — not the 422/582 the
TASK-119 estimate carried, which had not subtracted the card's 1px border on each side. The old
seven-control strip measured 660px: 240px past the 420px column, 80px past the 580px one. The
six-control not-installed set measured 566px, which overruns the 420px column by 146px but FITS
the 580px column with 14px to spare — so the estimate's claim that the full set overran at every
card width was true of the installed set only. The overrun is a clip and not a wrap, arranged
rather than inferred: given 420px the strip reports a desired width of exactly 420 (it clamps to
the constraint) while still arranging its last child with its right edge at 660px.

THE ARRANGEMENT. The strip keeps the primary action, Store page, All patch notes and a `More`
disclosure — 357px with Install, 347px with Play, 361/351 while it reads `Close`, against the
420px column. Open folder, Wrong game?, Edit details and Hide are folded into a vertical
left-aligned list that opens inline directly beneath the strip and above the divider, 104 x 144px.
Not a flyout (10.7, 12.3: FocusAdorner does not render inside a popup) and deliberately not in
the rest band's scroll region, so an action cannot scroll out from under its own trigger and the
disclosure needs no BringIntoView. Vertical is the growth answer: a fifth control costs 38px of
height and 0px of width. The trigger is Button.secondary rather than Button.link, so it cannot
read as a third outbound link beside the two Azure ones; the two classes share geometry, so it
costs no width.

KEYBOARD, WALKED NOT ASSUMED. KeyboardNavigationHandler.GetNext over the same tree shape:
closed, Primary -> StorePage -> AllPatchNotes -> More -> (past the band); open, Primary ->
StorePage -> AllPatchNotes -> More -> OpenFolder -> WrongGame -> EditDetails -> Hide -> (past
the band). While closed the four are not drawn and are therefore not Tab stops, which is the
disclosure contract 10.9 and 10.10 already use.

VALIDATION. dotnet build Winnow.slnx to the scratch output path: Build succeeded, 0 Warning(s),
0 Error(s). Winnow.Covers.Tests 78/78, Winnow.Recommend.Tests 152/152,
Winnow.Tests.GameDetailsViewModelTests 53/53 including two new tests pinning the disclosure's
open state and label swap. The full Winnow.Tests run shows one failure,
UserSetNameTests.A_user_set_name_reaches_the_details_modal_headline, which belongs to TASK-124's
concurrent work in this same tree and not to this change.

WHAT STILL WANTS A RUNNING APP. Nothing about width — that is measured. Two things are arithmetic
on measured pieces rather than measurements: that opening the list leaves the rest band a
comfortable height at the card's 720px maximum (Band 3's Auto row takes its desired height and
the star row below absorbs the loss, so nothing clips, but the remaining reading area has not
been seen), and the appearance of the Text-ink `More` beside the two Azure links. The four folded
controls' own handlers are untouched and still target their rest-band surfaces by name.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The details modal's action band went from seven controls in a non-wrapping horizontal strip to four, with the rest folded behind one inline disclosure.

The overrun was measured before the remedy was designed, in a headless Avalonia layout pass at the app's own 11.3.20 with the real fonts and the view's own style declarations (docs/spikes/details-action-band-width.md): the right column is 420px at the card's minimum width and 580px at its maximum, and the seven-control strip wanted 660px — 240px past the narrow column. The estimate that prompted this task was close for the installed set and wrong for the not-installed set, which actually fits at the maximum card width.

The strip now carries the primary action, Store page, All patch notes and a `More` disclosure and measures 357px. Open folder, Wrong game?, Edit details and Hide open as a vertical list inline beneath it, in the modal's own tree rather than a flyout (10.7, 12.3) and outside the rest band's scroll region so an action cannot scroll away from its own trigger. Vertical is what makes the arrangement survive the next control: a fifth costs 38px of height and no width at all. Tab order was walked rather than assumed and reaches every action in declaration order when open, with the folded four cleanly absent when closed.

design-system.md 10.3 records the arrangement and the rule for what may join the strip; 10.9, 10.10 and 16.2 were corrected and every superseded sentence is quoted in docs/decisions.md. All copy, comments and documentation prose were authored by docs-writer.

Verified: dotnet build Winnow.slnx clean, 0 warnings; Winnow.Covers.Tests 78/78, Winnow.Recommend.Tests 152/152, GameDetailsViewModelTests 53/53 including two new tests over the disclosure's state and label. The one failure in the full Winnow.Tests run belongs to TASK-124's concurrent work in the same tree.
<!-- SECTION:FINAL_SUMMARY:END -->
