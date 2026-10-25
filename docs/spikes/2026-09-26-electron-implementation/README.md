# Electron frontend implementation evidence

Validated locally on Windows on 2026-09-26 for TASK-350. This records the implementation
following the approved [Afterglow design study](../2026-09-26-electron-design/README.md).
Current behavior and authoring instructions live in the [frontend README](../../../src/Winnow.Electron/README.md)
and [Electron theme guide](../../electron-themes.md).

## Environment and boundaries

- Windows x64, Node 24.19.0, Electron 44.4.5, .NET 10 backend.
- Interactive runs used `C:\Temp\winnow-electron-20260926`, seeded by the Debug backend,
  with automatic synchronization disabled. The 40-game sample has no recorded sessions
  and no store launch IDs. No real game launches or account sign-ins were performed.
- Selected Steam artwork was imported through the authenticated artwork URL endpoint
  into that temporary library. Images are demonstration data, not bundled game assets.
- A separate `C:\Temp\winnow-electron-packaged-smoke` directory verified automatic startup
  of the self-contained backend shipped under the packaged executable's `resources/backend`.
  Health responded and the empty library appeared. That temporary backend was then shut down.
- The existing Avalonia implementation and the user's library were not changed.

## Automated and build results

`npm run typecheck` passed. With both opt-in test-directory variables set, `npm test`
reported **96 passed, 2 skipped across 13 test files**. Skips were the live journal check
(no recorded sessions) and the metadata operation check (configured IGDB credentials would
enable external work). Journal conflicts, draft retention and in-flight remounts have
component regression coverage independent of that live skip.

Coverage includes discovery/route restrictions, bearer isolation at the transport boundary,
SSE replay/resync, changes arriving during in-flight reads, real backend DTOs, list/manual
create-edit-delete and stale revisions, Epic challenge creation/cancellation, pending and
uncertain mutation handling, theme package validation, independent module import, profile
validation, stale theme loads, stylesheet cleanup, reduced-motion tokens and error recovery.

`npm run package` passed, including TypeScript/Vite production builds, self-contained backend
publish, collection of 44 dependency license notices and electron-builder Windows x64 packaging.
The executable reports `Winnow Afterglow` version `0.1.0` and includes the product icon.
Production dependency audit reported zero vulnerabilities. Build output contains upstream
Zod annotation warnings; they remove comments rather than application code. The new frontend
CI workflow parses as YAML and runs unit/component tests and production compilation; no remote
CI execution or release publication was performed in this task.

## Native application checks

The source development command and the packaged executable were opened and inspected using
Windows UI Automation and screenshots. Checks covered Afterglow discovery and library,
Catalogue's different shell and ledger, the Paper trail palette, game details, Theme Studio,
keyboard theme recovery, native fullscreen changes and a desktop window approximately 760px
wide. The narrow library uses two columns and a horizontally scrollable filter strip.
Navigation now preserves the header instead of scrolling it away when focusing main content.

The Reading room example was selected through the native folder picker and trust prompt,
then loaded in the packaged app through `winnow-theme:`. Its separate shell and screen
components rendered using the shared SDK and authenticated library data.

| Screenshot | Evidence |
| --- | --- |
| [Afterglow](01-afterglow.jpg) | Packaged app, live recommendation and authenticated hero art |
| [Theme Studio](02-theme-studio.jpg) | Bundled and installed compositions, appearance controls |
| [Catalogue](03-catalogue.jpg) | Separate navigation/ledger composition and light palette |
| [Game details](04-details.jpg) | Shared feature screen within Catalogue |
| [Narrow library](05-narrow-library.jpg) | Responsive filters, search and two-column grid |
| [Fresh packaged library](06-packaged-empty-library.jpg) | Companion startup and empty state |
| [Reading room](07-reading-room.jpg) | Independently installed package rendering live data |
| [Fullscreen](08-fullscreen.jpg) | Packaged Afterglow panorama and recommendation filmstrip |

Screenshots are evidence for this build, not fixed layout specifications. Early Catalogue
captures predate the navigation-focus adjustment; final packaged checks include that fix.

## Remaining validation

Physical controllers, TV seating distance, macOS/Linux packaging, actual game-launch
handoffs, provider authentication completion and a live recorded-session journal roundtrip
remain unverified. The full .NET test suite was not rerun because no backend source changed;
the companion publish and live API tests exercised the existing backend. The first Electron
frontend's explicit feature omissions are listed in its README; it is not full Avalonia parity.
