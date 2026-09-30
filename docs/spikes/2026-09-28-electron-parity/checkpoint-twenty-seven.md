# Startup presentation and readiness — 2026-09-29

Desktop and fullscreen now use the original Ground cover, dragon mark, WINNOW wordmark
and preparation copy. Primary library, feed, workspace, presentation and setup snapshots
remain covered until they publish and pass two layout barriers. Visible startup waits for
the first actual Chromium paint before starting primary library reads. Warm entry refreshes
under the cover and retains the selected page; exit and reentry share unfinished reads.

The normal-motion mark uses an OffscreenCanvas worker for a full 1.8-second circuit and
continues through the 180ms fade. Reduced motion skips the circuit wait and fade. Saved
fullscreen motion preferences must arrive before tracing begins. Failed preparation keeps
the native window enabled and offers Try again, with Back to desktop in fullscreen. Library
shortcuts and controller page/menu/play actions cannot escape the cover; Back, Space and
Retry remain available. F11 and Alt-F4 pass through to the window.

Readiness observes the currently published query state and version, rather than trusting
a refetch promise that invalidation can retire. This prevents a cancelled warm read from
revealing its reverted older cache entry. Native window visibility is carried through a
named preload signal: Chromium can report a visible page for a hidden native window, as
described in the [Electron BrowserWindow documentation](https://www.electronjs.org/docs/latest/api/browser-window).
Cold background preparation therefore finishes without invisible animation frames, and
restoration during a read starts the visible animation.

## Verification

- All 83 focused startup/application component cases pass across three files in 40.26s:
  `.tmp/startup-components-complete.log`. Three additional input-boundary cases pass in the
  subsequent full run.
- All 2,850 component and live API cases pass across 141 files without skips in 48.86s:
  `.tmp/startup-full-integration-final.log`.
- Build and typecheck pass: `.tmp/startup-final-build.log`.
- Four native workflows pass in 46.0s: `.tmp/startup-native-complete.log` and its copied
  `results.json`. They cover cold hidden startup, restoration during held data, desktop
  and fullscreen failure/retry, normal/reduced motion, 140% fullscreen text, controller
  Back/Retry, Space, blocked menu/page/play actions, shared inflight reads and retained pages.
  A 600ms blocked renderer advances more than fifteen worker frames. This measures independent
  drawing; it does not measure monitor scanout during the blockage.
- Desktop and enlarged fullscreen failure captures were inspected. Both cover the client
  viewport; the enlarged fullscreen actions remain reachable inside the scrollable surface.
- The expanded complete native run is still pending at this checkpoint's preparation.

Initial native tests exposed early reveal after cancelled reads, paint ordering and the
native/Chromium visibility distinction. Those production paths now have regression tests.
Fixture corrections also await a completed connection refresh before arming a held warm
read and retain the real app resource root for the tray icon. The setup assertion checks
the ancestor that the existing modal implementation hides, rather than assuming that a
particular child wrapper owns it.

Seventeen original startup methods now have complete ported evidence. Inventory:
1,040 ported, 552 retained backend, 15 framework-specific, 718 pending and 110 partial.
The four LoadingDragon geometry/color/lifecycle source methods remain pending; independent
renderer work for that next package is not included in the checks above. Physical controller
hardware and other desktop platforms remain unverified. Migration acceptance criteria remain open.
