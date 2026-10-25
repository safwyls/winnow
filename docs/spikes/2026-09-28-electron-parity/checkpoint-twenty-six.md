# Update contracts, staging and presentation — 2026-09-29

The Electron updater now owns cancellation of release checks as well as downloads.
Shutdown waits for that owned work before stopping the backend transport. Local or remote
channel changes discard pending installer files, including a staged prerelease when returning
to stable. Failed installation handoff also clears staging and restores backend availability.
The driver rechecks the staged file's size and SHA-512 immediately before explicit installation;
changed metadata or bytes cannot reuse earlier verification.

The installed electron-updater transport now validates every HTTPS request and redirect
against the release host allowlist. Unsupported native installation paths use bounded GitHub
release discovery: ten pages, a shared timeout and aggregate byte limit, strict draft and
prerelease handling, semantic version comparison, and exact platform/architecture assets.
Manual downloads open the canonical release page instead of an asset-supplied address.

Desktop and fullscreen restore the source update caption and fixed progress slot. Fullscreen
uses 24px scaled text, keeps the caption on one line at enlarged text, and reserves navigation
space. The controller quick action closes its menu before execution. Settings retains release
notes and browser download actions, sequential directional focus and polite recovery notices.
An available update never installs itself; restart remains an explicit action.

## Verification

- All 108 focused updater cases pass across five files in 1.85 seconds:
  `.tmp/update-contracts-final.log`.
- All 2,810 component and live API cases pass across 138 files without skips in 43.74 seconds:
  `.tmp/updater-full-integration.log`. The preceding nonintegration run exposed six standalone
  menu/theme fixtures without an updater bridge. Optional bridge guards fixed that composition;
  those original assertions remain unchanged.
- Build and typecheck pass: `.tmp/updater-final-integration-build.log`.
- The complete native suite passes all 204 cases in one run in 10.1 minutes:
  `.tmp/updater-native-full.log` and `.tmp/updater-native-full/results.json`. This includes
  the preceding Epic workflows and feed-readiness fixture correction.
- Three native full-application workflows pass in 25.2 seconds:
  `.tmp/update-native-final.log`, with the JSON report copied to
  `.tmp/update-native-final/results.json`. Desktop and fullscreen at 100%/140% text exercise
  caption geometry, controller invocation, progress, cancellation, download links, focus after
  staged readiness, explicit restart and failed-handoff recovery. Final captures were inspected.
- Real byte fixtures exercise the installed NSIS and AppImage download implementations,
  checksum verification, temporary-file cleanup, cancellation/retry, metadata cancellation,
  official and refused redirects, both channel-change paths, changed staging and failed handoff.
  Only the transport's request creation is remapped to an isolated HTTP fixture. Installer
  execution is an isolated callback; these are not platform installation smoke tests or a
  Chromium network-stack test.
- Initial native attempts caught a missing `.js` extension in an externalized ESM import and
  the quick menu remaining open during its update action. Both production issues were fixed.
  Later fixture corrections await actual rendered preferences and respect the controller's
  existing repeat interval. Focus assertions follow the source contract's available action
  rather than requiring a specific button. Visual review then found caption wrapping at 140%
  text; a reserved slot and single-line style fix it, with a native assertion.

Seventeen partial source methods now have complete ported evidence. The inventory contains
1,023 ported, 552 retained backend, 15 framework-specific, 735 pending and 110 partial methods.
Actual NSIS/AppImage installation, OS restart recovery, live release hosting and physical
controller hardware remain unverified. The migration and its acceptance criteria remain open.
