---
id: TASK-96
title: The transparency block shows far more than a user needs
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 18:14'
updated_date: '2026-09-04 18:43'
labels:
  - ui
dependencies: []
priority: high
type: bug
ordinal: 123000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The TRANSPARENCY section of AppearanceView.axaml carries contrast ratios against dark and white wallpaper, an AA tick on the slider track, Mica composite figures, ground and pane admittance percentages, a gap-and-title-bar section, and a paragraph about how input fields are cut into panes. That is design-document material. The user wants a slider, a backdrop choice, and a warning only when the choice actually hurts legibility.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The transparency section is reduced to the slider, the backdrop and reach choices, and their short labels
- [x] #2 The contrast ratios, AA tick, Mica composite figures and admittance percentages no longer appear as standing readouts
- [x] #3 A legibility problem is still surfaced, but only when the current setting actually falls under AA, and in one short sentence
- [x] #4 The removed measurements remain documented in design-system.md
- [x] #5 Build succeeds and no test regresses
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Cut the TRANSPARENCY block in src/Winnow.App/Views/AppearanceView.axaml down to: the reading + slider (SOLID / MOST DESKTOP labels), the backdrop choice, the reach choice, and their short section labels.
2. Delete the standing readouts: the AA tick on the track, the LABELS ON THE TITLE BAR contrast rows and note, the Mica composite figure, the ground/pane admittance percentages, THE GAPS AND THE TITLE BAR section, the input-fields-are-steps paragraph, and the duplicated status line.
3. Keep exactly one legibility warning, bound to the existing UnderAa predicate so it appears only when the current setting falls under 4.5:1 in the worst case; keep the two conditional state notices that are not readouts (backdrop substituted, compositor refused) because design-system.md 14.3 and 14.6 require them.
4. Remove the now-dead view-model members from src/Winnow.App/ViewModels/AppearanceViewModel.cs and their Refresh() notifications; leave ThemeService, the settings round-trip and ThemeAudit untouched.
5. Correct the design-system.md sentences that claim the screen prints those figures (14.3, 14.6, 14.2's aside) and append the superseded text to docs/decisions.md. All prose delegated to docs-writer.
6. dotnet build and dotnet test -p:BaseOutputPath=C:\Temp\winnow-a1\.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented in src/Winnow.App/Views/AppearanceView.axaml and src/Winnow.App/ViewModels/AppearanceViewModel.cs.

The TRANSPARENCY card is now: the 'Let the desktop through' row with the percent reading, the slider with SOLID / MOST DESKTOP under its ends, one conditional legibility warning, and — only once the slider leaves SOLID — the backdrop choice under WHAT WINDOWS PUTS BEHIND THE WINDOW and the reach choice under HOW FAR IT REACHES. Deleted from the markup: the AA tick panels on the track, the LABELS ON THE TITLE BAR section (both contrast ratios and the note), the Mica composite figure and its sentence, the ground/pane admittance rows, the THE GAPS AND THE TITLE BAR section, the paragraph about input fields being steps cut into panes, and the trailing status line that duplicated the reach card's own description. The TextBlock.ratio.under style went with them.

Deleted from the view model: ContrastOnDarkWallpaper, ContrastOnWhiteWallpaper, WhiteWallpaperNote, DarkWallpaperNote, ContrastNote, AaMarkMargin, the ThumbWidth constant, MicaPicked, MicaComposite, MicaCompositeNote, WallTranslucent, IsFloating, ShowGapNote, GapNote, GroundAdmits, PaneAdmits, PaneAdmitsNote, TransparencyStatus, the Admits() and Ratio(Color) helpers, and the matching Refresh() notifications.

Added: LegibilityWarning, one sentence gated on the existing UnderAa predicate (slider off SOLID and worst-case title-bar contrast against a white desktop under 4.5:1). It names AaCeiling so the user knows where the line is, and it is Amber rather than Danger per design-system.md 14.3.

Two conditional notices were kept deliberately, because they are state notices rather than readouts and design-system.md 14.3 and 14.6 require them: the backdrop-substitution notice and the 'Not available here' compositor notice. Neither is a legibility warning, so there is still exactly one of those.

ThemeService, the settings keys and round-trip, Colorimetry, ThemeAudit and the per-user-theme WHAT THIS THEME MEASURES report were not touched.

design-system.md: five sentences in 14.2, 14.3 and 14.6 claimed the Appearance screen prints figures it no longer prints; all five were corrected in place and quoted verbatim into a new dated entry in docs/decisions.md, per AGENTS.md. All prose in this change — XAML copy, the warning sentence, code and XML doc comments, the design-system edits and the decisions entry — was authored by the docs-writer agent.

Verification. dotnet build -p:BaseOutputPath=C:\Temp\winnow-a1\ : 'Build succeeded. 0 Warning(s) 0 Error(s)'. Compiled bindings are on for Winnow.App (AvaloniaUseCompiledBindingsByDefault), and AppearanceView declares x:DataType, so every binding path left in the view is checked by the XAML compiler — a stale binding to a deleted property would have failed the build.

dotnet test -p:BaseOutputPath=C:\Temp\winnow-a1\ immediately after the code change: 2850 passed, 0 failed (Winnow.Tests), 152 passed (Winnow.Recommend.Tests), 70 passed (Winnow.Covers.Tests).

A later full run in the same working tree shows 6 failures, all from other agents' concurrent work landing in the same checkout and none in files this task touched: SchemaDisciplineTests (migrations 0023-0025 have no checksum line), ManualEntryTests x3, IdentityReadInventoryTests (HiddenGameRepository/ManualEntryRepository), StoreChipLayoutTests. Re-running the theme, transparency, layout and documentation-enforcement tests after every edit here: 239 passed, 0 failed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Cut the Appearance screen's TRANSPARENCY block down to the slider, the backdrop choice, the reach choice and their labels. The AA tick, both title-bar contrast ratios, the Mica composite figure, the two admittance percentages, the gaps-and-title-bar section and the input-field paragraph are gone as standing readouts, along with the view-model members behind them. One legibility warning remains, drawn only when the setting the user is holding actually falls under 4.5:1 and naming the percent at which it crossed. The five design-system.md sentences that claimed the screen prints those figures were corrected and quoted verbatim into docs/decisions.md. Verified by dotnet build (0 warnings, 0 errors, with compiled bindings checking every remaining binding path) and dotnet test: 2850/2850 in Winnow.Tests at the time of the change, and 239/239 across the theme, layout and documentation-enforcement tests afterwards.
<!-- SECTION:FINAL_SUMMARY:END -->
