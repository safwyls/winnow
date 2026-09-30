# Fullscreen lifecycle and navigation — 2026-09-30

TASK-381.3 preserves the two presentation paths through startup, native fullscreen,
controller navigation and return to desktop. Preparation now exposes an accessible
fullscreen entry control before a library context exists. A mode transition releases
controller cursor ownership; subsequent mouse movement restores the hovered control's
cursor. Existing native window-state restoration and independent navigation state are
exercised through the production main/preload boundary.

Library, Activity and Settings show non-focusable LT/RT glyphs beside their local sections.
Labels reserve their bold width before selection. Activity reserves its scrollbar gutter,
and Plugins keeps its form padding inside the content so the Settings strip stays still.
The strip scrolls within its available width while both trigger hints remain visible.
The section wrapper is also reflected in Controller-guide and plugin-installation layouts;
compact Controller-guide row gaps preserve all ten mappings at 140% text and 10% margins.
Settings entry-focus selectors follow the named section navigation through the wrapper.
Details shows its unread count beside the pip without moving neighboring tabs. Selected
sections use the neutral underline; focused actions use the accent. Visible fullscreen
fallback titles now honor text scale without compounding repeated preference updates;
long names keep their beginning and truncate after three lines.

The glyphs reuse the original Kenney CC0 assets and the existing bundled license. Desktop
retains its section layouts, Details labels and typography. Its controller input, native
window restoration, search/sort state and shared Settings/plugin paths are checked separately.

## Ownership and source equivalence

Electron owns presentation subscriptions and per-surface view state, while borrowing raw
API snapshots from one QueryClient. Recreating Avalonia DI model instances would duplicate
those snapshots. The three source ownership combinations instead verify that detaching the
presentation releases its query/event subscriptions, preserves borrowed observers and cache,
and permits later shared-cache updates. Desktop/fullscreen journal sections and periods,
Library filters, manual/live list selections and selected collections remain independent.
Shared appearance and dormancy survive mode changes; fullscreen motion and sizing remain
scoped to fullscreen.

Native fixtures replay the original 1200×688 minimum window; normal and maximized native
transitions; repeated Menu from root and nested controls; delayed feed refresh on desktop
return; 1920×1080 page rendering and 1280×720 large text; 2560×1440 tab geometry at text
scales .7, 1 and 1.4; 1920×1080 section hints at 1 and 1.4; and 2560×1080/3440×1440
ultrawide fitting. All nine original palettes run at 1280×720 and 3840×2160 with 1.4 text
and 2% margins, including controller page cycling, loaded shelf navigation and backdrop colors.

The geometry tests exercise real production section labels, including Metadata & artwork,
instead of instantiating the source's synthetic Avalonia strip. Responsive CSS replaces the
original Viewbox transform; typography comparisons normalize the 1280-wide viewport to the
source's 1920-wide logical canvas. Per-method explanations are recorded in
`src/Winnow.Electron/tests/migration-fullscreen-lifecycle.json`.

## Verification

- Build and typecheck pass: `.tmp/fullscreen-lifecycle-build-final.log`.
- All **3,358 component/live API cases in 164 files** pass without skips in **56.66 seconds**:
  `.tmp/fullscreen-lifecycle-integration-final.log`. Seven new cases cover cursor ownership,
  the three borrowing combinations, shared/scoped presentation preferences and independent
  journal/list state. The shared backend executable is the existing Debug build at
  `.tmp/metadata-parity-artifacts/bin/Winnow.Backend/debug/Winnow.Backend.exe`.
- **87 distinct native cases pass after repairs**, using disposable data directories,
  seeded libraries and `--no-sync`: 21 lifecycle, 10 controller, 15 Details, 8 fullscreen
  Settings, 5 shared Settings, 4 metadata child-page, 10 plugin Settings, 10 plugin
  installation and 4 startup cases. The last batch passes all **33 cases in 349.46 seconds**
  with no skips or retries (`.tmp/fullscreen-lifecycle-native-final.log`).
- Earlier batches exposed Settings entry-focus selectors, compact Controller-guide spacing
  and plugin form padding; those failures now pass. This is combined evidence across
  affected suites, not one full native-aggregate run. Machine-readable reports and screenshots
  are retained in `.tmp/fullscreen-lifecycle-native-{initial,regressions,repairs,final}/`;
  `.tmp/fullscreen-lifecycle-native-summary.json` joins each case's latest executed result
  and has no unresolved cases.
- Inspected desktop Details and plugin layouts, fullscreen Library/Settings section hints,
  the 1280×720 Controller guide at maximum text/margins, and the small Library's long-title
  truncation. All changed Electron files pass Prettier; `git diff --check` passes.

## Remaining validation

All 17 assigned source methods now have complete replacement evidence. The inventory has
**1,284 ported, 625 retained backend, 30 framework-specific, 403 pending and 93 partial**
methods. `npm run migration:report` succeeds; the complete migration gate still fails for
the **496 unresolved methods** (`.tmp/fullscreen-lifecycle-migration-gate.log`).

Gamepad input and artwork are deterministic fixtures delivered through the production
controller and artwork paths. Native window transitions use actual Electron fullscreen;
layout matrices set client dimensions and send the same presentation event. These checks
do not establish physical-controller behavior, display scaling on other machines or
TV-distance legibility. Physical-device validation remains TASK-381.40.

This checkpoint does not rerun the entire native aggregate or .NET suite. The final
migration/release gates and primary Electron release cutover remain queued. Work stops at
this review boundary; TASK-381.4 is the next task only after a user continuation prompt.
