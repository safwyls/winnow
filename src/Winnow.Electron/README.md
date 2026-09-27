# Winnow Afterglow

An independent Electron/TypeScript frontend for Winnow's local backend. Afterglow uses
panoramic artwork and editorial typography; Catalogue presents the same library as a
reading desk and index. Both use the public theme interface available to installed themes.

Navigation and the footer stay visible as you browse. Library results and the list index
scroll independently; other screens scroll within the content pane. Afterglow's featured
recommendations rotate every nine seconds while the hero is visible and idle. Previous,
Next, and Pause controls are available on desktop and fullscreen. Selecting a recommendation
pauses rotation until Resume; hovering, keyboard focus, and leaving the window pause it
temporarily. Reduced motion disables automatic rotation.

## Run from source

Install a current Node.js release compatible with Vite 7 (Node 22.12 or later) and the .NET 10 SDK.
From this directory:

```powershell
npm ci
npm run dev -- --data-dir C:\Temp\winnow-electron-demo --seed-sample
```

Development requires an explicit data directory. It attaches to a responsive backend there,
or starts `Winnow.Backend` from the adjacent source project with automatic synchronization
disabled. Sample seeding works only with a Debug backend and an empty library. Sample games
do not include artwork or recorded sessions; missing data has visible empty states.

To view the production bundle without packaging:

```powershell
npm run build
npm run preview -- --data-dir C:\Temp\winnow-electron-demo --no-sync
```

Set `WINNOW_BACKEND_PATH` to an absolute backend executable or DLL to use another build.
Startup switches configure a newly started backend, not one already running. Closing the
frontend leaves the backend running, as described in the [API guide](../../docs/frontend-api.md).

## What is available

- Discover with real recommendation reasons, visible-card impressions, snooze, dismiss and undo.
- Searchable, virtualized Library with store/bucket/list filters, grid and list views, fixed
  and live list creation, manual games, hidden-game restoration and identity candidate review.
- Game details with store actions, update history, sessions, journal entries, ratings,
  achievement summaries, editable metadata and provider/URL artwork selection.
- Journal activity and recorded-play statistics. Approximate store-reported time remains
  separate from recorded sessions.
- Settings for Steam API credentials, IGDB credentials, Epic and provider connections,
  library preferences, background operations and recommendation feedback history.
- Theme Studio with three palettes, semantic color overrides, fonts, spacing, scale,
  motion, navigation placement, card styles, section ordering and JSON profile sharing.
- Developer themes that replace the shell and individual screens, with versioned contracts,
  declared settings, contained package assets and recovery to the bundled appearance.

Desktop and fullscreen have separate destinations and library filters. Editing drafts and
their original revisions survive navigation and mode changes for this frontend session;
they are not persisted across application restarts. F11 changes mode, Ctrl+K searches,
Escape returns from details, and Tab moves through controls. Fullscreen has larger targets
and a recommendation filmstrip. Basic Gamepad API navigation supports D-pad/stick, A and B;
physical controller and TV-distance validation remain outstanding.

This first frontend does not duplicate every Avalonia feature. It has no embedded Steam
browser sign-in, spending dashboard, setup wizard, local artwork-file upload, existing live
list filter editor, advanced identity/expansion editor, IGDB match assignment, provider package
manager, or application updater. Provider connections and game actions depend on backend
capabilities. A launch result means a launcher handoff, not confirmed gameplay or download
progress. Manual entries do not accept arbitrary launch commands.

## Themes

See [Electron themes](../../docs/electron-themes.md) for profile sharing, installation and
the complete authoring contract. Install `examples/themes/reading-room` from Theme Studio
to try a third composition loaded as an independent package. It needs no build tools.

Developer packages execute trusted JavaScript with library command access. Appearance
profiles are passive JSON. Use Ctrl+Shift+T to restore Afterglow, the native View menu's
recovery command (Ctrl+Shift+R), or `--safe-theme` at startup if an installed theme fails.

An explicit `--data-dir` also places Chromium state, profiles and installed themes beneath
`<data-dir>/electron-userdata`. Normal packaged launches use Electron's per-user application
directory, with profiles scoped by backend data directory. Appearance does not change the
Avalonia frontend's preferences.

## Build and verify

```powershell
npm run typecheck
npm test
npm run package
```

`package` builds the renderer and preload, publishes a self-contained backend for the current
OS/architecture, then produces an unpacked application. On Windows, run:

```powershell
& '.\release\win-unpacked\Winnow Afterglow.exe' --data-dir C:\Temp\winnow-electron-demo --no-sync
```

`npm run dist` additionally creates the configured distributable (Windows portable executable).
`npm run format` formats TypeScript, CSS, tests and the example package. Packaging collects
the production dependencies' license notices, including the bundled fonts, under `resources/`.
Windows builds are unsigned. Windows x64 is the locally verified target; macOS/Linux
configuration is provisional and has not been packaged or device-tested. This frontend is
not part of the repository's existing Avalonia release or update pipeline.

Opt-in integration tests make temporary library changes and clean up their entries:

```powershell
$env:WINNOW_TEST_DATA_DIR = 'C:\Temp\winnow-electron-demo'
$env:WINNOW_ELECTRON_TEST_DATA_DIR = $env:WINNOW_TEST_DATA_DIR
npm test
```

Use only a throwaway directory named `winnow-electron-*` for these tests. They require a
running backend. Journal integration needs an existing recorded session; the metadata
operation test skips when credentials would enable external work. Unit/component tests
exercise concurrency conflicts, uncertain creation, draft retention, theme compatibility,
loading recovery, event races and transport boundaries without external accounts.

## Structure and boundaries

`src/main` owns discovery, credentials, HTTP/SSE, authenticated image reads, dialogs and
package storage. `src/preload` exposes named operations. The sandboxed renderer has no Node
integration or bearer token. It uses React, TanStack Query/Virtual, Radix dialogs, Motion,
Lucide and bundled fonts. It never opens SQLite or duplicates backend scoring rules.

`src/shared/theme.ts` is the theme contract; `src/renderer/theming` hosts packages and Studio;
`src/renderer/features` holds reusable backend feature screens. Theme packages can reuse
those screens through the public context. See the [implementation evidence](../../docs/spikes/2026-09-26-electron-implementation/README.md)
for the validation scope and native screenshots.
