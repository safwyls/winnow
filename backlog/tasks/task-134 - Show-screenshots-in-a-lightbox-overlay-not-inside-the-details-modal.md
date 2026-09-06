---
id: TASK-134
title: 'Show screenshots in a lightbox overlay, not inside the details modal'
status: Done
assignee:
  - '@claude'
created_date: '2026-09-06 15:58'
updated_date: '2026-09-06 16:43'
labels:
  - ui
dependencies:
  - TASK-133
priority: high
type: enhancement
ordinal: 161000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User request after seeing the scaled modal: "rather than have them appear inside the details modal i think we should do a popover image that dims the background and has a close button and left/right navigation. bringing the focus onto the image and not smashing it in between the other controls in the details modal".

TASK-133 made the in-modal hero draw whole rather than cropped, and up to 1148x646 at 4K. That fixed the cropping but not the framing: a screenshot competing for room with the reception line, the axis, the action band and six sections is still a screenshot in a crowded column.

IMPORTANT, and it settles the obvious objection: this is an OVERLAY, not a popup. design-system.md §10.7 forbids flyouts in this modal because a popup is its own root with no adorner layer, so FocusAdorner does not draw and every focus ring would need hand-drawing. That rule does not bite here — the details modal itself is not a popup either. MainWindow.axaml line 1998 hosts GameDetailsView as a child spanning all columns of the window own Grid, which is why its focus rings work. The lightbox is the same pattern one layer up: an overlay in the window visual tree, above the modal. Do not implement it as a Popup or Flyout.

What it needs: the dimmed ground, a close control, left/right navigation across the game shots, and keyboard equivalents — Escape closes, arrows navigate. Focus must move into the lightbox on open and return to the originating thumbnail on close, and must not escape to the modal underneath while it is up. It is a dialog and should say so to a screen reader, with the shot position spoken — note PositionInSet is inert in Avalonia 11.3.20 per TASK-129, so a count must be spelled into the name string.

The thumbnail strip stays in ABOUT as the entry point. The in-modal hero goes away, and with it the three-tenths-of-window-height cap TASK-133 measured for it — that cap was sizing a thing that no longer exists. The lightbox can use nearly the whole window, so the 1582px ceiling reasoning does not apply to it either: that number was the card width at which the hero hit the native 1280x720 of t_screenshot_huge, and a full-window lightbox passes it easily.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Screenshots open in an overlay that dims what is behind it and gives the image the whole frame
- [x] #2 It has a close control, left/right navigation, and Escape plus arrow-key equivalents
- [ ] #3 Focus moves into the overlay on open, cannot reach the modal beneath while it is up, and returns to the originating thumbnail on close
- [x] #4 It is an overlay in the window visual tree, not a Popup or Flyout, so focus rings draw without hand-drawing
- [ ] #5 A screen reader identifies it as a dialog and can hear which shot of how many is showing
- [x] #6 The in-modal hero and its window-fraction cap are removed rather than left as dead sizing
- [x] #7 design-system.md records the overlay and why it does not breach §10.7
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Overlay, not popup. New ScreenshotLightboxView (UserControl) declared in MainWindow.axaml AFTER DetailsPanel, Grid.Column=0 ColumnSpan=3 — a sibling of GameDetailsView in the window's own Grid, one layer up. No Popup, no Flyout, so FocusAdorner is irrelevant and the drawn-ring rule (§10.7) works exactly as it does in the modal.
2. State. New ScreenshotLightboxViewModel: Shots, Index, Image, IsOpen, Open/Close/Previous/Next commands, wrap-around navigation, dialog+position automation name. One long-lived instance owned by LibraryViewModel, passed through GameDetailsViewModel into GameScreenshotsViewModel so the thumbnail's command opens it and CloseDetails/AfterHiding closes it.
3. Focus. Overlay container carries KeyboardNavigation.TabNavigation=Cycle (the same trap the Card already uses), so Tab cannot reach the modal beneath. On show the view focuses its close control; on hide it raises Closed and MainWindow returns focus to the thumbnail that opened it (GameDetailsView remembers the origin Button from the shot's Click). Escape/Left/Right answered by a new layer in MainWindow.OnKeyDown placed ABOVE the IsDetailsOpen layer and returning unconditionally, so one Escape closes the lightbox and leaves the modal up.
4. Sizing. Image box capped at 1280x720 DIP — the native size of IGDB t_screenshot_huge — with Stretch=Uniform so it shrinks to fit smaller windows and never upscales past native. Chrome (close row, arrow columns, caption) measured in a headless harness validated against a shipped figure, to state the window size at which native is reached.
5. Decode. CoverImaging.WidthBuckets tops out at 640, so the hero was already a 640 decode upscaled. Add a 1280 bucket so the lightbox draws the shot at native rather than at a 2x upscale; update the snap test.
6. Removal. Delete the in-modal hero Border/Image, the HeroHeightCap resource, GameScreenshotsViewModel.Hero/HasHero/HeroWidth/HeroAutomationName, GameScreenshotsCopy.HeroAutomationName, and every test that pinned them (DetailsModalScaleTests hero theory + the HeroHeight assertion, DetailsModalStructureTests hero fact + the HeroHeightCap line).
7. The 1582 card ceiling. Keep the number; its derivation (the width at which the hero hit native) dies with the hero, so §10.1's stated reason is rewritten to the one that survives — nothing in the card rewards more width.
8. Prose. design-system.md §10.1/§10.7, docs/decisions.md superseded sentences quoted from git diff, all copy strings, comments and XML docs delegated to docs-writer.
9. Verify: dotnet build -p:BaseOutputPath=C:\Temp\winnow-lightbox\ -m:1, then dotnet test per project --no-build.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented. Overlay layering: ScreenshotLightboxView is declared in MainWindow.axaml immediately after GameDetailsView, Grid.Column=0 ColumnSpan=3 — a sibling in the window's own Grid, so it is in the window's visual tree and draws over the modal. No Popup, no Flyout, no MenuFlyout, no ContextFlyout anywhere in it (pinned by ScreenshotLightboxStructureTests.The_overlay_is_not_a_popup).

Focus: KeyboardNavigation.TabNavigation=Cycle on the overlay panel, the same trap the modal's card already carries. On show the view focuses its close button with NavigationMethod.Tab so the drawn ring is visible; on hide it raises Closed, and MainWindow returns focus to the thumbnail that opened it via GameDetailsView.RestoreLightboxFocus, which also brings it back into the horizontally scrolled strip. The origin thumbnail is remembered on the press, not read back from the view model, because the overlay's selection mark moves as the user navigates. Escape/Left/Right are answered by a new layer in MainWindow.OnKeyDown placed above the IsDetailsOpen layer and returning unconditionally, so one Escape closes the lightbox and leaves the modal up.

Sizing: the frame is capped at 1282x722 — 1280x720 plus the 1px border each side. 1280x720 is the native size of IGDB t_screenshot_huge, so past it every pixel is upscale. Stretch=Uniform, so a smaller window shrinks the whole frame rather than cropping. Measured in a headless Skia harness validated first against a shipped figure (the reception figure IGDB USERS 82 / 1,234 ratings came out at exactly 184x17 DesiredSize). Native is reached from an overlay of 1424x828, i.e. a window of about 1424x864 after the 36px title bar. Drawn shot by window: 1200x640 -> 882x496; 1280x820 -> 1136x639; 1440x900 and every larger window -> 1280x720. Against the hero it replaces (352x198 at 1200x640, 434x244 at 1280x820, 1148x646 at 3840x2160) that is 6.3x, 6.9x and 1.24x the area.

Decode: CoverImaging.WidthBuckets topped out at 640, so the hero was already a 640-wide decode upscaled to 1148 at 4K. Added a 1280 bucket, the native width of t_screenshot_huge and the only asset drawn wider than a cover, so the lightbox draws at native rather than at a 2x upscale. Flagged as a deliberate addition beyond the acceptance criteria.

Removed: the in-modal hero Border/Image, the HeroHeightCap ScaledLength, GameScreenshotsViewModel.Hero/HasHero/HeroWidth, GameScreenshotsCopy.HeroAutomationName, DetailsModalScaleTests' hero theory and its floor assertion, and DetailsModalStructureTests' hero fact. The_scaling_resources_carry_the_measured_numbers now asserts HeroHeightCap is absent.

1582 card ceiling: kept as the number, but its derivation is gone with the hero. What survives is the reason for having a ceiling at all — nothing in the card rewards more width, because the object column is a fixed 200 and prose is bounded by the reading measure.

Verification. dotnet build Winnow.slnx -p:BaseOutputPath=C:\Temp\winnow-lightbox\ -m:1 succeeded with 0 warnings and 0 errors (TreatWarningsAsErrors is on). dotnet test --no-build per project: Winnow.Tests 3444 passed, Winnow.Recommend.Tests 152 passed, Winnow.Covers.Tests 84 passed, 0 failed and 0 skipped throughout. Baseline was 3436 / 152 / 82.

Checked criteria and the evidence for each. #1: ScreenshotLightboxTests.It_opens_on_the_shot_that_was_pressed proves a thumbnail press opens the overlay; the drawn shot was measured at eight window sizes in a headless Skia harness validated first against a shipped figure (docs/spikes/screenshot-lightbox-scale.md). The dimmed ground is markup — ModalScrim on a full-bleed Border, the same token and construction the shipping modal scrim uses — and is verified by inspection rather than by a test. #2: ScreenshotLightboxStructureTests.It_carries_a_close_control_and_navigation_with_keyboard_equivalents pins all three controls with names and tooltips and the Escape/Left/Right cases in MainWindow.OnKeyDown, including that the layer precedes the modal's; the command behaviour is covered by the wrap and close tests. #4: The_overlay_is_not_a_popup and It_is_declared_in_the_windows_grid_after_the_modal. #6: The_strip_stays_and_the_hero_is_gone, The_scaling_resources_carry_the_measured_numbers asserting HeroHeightCap is absent, and a clean build. #7: design-system.md §10.7 now carries the overlay-not-popup reasoning, §10.1 is corrected, docs/spikes/screenshot-lightbox-scale.md is new and docs/decisions.md logs the superseded text verbatim.

Left unchecked, because both need a running app. #3: TabNavigation=Cycle on the overlay panel is pinned by Focus_is_trapped_inside_the_overlay, but the focus actually moving to the close control on show and returning to the originating thumbnail on close is view code that only runs with a rendering platform, and no test project here hosts one. #5: the markup that makes the announcement is pinned (ControlTypeOverride=Window, AccessibilityView=Control, a bound Name carrying both the role and the count, and a Polite bound-Text caption with no Name of its own), and The_name_spells_out_both_the_role_and_the_count holds the string, but whether a screen reader actually speaks it needs NVDA or Narrator against the running window.

Scope note. Adding the 1280 entry to CoverImaging.WidthBuckets is beyond the stated criteria and was deliberate: without it the widest decode is 640 and the lightbox would draw a two-times upscale at its own cap, which would have made the design system's word 'native' false. It costs one array element and one test case. The consequence worth watching is memory: a 1280x720 vivid/floor pair is about 7.4 MB against the cache's 128 MB budget, so arrowing through a long strip can evict grid covers, which re-decode from the disk cache on return.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Screenshots now open in a full-window lightbox overlay rather than expanding to a hero inside the details modal. The overlay is a new ScreenshotLightboxView declared in MainWindow.axaml as a sibling of GameDetailsView, spanning all three columns of the window's own Grid and after it so it draws over it — an overlay in the window's visual tree, never a Popup or a Flyout, which is why design-system.md §10.7's ban on flyouts does not bite: the modal is not a popup either, and that is exactly why its focus rings work. The frame is capped at 1282x722, being the native 1280x720 of IGDB's t_screenshot_huge plus a 1px border each side, with Stretch=Uniform so a smaller window shrinks the whole frame rather than cropping it; native is reached from a window of about 1424x864 up, and at the app's own minimum window the shot is 6.3 times the area of the hero it replaces. A 1280 entry was added to CoverImaging.WidthBuckets so the shot is actually decoded at native — the old hero was already a 640-wide decode upscaled. The in-modal hero, the HeroHeightCap resource, GameScreenshotsViewModel.Hero/HasHero/HeroWidth and the tests that pinned them are gone; the 1582 card ceiling keeps its number but §10.1 now says it is retained rather than derived, since nothing else in the card rewards more width. design-system.md §10.1 and §10.7, docs/spikes/screenshot-lightbox-scale.md and docs/decisions.md were written by the docs-writer agent, with every superseded sentence quoted verbatim from git diff. Verified by dotnet build (0 warnings, 0 errors, TreatWarningsAsErrors on) and dotnet test per project: 3444 / 152 / 84 passing, 0 failed. Criteria 3 and 5 are left unchecked because the focus movement and the screen-reader announcement need a running app.
<!-- SECTION:FINAL_SUMMARY:END -->
