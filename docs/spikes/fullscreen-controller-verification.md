# Fullscreen controller verification

The fullscreen implementation was checked on Windows on 2026-09-09. This records the
verification method and its limits; `design-system.md` owns the interaction contracts.

The initial implementation Release run passed 4,273 tests, including 177 UI tests. Two Linux-native tests
skipped on Windows. The solution Release build completed with zero warnings and errors.

## Automated checks

Run from the repository root:

```powershell
dotnet test --artifacts-path C:/Temp/winnow-tv-release -c Release -m:1
dotnet build --artifacts-path C:/Temp/winnow-tv-release -c Release --no-restore -m:1
```

The UI suite uses Avalonia headless with real fonts and templates. Repository tests use
temporary SQLite databases; the production host and the user's library are not started.

| Area | Evidence |
|---|---|
| Separate surfaces | Runtime DI isolation of library, lists, journal and dormancy; desktop cuts and theme remain independent; window state restores after exit |
| Focus and navigation | Main-screen focus, explicit grid neighbors, paging, retained game/cell after detach, local detail sections, nested quick menu, keyboard isolation and controller disconnect |
| Library management | Staged filters, list ordering/removal, live-list save/revert, feedback history/undo and visible-shelf impressions |
| Game details | Opening without launch, version selection, selected-copy launch attribution, achievements, tracked history, expansion confirmation, editor retention during data refresh and long titles |
| Activity and settings | Real note persistence, live activity invalidation, hidden-game removal, selected-session restoration, shared visibility settings, independent appearance and confirmation before reset |
| Files and platform tools | File extension filtering, cancellation, overwrite confirmation, saved-page selection, CSV encoding/content, masked local API-key draft and clearing it on close |
| Browser adapter | Controller key mapping and the authentication input lock rejecting input before browser startup; no live provider login performed |
| Art | Bitmap leases requested and released on detach, dormancy updates, repository screenshot selection for the hero and scaled decode requests |

The full suite also exercises existing desktop behavior, launch validation, data access,
recommendations, ingest and cover-cache behavior. Linux native process tests explicitly skip
on Windows; a Windows result does not establish Linux device behavior.

## Render inspection

Capture only the fullscreen interaction tests; unrelated test capture hooks have their own
frame-timing assumptions:

```powershell
$env:WINNOW_UI_CAPTURE_DIR = 'C:/Temp/winnow-tv-renders'
dotnet test tests/Winnow.Ui.Tests --artifacts-path C:/Temp/winnow-tv-release -c Release -m:1 --filter FullyQualifiedName~FullscreenInteractionTests
Remove-Item Env:WINNOW_UI_CAPTURE_DIR
```

The capture helper forces an Avalonia render tick before saving the frame. Inspect For you,
Library, Activity, Settings, details and text entry at 1920×1080, plus smaller 1280×720 frames
and the largest body-text setting. The fixtures contain fabricated game records and mostly
missing artwork; they establish layout and fallback behavior. Independent lease tests cover
the real bitmap path. Generated design mockups are not runtime screenshots.

Inspection caught an early capture before the focus frame, crowded placeholder titles and
retained inactive tab underlines. The implementation keeps chrome within the safe area while
body text grows; headings and the header/footer retain their reference sizes.

## Visual refinement checks

Follow-up Release verification passed 4,284 tests: 4,096 non-UI tests and 188 UI tests.
Two Linux-native cases skipped on Windows. The final solution build had zero warnings and
errors. The complete UI suite passed after correcting the render-timing check described below.

The first visual polish introduced bundled Kenney CC0 controller glyphs, transparent
underline controls, a dragon wordmark, themed original SVG backdrops and a persisted
ultrawide-fit preference. That version used 5:6 cropped browse frames; the subsequent art
revision below verifies the complete-cover replacement.

Added interaction checks cover directional grid page edges with retained columns, selected
library backdrops, filter application from nested choices, cancellation, persistent filter
actions at 140% text/1280×720, directional week changes, distinct empty states, switch values,
controller help, ultrawide persistence/layout, and restoring a text field's I-beam cursor
when the mouse resumes. Cursor behavior is checked on the shared window; the visual controls
and appearance preference belong only to fullscreen. Existing desktop tests remain part of
the UI suite.

Rendered home, library, activity, settings, controller help and filter views were inspected,
including maximum text size and a wide canvas. The initial render pass exposed and corrected
browse text encoding and excess cover spacing. Reference and large-text captures use fake
records and fallback art; selected-background lease tests establish the real art path.

An intermittent feed-impression test initially checked the hit-test scene before the next
100ms observation cycle. The test now commits layout and waits for that existing timer.
Two consecutive full UI runs passed after this correction. A subsequent scaled-canvas case
also caught a coordinate mismatch: both tile corners now transform into window coordinates
when checking visibility. The final 188-test UI run covers both reference and 720p scaling;
production visibility and obscured-shelf checks remain intact.

## Cover and details art revision

The next refinement passed all 194 UI tests and a solution Release build with zero warnings
and errors. The UI suite includes desktop regression coverage. Non-UI suites were not rerun
for these presentation-only changes.

Compare `docs/mockups/fullscreen-v2/game-details.png` with the rendered detail page. The
previous implementation put a 320px-high image beside the title and retained global navigation.
The revised page supplies landscape art behind the whole canvas and uses a back header. It
has a 96px title, larger primary action, a stronger history lead, an overview divider and
larger screenshot previews. Unread updates have a count and marker beside their local tab.
Tests cover background preference over repository screenshots, independent art lease cleanup,
header/back navigation, fallback rendering and long titles at 140% text/720p.

The artwork-present capture uses an original geometric landscape bitmap fixture; it proves
rendering and composition, not the quality of a publisher's artwork. Reference, wide and
large-text captures also check whole cover images, adaptive column counts, selected-game
identity through reflow, partial pages and directional navigation. A synthetic 60-game page
fills both rows so fixture scarcity cannot hide spacing defects. Desktop cover composition
and application action semantics remain unchanged.

## Focus, local navigation and bounded overview revision

The 2026-09-09 follow-up passed 199 UI tests, 112 cover-pipeline tests and 3,828 core/app
tests in Release. The solution Release build passed with no warnings or errors. Commands
used `--artifacts-path C:/Temp/winnow-tv-final -c Release -m:1` and the three corresponding
test project paths. The first integrated UI run exposed two stale test expectations for
the replaced controller geometry and backdrop cache key; the corrected full run passed.

New checks move focus away from a hovered Screen margins control and verify its underline
clears, exercise trigger section changes and desktop filter isolation, sample rendered
cover padding pixels, and verify cover lease cleanup. Existing desktop tests remain part
of the UI and core/app runs; desktop layouts, screenshot downloads and action semantics
were not changed. Fullscreen and desktop continue as separate maintained presentation paths.

Inspected captures from the headless renderer:

- `C:/Temp/winnow-navigation-polish-renders/fullscreen-controller-large-text.png`: proportional
  vector controller, section focus and readable callouts at 720p/140%.
- `C:/Temp/winnow-details-controller-captures/fullscreen-details-long-title-140.png`: long title
  and synopsis with both screenshot buttons fully above the footer. Bounds assertions check
  their visibility and focusability; About game opens the complete description.

Backdrop tests verify a 4K window requests the 3840px decode bucket and releases leases on
detach. The IGDB source test verifies the separate high-resolution URL and cache identity.
The chosen rendition follows [IGDB's documented image sizes](https://api-docs.igdb.com/#images).
Synthetic artwork proves layout and request behavior, not live publisher source quality.

## Centered navigation and keyboard layout follow-up

Header tests vary clock and controller text and assert the view list stays at the canvas
center. Hovered actions remain unmarked after focus moves; selected sections retain a
neutral underline distinct from mint focus. Details tests traverse the overview horizontally
with zero, one and two screenshots, then return through the section tabs to the primary action.

The controller guide has no ScrollViewer. Rendered text bounds fit at 1920×1080 and
1280×720, including 140% text and 10% safe margins. The inspected capture is
`C:/Temp/winnow-guide-fit-captures/fullscreen-controller-large-text-safe-area.png`.

The shared keyboard has five weighted rows and an inverted-T arrow cluster. Tests exercise
X backspace, RT Enter, masking, binding and length constraints, grapheme deletion and
multiline caret movement on desktop and fullscreen. Inspected keyboard captures are
`C:/Temp/winnow-keyboard-layout-captures/keyboard-tv-720p.png` and `keyboard-desktop.png`
in that directory; the desktop fixture uses the minimum 1200×640 window.
The other composition changes are fullscreen-only; desktop layout and shared commands
retain their behavior.

The full Release UI suite passed 211 tests in 34 seconds using
`dotnet test tests/Winnow.Ui.Tests --artifacts-path C:/Temp/winnow-guide-fit -c Release -m:1
--verbosity quiet --blame-hang-timeout 30s`. An initial full-suite run stalled while the
guide fixture drained unrelated Home render jobs. The hang dump located the active UI
thread in the render timer under that initial dispatcher drain. The fixture now mounts
the controller guide before draining jobs; all visibility assertions remain in place.
Focused keyboard, desktop navigation and fullscreen interaction checks also passed together
(30 tests). No core/domain code changed in this follow-up.
The final solution Release build passed with zero warnings and errors.

### Remaining device checks

No physical controller or TV seating-distance test was performed in this environment. Check
the controller's device mapping, repeated navigation, reconnect, actual battery reporting,
4K rendering and return from a launched game on the target machine. The WebView2 bridge needs
live-provider validation for field focus, credential entry and cancellation. Provider CAPTCHA,
phone approval, external API-key registration and external launcher windows retain their own
input requirements; passing the adapter tests does not establish controller-only operation
through those systems.

Run any interactive app check with `-- --data-dir <throwaway-directory>` so clicks cannot
write to the real library. Keep TASK-4's broad controller-only acceptance criteria open until
the corresponding device checks are evidenced.
