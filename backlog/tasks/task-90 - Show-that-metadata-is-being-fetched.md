---
id: TASK-90
title: Show that metadata is being fetched
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 18:13'
updated_date: '2026-09-04 20:38'
labels:
  - ui
dependencies:
  - TASK-79
priority: medium
type: feature
ordinal: 117000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Enrichment and cover fetch happen in the background with no visible sign. A first run, or a run after a large import, looks like nothing is happening while covers and summaries slowly appear. Give the user one honest indication that a fetch is in flight and roughly how much is left, without a spinner in every tile. TASK-79 must settle the indeterminate-progress rule before any animated indicator ships.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A single, quiet indicator states that metadata is being fetched while a pass is in flight
- [x] #2 The indicator names what is left in real terms (a count) rather than an indeterminate animation, unless TASK-79 has settled otherwise
- [x] #3 The indicator disappears on completion and does not reappear for steady-state polls
- [x] #4 Reduced-motion is respected
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
UI half. The data is already there: EnrichmentSyncService knows targets.Count before the first slice and Program.cs calls EnrichAsync exactly once per launch.
1. TASK-79's rule (design-system.md §8) is words in a status field, never a spinner. The indicator is therefore TEXT ONLY: a label line and a Data count. No animation at all, which is how (a)-(d) of the motion clause and AC4 are satisfied by construction rather than by a reduced-motion branch.
2. FetchStatusViewModel (ObservableObject, singleton): IsActive, the label, the count as a formatted figure, the words beside it. Pure - no Dispatcher - so it is testable on the xunit thread.
3. EnrichmentSyncService gains an optional init-only IProgress<EnrichmentProgress> (Total, Remaining). Null by default, so no existing caller or test changes. Reported once at the start with the real target count and once after each committed slice.
4. FetchStatusReporter (Services) adapts that to the view model over Dispatcher.UIThread.Post. Only the composed app uses it.
5. The field lives in the rail's pinned bottom, above the gear: one indicator for the whole window, so no screen can ever draw a second. Border.note.working (Volt edge), the Stores panel's pattern generalised.
6. Never begins when the pass has nothing to do - EnrichAsync returns early on zero targets - so a steady-state relaunch shows nothing. Clears in a finally, so a cancelled or failed run removes it too.
7. No Cancel: §8 says 'where there is one'. This pass is not user-initiated and nothing can restart it before the next launch, so a Cancel would be a one-way stop dressed as a choice.
8. Copy by docs-writer; design-system.md §6 gains the component.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implementation landed (UI). Files: src/Winnow.App/ViewModels/FetchStatusViewModel.cs, src/Winnow.App/ViewModels/FetchStatusCopy.cs, src/Winnow.App/Services/EnrichmentProgress.cs, src/Winnow.App/Services/FetchStatusReporter.cs, plus the rail field in src/Winnow.App/Views/MainWindow.axaml, a Fetch property on MainWindowViewModel, one optional constructor parameter on EnrichmentSyncService and two DI lines in Program.cs.

WORDS ONLY, NO MOTION ANYWHERE. That is the conforming answer to design-system.md §8 rather than a shortcut: the rule is that the interface states what it is doing and what it is waiting for, in words, in a status field, and that motion may be added OVER a status field but may never replace one. With no motion there is nothing for reduced motion to disable, no local Transitions value to leak past a style (§12.5), and the surface is identical in both motion settings — an accessibility floor with no branch in it cannot be got wrong. That is how AC4 is met.

THE COUNT IS REAL. IWorkRepository.GetEnrichmentTargetsAsync returns the whole backlog before the first slice is asked for, so the total is known up front and the figure falls by a committed slice of 40 at a time. Every value shown was true when it was written; nothing is estimated.

IT CANNOT APPEAR FOR A STEADY-STATE POLL. EnrichAsync returns early on an empty target list, before it reports anything, so a warm library — every launch after the first — shows nothing. Program.cs calls EnrichAsync exactly once per launch and nothing else reports on this channel; the update poller and the facet pass are silent. Cleared in a finally, so a run cut short by shutdown or a failure takes the field with it instead of leaving a stale count.

NO CANCEL, DELIBERATELY. §8 says 'and offers Cancel where there is one'. This pass is not something the user started and nothing can restart it before the next launch, so a Cancel here would be a one-way stop dressed as a choice. Cancel belongs on the Stores panel's sign-in, which the user does start.

ONE INDICATOR FOR THE WHOLE WINDOW. It sits in the rail's pinned bottom row, above the settings gear, so it is visible from the library, the feed, the Merges screen and settings alike and no screen can draw a second one. Drawn as a Well field with a Volt edge — the Stores panel's Border.note.working pattern, which is the pattern §8 names.

SEAMS. FetchStatusViewModel touches no Dispatcher, so a test drives it on its own thread; FetchStatusReporter is the piece that marshals, via Dispatcher.UIThread.Post. The progress hook is an optional final constructor parameter on EnrichmentSyncService defaulted to null, so every existing caller and test is unchanged and a host that does not register it pays nothing.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a words-only fetch status field in the rail bottom, above the settings gear, so one indicator serves the whole window. FetchStatusViewModel/FetchStatusCopy carry the state and copy; EnrichmentSyncService gained an optional IProgress<EnrichmentProgress> (Total, Remaining) defaulted to null so no existing caller changed; FetchStatusReporter marshals to the UI thread. The count is real rather than estimated: GetEnrichmentTargetsAsync returns the whole backlog before the first slice, and the figure falls by a committed slice of 40. Conforms to the TASK-79 rule (design-system.md) by carrying no motion at all, so reduced motion has nothing to disable and both motion settings render identically. No Cancel, deliberately: the pass is not user-initiated. Verified by 102 passing tests across FetchStatusTests and EnrichmentSyncServiceTests (a pass reports its backlog first and zero last; an empty library reports nothing at all, which is what keeps it off a steady-state relaunch), plus a clean solution build with TreatWarningsAsErrors.
<!-- SECTION:FINAL_SUMMARY:END -->
