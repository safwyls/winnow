---
id: TASK-103
title: Add a rating cap filter to the library view
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 22:50'
updated_date: '2026-09-06 17:46'
labels:
  - ui
  - data
dependencies:
  - TASK-101
priority: medium
type: feature
ordinal: 130000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The 18+ adult toggle stays in library settings as a single on/off gate. Separately, the user should be able to decide how much maturity they want to see at all, from the library view itself, without going into settings. The maturity evidence already stored per work (work_maturity, migration 0024) carries board tiers that are ordered, so a cap is expressible: show nothing above the chosen tier.

Depends on TASK-101, which splits the adults-only signals away from the broad 18+ board tiers; the cap consumes the tier scale that split produces.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The library view carries a rating cap control alongside its other filters
- [x] #2 Choosing a cap hides works whose stored maturity evidence exceeds it, across the grid, the list view and the bucket counts
- [x] #3 Works with no maturity evidence are shown at every cap, and that default is stated
- [x] #4 The cap and the settings 18+ toggle compose without contradicting each other, and it is clear which one is hiding a game
- [x] #5 The cap persists across launches
- [x] #6 Tests cover the ordering of tiers and the interaction between the cap and the toggle
- [x] #7 Steam content descriptors other than adult-only carry a tier so the cap can act on them; today only adult_only_sexual_content maps to a tier, so a nudity-heavy game reads as Unrated and sits inside every cap
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Core/Queries/Maturity.cs — give the four non-adult Steam descriptors tiers on the scale.
   nudity_or_sexual_content -> Mature; violence_or_gore -> Mature; general_mature_content -> Mature;
   frequent_nudity_or_sexual_content (new Core constant, id 4) -> Restricted18. adult_only_sexual_content
   stays AdultsOnly and stays the sole descriptor in the explicit gate. Add a stable token round-trip
   (MaturityTiers.Token / ParseToken) so a cap can be stored as text.
2. Core/Queries/Buckets.cs — BucketThresholds gains MaturityCap (default AdultsOnly = nothing capped),
   MaturityCapSettingKey "library.maturity_cap", ParseMaturityCap/FormatMaturityCap, and the composition:
   EffectiveMaturityCap = ShowExplicitContent ? MaturityCap : min(MaturityCap, Restricted18). The 18+ toggle
   is the CEILING of the cap, so the two controls are one ordered comparison and cannot contradict.
3. Data/LibraryQueryRepository — replace the IsExplicit block with the single cap test against
   EffectiveMaturityCap (identical behaviour at the defaults, so the existing explicit tests still hold),
   and add CountHiddenByRatingCapAsync: the same both-ways subtraction, cap lifted to the toggle's ceiling
   vs cap as stored, so the number is what the CAP alone is hiding. Add it to ILibraryQueryRepository.
   No migration: the evidence is already in work_maturity (0024) and the verdict is taken in C# on read.
4. App — LibraryViewModel.MaturityCap, passed into thresholds in LoadAsync so the grid, the list view and
   every rail count move together. DisplaySettingsViewModel gains the cap (slider index + label + the
   hidden count + AdultContentAllowed, which is only used to EXPLAIN the clamp, never to apply it).
   MainWindowViewModel syncs the ceiling and reloads; MainWindow.axaml.cs re-reads at startup exactly as
   GroupExpansions does, which is how the cap survives a launch.
5. XAML — the cap slider goes in the command bar's Display popover, beside "Show non-game entries": that
   popover is the library view's own home for persisted preferences that change which tiles exist. NOT the
   filter panel: design-system.md 11.4 says every group there is one a live list can store, and the cap is
   a standing boundary on the library rather than a cut of it. Delegate the control's visual design to
   avalonia-ui.
6. Tests — tier ordering, the four descriptor tiers, Unrated inside every cap, cap-and-toggle composition
   in all four states, the hidden count, and the settings round-trip.
7. Docs — game-library-design.md 6.4, design-system.md 8, docs/decisions.md, and all labels/copy/comments
   authored by docs-writer.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Context from TASK-101 (not yet decided, needs the user): Steam descriptor 3 (Adult Only Sexual Content) is the explicit gate and is correct there. Descriptor 4 (Frequent Nudity or Sexual Content) is stored but has no tier, so it reads as Unrated and passes every cap — as do descriptors 1 (Some Nudity or Sexual Content), 2 (Frequent Violence or Gore) and 5 (General Mature Content). Recommendation: leave the explicit toggle on descriptor 3 alone, and give 1/4/5 tiers on the MaturityTier scale so the rating cap is what catches "lots of nudity" rather than widening the adults-only gate. That keeps the two controls doing different jobs, which is the point of splitting them.

2026-09-06 UI verification: instantiated the compiled MainWindow and opened its actual Display flyout under Avalonia.Headless 11.3.20 using real App resources and Fluent templates. Supplied an isolated DisplaySettingsViewModel to the flyout content (the rest of the shell was deliberately unwired). Keyboard Left events on MaturityCapSlider traversed indexes 4 to 0: 18+, Mature, Teen, Preteen, All ages. Every step updated the bound view model and chosen-tier label. The unrated-default explanation was present and visible. Returning to index 5 with AdultContentAllowed false rendered the explicit-toggle clamp explanation. This complements the root agents data/persistence tests; the UI probe alone does not exercise the library reload or persistence wiring. Harness source: C:/Temp/winnow-task105/Program.cs.

Combined verification: coordinating agent ran dotnet test tests/Winnow.Tests --no-build -p:BaseOutputPath=C:/Temp/winnow-backlog-review/ with filters MergeQueueViewModelTests, GameMetadataEditorViewModelTests, RatingCap, ManualGameFromExecutableTests and GameExecutableIndexTests: 164 passed. Rating-cap tests cover ordered tier comparisons, Steam descriptor tiers, unrated games, cap/toggle composition, hidden counts, persistence and round trips. The actual Display flyout interaction described above supplies the rendered-control evidence.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented rating cap already complete. Closed after real Display-flyout keyboard/label/explanation verification and passing tier, query, toggle-composition, hidden-count and persistence tests (164 tests in the combined verification run).
<!-- SECTION:FINAL_SUMMARY:END -->
