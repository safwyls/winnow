# Populated desktop Details and scroll restoration

Desktop Details now preserves its expanded relationships when switching sections. Scroll
cleanup no longer replaces a section's saved position with the next section's shorter,
clamped position. This shared correction also applies to fullscreen reading regions.

Long desktop titles and publishers wrap again. The title uses the source's 26px type and
30px line height; the previous single-line ellipsis hid most of the original long fixture.
Fullscreen retains its separate title treatment.

## Verification

- Build and typecheck pass: `.tmp/details-desktop-build-final.log`.
- All 3,322 component/live API cases pass across 162 files without skips in 60.34 seconds:
  `.tmp/details-desktop-integration-final.log`.
- All 43 native Details/controller cases pass without skips or retries in 3.0 minutes:
  `.tmp/details-desktop-native-final.log` and `.tmp/details-desktop-native-final/results.json`.
  This batch verifies the shared scroll and disclosure changes on both surfaces.
- After restoring desktop title/publisher wrapping, all 18 affected native modal/layout
  cases pass without skips or retries in 1.1 minutes: `.tmp/details-desktop-wrapping-final.log`
  and `.tmp/details-desktop-wrapping-final/results.json`.
- The new native fixture preserves the original title/publisher, three installed stores,
  740 minutes per copy, twelve update events, six lists, three notes, three expansions,
  three reception sources and eight screenshots. Both 1200×640 and 1280×820 windows traverse
  all five sections, reach their last visible content, preserve the header and restore exact
  scroll offsets. The long-header case checks every action and both action glyphs inside the
  header, with at least 80px of reading height.
- Initial runs reproduced collapsed relationships and overwritten scroll positions. Test-only
  corrections wait for reception hydration before measuring the header and use the Updates
  tab's full accessible name, including its twelve unread updates.
- The final wrapping assertions check the full title's measured bounds, normal wrapping and
  source typography. Screenshots were inspected for header containment and readable content.

Two original methods move from partial to ported. The inventory is 1,257 ported, 625 retained
backend, 23 framework-specific, 434 pending and 96 partial out of 2,435. Complete migration,
the full native aggregate and the primary Electron release cutover remain open.
