# Shared desktop sort menu and merge copy

Library and Merges now use the same compact desktop sort button and menu. The button names
the selected order. Radio rows retain the original 30px minimum height, 6px selected dot,
4px inset and rounded corners. Choosing a row closes the menu and restores focus. Arrow
keys, Home/End and typed initials navigate; Escape closes one layer and Tab moves onward.
Fullscreen keeps its separate Library filter panel and merge sort sheet.

The menu inherits the invoking theme and stays within the window at 80%, 100% and 140%
interface scale. A top-layer popover avoids clipped or transformed containers while remaining
inside an owning dialog's focus boundary. Scroll dismissal compares the current anchor
position with its opening position: an event queued before the click cannot close a newly
opened menu. Static merge markup reads its text and accessible copy through bindings to
the copy module; an AST guard enforces the original markup rule.

## Verification

- Build and TypeScript check pass: `.tmp/sort-menu-final-build.log`.
- All 3,190 component/live API cases pass in 158 files without skips, in 51.16s:
  `.tmp/sort-menu-integration.log`.
- All 17 final native merge and menu cases pass in 51.6s, including the real-backend
  navigation sequence that exposed premature scroll dismissal. Evidence:
  `.tmp/sort-menu-native-final-merges.log` and `.tmp/sort-menu-native-verified/results.json`.
- The preceding native run passed all 20 Library, browse-spine and fullscreen workflow
  cases plus 16 of 17 merge/menu cases. Its one failure led to the scroll fix above:
  `.tmp/sort-menu-native-final.log`, `.tmp/sort-menu-native-36/results.json`.
- All 22 Avalon layout cases passed in the earlier 59-case affected run. That run had
  five obsolete input-only button assertions and the scroll failure; 53 cases passed.
  The assertions now check the button value, and their affected workflows pass above.
  The full native suite has not been rerun as one combined final pass.
- Scale probes measure window bounds, row and dot sizes, selected state, focus and
  Chromium's one-device-pixel border. The enlarged lower-corner capture was inspected.
  The first probes incorrectly compared scaled border CSS units with an unscaled pixel;
  converting to device units fixed the measurement without changing the rendering.
- The first focused component group passed 279 cases and exposed 15 helpers still using
  the replaced desktop select. Both affected Library files subsequently pass all 126 cases.
  A headless Popover visibility limitation is handled by the component's non-Popover
  fallback; native tests use Chromium's actual top layer.

The two remaining `MergesSurfaceTests` methods have complete evidence. The inventory now
records 1,180 ported, 625 retained backend, 20 framework-specific, 509 pending and 101 partial
methods, out of 2,435. Complete migration remains open. No .NET source changed; the full
Release result from checkpoint 39 remains applicable. Cover-presenter contracts are next.
