---
id: TASK-4
title: Build full-screen gamepad mode
status: In Progress
assignee:
  - codex
created_date: '2026-08-29 21:52'
updated_date: '2026-09-09 22:49'
labels:
  - ui
  - accessibility
milestone: m-3
dependencies:
  - TASK-3
priority: low
ordinal: 60000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the M10 deliverable: a 10-foot UI navigable entirely by gamepad. This is a second complete UI surface with its own focus management, controller input, navigation model, and layouts. Deliberately last because it serves the narrowest user segment. Source: ROADMAP.md section 4, M10 row.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every screen and interaction is reachable via gamepad
- [ ] #2 Focus is always visible
- [ ] #3 No mouse or keyboard is required for any operation
- [x] #4 The full-screen surface shows a clock
- [x] #5 Connected controller battery level is shown when the platform reports it, and the absence of that reading is not treated as an error
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Implementation authorized after mock review. 1. Build an independent fullscreen host, presentation context, explicit controller focus/navigation, and shared TV components. 2. Implement For you and paged Library with real art, search, filters, lists and game actions. 3. Implement full-page game details, updates, journal, screenshots and management flows. 4. Implement Activity and TV-native Settings/library tools backed by shared application behavior. 5. Integrate fullscreen entry/exit, input mappings, text entry, clock/battery and lifecycle without disturbing desktop state. 6. Validate real rendered screens against mocks, controller-only headless flows, desktop regressions and Release build; update governing docs and record any native/hardware limitations honestly.

Visual refinement requested after implementation: enlarge and tighten TV game grids; add controller glyphs and underline actions; cursor input-mode handling, brand icon and ultrawide fit; selected-library and generic activity/settings backdrops; filter shortcuts and groups; grid/week edge navigation; distinct empty states, controller diagram and switches. Verify fullscreen rendering and interaction tests plus desktop regressions. Desktop presentation stays unchanged; shared application behavior remains consistent.

Second visual refinement: preserve complete portrait cover art and use adaptive columns/page capacity instead of5:6 cropping, preserving selected game and directional paging on resize. Compare details mock to runtime and restore edge-to-edge cinematic landscape, simplified back header, larger title/action hierarchy and separated overview columns. Verify artwork-present and fallback renders, resize/navigation at reference/720p/ultrawide, and desktop regressions. Keep shared operations intact.

Controller polish: make underline exclusive to focus (remove latent hover/current underline); LT/RT select local sections/collections/shelves instead of page navigation, retaining directional grid edges. Replace malformed controller illustration with proportion-correct vector. Preserve whole covers and blend letterbox areas using artwork-derived color. Audit full details information hierarchy for fixed visible overview/screenshots and high-resolution landscape selection/decode. Keep desktop semantics and cache behavior compatible, document both surfaces, verify input/rendering/art lifecycle with focused and full UI checks.

Follow-up: center root navigation independently of status/clock widths; fit controller guide at16:9 including large text; restore selected section/collection underline without hover underline; use horizontal detail overview focus neighbors; revise shared keyboard to standard key arrangement with X backspace and RT Enter. Verify both keyboard surfaces, focused navigation/layout checks and full UI suite; update specs and decisions.

Audit fullscreen crash evidence and fix confirmed failure; correct Steam/Epic status lifecycle and all platform action focus directions; synchronize theme across surfaces; allow70-140%fullscreen text with mouse decrement/increment; replace Activity art with intentional vector composition; move desktopfullscreen entry to icon beside settings. Verify regression tests for each surface, finalbuild/fullUI; document confirmed crashcause or unresolvedevidence separately.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented fullscreen shell scaling, clock, optional XInput battery, Windows XInput and Linux joydev polling, D-pad/left-stick spatial and existing grid navigation, shoulder focus cycling, right-stick scrolling, and on-screen text entry with masked password preview. Shared shell commands and dialogs are reused. All 4224 tests passed across the five suites; two Linux-only process tests skipped on Windows. Full UI suite: 133 passed. Headless rendering verified minimum-size fullscreen keyboard and visible exit. Review found and fixed ordinary flyout D-pad navigation, covered by a regression test. Physical controller and complete controller-only operation remain unverified; native file dialogs and embedded sign-in retain their own input requirements. Keep TASK-4 In Progress because acceptance criteria 1-3 are broader than the verified shell behavior.

Release solution build passed with zero warnings and zero errors. Changes are on codex/gamepad-fullscreen.

User rejected the scaled desktop approach as bolted on and explicitly requested a fresh gamepad-first fullscreen surface. Current turn is design and mockups only. Desktop and fullscreen will be maintained as distinct presentation paths with shared domain/application behavior; implementation waits for design review.

First design review proposes For you, Library, Activity and Settings as fullscreen sections; a focused recommendation shelf drives the home artwork/reason, and game details becomes a separate full page. The visual spec records candidate TV typography and explicit controller navigation. AGENTS.md now requires assessing both presentation paths for user-facing changes; game-library-design.md records shared application behavior and separate UI ownership. Existing acceptance checks are reset because they verified the superseded adapter, not the proposed fullscreen surface. No application or prototype code changed during this design turn.

User responded positively to the initial visual direction and requested the other views. This authorizes continuing mock design, not implementing the new fullscreen UI.

Extended the design review to Library, Activity and Settings. The visual spec now records each composition plus a supporting-view inventory covering search, filter/sort, lists, context actions, updates, journal editing, library tools, account summary, platform sign-in, quick menu and recovery states. Mock data and generated imagery are illustrative; controller flow and distance validation remain pending. No application or prototype code changed.

Saved the five-screen concept set under docs/mockups/fullscreen-v2 with generation prompts and a review index. Visually checked all three new screens; corrected Library sort copy and journal icon color. Mockups remain design proposals; application code is unchanged.

User has explicitly authorized full implementation of the reviewed separate UI. Design-review implementation pause is lifted; retain dual presentation paths and shared action semantics.

Implemented the reviewed standalone TV presentation: independent Home/Library/Activity/Settings/details, explicit focus rows and retained navigation, scoped appearance, file/text entry, lists and feedback history, journal/history/achievements, metadata/identity/manual tools, platform configuration/import/export, and controller input for embedded browser windows. Removed the retired desktop scaling/footer adapter. Shared launch attribution now uses the playable store copy on both surfaces. Full Release verification: 4273 tests passed (177 UI); 2 Linux-native tests skipped on Windows. Solution Release build: zero warnings/errors. Main-screen render inspection includes 1920x1080, 1280x720 and maximum body text; art lease/lifecycle tests cover bitmap behavior. Verification method and limitations: docs/spikes/fullscreen-controller-verification.md. AC 1-3 remain open for physical-controller, actual seating distance and live native/provider validation; CAPTCHA, phone approval, external key registration and launcher windows retain their own input requirements. No claim of universal controller-only external operation.

Completed requested visual polish: larger 5:6 uniformly cropped browse covers with compact gaps; selected-library art; Kenney CC0 controller glyphs and dragon branding; transparent underline controls; activity/settings SVG backdrops; TV ultrawide-fit preference; fixed filter actions with Y apply/close and B cancel; directional grid/week paging; distinct journal empty state; controller diagram and visual switches. Shared window cursor hides on controller input including child I-beam overrides and restores on mouse use/exit; desktop presentation otherwise remains independent. Release checks:4096 non-UI tests and188 UI tests passed (4284 total),2 Linux-only skips; final solution build0warnings/errors. Render checks cover main views, controller help, filters at140% text/1280 and wide layout. Corrected impression hit coordinates under scaling and synchronized test with the existing observation timer after an intermittent failure; two full stability runs plus final scaled-case suite passed. Evidence in docs/spikes/fullscreen-controller-verification.md. Broad hardware/provider acceptance remains open.

Replaced cropped5:6 TV covers with complete art in2:3 frames; library/search column counts and page capacity adapt to width,row height,text size while preserving selected release and directional paging. Home adapts actual recommendation cards only. Compared details mock/runtime: removed boxed320px artwork and persistent global navigation; added fullcanvas highresolution landscape with text/header veils, B origin header,96px title,stronger primary action,overview divider,screenshots,and unreadtab count. Shared application actions and desktop layout unchanged. All194 UI tests passed including desktop regressions; solution Release build0warnings/errors. Inspected synthetic artwork-present and fallback layouts,720p/140% text andwide grids. Tests cover artwork priority/lease lifecycle,origin navigation,uncropped images and reflow identity. No physical-device/provider claim added; existing broad acceptance remains open. Evidence:docs/spikes/fullscreen-controller-verification.md.

2026-09-09 controller polish implemented: only focused actions underline; current sections use bold. LT/RT switches local tabs/shelves (Library four preset collections), directional grid paging and Activity week navigation retained. Proportional Kenney-derived controller diagram replaces malformed drawing. Uncropped cover gaps sample edge colors with dormant/vivid layers. Details overview has visible screenshot previews and bounded synopsis; About game opens full description/reception. Separate high-resolution IGDB backdrop cache uses documented 1080p_2x with decode buckets through3840 and display-resize lease upgrades. Desktop presentation and screenshot rendition unchanged; shared cache behavior covered. Verification: Release solution build zero warnings/errors;199 UI,112 covers,3828 core/app tests pass. Inspected720p/140% controller and long-title details captures. Physical controller,TV distance and live-provider checks remain outstanding; broad acceptance criteria remain open. Specs,README,art attribution and verification report updated.

Follow-up completed: root navigation stays centered independently of clock/controller widths; selected sections and collections retain neutral underlines while hover stays unmarked and focus is mint. Controller guide fits16:9 at720p/140%text/10%margins without a ScrollViewer. Details overview controls navigate horizontally to/from screenshots. Shared keyboard now has standard weighted QWERTY rows, wideSpace, inverted-T arrows, X backspace and RT Enter with preserved multiline/single-line semantics. Desktop minimum1200x640 and fullscreen720p keyboard captures inspected. Both keyboard surfaces covered; other composition changes are fullscreen-only, shared commands unchanged. FullUI211passes; combinedkeyboard/desktopnavigation/fullscreeninteraction30passes; finalRelease solutionbuild zero warnings/errors. Initialfullsuite stall traced to guidefixture draining unrelatedHome render jobs; fixture mountsguidebefore drain, retains allboundsassertions, fullsuite passes. Docs/spec/README/decisions updated. Physicalcontroller/TVdistance/liveprovider checks remain open; TASK-4 staysInProgress.

Hang investigation: WindowsApplication events1001/1002 at2026-09-09 15:36:43 recordedAppHangB1/closure, not a managedexception; no newdump. User clarified changingtheme thenreturningHome. Read-onlysettings inspection showed140%text,2%margins,fitoff. Reproduced a runawayHome rebuild with longtitle/reason at both720p and4K: unscalednewcontrols produceonecovercapacity, late textscalingproducesanother, queuedrebuildrepeats. Regressionfails at80PageChangedevents within4s beforefix. Applyingtextscale on PageChanged beforefirstlayout stabilizescapacity (verificationinprogress).

Completed this revision. Activity uses an original open-journal vector. Desktop fullscreen entry is an accessible icon beside Settings. Steam/Epic platform pages and summaries refresh on entry and bind to the shared account state; vertical consent, API-key and import actions now navigate vertically. Sign-out no longer pops two pages. Both surfaces share ThemeService and appearance.theme; fullscreen ignores its old theme override and reset preserves the shared theme. Text size supports 70–140% with mouse minus/plus controls and controller adjustment. Final hang fix coalesces cover-capacity calculation after text scaling and layout settle; the earlier synchronous scaling attempt was insufficient and was removed. Long Home fixtures failed before the fix and pass afterward at 720p and 4K, including the reported theme-change/return sequence. The Windows report identifies a hang, with no new exception dump; this matching reproduced mechanism is fixed without claiming it was the only possible cause. Validation: 221 Release UI tests pass in 36 seconds; final solution Release build has zero warnings/errors. Desktop and fullscreen behavior, platform navigation, mouse controls and shared state are covered. No live account sign-in or physical controller check was performed. Specs, README, decisions and verification evidence updated; broad TASK-4 hardware/provider criteria remain open.
<!-- SECTION:NOTES:END -->
