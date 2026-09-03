---
id: TASK-70.9
title: Consolidate the Same Game screen into one designed thing
status: In Progress
assignee:
  - '@safwyl'
created_date: '2026-09-02 12:35'
updated_date: '2026-09-02 17:51'
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
STAGE 2-3-4 (partial), carrying the user's four decisions. Stage 1 shipped separately.

1. CONVERGE THE CARD onto one arrangement (decision 1). Primary capsule 200x300 in a
   200px left column; every other member a row on the right, at every member count.
   MergeGroupMemberViewModel gains IsSoleChild, set by MergeGroupViewModel.ApplyCore
   when Others.Count == 1, and the row varies on it:
    - sole child: cover 200x300, the four-row diff open (no disclosure), NO checkbox,
      forced IsIncluded (the two answer buttons carry include/exclude, TASK-70.3);
    - three or more: 64x96 chip, condensed one-line evidence, disclosure, checkbox.
   ShowIncludeControl is resurrected with the meaning the pass gives it:
   !IsPrimary && !IsSoleChild. Deletes the IsPair grid, MergeGroupViewModel.Left/Right/
   PairEdge/PairHasNoSignals and the duplicated inline signal template (one shared
   MergeSignalTemplate resource). RequestCovers gives the primary AND the sole child the
   capsule width; only chips are scaled.

2. EVIDENCE (decision 2). Keep CONFIDENCE. Delete the contribution column, its three
   styles, and MergeSignalViewModel.ContributionText / Contribution / IsForMatch /
   IsAgainstMatch / Signed. Delete BestScoreText and its binding. Delete the visible
   entry numbers from all four templates and with them MergeSideViewModel.ReleaseText,
   MergeGroupMemberViewModel.ReleasesText, ExpansionMemberViewModel.ReleasesText.
   Automation labels are re-derived PROGRESSIVELY from facts already on the row -- title,
   then stores, then year, then publisher, then a positional last resort -- assigned by
   the group so two same-titled members are still distinguishable without a database id
   (design-system 10.5).

3. BAND LABEL (decision 3). TOP OF QUEUE binds IsPriority, the matcher's confidence
   band, not a queue position. docs-writer names it from the facts; the tooltip goes
   (notes.md bans the blurb). Literal moves into MergeCopy, as do Same game, Different
   games, CONFIDENCE, TITLES and the roster column labels.

4. HISTORY (decision 4). One chronological log, newest first, retracted acts REMOVED
   rather than stamped -- this reverses the 'a retracted row stays on screen with the
   date it was reversed' line in the pass's keep-list, on the user's explicit decision.
   BuildLinkHistoryAsync filters to live acts; MergeLinkHistoryRowViewModel loses
   IsLive/RetractedAt/IsRetracted/RetractedLabelText/RetractedAtText/ChildCountText.
   RETRACT is renamed UNDO everywhere on this screen: copy, tooltips, automation names,
   commands, view-model members and the comments that explain the control. The
   repository API (IIdentityLinkRepository.RetractActAsync) and the schema are another
   layer and keep their names.

5. ARRANGEMENT. One centred 840 measure carrying the segment strip's content, each
   header with its count, the report note, the card list, the empty state and the
   history list. MaxWidth/HorizontalAlignment come off Border.card so cards fill the
   column instead of setting it.

6. DELETIONS beyond the above: MergeGroupMemberViewModel.IncludeControlText,
   MergeSideViewModel.NormalizedTitle and HasPublisher,
   MergeQueueViewModel.DifferentGamesTooltip, MergeLinkHistoryRowViewModel.ChildCountText,
   the six dead geometry constants (ExpansionGroupViewModel.CoverWidth/CoverHeight,
   ExpansionMemberViewModel.ChipWidth/ChipHeight, MergeGroupMemberViewModel.ChipHeight,
   MergeQueueViewModel.CoverHeight), the KEEP note box (its WHY line moves under the
   primary capsule), the report block written out three times (one shared template), and
   MergeCopy's empty and duplicated section banners. EffectLine folds into each segment
   header, since deleting the KEEP box removes its home.

7. RELATION LABEL. ExpansionProposalMember.RelationLabel now carries the storefront's
   own word (demo, beta, playtest, expansion, dlc, remaster, port, mod, superseded...).
   Thread it to ExpansionMemberViewModel and show it on the row beside the title, so a
   playtest stops reading as an expansion. Display only; the parent stays fixed by the
   relation and no survivor chooser appears.

8. COPY. Every string re-authored by docs-writer with the brevity instruction: the band
   label, the undo vocabulary, the folded intro lines, the history heading naming both
   relations, and the new automation formats.

TESTS (scoped, then full, via --artifacts-path into the session scratchpad; no commit):
the converged card at two, three and six members; the include control's meaning at two;
confidence surviving while points and entry numbers are gone; automation names
distinguishing same-titled members without entry numbers; the renamed band label and its
absent tooltip; history chronological with retracted rows absent; no user-facing string
saying retract; the 840 measure covering header, count, report and empty state; the
deleted members genuinely unreferenced; the relation label on the expansion row.

STAGE 4 (partial) — the segment counts, and the rail scope they settle.

9. COUNTS ON EVERY SEGMENT. Each of the three tabs draws its own count beside its
   label: REVIEW binds PendingCountText/HasPending, EXPANSIONS binds
   ExpansionCountText/HasExpansions, HISTORY binds a new LinkHistoryCountText with
   the existing HasLinkHistory. Number in the 'data' class (IBM Plex Mono,
   FontFeatures tnum) at 11px, the same treatment the rail already gives a count
   beside a display-s label. A zero draws nothing, the rule this screen's headers
   already used ('a permanent zero is noise'). LinkHistory is built by LoadAsync,
   not only on arrival, so the HISTORY count is populated before the surface is
   first opened.
   Ink: controls.axaml's 'Button.seg.tab TextBlock' rules are overridden by
   tokens' later-added '.data' Foreground, so the count would have rendered full
   Text white on an unlit tab. Three styles in MergeQueueView's own
   UserControl.Styles put the count back on the segment grammar (TextDim / Text on
   hover / Volt when on). Control-level styles beat application-level ones —
   verified against Avalonia's styling/style-precedence docs, not assumed.

10. DELETE THE IN-HEADER COUNT. Both 22px count blocks go, and with them
    MergeQueueViewModel.PendingCountLabel / ExpansionCountLabel and
    MergeCopy.PendingCountLabel / ExpansionCopy.PendingCountLabel. Each header
    collapses from a two-column Grid to the question and its intro line.

11. THE RAIL COUNTS BOTH SURFACES AGAIN. MainWindow.axaml binds
    OutstandingCountText/HasOutstanding; Opacity stays on RowOpacity, which
    already followed the same pair. TASK-71 moved the rail to the review count
    because 63 stood against a 45 GROUPS header and agreed with neither. Deleting
    that header count removes the thing it contradicted: the rail names the
    screen, the tabs name the surfaces, and the rail's number is now the two tab
    numbers added up. HISTORY is excluded because a log is not outstanding work.

12. THE RAIL TOOLTIP moves into MergeCopy.RailTooltip and is bound rather than
    written as a literal, so the screen's copy and the rail's description of it
    live in one file.

13. AUTOMATION NAMES on the three tabs, so a bare number in a control is not what
    a screen reader hears (§8). One format per tab, authored by docs-writer.

14. RETARGET, NOT DELETE, the TASK-71 rail tests: SameGameSurfaceTests's markup
    guard and MergeQueueViewModelTests's view-model guard both asserted the rail
    equals the review count. They are re-pointed at the invariant that replaces
    it — the rail equals the two segment counts summed, and no number on the
    screen contradicts it. The_rail_recedes_only_when_both_surfaces_are_empty is
    unchanged and still correct. New tests cover the three tab bindings, the tnum
    face, the absence of the in-header count, and the count at zero.

15. COPY. One docs-writer delegation: the three automation formats, the relocated
    rail tooltip, every new and changed doc comment, and the comments recording
    why the rail's scope is the screen and the tabs' scope is a surface.

16. VERIFY. Scoped tests, then the full suite, both with --artifacts-path into the
    session scratchpad. No app run: TASK-72's --data-dir override is not ready and
    repointing %LOCALAPPDATA% does not isolate the live database. No commit.
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

## Stage 2-3-4 (partial) implemented, carrying the user's four decisions - NOT finalized

Structural change, arrangement change, delete list, four decisions, plus the relation
label the storefront work added since the pass was written. Stage 1's eight defects were
already in the tree.

### Decision 1 - the two card layouts converged
One arrangement at every member count: the primary's capsule at 200x300 in a 200px left
column, every other member a row on the right. What varies is inside the row, and the row
asks the member:
 - MergeGroupMemberViewModel.IsSoleChild, set by MergeGroupViewModel.ApplyCore when
   Others.Count == 1, drives CoverWidth/CoverHeight (200x300 vs 64x96),
   PlaceholderFontSize/LineHeight, ShowFullEvidence, ShowCondensedEvidence,
   ShowEvidenceDisclosure, ShowNoEvidenceNote and ShowIncludeControl;
 - the dead ShowIncludeControl is resurrected as !IsPrimary && !IsSoleChild, which is
   the meaning the pass proposed for it;
 - a sole child is forced IsIncluded, because the two answer buttons are its only
   include control (TASK-70.3's 'NO CHECKBOXES at two members');
 - CoverWidth is now the single source for the decode width too: RequestCovers passes
   displayWidthPixels * member.CoverWidth / CoverWidth, so nothing decodes a 600x900
   source for a 64px chip and the sole child's 200x300 cover is asked for at capsule
   width.
Deleted with it: the IsPair grid, the duplicated inline signal template (one shared
MergeSignalTemplate), MergeGroupViewModel.Left / Right / PairEdge / PairHasNoSignals, and
the public Ordered (now a private field). Card geometry: the roster still sets the 840
ceiling (44 + 200 + 28 + 526.0 = 798); a two-member card needs 44 + 200 + 28 + (30 + 200
+ 14 + evidence + 14 + 102.3), about 772 at the evidence line's minimum, so the measured
ceiling is unchanged and the roster remains the binding density.

### Decision 2 - evidence detail
CONFIDENCE stays on the card head. Deleted: the contribution column, its three styles,
MergeSignalViewModel.ContributionText / Contribution / IsForMatch / IsAgainstMatch and
the Signed() helper; the per-row BestScoreText and the BestScore it read. Entry numbers
are gone from all four templates, and with them MergeSideViewModel.ReleaseText and both
ReleasesText forwarders.
Automation names no longer lean on the entry number. New MergeMemberLabels assigns a
card's member labels progressively - title, then stores, then year, then publisher, each
added only while two members would otherwise share a label, with a position ('1 of 3')
as the last resort. So Prey against Prey on one storefront is told apart by the year the
row already prints, and no database id reaches a screen reader (design-system 10.5).

### Decision 3 - the band label
TOP OF QUEUE renamed; the literal and its tooltip are gone from the markup, the label is
MergeCopy.PriorityBandLabel and docs-writer named it from the facts (it binds IsPriority,
the matcher's top confidence band, not a queue position). Same pass moved Same game,
Different games, CONFIDENCE, TITLES, the roster column labels and 'Same game?' into
MergeCopy; a guard now fails any literal Text/Content/ToolTip.Tip on the screen.

### Decision 4 - history and the undo vocabulary
BuildLinkHistoryAsync drops any act with no live link, so an undone act LEAVES the log
rather than staying stamped RETRACTED. This REVERSES the pass's keep-list line 'a
retracted row staying on screen with the date it was reversed' - recorded here because it
changes a decision: the user's reason is that the log should show what is in force, and
undo remains reachable from the report note the moment the act is performed. Order is
unchanged (newest first, one list, both relations).
Retract is renamed UNDO across the screen: MergeCopy.UndoButton / UndoTooltip / Undone /
UndoneAlready / UndoAutomationFormat, MergeQueueViewModel.UndoCommand /
UndoReportCommand / CanUndoReport / ReportUndoActId / ReportUndo*Text /
ReportUndoAutomationName / UndoActAsync, MergeLinkHistoryRowViewModel.IsUndoing /
CanUndo / UndoButtonText / UndoTooltip / UndoAutomationName, and every comment that
explains the control. The repository API (IIdentityLinkRepository.RetractActAsync) and
the schema keep their names: another layer, not the interface.
Deleted with the split: IsLive, RetractedAt, IsRetracted, RetractedLabelText,
RetractedAtText, ChildCountText, MergeCopy.RetractedLabel.

### Arrangement - one 840 content column
The measure came off Border.card (it set MaxWidth and HorizontalAlignment alone) and
became a ':is(Control).measure' style carried by eight columns: the segment strip's
content, both surface headers with their counts and outcome reports, both card lists,
both empty states and the history log. The segment strip is included because the user
named it; its rule still crosses the pane (11.1), only its content aligns with the cards.
Verified ':is(Control).measure' against current Avalonia docs (styling/style-selector-
syntax) rather than memory.

### Deletions beyond the above
MergeGroupMemberViewModel.IncludeControlText and ChipHeight; MergeSideViewModel
.NormalizedTitle (and its ctor parameter) and HasPublisher (GameDetailsViewModel has its
own); MergeQueueViewModel.DifferentGamesTooltip and CoverHeight; ExpansionGroupViewModel
.CoverWidth / CoverHeight / EffectLine; ExpansionMemberViewModel.ChipWidth / ChipHeight;
MergeGroupViewModel.PrimaryLabel and MergeCopy.PrimaryLabel with the KEEP note box (the
WHY line moved under the primary's radio, which is now the only place the card says which
title is kept); the report note block, written out three times, now one shared
ReportNoteTemplate; MergeCopy's empty 'Merge modes and limits' banner and its duplicated
'Automation' banner; MergeCopy.LinkEffect and ExpansionCopy.GroupEffect, folded into the
two segment headers because deleting the KEEP box removed the effect line's home;
ExpansionCopy.Retracted, which nothing called.

### Relation label
ExpansionProposalMember.RelationLabel is threaded to ExpansionMemberViewModel
(RelationText, uppercased, and HasRelation) and drawn top-right on the pack row, so a
playtest reads PLAYTEST instead of arriving under an EXPANSIONS heading with nothing to
say otherwise. Display only: the base stays fixed by the relation, no survivor chooser,
no number moves.

## Copy as shipped, tests and verification

### Copy (all authored by docs-writer with the brevity instruction)
 - Confidence band, replacing TOP OF QUEUE: **STRONG MATCH**, and its tooltip is gone.
   Two uppercase words, naming the matcher's band rather than a position.
 - Review intro, absorbing the per-card effect line: **One tile, a chip per store. Undo
   any time.**
 - Expansions intro, absorbing its effect line: **Display only. Hours and counts stay
   separate. Undo any time.**
 - History heading, now naming both relations: **Linked and grouped**, under it **Newest
   first. Undo any time.**
 - Undo vocabulary: button **Undo**, tooltip **Proposals return to review.**, reports
   **Undone. Returns to review.** and **Already undone.**, automation **Undo: {0}**.
 - Segment tooltips: **Groups waiting for an answer** (was 'Pairs waiting for an answer',
   pair-model residue) and **What you have linked and grouped**.
 - Card literals moved into MergeCopy unchanged in wording: Same game, Different games,
   'Same game?', CONFIDENCE, TITLES, TITLE / YEAR / PUBLISHER.
 - Member labels: '{0} ({1})' with qualifiers joined by ', ' and '{0} of {1}' as the last
   resort, so a screen reader hears 'Prey (Steam, 2017)' rather than 'Prey Steam #1024'.

### Tests
Updated: MergeQueueViewModelTests (Left/Right, PairEdge, ReleasesText, the contribution
assertions, the undo vocabulary, and the history row that used to stay stamped),
SameGameSurfaceTests (the report note is now one ContentControl template; the roster
evidence line is found by its automation name), StoreChipLayoutTests (the measure moved
off Border.card onto the content column), SameGameSignalTests (chip width constant).

New:
 - A_two_member_card_draws_its_child_at_full_size_with_no_checkbox
 - Moving_the_primary_at_two_members_moves_the_full_size_row
 - A_three_member_card_makes_every_child_a_chip_with_an_include_control
 - A_six_member_card_draws_five_rows_and_six_distinct_names
 - Every_member_asks_for_the_cover_at_the_size_it_draws
 - The_card_states_its_confidence_and_the_matchers_band
 - The_history_log_is_newest_first_and_holds_only_what_stands
 - An_expansion_row_states_the_relation_in_the_stores_own_word
 - five MergeMemberLabels cases (title alone, +stores, +year for one title on one store,
   the positional last resort, and no '#' at any depth)
 - No_layout_on_the_screen_switches_on_the_member_count
 - The_evidence_shows_no_arithmetic
 - No_member_on_the_screen_shows_its_entry_numbers
 - The_confidence_band_is_named_from_copy_and_carries_no_tooltip
 - Every_user_facing_string_on_the_screen_comes_from_copy
 - No_user_facing_string_says_retract
 - The_expansion_row_draws_the_relations_own_word
 - Every_member_the_pass_deleted_is_gone (35 members, by reflection)
 - Every_column_of_the_same_game_screen_takes_the_measure
 - The_same_game_screen_holds_one_measured_content_column

### Each guard checked against the defect it names
Reintroduced one at a time, then reverted:
 - checkbox back at two members -> 2 failures
 - sole child back to a 64x96 chip -> 2 failures
 - a copy string reading 'Retract' -> 1 failure
 - TOP OF QUEUE and its tooltip back in the markup -> 2 failures
 - the 840 measure back on Border.card alone -> 1 failure
 - undone acts kept on the history log -> 2 failures
Entry numbers could not be reintroduced at all: binding ReleasesText fails the build
(AVLN2000, no such property), which is stronger than the text guard.

### Results, verbatim
Build: 0 Warning(s), Build succeeded, under TreatWarningsAsErrors.
    Winnow.Tests           Failed: 0, Passed: 2657, Skipped: 0, Total: 2657
    Winnow.Covers.Tests    Failed: 0, Passed:   70, Skipped: 0, Total:   70
    Winnow.Recommend.Tests Failed: 0, Passed:  107, Skipped: 0, Total:  107
Built and tested via --artifacts-path into the session scratchpad; the running app's
src/Winnow.App/bin was never touched. Not committed.

### Concluded against, or out of scope
 - The pass's fallback (keep the symmetric pair layout) was NOT needed. The converged card
   reads better at two members and the width budget holds: a two-member card needs about
   772px against the roster's 798, so the roster still sets the measured 840 ceiling.
 - Others is still ordered by work id. Ordering by evidence (direct edges by score, then
   indirect) was in the pass's 'reduce repetition' section, not in the four decisions, and
   it changes which row a user answers first; left for a decision of its own.
 - Not done, and not asked for in this pass: virtualization of the card lists, counts on
   the segment tabs, the Escape ladder, AutomationProperties.Name on cards and store
   chips, unifying HasCompletedSweep with _scannedExpansions, and UndoActAsync's full
   LoadAsync rescan. AC #11's second clause ('separates acts currently in force from
   retracted acts') is superseded by decision 4: undone acts leave the log, so everything
   on it is in force.

## Stage 4 (partial): counts on every segment, and the rail's scope — implemented, NOT finalized

Only AC #6. No other stage-4 item shipped: the 840 measure's scope, virtualization, the
Escape ladder, the history live/retracted split and the card/chip automation names are all
still open.

### The count is on the tab now, not in the header

Each of the three tabs draws its label then its own number: REVIEW binds
PendingCountText/HasPending, EXPANSIONS binds ExpansionCountText/HasExpansions, HISTORY
binds a new LinkHistoryCountText with the existing HasLinkHistory. The number takes the
`data` class, which carries IBM Plex Mono and FontFeatures=tnum, at 11px — the treatment
the rail already gives a count beside a display-s label. A zero draws nothing, which is the
rule the page headers used before the number moved.

Both 22px in-header count blocks are deleted, and each header collapsed from a two-column
Grid to the question and its intro line. MergeQueueViewModel.PendingCountLabel /
ExpansionCountLabel and MergeCopy.PendingCountLabel / ExpansionCopy.PendingCountLabel went
with them; all four are in Every_member_the_pass_deleted_is_gone now.

INK, worth recording because the three styles it needs look redundant and are not.
controls.axaml's `Button.seg.tab TextBlock` rules already state the segment's ink grammar,
but App.axaml.cs promotes tokens' TextStyles into Application.Styles AFTER the
controls.axaml include, so tokens' `.data` Foreground=Text wins and the count would have
rendered full white on an unlit tab. MergeQueueView restates the grammar in its own
UserControl.Styles for TextBlock.data. Verified against Avalonia's styling/style-precedence
docs rather than assumed: a UserControl's styles are evaluated after the application's, so
the closer scope wins. Guarded by The_segment_count_takes_the_tabs_own_ink.

### The rail counts both surfaces again — the TASK-70.9 / TASK-71 tension, resolved

TASK-71 pointed the rail at PendingCountText because SAME GAME? 63 stood beside a 45 GROUPS
header and agreed with neither, leaving RowOpacity on the combined count — so the row
stayed lit with no number at all while only expansion cards waited. Silent rather than
wrong, and its own notes filed the rest as 70.9 work.

Restored to OutstandingCountText/HasOutstanding, and the reasoning is that deleting the
in-header count removes the thing the rail contradicted. THE RAIL NAMES THE SCREEN; THE
TABS NAME THE SURFACES. Different scopes, so no contradiction: the rail's figure is now
reachable by adding the two answerable tab numbers, which is a relation a reader can check
on screen rather than a third number appearing from nowhere.

The unit objection TASK-71 raised — that the sum mixes same-game groups with expansion base
games — does not survive the move. At the rail's grain both are one card, one act, one
answer: a question waiting for you. The tooltip has said so since stage 1 and now sits over
a count that means it.

HISTORY is deliberately outside the sum. A log is not outstanding work, so its tab counts
and the rail does not.

Opacity still binds RowOpacity, which still follows the same pair, so the row's count and
its standing can no longer disagree about whether there is work on the screen.

### The rail tooltip

Already reworded in stage 1 (`Pairs that might be the same game` became `Groups that might
be the same game, or expansions of one`), so the pair-model residue the pass flagged was
gone. What was left was that it sat as a literal in MainWindow.axaml. Moved to
MergeCopy.RailTooltip and bound, so the screen's copy and the rail's description of it are
one file. Wording unchanged — it was already right for a count covering both questions.

### DEFECT FOUND AND FIXED while wiring the HISTORY count

SameGameAsync and GroupExpansionsAsync write an act and never rebuilt LinkHistory; only
LoadAsync and arriving at HISTORY did. With no count on the tab that was invisible. With
one, the strip would have said an act was recorded — the outcome report note is on screen
saying so — and that HISTORY holds nothing, until the user opened HISTORY and the number
caught up. Both act-writing paths now rebuild. Cost is one pass over the act log, which
holds acts the user performed by hand and does not grow with the library, and it reads no
candidate, so Answering_reads_nothing_however_long_the_queue_is still holds. Guarded by
Every_segment_count_follows_its_own_surface.

### Accessibility

A tab now holds a bare number, so each carries an AutomationProperties.Name naming what its
number counts. docs-writer phrased all three so a count of 1 cannot read against a plural
noun — `Review, {0} to answer`, `Expansions, {0} to answer`, `History, {0} recorded` —
which is the TASK-73 class of bug avoided rather than repeated. No pluralization helper was
needed. The units themselves (groups, base games, acts) stay on the tooltips, which were
already correct.

### TASK-71 tests retargeted, not deleted

Both asserted the rail equals the review count, which is a behaviour deliberately changed
here, so both were re-pointed at the invariant that replaces it rather than removed.

- SameGameSurfaceTests.The_rail_count_is_the_number_the_review_header_shows became
  .The_rail_count_is_the_answerable_segment_counts_added_up. Asserts the rail binds
  OutstandingCountText/HasOutstanding, that Opacity still binds RowOpacity, that the two
  answerable tabs bind the two counts the rail sums, and that the tooltip is copy.
- MergeQueueViewModelTests.The_rail_count_and_the_review_header_are_one_number became
  .The_rail_count_is_the_two_answerable_segment_counts_added_up. Same fixture (a review
  group and expansion work waiting at once, the case that made the two numbers differ); now
  asserts OutstandingCount == PendingCount + ExpansionCount, the three rendered strings,
  and that history is not in the sum.
- .The_rail_counts_expansion_work_with_an_empty_review_queue kept, strengthened: the rail
  now DRAWS 2 where it used to be silent, and the EXPANSIONS tab draws 2 beside it. Its
  comment about the rail drawing no number was removed because it no longer describes the
  screen.
- .The_rail_recedes_only_when_both_surfaces_are_empty unchanged and still correct.

### New tests

SameGameSurfaceTests: Every_segment_tab_states_its_own_count (three tabs, three bindings,
the visibility gate, the data class with no local FontFamily or FontFeatures opting out of
tnum, the label leading the number, an automation name on every tab),
No_page_header_repeats_a_segment_count (no count binding outside the strip; both header
labels unreferenced), The_segment_count_takes_the_tabs_own_ink.

MergeQueueViewModelTests: Every_segment_count_follows_its_own_surface,
Every_segment_tab_announces_what_its_number_counts.

Each guard was checked against the defect it names. With the rail pointed back at the
review count, the HISTORY tab's count removed and the three ink styles deleted, three
markup guards fail and pass once restored; with the history rebuild removed,
Every_segment_count_follows_its_own_surface fails.

StoreChipLayoutTests.Every_column_of_the_same_game_screen_takes_the_measure needed no
change — the counts moved into the segment strip, which already carries the measure — but
its comment claimed the headers carry the counts and was corrected.

### Verification

dotnet build and dotnet test from the repo root with --artifacts-path outside the tree.
Build clean under TreatWarningsAsErrors, 0 warnings. Full suite green: Winnow.Covers.Tests
70, Winnow.Recommend.Tests 115, Winnow.Tests 2,683 — 2,868 passed, 0 failed. Not committed.

The app was NOT run. TASK-72's --data-dir override is not ready, and repointing
%LOCALAPPDATA% does not isolate the live database (WinnowDataLocation resolves through the
Windows shell API and ignores the variable). Everything here is verified through markup and
view-model tests.

NOTE: a `Winnow` app process and a stray `testhost` were observed holding files in the
shared build output during this work; neither was launched from this task. Both had exited
before the final build and suite run, which are therefore clean.
<!-- SECTION:NOTES:END -->
