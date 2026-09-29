# Winnow Electron

An independent Electron/TypeScript frontend for Winnow's local backend. **Avalon** is the
default composition, carrying the original Winnow palette, bundled typography, portrait
covers and dormancy treatment into Electron. Desktop uses a side rail and cover wall;
fullscreen uses a hero and a directional recommendation shelf. Theme Studio also offers
**Afterglow**, with a panoramic featured game and editorial typography, **Rift**, with
floating covers and artwork portals, and **Catalogue**, with a reading desk and index.
All compositions use the public theme interface available to installed themes.

Avalon's desktop Details opens over the retained library with five sections and a fixed
header. Fullscreen Details uses the original cinematic layout, two screenshot previews and
separate About and Play history reading pages. More contains the shared metadata, matching,
artwork and browsing actions. Closing restores the originating game and library position.
Metadata opens in its own desktop dialog, bounded to 1440 × 1000 with a fixed Back button.
Fullscreen uses an ordered field menu: Back cancels the active field's draft and returns to
its row; successful saves return with updated attribution. A edits, Y opens the keyboard,
and B returns. Desktop retains unsaved drafts across dialog navigation.

Settings groups Steam, Epic and GOG under **Platforms**, with one card at a time, attention
markers and title counts from the whole library. Steam keeps credential guidance, account
scope, purchase import and sign-in consent in separate dialogs. IGDB and artwork source
preferences live under **Metadata & artwork**. Manual games in **Manage library** report
validation and identifier conflicts beside their fields and ask before removing a named entry.

Avalon reads existing authored Winnow JSON palettes from the library's `themes` folder,
including their fonts and opening preferences. Theme Studio provides reload, diagnostics,
folder access and safe export; saved file edits update the active palette automatically.
Palette files use the original data-only format and are separate from executable developer
themes. See [Electron themes](../../docs/electron-themes.md) for the validation limits.

The approved [Rift design study](../../docs/spikes/2026-09-28-rift-design/README.md)
records the composition that informed the production theme. Production screens use the
real backend and cached artwork; the mock's sample games are not shipped with the app.
The existing executable and application ID retain their Afterglow names for compatibility.

Navigation and the footer stay visible as you browse. Library results and the list index
scroll independently; other screens scroll within the content pane. Afterglow's featured
recommendations rotate every nine seconds while the hero is visible and idle. Previous,
Next, and Pause controls are available on desktop and fullscreen. Selecting a recommendation
pauses rotation until Resume; hovering, keyboard focus, and leaving the window pause it
temporarily. Reduced motion disables automatic rotation.

Afterglow uses still, filled 2:3 covers, with captions over the artwork on hover or keyboard
focus. Missing artwork keeps its caption visible; touch devices always show captions.
Cards keep the same size regardless of title length. Pick up the thread shows up to eight
recently played games. Saved landscape and compact-record preferences still work.

Theme Studio's **Artwork materials** controls surface finish, intensity, foil on light areas,
metal color, pointer movement, lift and tilt for themes that opt in. Afterglow does not apply
these effects, and keeps the saved settings available for other themes. Catalogue's Library
retains material effects and descriptive side previews. Themes can reuse `ArtworkEffects`,
`GamePreview` or the composed `GameCard` independently. One lazy Pixi renderer draws an active cover using
its already-decoded image; static artwork remains available when WebGL is unavailable.
System and profile reduced-motion settings keep focus effects stationary.
Resting cards do not request permanent 3D layers. Loading covers use blank, static skeleton
tiles across designs and presentation modes. Hero artwork retains its loading indicator.

Rift's Discover deck shuffles when you use its arrows, neighboring covers or left/right
keys. The outgoing card swings aside as the next card rises into place over 520 ms.
Repeated input continues from the current positions; selection and details activation
update immediately. Reduced motion switches cards without the shuffle. The reusable
`DeckShuffle` controller animates outer cards independently of their artwork materials.
Stacking order stays fixed during the motion and is restored when it finishes. Discover
keeps its portal renderer alive while changing the selected game's artwork and text;
cycling the deck does not restart the portal entrance or create a new graphics context.

Rift's Library opens a fixed-size artwork portal beside a hovered or focused cover. It flips
left or docks inside narrow windows. The preview is informational: it has no buttons or
pointer interaction, and follows only the cover's hover or keyboard focus. Click the cover
or press Enter to open details; Tab moves to the next control and Escape dismisses the preview.
Opening a game
expands the portal across the content pane into the complete shared details screen, including
editions, external links, history, journal, metadata and artwork controls. Back restores the
browsing position. Portal roundness, edge shape and activity, star brightness and cover size
are Rift settings in Theme Studio; activity at zero holds the edge still. The star field has
no continuous animation. Portals stop when inactive, and the full-page renderer is released
after the entrance. Full-view entrances use one shared clipping mask with temporary
compositor hints, keeping artwork and text at their final size. The rim stops rendering
once it is safely beyond the visible pane. These changes do not alter Electron's display
defaults. `PortalSurface` can be reused by other themes independently of Rift.

Switching compositions remembers each one's appearance and layout. Interface size and
reduced motion remain shared accessibility preferences. Existing saved Afterglow, Rift,
Catalogue and installed-theme profiles retain their choices; fresh installs and appearance
recovery use Avalon.

On the first Electron launch for a library, Avalon loads its authored theme files before
importing the saved palette from `appearance.theme`. The legacy default identifier maps to
Winnow only when no authored theme claims that ID. An existing Electron profile takes
precedence. Saved role fonts and text sizes for bundled and authored palettes also import
from `appearance.typography`. This import does not change Avalonia's palette or typography
preferences.

Avalon's **Avalon palette** setting includes the nine original bundled palettes: Winnow,
Nightshift, Tungsten, Box art, Bottle green, SilkCircuit, SilkCircuit Dawn, Rosé Pine and
Rosé Pine Dawn. **Studio colors** uses the shared palette and custom color controls instead.
Palette changes include dialogs and the native light/dark control scheme. Avalon bundles
the original Bricolage Grotesque, Plus Jakarta Sans and IBM Plex Mono static font faces.
Differing **Heading font**, **Interface font** and **Data font** choices, and the 80–120%
**Theme text size**, follow each Avalon palette. **Find installed fonts** adds local font
families to the choices; a family can also be entered directly. Missing fonts fall back
to the bundled face for that role. Text size leaves interface zoom, cover size and icons
unchanged, and **Reset theme typography** restores that palette's authored defaults, or
the bundled roles when the theme declares none.
Dormant covers wake on hover or focus. Missing artwork keeps a readable title, and hover
previews show compact, attributed ratings without game actions.

**Window appearance** shares the existing backend transparency, Acrylic/Mica, pane reach
and floating/flush preferences. Supported Windows desktops request a native material;
fullscreen and unsupported environments stay solid. Covers remain opaque. See
[Electron themes](../../docs/electron-themes.md) for platform limits and contrast behavior.

Artwork selection still refreshes with library events. Unchanged images reuse encoded bytes
for up to two minutes, with explicit artwork changes and reconnects forcing revalidation.
The first image load still uses the backend's cached PNG/base64 transport; this frontend
change does not introduce smaller backend image variants.

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

An invalid startup argument or unusable frontend data directory exits with code 2. A failure
during frontend initialization shows a diagnostic message and exits with code 3; cancellation
exits cleanly. Messages redact credentials and incidental paths. When the selected library
directory exists, initialization failures also write a bounded, redacted log in its `logs`
folder. An unavailable logger or native alert does not replace the original exit status.

## What is available

- Discover with real recommendation reasons, visible-card impressions, Play/Install, snooze,
  dismissal and Undo. Avalon keeps saved receipts in place while the reader hovers or focuses
  them; response history also retains expired and reversed choices.
- Searchable, virtualized Library with faceted filters, grid and list views, selection and bulk
  actions, fixed and live list editing, manual games, hidden-game restoration and identity review.
  Optional expansion grouping changes the Library tile set and its counts before browsing cuts;
  the base keeps its own playtime and stores, and the count mark identifies unplayed packs.
  Saved default-sort changes apply immediately, or when an open manual list closes.
- Game details with store actions, update history, sessions, journal entries, ratings,
  achievement summaries, per-field metadata editing, IGDB assignment, edition/expansion management
  and provider, URL or local-file artwork selection.
- Weekly journal browsing, per-game playtime timelines, gameplay charts and Steam spending
  statistics. Approximate store-reported time remains separate from recorded sessions, and
  spending totals keep currencies and wallet funding separate.
- Settings for Steam API credentials, IGDB credentials, Epic and provider connections,
  library preferences, background operations, provider package management and feedback history.
- Resumable first-run setup and embedded Steam sign-in, with separately optional account-page
  capture and explicit review before import. See [Steam capture](../../docs/electron-steam-capture.md).
- Embedded Epic sign-in with explicit consent, desktop/fullscreen account input and a system-browser
  fallback. See [Epic sign-in](../../docs/electron-epic-sign-in.md) for capture boundaries and verification.
- Theme Studio with five shared color presets and Avalon's nine original palettes, semantic color overrides, fonts, spacing, scale,
  artwork finishes and depth, motion, navigation placement, card styles, section ordering
  and JSON profile sharing.
- Native tray preferences, session-note notifications, isolated single-instance activation,
  a Windows recent-games Jump List and in-app browsing for external links.
- Developer themes that replace the shell and individual screens, with versioned contracts,
  declared settings, contained package assets and recovery to the bundled appearance.

Desktop and fullscreen have separate destinations and library filters. Editing drafts and
their original revisions survive navigation and mode changes for this frontend session;
they are not persisted across application restarts. F11 changes mode. Ctrl+K searches the
desktop Library inline; Avalon fullscreen opens a dedicated Search page, also available
through controller View. Search retains its query and result through Details, uses Y for
the keyboard and LT/RT for two-row paging, and returns to its originating page on Back.
Escape returns from details, and Tab moves through controls. Fullscreen has larger targets
and a recommendation filmstrip. Gamepad API navigation supports directional movement,
page and tab switching, actions, a quick menu and an on-screen keyboard. Physical-controller
and TV-distance validation remain outstanding.

Complete Avalonia parity has not yet been established. The [migration inventory](../../docs/spikes/2026-09-28-electron-parity/test-inventory.json)
tracks original presentation contracts individually; unported and partially verified tests
remain visible and fail the completion gate. Provider connections and game actions depend on backend
capabilities. A launch result means a launcher handoff, not confirmed gameplay or download
progress. Manual entries do not accept arbitrary launch commands.

Game details group browsing shortcuts under **Explore the game**: Steam's game page,
the store page, patch notes, SteamDB and IGDB. Links use known API identities and cached
store URLs. Epic/GOG store links appear when available; without a Steam ID, the latest
recorded update URL supplies the patch-notes link. Missing destinations are omitted.
Steam viewing opens its library page for Steam-owned copies, or its store page for a
Steam-mapped copy owned elsewhere. These links do not launch a game.
The [reading browser](../../docs/electron-link-routing.md) supports HTTP and HTTPS with
isolated page content, history controls, fullscreen controller navigation and visible
browser fallback notices. Native launcher viewing links remain separate from game actions.

## Themes

See [Electron themes](../../docs/electron-themes.md) for profile sharing, installation and
the complete authoring contract. Install `examples/themes/reading-room` from Theme Studio
to try a composition loaded as an independent package. It needs no build tools.

Developer packages execute trusted JavaScript with library command access. Appearance
profiles are passive JSON. Use Ctrl+Shift+T to restore Avalon, the native View menu's
recovery command (Ctrl+Shift+R), or `--safe-theme` at startup if an installed theme fails.

An explicit `--data-dir` also places Chromium state, profiles and installed themes beneath
`<data-dir>/electron-userdata`. Normal packaged launches use Electron's per-user application
directory, with profiles scoped by backend data directory. Existing prototype Electron
profiles under `Winnow Afterglow` or `winnow-electron` are reused in place when the new
directory has no profile catalogue. Appearance profiles remain local to Electron;
shared fullscreen and native-window preferences use the backend.

## Build and verify

```powershell
npm run typecheck
npm test
npm run test:integration
npm run test:rendered
npm run migration:report
npm run package
```

`test:rendered` launches the built Electron app and an isolated sample backend. Set
`WINNOW_BACKEND_PATH` to a Debug backend build when testing sample data. It exercises both
presentation paths at fixed viewport sizes; actual display fullscreen and physical hardware
still require native checks. `test:migration` is a separate completion gate and fails while
the source-method inventory contains pending or partial contracts. Passing the current
Electron suite does not mean every original test has been migrated.

`package` builds the renderer and preload, publishes a self-contained backend for the current
OS/architecture, then produces an unpacked application. On Windows, run:

```powershell
& '.\release\win-unpacked\Winnow.exe' --data-dir C:\Temp\winnow-electron-demo --no-sync
```

`npm run dist` additionally creates the configured distributables (Windows NSIS installer and
portable executable). The [Electron updater](../../docs/electron-updates.md) stages verified
updates and requires an explicit restart to install; its new installer paths still need
disposable-machine update and recovery smoke tests.
`npm run format` formats TypeScript, CSS, tests and the example package. Packaging collects
the production dependencies' license notices, including the bundled fonts, under `resources/`.
Windows builds are unsigned. Windows x64 is the locally verified target; macOS/Linux
configuration is provisional and has not been packaged or device-tested. This frontend is
not part of the repository's existing Avalonia release or update pipeline.

The integration runner starts and stops its own backend with a fresh sample database,
adds a synthetic recorded session, and clears inherited IGDB credentials in that process.
Set the executable path to a Debug build so sample data is available:

```powershell
$env:WINNOW_BACKEND_PATH = 'C:\Temp\winnow-debug\Winnow.Backend.exe'
npm run test:integration
```

The runner creates its directory beneath the repository's ignored `.tmp` folder. It runs
the complete unit/component suite and live API tests together. Direct `npm test` runs skip
live API tests unless both `WINNOW_TEST_DATA_DIR` and `WINNOW_ELECTRON_TEST_DATA_DIR` point
to an already-running throwaway `winnow-electron-*` backend. Unit/component tests
exercise concurrency conflicts, uncertain creation, draft retention, theme compatibility,
loading recovery, event races and transport boundaries without external accounts.

## Structure and boundaries

`src/main` owns discovery, credentials, HTTP/SSE, authenticated image reads, dialogs and
package storage. `src/preload` exposes named operations. The sandboxed renderer has no Node
integration or bearer token. It uses React, TanStack Query/Virtual, Radix dialogs, Motion,
Lucide and bundled fonts. It never opens SQLite or duplicates backend scoring rules.

Renderer requests can carry an AbortSignal. The preload forwards cancellation by an
opaque request identity; main accepts it only from the owning trusted renderer while
that request is active. Completion and renderer destruction clean up the request.
Leaving identity review cancels its manual suggestion refresh, and late responses
cannot replace the next page's data or focus.
Library, workspace and Details query hooks also consume cancellation. A refresh retires
earlier reads before starting new ones, and late results cannot publish after cancellation
or unmount. Both surfaces close Details when its ownerships leave the published library.

`src/shared/theme.ts` is the theme contract; `src/renderer/theming` hosts packages and Studio;
`src/renderer/features` holds reusable backend feature screens. Theme packages can reuse
those screens through the public context. See the [implementation evidence](../../docs/spikes/2026-09-26-electron-implementation/README.md)
for the validation scope and native screenshots.

The [Rift integration evidence](../../docs/spikes/2026-09-28-rift-integration/README.md)
records the alternative design's desktop/fullscreen checks, browser captures and remaining
device-validation limits.
