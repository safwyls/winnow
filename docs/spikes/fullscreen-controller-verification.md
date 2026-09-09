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

The follow-up polish uses bundled Kenney CC0 controller glyphs, transparent underline
controls, a dragon wordmark, themed original SVG backdrops and a persisted ultrawide-fit
preference. Browse covers use 5:6 frames with uniform cropping and compact spacing.

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

## Remaining device checks

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
