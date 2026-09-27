# Electron viewport and artwork polish — 26 September 2026

Evidence for TASK-351, following the first Afterglow implementation. Current behavior is
described in the [frontend README](../../../src/Winnow.Electron/README.md) and
[theme guide](../../electron-themes.md).

## Method

Ran the native Electron frontend with `--data-dir C:\Temp\winnow-electron-20260926
--no-sync --force-renderer-accessibility`. This isolated library contains 40 sample games
and previously imported test artwork. Three temporary manual games with different title
lengths exercised card alignment; these were removed after inspection. The user's real
library was not used for interaction.

Inspected Windows desktop at 1440×980, the minimum 760×560 window, interface size 100% and
130%, and ultrawide fullscreen at 3440×1440. Captures omit the invisible native window border.

## Results

- Afterglow's header and footer stay visible during content scrolling. Library results
  scroll independently of the filters and list index; no document scrollbar remains.
- Short, wrapped and truncated titles share artwork, title and playtime alignment. Full
  titles remain available in the accessible button name and hover tooltip.
- Details images fill a consistent frame without top padding. Loading, decoded artwork
  and missing artwork share the frame. Component tests verify image readiness and failures.
- Journal statistics and activity return successfully on desktop and fullscreen. The
  sample library has no recorded sessions, so the screen shows valid zero/empty results.
- Discover advanced from Dyson Sphere Program to Satisfactory and Vintage Story while
  idle. Manual selection returned to the first game and exposed Resume. Timer tests cover
  hover/focus, foreground visibility, intersection, feedback gating and reduced motion.
- Afterglow and Catalogue remain navigable at minimum size and 130% scale. Short viewports
  use compact controls and thumbnail cards. Catalogue's fullscreen Library also retains
  its fixed rail and footer with a scrolling results pane.
- Native selects have a centered indicator in a reserved trailing area, including Studio.

## Automated checks

`npm run typecheck`, `npm run build`, and the frontend suite with both opt-in live API test
environment variables pointing to the throwaway library passed: **118 passed, 2 skipped**
across 16 files. The existing skips require a recorded journal session, or would initiate
metadata work with configured credentials. The new live activity/statistics check passed.
Focused final review checks also passed without React duplicate-key warnings.

`electron-builder --dir` rebuilt the unpacked Windows app successfully. Launched that
executable against the same isolated library and verified desktop startup, visible header
and footer, automatic recommendation changes, manual controls and decoded hero artwork.

The build retains the existing harmless Rollup warnings about two upstream Zod comment
annotations. No .NET source changed. These checks do not establish Linux/macOS or physical
controller coverage.

## Captures

- [Library title alignment](01-library-title-alignment.png)
- [Desktop Journal](02-journal-desktop.png)
- [Fullscreen Discover carousel](03-discover-fullscreen.png)
- [Fullscreen details hero](04-details-fullscreen.png)
- [Minimum window at 130%](05-minimum-window-130-percent.png)
- [Catalogue minimum window](06-catalogue-minimum-window.png)
- [Catalogue fullscreen Library](07-catalogue-fullscreen.png)
- [Fullscreen Journal](08-journal-fullscreen.png)
- [Rebuilt packaged desktop](09-packaged-desktop.png)
