# Winnow Electron

An independent Electron/TypeScript frontend for Winnow's local backend. **Avalon** is the
default composition, carrying the original Winnow palette, bundled typography, portrait
covers and dormancy treatment into Electron. Desktop uses a side rail and cover wall;
fullscreen uses a hero and a directional recommendation shelf. Theme Studio also offers
**Afterglow**, with a panoramic featured game and editorial typography, **Rift**, with
floating covers and artwork portals, and **Catalogue**, with a reading desk and index.
All compositions use the public theme interface available to installed themes.

Avalon's desktop covers reveal a compact Play or Install action and a folded Details
corner on hover or keyboard focus. Clicking the cover still opens Details. Selection
keeps its border without pinning the action dock open; recycled tiles release outgoing
focus and pending feedback. Store words and compact playtime/idle figures share the
Library scrim. Fullscreen keeps its separate directional cover and Details action path.
Accessible cover and list names state the unread patch count, with singular/plural wording
and no duplicate count for store copies. The Patched collection announces its game count
and meaning. Recommendations expose their reason at the cover's focus stop and announce
feedback only after it is saved.

Desktop recommendations keep feedback in a 48px strip inside the portrait cover, with
the title and reason below it. A saved verdict preserves the card's size and offers Undo
in place. Hover previews stay inside the window and close when their tile is rebound;
Escape keeps them closed until the pointer leaves the card. Fullscreen retains its
separate hero actions.

Cover images remain visible while a larger size loads. Shrinking a realized tile keeps its
best decoded image, and recycling one tile leaves other surfaces showing that game intact.
Dormancy changes reuse the same pixels on desktop, fullscreen and desktop merge thumbnails.

Desktop **Merges** opens directly from the rail. Its label and tooltip stay present when
the queue is empty. Details returns to the same member, and Escape returns to Library.
Up/Down moves the row cursor, Space chooses the header, S or Enter accepts the group and
D keeps its games separate. Other keys remain with the focused control. Fullscreen
continues through Manage library → Identity review with its member and confirmation sheets.

Desktop Library and Merges share a compact sort menu. Its button states the selected order;
choosing a row closes the menu and returns focus. Arrow keys, Home/End and typed initials
move within it; Escape returns to the button and Tab continues to the next control.
The menu stays inside the window at the saved interface scale. Fullscreen keeps its own
filter and sort panels.

Avalon's desktop Details opens over the retained library with five sections and a fixed
header. Fullscreen Details uses the original cinematic layout, two screenshot previews and
separate About and Play history reading pages. More contains the shared metadata, matching,
artwork and browsing actions. Closing restores the originating game and library position.
Updates counts correlated patches per release, using the largest count for linked editions.
Mark as read preserves confirmed partial saves and retries only the remaining unread releases;
Show it again restores their flags. The Activity timeline keeps acknowledged history in
neutral ink and retains the selected range. Results remain visible without taking focus from
a different control.
Fullscreen More and Library options use a right-edge action panel with saved safe margins,
independent scrolling and retained origin focus. Escape, B, right-click or the scrim closes
it. Nested Hide starts on Cancel; a child editor returns directly to its originating page.
Metadata opens in its own desktop dialog, bounded to 1440 × 1000 with a fixed Back button.
Fullscreen uses an ordered field menu: Back cancels the active field's draft and returns to
its row; successful saves return with updated attribution. A edits, Y opens the keyboard,
and B returns. Desktop retains unsaved drafts across dialog navigation.

Settings groups Steam, Epic and GOG under **Platforms**, with one card at a time, attention
markers and title counts from the whole library. Steam keeps credential guidance, account
scope, purchase import and sign-in consent in separate dialogs. IGDB and artwork source
preferences live under **Metadata & artwork**. The shared IGDB form in Settings and setup
reports saved, unreadable and externally configured credentials without revealing a secret.
Saving protects the secret through the backend and queues metadata refresh; removal takes
effect immediately and reports configuration fallback. Refused writes retain the masked
draft, and leaving the form clears it. Fullscreen provides the controller text keyboard.
**Sync metadata now** shares one operation and progress message across desktop, fullscreen
and Operations. Leaving settings keeps it running. Completion explains missing credentials,
partial updates or a library refresh failure; an interrupted response retains the accepted
operation ID for retry. Fullscreen opens **IGDB metadata** and **Artwork source order** as
separate reading pages. Back returns to the originating action, and moving an artwork
source keeps controller focus on that source after its order is saved.
Manual games in **Manage library** report
validation and identifier conflicts beside their fields and ask before removing a named entry.

Fullscreen Settings opens on **Appearance**, followed by **Controller**, **Library**,
**Platforms**, **Metadata & artwork**, **Plugins** and **Application**. Appearance has
directional adjustment rows, visible On/Off switches, shared theme and font pickers, and
a live cover preview. Text size moves in ten-point steps; interface scale moves in
five-point steps. Reset confirms the five fullscreen preferences and preserves shared
theme, typography and cover choices. The Controller guide keeps ten mappings and keyboard
help on one screen. Spending and recommendation history open from Library; background
operations open from Application.

Fullscreen Library and Application use the original adjustment rows, switches, section
rules and separate reading panes. Library sort, journal prompts, visibility, expansion
grouping, startup, tray and link choices share their saved values with desktop. Left and
Right set switches explicitly; controller focus stays on a row after saving. Library tools
opens from Library settings and Back returns to its action. Application includes diagnostics
help, the active backend's log directory, setup replay and update controls. Unsupported
startup registration is omitted from fullscreen and explained in desktop settings.

The notification-area icon appears when either tray preference is enabled or Winnow starts
in the background. A hidden window keeps its icon until restored, even if another frontend
disables both preferences. **Open Winnow** preserves normal, maximized or fullscreen
presentation; **Exit** closes the frontend. Ordinary windows need no icon. Background
launches suppress the fullscreen startup preference and show the window if the notification
area is unavailable. Existing capitalized boolean preferences remain supported.

**Plugins** gives every runtime-loaded provider its own tab, followed by **Manage plugins**.
Overflow arrows scroll the tabs without changing the selection. Ordinary drafts survive
navigation; secret replacements clear on departure or successful save. Advanced fields
keep their values when collapsed, and required fields reveal themselves before saving.
Device sign-in shows the provider's code and verification address only after you start it.
Leaving or canceling stops that attempt. Pending writes and installations prevent a service
restart; a completed restart reconnects and reloads the plugin catalogue.

Website **Install in Winnow** links start the named package immediately. Desktop shows
progress in Plugins settings; fullscreen opens a separate progress page and, on success,
the provider's settings page. Leaving either flow keeps the operation running without
taking navigation back. Progress, cancellation and retry share one state across surfaces.
An interrupted response retains its operation identity for retry. Setup pauses for a
handoff and **Resume setup** returns to the same saved step.

Each data directory owns one Electron session. Additional launches restore its window
and deliver requests in order. Before the renderer is ready, the queue holds up to 64
requests and coalesces adjacent duplicates; after readiness, repeated requests each
dispatch. Malformed structured activations do not restore the window or enter the queue.
Separate library directories remain independent, including their backend and browser profile.
Shell activation IDs above JavaScript's safe integer range travel as canonical decimal
strings. The backend selects their current primary action, preserving the exact ownership
ID and the ordinary operation retry rules.

The first backend attachment allows 45 seconds for discovery and the event handshake before
initial snapshot requests fail. Preparation remains visible while those requests wait.
After a successful connection, requests during a disconnect wait up to 12 seconds for
reconnection. Canceling a queued request prevents it from being sent later.

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
Visible images share decoded pixels through leases. The renderer admits at most 128 image
loads, runs six at once and keeps a 32 MiB decoded scrollback cache; visible covers remain
valid after eviction. The last departing consumer cancels its pending request. Detached
images clear their sources, and unused encoded entries expire after five minutes.
Requests use the original display-width buckets, measured at the actual pixel density,
over the existing backend PNG/base64 transport. Avalon's saved Fit/Fill preference changes
the crop without changing cover bounds or fetching the image again.

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

Startup, fullscreen entry and return to desktop keep the library covered while the library,
primary recommendations and layout settle. The dragon traces its contours for a complete
1.8-second circuit before a 180ms reveal; an OffscreenCanvas worker keeps it moving during
UI-thread work. Desktop respects system reduced motion and fullscreen uses its saved motion
preference. Reduced motion and hidden preparation skip the animation delay, while retaining
data readiness. A failed preparation offers Try again; fullscreen also keeps Back to desktop
available. Reentry shares an unfinished read and refreshes completed reads without losing the page.
Theme colors and mark size update within the same animation circuit; hiding or detaching the
presentation releases its worker and canvas.

Avalon's desktop command bar keeps Density beside **Display**, which opens the saved cover,
dormancy, non-game, rating-cap, expansion and journal preferences. The six-step rating cap
names the selected age level and counts only the titles it hides; unrated games stay visible.
The explicit-content toggle remains in Library settings, and the cap explains when that
toggle is still hiding adults-only content. Fullscreen exposes **Content age limit** in
Library settings, with controller adjustment and scaled text. Setup and alternative layouts
also expose the shared cap preference.

Session journal prompts are off until enabled in Display preferences. Each prompt names one
finished sitting. Desktop uses a compact dock with a note field and retractable rating dots;
fullscreen uses a larger editor with controller keyboard support. An untouched prompt expires
after two minutes. A draft or pending save stays open and cannot be replaced by another session.
Dismissal writes nothing. Save failures keep the draft, and an unchanged stored revision lets
the same Save button retry. An accepted save finishes in the backend even if the frontend closes.

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
npm run test:packaged
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

`test:packaged` runs Windows executable and real shortcut activation checks against the
unpacked build and its bundled backend. It creates a temporary library whose path contains
spaces, verifies profile ownership and preserves the global installation-link association.
Explicit `--data-dir` sessions do not register themselves as the global `winnow://` handler;
the ordinary packaged profile registers that handler on startup. The packaged suite does
not install or publish a release. `WINNOW_PACKAGED_EXE` can select another unpacked build.

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
