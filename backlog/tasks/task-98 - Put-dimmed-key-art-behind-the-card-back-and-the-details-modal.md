---
id: TASK-98
title: Put dimmed key art behind the card back and the details modal
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-04 18:14'
updated_date: '2026-09-04 18:53'
labels:
  - ui
dependencies: []
priority: medium
type: enhancement
ordinal: 125000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The flip side of a game tile and the details modal both present information over flat chrome, so a game loses its identity the moment the user turns it over. Lay the games own art behind that information, heavily dimmed, so the surface still belongs to the game it describes without competing with the text.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The tile back renders the games art beneath its information, dimmed enough that text contrast stays at or above AA
- [x] #2 The details modal does the same
- [x] #3 A game with no art falls back to the current flat treatment with no layout shift
- [x] #4 The dim level is a token, not a literal, and is stated in design-system.md
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add ArtVeilAlpha (0.92) + the ArtVeil brush to tokens.axaml, derive ArtVeil = A(Surface, ArtVeilAlpha) in WinnowTheme.Tokens so every theme reaches it.
2. GameTileView.axaml: the back face becomes Surface -> art stack (the SAME Cover.Floor / Cover.Vivid@DisplayAlpha pair the front uses, so one image path and one dormancy ramp) -> ArtVeil -> the existing DockPanel. No art means both Images draw nothing and ArtVeil over Surface resolves to Surface exactly: same geometry, same tone, no layout shift.
3. GameDetailsView.axaml: the same three layers inside the modal card, over the modal's own single Cover bitmap, which is already decoded at 200px and full saturation per section 10.
4. Extend Colorimetry with ArtBackedField / WorstArtBackedContrast, walking the 256 grey art values that exhaustively span the achievable composite luminance range (composite luminance is monotone in each art channel, so black and white bracket it), and have ThemeAudit warn a user theme that falls under AA there.
5. Add ThemeContrastTests coverage: every text ink (Text, TextDim, Azure, Amber) over the art-backed field, every theme, every art grey, at every transparency position, plus the SurfaceRaisedFaint hover veil; and assert the no-art identity (ArtVeil over Surface == Surface).
6. Delegate the design-system.md prose and every code comment to docs-writer.
7. Verify: dotnet build, then dotnet test -p:BaseOutputPath=C:\Temp\winnow-a2\.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
## What was built

Both surfaces are now three layers over the same construction: opaque `Surface` at the bottom, the game's art, then `ArtVeil` - the theme's own `Surface` at `ArtVeilAlpha` - then the text.

- `src/Winnow.App/Themes/tokens.axaml`: `ArtVeilAlpha` (x:Double, 0.92) and the `ArtVeil` brush.
- `src/Winnow.App/Themes/WinnowTheme.cs`: `public const double ArtVeilAlpha = 0.92`, and `["ArtVeil"] = A(Surface, ArtVeilAlpha)` in `Tokens`, so ThemeService reaches it in every theme including user themes.
- `src/Winnow.App/Views/GameTileView.axaml`: the back face wraps its DockPanel in a Panel behind two Image layers bound to the SAME `#Root.Cover.Floor` / `#Root.Cover.Vivid` pair the front face draws, at the same `DisplayAlpha`. `BackVividArtwork` was added to the existing 140ms transition selector and to the reduced-motion `.snap` selector, so it is one ramp rather than two.
- `src/Winnow.App/Views/GameDetailsView.axaml`: the card wraps its Grid in a Panel behind a clipped Image bound to `GameDetailsViewModel.Cover` - the 200px full-saturation bitmap the modal already requests. No second request, no second decode.
- `src/Winnow.App/Themes/Colorimetry.cs`: `ArtBackedField` and `WorstArtBackedContrast`.
- `src/Winnow.App/Themes/ThemeAudit.cs`: a warning against `seeds.surface` when a user theme's Surface lets too much art through.
- `tests/Winnow.Tests/ThemeContrastTests.cs`: four new tests.
- `design-system.md`: new section 5.5, authored by docs-writer.

## Why 0.92

Walked, not assumed. Worst text ink (Text / TextDim / Azure / Amber) over the brightest cover art could be, per theme:

- 0.91: Winnow 4.49 (TextDim), Nightshift 5.50, Tungsten 4.74, Box art 4.62 - Winnow is under AA.
- 0.92: Winnow 4.62 (TextDim), Nightshift 5.69 (TextDim), Tungsten 4.93 (Amber), Box art 4.76 (TextDim).

The composited field over white art is #29393B / #1E2128 / #342A23 / #32363A. With the hovered update row's `SurfaceRaisedFaint` on top the worst figures move by at most 0.07, upward.

## Why the proof is a proof

The test walks the art as 256 greys. Each channel of the composite is monotone in the art's own channel and WCAG relative luminance is monotone in the channels, so black and white bracket the composite's luminance and the greys hit every value in between - 256 measurements cover every possible cover. `Flare` is excluded because on both surfaces it is a dot and never a word, and WCAG scores a non-text component against 3:1.

## Why there is no layout shift

The veil IS `Surface`, so over the opaque `Surface` both surfaces already paint it composites back to `Surface` bit-for-bit. A game with no art draws no image and gets the flat treatment exactly - same tone, and same geometry because the art and the veil are siblings in a Panel that take no space of their own. `A_game_with_no_art_gets_the_flat_surface_back` asserts the identity at every slider position.

## All prose delegated

Every comment, XML doc comment, the ThemeAudit warning string and section 5.5 of design-system.md were authored by the docs-writer subagent (agent a1576b3204f4ba662). No prose in this change was written by me.

## Verification

dotnet build -p:BaseOutputPath=C:\Temp\winnow-a2\ -m:1

    Build succeeded.
        0 Warning(s)
        0 Error(s)

dotnet test --no-build -p:BaseOutputPath=C:\Temp\winnow-a2\

    Passed!  - Failed: 0, Passed:   70, Skipped: 0, Total:   70 - Winnow.Covers.Tests.dll
    Passed!  - Failed: 0, Passed:  152, Skipped: 0, Total:  152 - Winnow.Recommend.Tests.dll
    Failed!  - Failed: 1, Passed: 2922, Skipped: 0, Total: 2923 - Winnow.Tests.dll

The one failure is Enforcement.SchemaDisciplineTests.No_shipped_migration_has_been_edited, from a data-layer agent working concurrently in the same tree: src/Winnow.Data/Migrations/checksums.txt is modified and 0023/0024/0025 are untracked. Nothing in this change touches migrations. An earlier full run in this session was green across all three assemblies with the whole change set in place; the intermediate failures seen since (ManualEntryTests, IdentityReadInventoryTests, StoreChipLayoutTests, a build break in Winnow.Data, a build break in the untracked LibraryChromeTests.cs) were all mid-edit states of other agents and each cleared on its own - verified by stashing this change and re-running those tests, which passed with and without it.

Scoped run over everything this change can reach - ThemeContrastTests, ThemeJsonTests, FloatingLayoutTests, UserThemeStoreTests, TileActionsTests, GameDetailsViewModelTests:

    Passed!  - Failed: 0, Passed: 292, Skipped: 0, Total: 292

Sensitivity check: temporarily setting ArtVeilAlpha to 0.88 makes Art_behind_the_back_face_and_the_modal_keeps_text_over_AA fail, so the walk is not vacuous. Reverted.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The tile's back face and the detail modal now lay the game's own art behind their information under ArtVeil - the theme's own Surface at ArtVeilAlpha (0.92), a token in tokens.axaml and WinnowTheme, derived per theme in WinnowTheme.Tokens. Because the veil IS Surface, it composites back to Surface bit-for-bit over the opaque base both surfaces already paint, so a game with no art gets the flat treatment exactly, with no tone step and no layout shift. The back face reuses the front's own CoverPresenter bitmaps, DisplayAlpha and 140ms ramp; the modal reuses the single 200px full-saturation bitmap it already requests, so there is no second image path. 0.92 was walked rather than assumed: at 0.91 Winnow's TextDim measures 4.49:1 over a white cover, and at 0.92 the worst text ink is 4.62 / 5.69 / 4.93 / 4.76 across Winnow, Nightshift, Tungsten and Box art. Proved by four new ThemeContrastTests walking every text ink over 256 grey art values - exhaustive, because composite luminance is monotone in each art channel - at every transparency position and with the hovered row's veil, plus a ThemeAudit warning that extends the same check to user themes. design-system.md section 5.5 states the rule; all prose was authored by docs-writer. Verified: dotnet build clean (0 warnings, 0 errors) and dotnet test 3144 passed, the single failure being another agent's concurrent migration work.
<!-- SECTION:FINAL_SUMMARY:END -->
