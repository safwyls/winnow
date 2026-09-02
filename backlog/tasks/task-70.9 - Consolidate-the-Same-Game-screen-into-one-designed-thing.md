---
id: TASK-70.9
title: Consolidate the Same Game screen into one designed thing
status: In Progress
assignee:
  - '@safwyl'
created_date: '2026-09-02 12:35'
updated_date: '2026-09-02 13:08'
labels: []
dependencies: []
documentation:
  - design-system.md
  - notes.md
parent_task_id: TASK-70
type: task
ordinal: 96000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
## Background

The Same Game screen was rebuilt three times in two days: pairwise merge queue, then apply/history bolted onto one scroll, then a focused REVIEW/HISTORY queue, then group-based retractable links with a survivor radio, include checkboxes, a roster layout, an EXPANSIONS segment, store chips and a width ceiling. Each stage was tested and reviewed on its own terms, on TASK-70.3, TASK-70.5 and TASK-70.8. Nobody has looked at the result as one designed thing.

The verdict: the information architecture is right and the finish is not. This is a consolidation pass, not a rework.

## Scope

Files in play: `MergeQueueView.axaml` (1348 lines), `MergeQueueView.axaml.cs`, `MergeQueueViewModel.cs` (1055 lines), `MergeGroupViewModel.cs`, `MergeGroupMemberViewModel.cs`, `MergeEdgeViewModel.cs`, `MergeSideViewModel.cs`, `MergeSignalViewModel.cs`, `MergeLinkHistoryRowViewModel.cs`, `ExpansionGroupViewModel.cs`, `ExpansionMemberViewModel.cs`, `MergeCopy.cs`, `ExpansionCopy.cs`, plus `MainWindow.axaml` (the rail row and pane styles) and `MainWindow.axaml.cs` (`OnMergeQueueKeyDown`).

## Three segments: keep the shape, fix the navigation

REVIEW and EXPANSIONS take different answer verbs (S/D against G/N) and different evidence; interleaving them would make S ambiguous across two contexts. This was argued and decided on TASK-70.5 and is correct. HISTORY as the third segment is right: it is the retraction surface for both relations.

Two changes are needed.

**Counts on every segment.** Today only REVIEW carries a count, a 22px number inside its header. From REVIEW, you cannot see that EXPANSIONS has eight cards waiting. TASK-66's fix F4 put an outstanding count on the HISTORY segment; the 70.x rework dropped it. Put the count on each segment tab itself, in Plex Mono `tnum`. This deletes the large in-header count and places one number on the control that also navigates to that surface.

**The rail row must count both surfaces.** `SAME GAME?` binds `MergeQueue.PendingCountText`, which is the review count only, and `MergeQueue.RowOpacity`, which dims the row to 40% when review is empty. A library with zero same-game groups and twelve expansion groups shows a dimmed `SAME GAME? 0` and nothing indicates that twelve cards are waiting. The rail count must be review plus expansions. Its tooltip `Pairs that might be the same game` is pair-model residue: the unit is a group and the screen holds two questions, not one.

## Converge the two card layouts

The card today holds two separate `Grid`s switched on `IsPair` (visible at lines 901-903 and 966-968 in `MergeQueueView.axaml`) and two `DataTemplate`s, one of which (`MergeMemberTemplate`) serves both as a pair side and as the roster's primary column. Two designs in one card.

**Proposal:** one arrangement at every member count. The primary's capsule at 200x300 on the left, every other member as a roster row on the right.

What varies with member count is inside the row:

- Exactly one other member: the row draws its cover at 200x300, so the "two covers side by side at 200x300" that §6 specifies is preserved literally. Its evidence is the full four-row diff, open, not behind a disclosure. It draws no include checkbox.
- Three or more members: 64x96 chip, condensed one-line evidence, a disclosure, and the checkbox.

Why this is right and not merely tidier: under the link model the act is asymmetric. One parent, N children, one act, one retraction. A symmetric two-cover layout is the pairwise model's geometry, and it is why the card currently states which title survives three times (the checked radio under the cover, the KEEP block, and the automation name). Left and right are ordered by work id, which means nothing to a reader. Primary-left, children-right states the act's shape in the layout.

This contradicts no recorded decision. TASK-70.3 required the primary to keep its 200x300 capsule at both densities so the card's outer geometry never changes (holds). §6 asks for two covers side by side at 200x300 (holds at two members). TASK-70.3 recorded "NO CHECKBOXES at two members, the two buttons already carry include/exclude" (holds, and the currently dead `ShowIncludeControl` gets a real meaning: `!IsPrimary` when the group has three or more members).

**Fallback** if the symmetric pair layout is preferred: keep the two grids but make the header, the KEEP treatment, the evidence treatment and the answer row identical, and delete the duplicated markup. Smaller, still worth doing.

## Controls: reduce repetition, not the set

Radio, checkbox, two buttons is coherent once stated as: who is this (radio), who joins (checkbox), yes or no (buttons). It reads as a pile today for three reasons, none of which requires removing a control.

1. **The KEEP block repeats the radio.** A bordered `note` box under the layout prints `KEEP` / the title / `WHY <reason>` / the effect line. The title is already on screen at 200x300 with a checked radio reading `Keep this title` under it. Collapse: the reason moves onto the primary's radio line or a short `WHY` line under the primary capsule, and the box goes.
2. **`EffectLine` is a screen constant.** `One tile, a chip per store.` is identical on all forty cards; so is the expansion card's `Hours and counts stay separate.` These belong in the segment header beside `QueueIntro` and `ExpansionCopy.Intro`, or replacing them. They should not repeat per card.
3. **`Others` is ordered by work id.** Order by evidence instead: direct edges by best score descending, indirect members last. The rows a user must think about are the indirect ones, marked Amber, and they are currently scattered through the roster.

## Signal evidence

Today: `CONFIDENCE 0.87` at 20px at the head of the card, a four-row grid of label / value / signed contribution plus a wrapped `Detail` sentence, and a per-roster-row `BestScoreText`. That is a scorer explaining itself, not evidence about games.

What actually decides "is Prey the same as Prey" is already on the card: titles, years, publishers, stores, covers. What the diff adds that the card cannot show on its own is the matcher's `Detail` sentence (`2006 vs 2017 (Δ11)`) and the fact that a signal did not fire, the difference between a 0.65 built on title alone and a corroborated 0.65. That distinction is load-bearing and TASK-70.3 was right to keep it.

**Proposal:** keep the four rows and the unfired treatment. Drop the contribution column and with it `TextBlock.contribution`, its `.pos` and `.neg` styles, `MergeSignalViewModel.IsForMatch`, `IsAgainstMatch` and `ContributionText`. Signed points are the scorer's arithmetic and nobody on this screen is tuning weights. What survives is the label, the value and the sentence, which is what §6 actually specifies: title distance, year delta, publisher.

Delete the per-row `BestScoreText`: it restates the group score at member grain and is never explained.

The four rows then earn their space at both densities: the middle column at two members, a condensed line plus a disclosure at three or more.

Whether `CONFIDENCE 0.87` stays on the card is a product decision. It is the matcher's number, not a fact about the games. The recommendation is to keep it smaller and move it into the header rather than deleting it, since it still sorts the queue.

## History

Four problems.

1. The heading is `Linked games` (`MergeCopy.LinkHistoryHeading`) but the list also holds expansion groupings, which read `grouped under` and carry a `GROUPED` label. The name is wrong for the contents.
2. Unbounded and unstructured: `GetHistoryAsync(null)` returns every act, newest first, with no count, no filter, no grouping, and retracted rows inline forever.
3. One empty state covers two different facts: nothing has ever been linked, and everything linked has been retracted. Both render the same sentence.
4. No count on the segment tab, so you cannot see how many acts are recorded without switching to HISTORY.

**Proposal, in value order:** rename the heading to cover both relations; put a count on the segment like the other two surfaces; separate what is currently in force from what has been retracted so the live state is readable at a glance. Nothing else. History is a log and should look like one.

## How it reads at empty, one and forty cards

**Empty.** The Display L question still draws with the intro and count hidden, over a centred grey sentence. That is fine. `HasCompletedSweep` for review and `_scannedExpansions` for expansions are two mechanisms for one idea (has the relevant scan run yet) and should be unified.

**One card.** This is the most visible unfinished thing on the screen. The 840px ceiling is set on `Border.card` alone. The 48px segment strip, the `Same game?` header row with its right-aligned count, the report note and the empty state all span the pane. On a 1920-wide window the count sits roughly 500px to the right of the card it counts, and the screen reads as a full-width header over a narrow centred column. The number 840 is right and was measured on TASK-70.8; only its scope is wrong. Put the measure on one centred content column so the header, the report and the cards all sit in the same track.

**Forty cards.** The cards sit in a plain `ItemsControl` inside a `ScrollViewer`, so nothing virtualizes. Every card holds decoded bitmaps. `LoadAsync` builds every group's face eagerly and `RequestCovers` fires a decode for every member of every card. Give the list a `VirtualizingStackPanel` items panel and request covers on realization, not on load. The `CoverWall` rule does not apply here: that rule bans `ItemsRepeater`/`UniformGridLayout` for the wall's flush-row geometry, and this is a plain vertical list. Whether a bare `ItemsControl` virtualizes with that panel or requires a `ListBox` must be verified against Avalonia 11's current API before implementation; a `ListBox` with `SelectedItem` bound to `SelectedGroup` would also fix the focus defect below.

## Keyboard and selection: three defects

1. **Expansion cards have no selection input.** The expansion `ItemsControl` has no `GotFocus="OnCardFocus"`, the expansion card `Border` has no `PointerPressed`, and `OnMergeQueueKeyDown`'s expansions branch handles only G, N and Enter, with no Up or Down. `SelectedExpansionGroup` is whatever `LoadAsync` set, which is the first card. Clicking a card does not select it, and the Volt `.selected` edge is bound but unreachable. G therefore groups the first card regardless of what the user is looking at. This writes to the library, so it is a defect.
2. **Arrow keys move selection without moving focus.** `Border.card` is not focusable and `OnCardFocus` fires only from descendants, so Up and Down move `SelectedGroup` while focus stays where it was. The next Tab then moves focus from the old place and snaps selection back. `MergeQueueView.axaml.cs`'s own comment says selection must follow focus because answering writes to the library, and it does, but only in one direction.
3. **Escape from EXPANSIONS or HISTORY returns straight to the library** rather than to REVIEW. §12.4's ladder is one visible layer per press.

Minor, worth recording: S, D, G and N are not guarded by a focused-text-field check. There is no `TextBox` on this screen today so it is correct now and fragile, and §12.4 states the rule globally.

## Accessibility

1. **Unfired signal contrast.** `Grid.signal.unfired` sets `Opacity` 0.55 on the whole row. `TextDim` composited at 0.55 runs 2.78:1 on `Surface` (the pair diff) and 3.00:1 on `Well` (the roster disclosure), against §8's floor of 5.04:1 and its instruction not to dim further. The unfired state is already carried by an em-dash value and a reason sentence, so mark it with those rather than opacity, or dim the value cell alone rather than the label and the sentence.
2. **Roster evidence has no automation name.** The condensed evidence is six unlabelled `TextBlock`s. `MergeEdgeViewModel.SummaryText` and `MergeCopy.EdgeSummaryFormat` exist for exactly this purpose and are bound nowhere. Bind `SummaryText` as the evidence row's `AutomationProperties.Name`.
3. **Cards carry no `AutomationProperties.Name`.** A screen reader walking the queue hears controls and never hears what group it is in.
4. **Store chips have no automation name.** The chip row has `ToolTip.Tip` but no automation name, so chips are announced as bare letters. §5.3's amendment requires the store words to be reachable in the automation name; they are, on the answer buttons through `Label`, but not on the visible chip row.
5. **Entry numbers are database ids shown to the user.** §10.5 rejected this for `account_ref` for the same reason. With store chips now on every member, the store usually disambiguates. Consider showing entry numbers only when two members share both title and store. Flag as a product decision, not a defect.

## Copy

- `MergeCopy`'s own header says every string lives in one file, and it does not. `Same game`, `Different games`, `CONFIDENCE`, `TITLES`, `TOP OF QUEUE`, the roster's `TITLE` / `YEAR` / `PUBLISHER` labels and the `TOP OF QUEUE` tooltip are literals in the XAML.
- `TOP OF QUEUE` is wrong twice. It binds `IsPriority`, which is the matcher's band and not a queue position: several cards can carry it, and the queue is already sorted by score so the top card is at the top regardless. Its tooltip reads `The strongest evidence in the queue. Still your call — nothing is linked on its own.`, which is exactly the over-explanatory blurb notes.md asks us to drop. Rename it to what it means or delete it.
- Both expansion templates separate the year from the entry numbers with an ASCII full stop (`.`) where the merge templates use a middle dot (`·`). Two occurrences, in `ExpansionBaseTemplate` and `ExpansionRosterRowTemplate` (lines 317 and 408 in `MergeQueueView.axaml`).
- `QueueIntro` and `ExpansionCopy.Intro` are already short and stay. `EffectLine` folds into them rather than repeating per card.

## Dead members to delete

No binding and no caller:

- `MergeGroupMemberViewModel.ShowIncludeControl` (dead as written; either resurrect with the new meaning, `!IsPrimary` at three or more members, or delete)
- `MergeGroupMemberViewModel.IncludeControlText` (the merge roster's checkbox has no tooltip; only `ExpansionMemberViewModel.IncludeControlText` is bound, on line 367)
- `MergeSideViewModel.NormalizedTitle` (always constructed null by `DescribeWorkAsync`, never bound; its doc comment "Shown because why is the screen" describes a retired stage)
- `MergeSideViewModel.HasPublisher` (no binding in `MergeQueueView.axaml`, only used on `GameDetailsView.axaml`)
- `MergeQueueViewModel.DifferentGamesTooltip` (the XAML binds the group's copy on line 1045, not the screen-level one)
- `MergeLinkHistoryRowViewModel.ChildCountText` (only reader is a test assertion)
- Six geometry constants with no binding and no code reader: `ExpansionGroupViewModel.CoverWidth`, `ExpansionGroupViewModel.CoverHeight`, `ExpansionMemberViewModel.ChipWidth`, `ExpansionMemberViewModel.ChipHeight`, `MergeGroupMemberViewModel.ChipHeight`, `MergeQueueViewModel.CoverHeight`. Note: `MergeQueueViewModel.CoverWidth` and `MergeGroupMemberViewModel.ChipWidth` ARE live and stay.

`MergeEdgeViewModel.SummaryText` and `MergeCopy.EdgeSummaryFormat` are dead but should be bound for accessibility rather than deleted.

## Dead markup to delete

- The pair `Grid` and its inline signal `DataTemplate`, which is a near-verbatim copy of the roster's.
- The report note block, which is written out three times identically in the review header, the expansions header and the history scroll; should be one shared resource.
- The KEEP note block.
- The contribution column and its three styles (`TextBlock.contribution`, `.pos`, `.neg`).
- The `BestScoreText` binding.

## Stale comments to delete

- `MergeQueueView.axaml` lines 17-21: the HISTORY comment explains what migration 0019 removed; it was worth saying during TASK-70.7 and is now archaeology in a file that never shipped the thing being described.
- `MergeQueueViewModel`'s class-level `<summary>` comment (lines 15-31): same, the paragraph about 0019 retiring the destructive executor explains a transition that completed.
- `Border.entry` style comment says "the three lists on the history surface" and there is one list.
- `MergeQueueView.axaml.cs`'s `OnCardFocus` comment says "pressing S merges a different pair" and nothing merges; the screen links.
- `MergeCopy` carries an empty section banner `Merge modes and limits` with nothing under it, and a duplicated `Automation` banner.

## Bugs found while reading

1. **Expansion covers decode at the wrong size.** `ExpansionGroupViewModel.RequestCovers` passes the full 200px display width to every pack, which draws at 64x96. `MergeGroupViewModel.RequestCovers` correctly scales by `ChipWidth / CoverWidth`. The expansion surface decodes roughly ten times the pixels it draws.
2. **Expansion selection is unreachable** (described above under keyboard defects).
3. **`ReportMessage` is never cleared.** Nothing nulls it, not `LoadAsync`, not a segment switch. One answer leaves the Amber attention note up for the rest of the session. `HasReport` is shared across the three surfaces, so a same-game report reading `Linked 2 under Prey.` renders in the EXPANSIONS header with a Retract button beside it. Clear it on load and on segment change, or scope it to the surface that produced it.
4. **Unfired-signal contrast** (described above under accessibility).
5. **`RetractActAsync` calls the whole of `LoadAsync`**, which re-runs `LibraryExpansionScan` and re-describes every release. Retracting one link from HISTORY costs a full rescan.
6. **Two ASCII full stops** where middle dots are used everywhere else (described above under copy).
7. **The rail count and its dimming ignore expansions** (described above under segments).
8. **Corner radius inconsistency** reported in notes.md: `Border.screenpane` does carry `RadiusPane` and `ClipToBounds` under the floating layout, same as `Border.wallpane`, so this could not be reproduced from source. Needs a visual check on the running window before any change.

## What to keep unchanged, and why

- Three segments, with REVIEW and EXPANSIONS as separate surfaces with separate verbs and separate keys (TASK-70.5, argued and right).
- Group as the unit, and the clique-not-component default-checked rule with two-member groups exempt (TASK-70.3, the Prey 2006 / Prey 2017 guard).
- No survivor chooser on an expansion card. `ExpansionMemberViewModel` has no `IsPrimary` at all, so the wrong direction is not a mistake the type can express. This is a recorded user decision.
- Expansion links move no number, and the card says so.
- Retractability everywhere; retraction as an ordinary repeatable act; a retracted row staying on screen with the date it was reversed.
- Answering reads nothing, per TASK-70.3's disjoint-components argument, and the counting test that guards it.
- No Flare anywhere on this screen. Amber for attention. Volt for selection and the primary answer.
- Two answer buttons named for games rather than for the data model (§7).
- A store chip on every member at both densities (TASK-70.8).
- The measured 840 ceiling. The number is right; only its scope is wrong.
- The placeholder: title in Bricolage on a Surface field, never a spinner.
- The hand-templated `CheckBox` and `RadioButton` with §8's 2px Volt ring drawn as brush opacity at fixed thickness, so nothing reflows as focus moves.

## Product decisions required

These cannot be resolved by implementation alone.

1. Converge the two card layouts or keep the symmetric pair? The recommendation is to converge (it keeps every recorded decision), but the symmetric comparison is what §6 describes and what the user has said is not bad.
2. Whether `CONFIDENCE 0.87` stays on the card, since it is the matcher's number and not a fact about the games.
3. Whether the signed contribution points stay, which is the same question one level down.
4. Whether entry numbers remain visible now that stores are chips, or whether they appear only when two members share both title and store.
5. Whether HISTORY splits live from retracted or stays one chronological log.
6. Whether `TOP OF QUEUE` is renamed to something accurate or deleted. The score already sorts the queue and is already visible.

## Staging

Four stages, each independently shippable.

1. **Defects.** Expansion selection, expansion cover sizing, `ReportMessage` clearing and scoping, unfired-signal contrast, the two dot/period separators, the rail count. No design change; all of it is wrong today.
2. **Deletions.** Dead members, the tripled report block, stale comments, the empty copy banners. Pure subtraction, no behaviour change.
3. **The card.** Converge the layouts, collapse the KEEP block, move `EffectLine` to the header, drop the contribution column and `BestScoreText`, order `Others` by evidence, settle the `CONFIDENCE` question.
4. **The screen.** One centred 840 measure for the header, report and cards; counts on the segments; the history heading and the live/retracted split; virtualization; the `Escape` ladder; the automation names.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The expansion surface answers the card the user selected, reachable by pointer click and by Up/Down arrow keys
- [ ] #2 Expansion pack covers decode at chip resolution (64px width) and not at capsule resolution (200px width)
- [ ] #3 The outcome report is scoped to the surface that produced it, is cleared on segment change and on reload, and does not render in an unrelated segment's header
- [ ] #4 No text on the screen composites below §8's contrast floor of 5.04:1, including an unfired signal row's label and detail sentence
- [ ] #5 The review header, the outcome report and the cards share one centred content column at every window width, capped at 840px
- [ ] #6 Each of the three segment tabs states its own outstanding count in Plex Mono tnum, and the rail row's count and opacity reflect review plus expansions
- [ ] #7 The review card uses one layout whose primary member is a 200x300 capsule on the left and whose non-primary members are rows on the right, with two-member groups keeping 200x300 covers and drawing no include checkbox
- [ ] #8 Every named dead member, dead geometry constant and stale comment is removed and the build is clean under TreatWarningsAsErrors
- [ ] #9 Every user-facing string on the screen lives in MergeCopy or ExpansionCopy, including Same game, Different games, CONFIDENCE, TITLES, and all roster column labels
- [ ] #10 A screen reader can identify a card by its AutomationProperties.Name and can read a roster row's evidence via the bound SummaryText automation name without traversing individual controls
- [ ] #11 The history list heading names both relations it holds (links and expansion groupings) and separates acts currently in force from retracted acts
- [ ] #12 The card list uses a virtualizing panel so that forty groups do not realize every card and decode every cover simultaneously
- [ ] #13 No Flare colour appears anywhere on the screen, and expansion cards have no survivor chooser (no IsPrimary, no radio)
- [ ] #14 The full test suite passes with no new warnings
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
STAGE 1 ONLY — the eight defects. The structural/arrangement proposals and the six product decisions are with the user and are NOT pre-empted; the deletion list beyond four stale comments is untouched because it depends on the card-convergence decision.

1. Expansion card selection (writes to the library, so first).
   ExpansionGroupViewModel already carries IsSelected and MergeQueueViewModel already
   carries SelectExpansion; nothing ever calls it but LoadAsync and RemoveExpansion, so
   SelectedExpansionGroup is always the first card and G groups that card whatever the
   user is looking at. Give the surface the merge card's own selection model:
   - MergeQueueViewModel.MoveExpansionSelection(int delta), mirroring MoveSelection.
   - MergeQueueView.axaml: name the expansion ItemsControl, add GotFocus, add
     PointerPressed on the expansion card Border.
   - MergeQueueView.axaml.cs: OnExpansionCardFocus, OnExpansionCardPressed,
     ScrollExpansionIntoView(int).
   - MainWindow.axaml.cs OnMergeQueueKeyDown: Up/Down in the expansions branch.
   Audit of the rest of that handler: the review branch answers on SelectedGroup and the
   expansions branch on SelectedExpansionGroup; nothing else in it indexes a list.

2. Report scoping. Add MergeReportSurface (None/Review/Expansions/History) and
   ReportSurface alongside ReportMessage, with HasReviewReport / HasExpansionsReport /
   HasHistoryReport replacing the shared HasReport on the three note blocks. One
   Report(message, actId) helper stamps the surface that is up. ClearReport() on
   ShowReview, ShowExpansions, ShowHistoryAsync and at the top of LoadAsync;
   RetractActAsync reloads FIRST and reports after, so the reload does not eat its own
   outcome line.

3. Unfired-signal contrast. Opacity 0.55 on Grid.signal.unfired composites TextDim to
   2.78:1 on Surface and 3.00:1 on Well against section 8's 5.04 floor, and any opacity
   below 1 over a dark field lowers contrast, so opacity cannot be the mark. Drop it and
   demote the unfired row's VALUE cell from Text to TextDim — the app's own
   content-to-metadata step — which lands 5.88:1 on Surface and higher on Well. The state
   stays triple-carried: em-dash value, reason sentence, demoted value ink. Verified with
   Winnow.App.Themes.Colorimetry, not estimated.

4. Expansion cover sizing. ExpansionGroupViewModel.RequestCovers scales the pack chip by
   ChipWidth / CoverWidth exactly as MergeGroupViewModel.RequestCovers does, so a 64x96
   chip stops decoding at 200px width.

5. Rail. Add OutstandingCount / OutstandingCountText / HasOutstanding (review plus
   expansions); RowOpacity reads HasOutstanding. MainWindow.axaml binds the count and its
   visibility to the outstanding pair. The row's tooltip currently says 'Pairs that might
   be the same game', which is both pair-model residue and false about a count that now
   includes expansion groups: facts to docs-writer for the replacement.

6. Two ASCII full stops to middle dots in ExpansionBaseTemplate and
   ExpansionRosterRowTemplate.

7. Bind MergeEdgeViewModel.SummaryText (which formats MergeCopy.EdgeSummaryFormat) as
   AutomationProperties.Name on the merge roster's condensed evidence line, so the six
   unlabelled TextBlocks announce as labelled values.

8. Delete exactly the four stale comments the pass names: MergeQueueView.axaml 17-21,
   the migration-0019 paragraph in MergeQueueViewModel's class summary, the Border.entry
   'three lists' comment, and OnCardFocus's 'pressing S merges a different pair'. Nothing
   else on the delete list.

TESTS (scoped first, then the full suite, no commit):
 - a shortcut acts on the focused expansion card, not the first, asserted the way
   Selection_moves_by_card_and_clamps_at_the_ends and Answering_moves_the_cursor asserts
   the review equivalent;
 - a report raised on one surface does not render on another, and a segment switch and a
   reload clear it;
 - the unfired signal's inks clear 5.04:1 via Colorimetry, plus a source guard that the
   unfired style sets no opacity (the StoreChipLayoutTests XAML-guard pattern);
 - the pack chip asks for a chip-sized cover, against a recording ICoverCache;
 - the rail count and opacity reflect expansions with an empty review queue.
Build and test via --artifacts-path into the session scratchpad; the user's app holds
src/Winnow.App/bin. Prose via docs-writer. ExpansionDetector and the enrichment layer are
another agent's; not touched.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
## Stage 1 (defects) implemented — NOT finalized

Only the eight defects. No structural or arrangement change, no product decision pre-empted,
no deletion beyond the four named stale comments. ExpansionDetector and the enrichment layer
untouched (another agent's).

### 1. Expansion cards had no selection input (highest: it writes to the library)
The expansion `ItemsControl` declared no `GotFocus`, the expansion card `Border` no
`PointerPressed`, and `OnMergeQueueKeyDown`'s expansions branch handled only G/N/Enter.
`SelectedExpansionGroup` was therefore whatever `LoadAsync` set — the first card — so G
grouped the first card whatever the user was looking at. Same class as the TASK-66 S/D fix.
Given the merge card's own model, in all four places it lives:
 - `MergeQueueViewModel.MoveExpansionSelection(int)`, mirroring `MoveSelection`;
 - `MergeQueueView.axaml`: `Name="ExpansionList"`, `GotFocus="OnExpansionCardFocus"`, and
   `PointerPressed="OnExpansionCardPressed"` on the card Border;
 - `MergeQueueView.axaml.cs`: `OnExpansionCardFocus`, `OnExpansionCardPressed`,
   `ScrollExpansionIntoView(int)`;
 - `MainWindow.axaml.cs`: Up/Down in the expansions branch.
Audit of the rest of that handler, as asked: the review arm answers on `SelectedGroup` and
the expansions arm on `SelectedExpansionGroup`; Escape leaves the screen. Nothing else in
`OnMergeQueueKeyDown` indexes a list. Guarded by
`Every_expansion_shortcut_acts_on_the_selected_card`, which walks the branch's source and
fails any `Command.Execute` line that does not name the selection.

### 2. ReportMessage never cleared, HasReport shared across three surfaces
Added `MergeReportSurface` (None/Review/Expansions/History) and `ReportSurface`; the three
note blocks now bind `HasReviewReport` / `HasExpansionsReport` / `HasHistoryReport`, so a
same-game outcome can no longer render in the EXPANSIONS header with a Retract button for an
act that surface never performed. `Report(message, actId)` stamps the surface that is up;
`ClearReport()` runs on every segment switch and at the top of `LoadAsync`.
`RetractActAsync` now reloads FIRST and reports after, or the reload would have eaten the
retraction's own outcome line.

### 3. Unfired-signal contrast
Recomputed with `Winnow.App.Themes.Colorimetry` rather than estimated. `Opacity` 0.55 put
TextDim at **2.79:1** on Surface and **3.01:1** on Well against §8's 5.04 floor — and the
FIRED value cell too, at **4.96:1** on Surface, which the pass did not mention. Every ink
here is a token on a dark field, so any alpha below 1 lowers contrast and opacity cannot be
the mark at any value. Dropped it; the row keeps full ink and its VALUE cell steps Text →
TextDim, the app's own content-to-metadata step:
`Selector="Grid.signal.unfired > :is(TextBlock).data"`. Verified against current Avalonia
docs (styling/style-selector-syntax): `>` matches direct children in the logical tree and
`:is()` combines with a style class. Measured after: TextDim **5.88:1** on Surface,
**7.53:1** on Well; a fired value 13.11 / 16.79. The state stays carried three ways — the
em-dash value, the reason sentence, and the demoted ink.

### 4. Expansion covers decoded at ten times the drawn pixels
`ExpansionGroupViewModel.RequestCovers` scaled nothing: every 64x96 pack chip was given the
200px capsule width. Now scales by `ExpansionMemberViewModel.ChipWidth / CoverWidth`, as
`MergeGroupViewModel.RequestCovers` already did.

### 5. Rail counted review only
Added `OutstandingCount` / `OutstandingCountText` / `HasOutstanding` (review + expansions);
`RowOpacity` reads `HasOutstanding`, so the row recedes only when BOTH surfaces are empty.
`MainWindow.axaml` binds the count and its visibility to the outstanding pair. The row's
tooltip was `Pairs that might be the same game` — pair-model residue, and false about a
count that now covers two questions. docs-writer replaced it with
`Groups that might be the same game, or expansions of one`. The visible `SAME GAME?` label
is unchanged; renaming it is one of the product decisions and was not touched.

### 6. Two ASCII full stops
`ExpansionBaseTemplate` and `ExpansionRosterRowTemplate` now use the middle dot every merge
template uses. Guarded so no separator can regress.

### 7. SummaryText / EdgeSummaryFormat bound rather than deleted
The merge roster's condensed evidence line now carries
`AutomationProperties.Name="{Binding Evidence.SummaryText}"`, so six unlabelled TextBlocks
announce as `Title 0.04, year Δ11, publisher SAME` instead of bare values.

### 8. Four stale comments deleted
`MergeQueueView.axaml` 17-21 (migration 0019); the 0019 paragraph in
`MergeQueueViewModel`'s class summary; `Border.entry`'s "the three lists on the history
surface"; `OnCardFocus`'s "pressing S merges a different pair". Nothing else on the delete
list, which depends on the card-convergence decision.

## Tests
New: `tests/Winnow.Tests/SameGameSignalTests.cs`, `tests/Winnow.Tests/SameGameSurfaceTests.cs`,
and eleven cases appended to `MergeQueueViewModelTests.cs`. The markup/source guards follow
`StoreChipLayoutTests`' existing precedent (no headless renderer in this project).

Each guard was checked against the defect it names: with all four markup/source defects
reintroduced, seven of the new tests fail (opacity, unfired mark, GotFocus, PointerPressed,
report scoping, arrow keys, separator) and pass once restored.

**Full suite, isolated.** The working tree also holds another agent's in-flight enrichment
work, so the suite was re-run in a clean worktree at HEAD carrying ONLY this change:

    Winnow.Covers.Tests    Failed: 0, Passed:   70
    Winnow.Recommend.Tests Failed: 0, Passed:  107
    Winnow.Tests           Failed: 0, Passed: 2570

Build clean under TreatWarningsAsErrors, 0 warnings. Not committed.

In the live tree the suite shows 9 failures — FacetSyncServiceTests, EnrichmentSyncServiceTests,
EnrichmentTargetQueryTests, DatabaseBackupTests — all in the concurrently-edited IGDB/Steam
enrichment code and the new 0021/0022 migrations. None reference Winnow.App.

## Prose
All comment, doc-comment and copy text authored by docs-writer. The `TODO(docs-writer)`
markers still in the tree are the other agent's, in ExpansionDetector, StorefrontRelation,
the IGDB/Steam enrichment models and migrations 0021/0022 — none in the files this task
touched.

## Not done, deliberately
Card convergence, CONFIDENCE, contribution points, entry numbers, the HISTORY split, TOP OF
QUEUE, the 840 measure's scope, segment counts, virtualization, the Escape ladder, card and
chip automation names, copy consolidation, `RetractActAsync`'s full rescan, and the rest of
the dead-member and dead-markup deletions. Stages 2-4 and the six product decisions.
<!-- SECTION:NOTES:END -->
