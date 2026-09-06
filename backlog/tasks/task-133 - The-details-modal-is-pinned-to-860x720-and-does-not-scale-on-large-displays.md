---
id: TASK-133
title: The details modal is pinned to 860x720 and does not scale on large displays
status: Done
assignee:
  - '@claude'
created_date: '2026-09-06 05:48'
updated_date: '2026-09-06 06:29'
labels:
  - ui
dependencies: []
priority: high
type: bug
ordinal: 160000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reported by the user: "when working on a TV or large monitor it is taking a comically small section of the screen and the screenshot hero is not even fully visible".

GameDetailsView.axaml line 324 sets MinWidth 700, MaxWidth 860, MaxHeight 720 — absolute pixels with no relationship to the window or the display. On a 4K screen the card occupies roughly a fifth of the width and a third of the height. The screenshot hero is separately capped at MaxHeight 200 (line 1316), so on any card it is small, and against a 720px card there is no room to grow.

The fix is not simply raising the numbers. Three constraints interact:

1. READING MEASURE. TASK-82 is an open task to set a reading-measure rule for prose surfaces, and it becomes load-bearing here: ABOUT and the other prose runs must not stretch to 2000px because the card can. The card may grow while the prose column stays readable — those are different limits and the design should say so.

2. THE MEASURED LAYOUT DECISIONS ASSUME THE CURRENT RANGE. The reception line is a WrapPanel because three figures sum to 564px against a 420px minimum right column (docs/spikes/details-modal-additions-width.md); the action strip was measured at 347-361px against the same range (docs/spikes/details-action-band-width.md). Growing the card does not invalidate those — everything gets easier — but the minimum must not move, and anything newly sized should be measured the same way rather than estimated.

3. THE HERO IS THE POINT OF THE COMPLAINT. A screenshot the user cannot see fully is the specific failure. Whatever scaling rule is chosen, the hero should get materially more room on a large display, and its own cap should be expressed in terms of the space available rather than a fixed 200px.

Decide whether the card scales against the window or the screen, what the new ceiling is and why, and whether there is one at all above some size. Record the rule in design-system.md section 10.1 so the next addition knows what it may assume.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The card scales up on a large display rather than staying at 860x720
- [x] #2 The screenshot hero is fully visible and materially larger on a large display
- [x] #3 Prose runs keep a readable measure as the card grows, rather than stretching with it
- [x] #4 The 700px minimum and the measurements that depend on it still hold
- [x] #5 Any new size is measured in the headless harness rather than estimated, matching how the strip and reception line were established
- [x] #6 design-system.md section 10.1 records the scaling rule and what a future addition may assume
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Measure in a headless Avalonia 11.3.20 harness (Skia text shaping, real Window, fonts from the repo), validated first by reproducing three shipped figures: the reception figures 184/210/170, their WrapPanel rows 34/34/17, and the right column 420/580.
2. Scale the card against the WINDOW, not the display: MaxWidth = clamp(0.50 x window width, 860, 1582); MaxHeight = max(0.667 x window height, 720). Floors are today's caps, so nothing shrinks at any window size the app allows; MinWidth 700 and Margin 40 are untouched. The 1582 ceiling is the measured card width at which the hero is drawn at the screenshot's native 1280x720 and every further pixel is upscale.
3. Express the rule as a typed converter (Winnow.App/Converters/ScaledLength.cs: Fraction/Least/Most) declared as three named resources in the view, bound to $parent[Window].Bounds. Window bounds never depend on the card, so there is no layout feedback.
4. Hero: Stretch Uniform instead of UniformToFill (the crop is why it was never fully visible), box left-aligned so there are no pillarbox bars, MaxHeight = max(0.30 x window height, 200) instead of a fixed 200.
5. Prose measure: a measured token rather than a second in-file number. 66 characters of Body 13/20 measures 410px; the same 410 is 72 characters at 12/18, so one token sits inside the 45-75 band at both prose sizes. Add ProseMeasure and a .prose class to tokens.axaml and put it on the ABOUT runs.
6. design-system.md: §10.1 records the scaling rule and what a future addition may assume; §3 states the prose measure; §5.5's 'a card up to 860px wide' is corrected. Every superseded sentence quoted from git diff into docs/decisions.md.
7. New spike docs/spikes/details-modal-scale.md carrying the harness method, the validation figures and every new measurement.
8. Tests: ScaledLength unit tests pinning the card and hero sizes at seven window sizes from 1200x640 to 3840x2160, plus enforcement tests on the markup.
9. All prose delegated to docs-writer. Build and test with BaseOutputPath=C:\Temp\winnow-scale\.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Measured, not estimated. A headless Avalonia 11.3.20 harness (Skia shaping, real Window, the repository's own font files under an assembly named Winnow, card geometry copied verbatim from the view) was validated first by reproducing three shipped figures exactly: reception figures 184x17 / 210x17 / 170x17, their WrapPanel at two rows (34) at 420 and 500 and one row (17) at 580, and the right column at 420 against MinWidth 700 and 580 against the old MaxWidth 860. One correction for the next person: those figures reproduce only with the count strings the view model builds (1,234 ratings, not / 1,234 ratings) - the oblique in the design system's sketch is not in the string and costs 12px, two characters of Plex Mono at 10px.

The rule. The card is capped against the WINDOW, never the display: the window is what the user sized, a display-relative card overflows a small window on a big screen, and a Window's own Bounds cannot feed back into layout the way the modal's own host could. MaxWidth = clamp(half the window width, 860, 1582); MaxHeight = max(two-thirds of the window height, 720), no ceiling; hero MaxHeight = max(three-tenths of the window height, 200). Both card floors are what the card had before, so no window size the app allows (its own minimum is 1200x640) produces anything smaller than shipped. MinWidth 700 and Margin 40 untouched, so the 420px right column every existing measurement was taken against is unchanged.

1582 is measured rather than chosen: it is the card width at which the hero is drawn at the native 1280x720 IGDB's t_screenshot_huge delivers (1580 gives 1278x718.88, 1584 gives 1282x721.13). Past it every pixel is upscale, and nothing else in the card rewards width - the object column is a fixed 200 and prose is bounded by the reading measure. There is deliberately no height ceiling: the bands are bounded scroll regions, so more height is more content on screen.

Produced caps and arranged sizes: 860x720 up to a 1720-wide window; 960x721 at 1920x1080 (right column 680); 1280x961 at 2560x1440 (1000); 1582x961 at 3440x1440 and 1582x1441 at 3840x2160 (1302). The reception line is one row at every one of those columns and still two at 420, so its WrapPanel and its recorded sentence still hold. Verified separately that MinWidth wins over a smaller MaxWidth (Border MinWidth 700 + MaxWidth 500 arranges at 700), which is what makes flooring the computed cap safe.

The hero was the complaint. Two faults: UniformToFill cropped a 16:9 frame, and the box was a fixed 200px. Now Stretch=Uniform, box left-aligned so it takes the shot's own width rather than the column's, cap three-tenths of the window height (200px was 0.29 of the modal's host height at a 720-tall window, so the fraction reproduces what shipped at the smallest window and grows). Drawn shot: 352x198 at 1200x640, 434x244 at 1280x820, 572x322 at 1920x1080, 764x430 at 2560x1440, 1148x646 at 3840x2160 - against a 198px-tall crop at every size before. Honest cost: the box is narrower than the full-width crop at every size because a whole 16:9 frame in a height-capped box is narrower than a strip of it; area is about equal at the default window, larger from 1600x900 up, 2.9x at 4K, and it is the whole picture at every size.

Expressed as three ScaledLength resources (Fraction/Least/Most) bound to $parent[Window].Bounds, so each cap is one named object rather than a number in a layout attribute. Verified live in the harness with the real converter class: resizing a headless window updates all three caps and re-arranges the card, with no layout cycle.

Reading measure. Measured in the shipped face: 66 characters is 410px at Body 13 and 379px at 12; at a 410 cap a paragraph runs up to 66 characters at 13/20 and up to 72 at 12/18, both inside the 45-75 band, so ONE token serves both prose sizes. Added ProseMeasure=410 and a .prose class (measure, Left alignment because a maximum under Stretch centres the paragraph off the column's left edge, Wrap) and put it on ABOUT's summary and empty-body runs - the runs that would otherwise stretch to 1302px (measured: 215 characters on a line there, 66 at the measure).

Verification. dotnet build Winnow.slnx -p:BaseOutputPath=C:\Temp\winnow-scale\ -m:1: Build succeeded, 0 Warning(s), 0 Error(s). Winnow.Tests 3436 passed (baseline 3403 + 33 new: 26 converter theories/facts and 4 markup enforcement tests, plus theory cases), Winnow.Recommend.Tests 152 passed, Winnow.Covers.Tests 82 passed, 0 failed anywhere.

Each criterion's evidence: (1) and (2) are arranged sizes off real Avalonia measure-and-arrange passes in the validated harness, including one pass driving the SHIPPED ScaledLength class through $parent[Window].Bounds bindings on a headless window resized between passes - the caps update, the card re-arranges, no layout cycle. (3) measured: 215 characters on a line at the 1302px column against 66 at the measure. (4) MinWidth 700 and the 420px right column reproduced exactly, and the reception line still takes two rows there. (5) the harness reproduced three shipped figures before any new number was taken from it. (6) read off the design-system.md diff.

What still needs a running app, and nobody should tick these off a headless pass: whether a 1582x1441 card on a 4K display still READS as a modal over the library rather than as a screen; the art-backed field behind the card, which is the same 200px cover bitmap now upscaled to as much as 1582 rather than 860 (the veil is heavy and §5.5 says the upscale is what softens it, but 7.9x is a judgement); and whether the hero, the thumbnail strip and the caption sit comfortably together in the rest band at 1080p - the hero alone fits the band at every window size, but hero plus strip plus caption is within about 20px of the band's height at 1920x1080, so it may want a small scroll.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The details modal now scales against the window rather than sitting at a fixed 860x720. Card MaxWidth is half the window's width floored at 860 and capped at 1582; MaxHeight is two-thirds of the window's height floored at 720 with no ceiling; the screenshot hero draws the whole frame (Stretch=Uniform, box left-aligned) capped at three-tenths of the window's height floored at 200. Both card floors are the old fixed caps, so nothing shrinks at any window size the app allows, and MinWidth 700 with its 420px right column - the width every measurement in §10.1 and §10.3 was taken against - is untouched. 1582 is measured rather than chosen: it is the card width at which the hero is drawn at the native 1280x720 IGDB delivers, past which every pixel is upscale. The three caps ship as named ScaledLength resources (Fraction/Least/Most) bound to $parent[Window].Bounds. A measured reading measure, ProseMeasure=410 with a .prose class, keeps ABOUT's prose at 66 characters a line instead of the 215 a 1302px column would give it.

Measured in a headless Avalonia 11.3.20 harness validated by reproducing three shipped figures exactly (reception figures 184/210/170, their wrap rows 34/34/17, right column 420/580); every new number is in docs/spikes/details-modal-scale.md. On a 4K window the card goes from 860x720 to 1582x1441 and the hero from a 1280x198 crop to a complete 1148x646 shot.

Verified: build succeeded with 0 warnings and 0 errors; Winnow.Tests 3436 passed, Winnow.Recommend.Tests 152, Winnow.Covers.Tests 82, none failed. The app was not run - the notes name the three judgements that still want it.
<!-- SECTION:FINAL_SUMMARY:END -->
