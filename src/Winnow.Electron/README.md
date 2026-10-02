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
Escape keeps them closed until the pointer leaves the card. Their artwork, outline
and pointer share one shape, and delayed ratings reposition the measured bubble
inside the window. Compact ratings identify each population; accessible text retains
counts and Steam's percentage. Closing a preview cancels its unfinished metadata and
releases its artwork consumer. Fullscreen keeps this information in its selected-game
hero without opening a pointer-hover bubble.

Desktop keeps five cards per recommendation shelf and promotes replacements in place.
Fullscreen exposes the complete scored shelf through horizontal navigation. Only cards
that enter the visible, focused window record impressions; Recently played records none.
Verdict controls appear only when the backend can save feedback.
**What you've told the feed** replaces the feed body and returns to the same cards.
Fullscreen **Y · More** opens the selected game's actions; Recently played omits verdicts.
The fullscreen list picker shows **Y · Keyboard** while its name field has focus and
returns focus to the originating card action when closed.
Primary refreshes coalesce behind an unfinished scoring pass, while optional shelves
cannot publish after their generation or feedback state changes.

Cover images remain visible while a larger size loads. Shrinking a realized tile keeps its
best decoded image, and recycling one tile leaves other surfaces showing that game intact.
Dormancy changes reuse the same pixels on desktop, fullscreen and desktop merge thumbnails.

Library responses validate in batches of 128 games or lists so input and cancellation can
run before a large response finishes. The query cache receives one complete snapshot;
cancelled or superseded reads cannot publish partial data. Desktop and fullscreen share
this preparation path. Inactive panes unmount, while saved Appearance choices and each
surface's navigation state remain available on reentry.

Desktop **Merges** opens directly from the rail. Its label and tooltip stay present when
the queue is empty. Details returns to the same member, and Escape returns to Library.
Up/Down moves the row cursor, Space chooses the header, S or Enter accepts the group and
D keeps its games separate. Other keys remain with the focused control. Fullscreen
continues through Manage library → Identity review with its member and confirmation sheets.

Saved same-game groups offer **Header store** on both surfaces. The chosen store supplies
the title, cover and first store entry; Automatic restores the canonical header, falling
back to a visible member. Unavailable choices stay saved until that store returns.
Metadata and editing still belong to the canonical game, and an installed, reachable copy
keeps priority for Play. Header saves retain focus and show refusals beside the choices.
Fullscreen keeps its choice sheet open while saving and returns to Header store after success.

Merge suggestions refresh beside the review actions. Busy, failed and completed checks
have a persistent status; previous answers stay intact. A background library update can
replace proposals even when their count stays the same. Details opens the selected owned
member without promoting it; a missing library entry leaves the review open with a message.
Fullscreen review sheets use large action text, a fixed title and controller hints, and
scroll their choices within the window. Desktop keeps group answers beside the title.

Desktop Library and Merges share a compact sort menu. Its button states the selected order;
choosing a row closes the menu and returns focus. Arrow keys, Home/End and typed initials
move within it; Escape returns to the button and Tab continues to the next control.
The menu stays inside the window at the saved interface scale. Fullscreen keeps its own
filter and sort panels.

Avalon's desktop Details opens over the retained library with five sections and a fixed
header. Fullscreen Details uses the original cinematic layout, two screenshot previews and
separate About and Play history reading pages. More contains the shared metadata, matching,
artwork and browsing actions. Closing restores the originating game and library position.
Fullscreen artwork fills the window behind the safe margins. Its screenshot previews use
the available reading height, preserve all image edges and scroll fully into view on focus.
Section navigation includes list-membership controls; toggling a list preserves its focus.
Desktop About keeps its 410px reading measure and the card follows the original viewport
caps. Long titles and publishers wrap. Each section keeps its scroll position, and expanded
relationships remain open when returning to Overview. Desktop expansion and base-game
relationships live in Overview; linked editions and variants live in Library. Fullscreen
keeps its relationships in Library. Technical facts are visible directly in Library. Refetch metadata closes More and
reports progress and completion in a persistent footer outside the reading area; fullscreen
uses its own text scale. Refreshing keeps the selected section and returns focus to More.

Desktop reading paragraphs in Platforms, account statistics, Settings, Merges and Steam
consent share the same left-aligned 410px measure. Fullscreen prose uses the available
width and 28px base text, adjusted by the saved text scales. Details tabs retain a 2px
keyboard focus underline. Screenshot thumbnails name their position in both accessible
labels and tooltips; their lightbox stays above Details in the same window.

Owned copies retain their own titles, store badges, playtime and last-played dates. Achievement
counts and percentages belong to each release; unsupported stores, unfetched Steam progress,
unavailable progress and games without achievements have distinct labels. Expansions and
Extends show the visible counterpart's own figures and provide View and Separate actions.
Expansion playtime stays separate from the base game's total. Hidden or account-scoped-away
counterparts do not appear. Separating a relationship targets its child and leaves sibling
links and original library records intact on both surfaces.
Escape or controller B closes only the confirmation and restores its opener. The
fullscreen confirmation uses its own text scale and keeps D-pad Choose, A Select and
B Cancel hints visible inside the dialog.

Controllers work on both surfaces. Desktop shoulders move among controls in the active
dialog or flyout; fullscreen shoulders switch root pages. Accept operates the focused control
or opens text entry. Right-stick scrolling keeps focus in place. Fullscreen Details uses
explicit focus rows: left/right moves tab focus, A selects, and triggers switch sections.
Its Add to list action lives in More, and About supports directional reading from Back.
Held controls are suppressed independently after reconnect or focus return. Device input
uses Chromium's standard mapping. Fullscreen root pages and Details show a local clock
and controller status. On Windows, a read-only main-process XInput helper supplies wired
and known battery labels, caching battery reads for 30 seconds. The selected browser
controller must uniquely match a native device's current controls; browser indexes are
not native slots. Unknown, unsupported or ambiguous readings show only “Controller connected”.
Disconnecting shows “Controller disconnected”. The helper closes when fullscreen detaches
or the window hides; the clock releases its timer on detach. Physical-device validation
remains open in TASK-381.40; automated native-boundary fixtures do not establish it.
Reading links use the same validation before rendering and dispatch: valid HTTP and HTTPS
destinations remain available, while invalid targets or empty labels produce no button.
Desktop and controller settings share the saved reading destination. Browser fallback
notices remain reachable above open dialogs; dismissing one restores its originating
action, and Escape from the notice leaves the dialog open.
Steam account capture follows the page's own paginator in the private sign-in session and
requests fresh documents. Page-script errors leave capture failures for the host to report.
Updates counts correlated patches per release, using the largest count for linked editions.
Mark as read preserves confirmed partial saves and retries only the remaining unread releases;
Show it again restores their flags. The Activity timeline keeps acknowledged history in
neutral ink and retains the selected range. Results remain visible without taking focus from
a different control.
Library **Mark as read** is available only for unread selections in Patched. It captures
the selected games and each release's displayed patch timestamp before saving, so later
pushes remain unread. Repeated activation shares the pending operation; retry skips
releases already acknowledged and continues after a refusal on another release.
Fullscreen More and Library options use a right-edge action panel with saved safe margins,
independent scrolling and retained origin focus. Escape, B, right-click or the scrim closes
it. Nested Hide starts on Cancel; a child editor returns directly to its originating page.
Metadata opens in its own desktop dialog, bounded to 1440 × 1000 with a fixed Back button.
Fullscreen uses an ordered field menu: Back cancels the active field's draft and returns to
its row; successful saves return with updated attribution. A edits, Y opens the keyboard,
and B returns. Desktop retains unsaved drafts across dialog navigation.
**Edit details** keeps its label and explanatory tooltip. Saving one field updates the
headline, tile, sorting and filter facts without replacing other unfinished fields or
their source attribution. If the metadata editing service is unavailable, Details remains
usable and hides the editor. Without a file picker, both artwork fields keep their URL
route and omit file selection.

The artwork browser's **Current** cover follows the displayed header on both surfaces.
A saved choice keeps priority until **Use automatic** restores the live cover. Previewing
another candidate preserves that selection while the header changes; saving and resetting
still edit the canonical game. Source-order changes apply to open backdrops immediately.
The old image stays visible until its replacement is ready, and closing the view releases
its artwork and preference subscription.
Fullscreen fills the viewport with the saved safe margins and larger controls. Its
artwork-type, preview and Back hints stay beside the actions; focusing the URL field
adds **Y · Keyboard**. Exact ultrawide hero fitting remains stable under interface zoom.

Settings groups Steam, Epic and GOG under **Platforms**, with attention markers and title
counts from the whole library. Desktop shows one card at a time. Fullscreen opens each
platform from a summary, with separate API key and purchase-history pages. Back restores
the invoking action; vertical navigation and Select, Back and keyboard hints stay beside
the controls. Steam keeps credential guidance, account scope, purchase import and sign-in
consent separate. Epic asks before signing out, and cancelling preserves the current account.
Saved Steam pages are selected first and imported only after **Read**. Fullscreen uses
the controller file browser; leaving the page discards its selection. CSV exports retain
the UTF-8 signature and the backend's original contents. Cancelling the save writes nothing.
**Export acquisition CSV** in Library settings and **Export acquisitions** in account
statistics share one pending operation. They report the exported ownership count, cancellation
or a safe save failure, and remain disabled when the host has no export capability.
Steam-reported activity stays separate from recorded sessions. Desktop Details shows its
observations inline; fullscreen Play history opens a separate projection, then a reading
page for the selected observation. Each reader includes its observed interval, uncertainty
and explanation, with local controller hints and Back focus restoration.
**Show only your account**
becomes available after Steam confirms the account. Its separate count uses the data
font and appears only when games from other accounts can be hidden. Games without
account attribution stay visible; own-account mode also scopes the displayed playtime.
IGDB and artwork source
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
**Add from executable…** opens the form and file chooser together. File inspection proposes
a title and searches IGDB; choosing a match fills the form, and only **Save game** writes
the entry. Browsing again replaces Winnow's previous guess while preserving a typed title.
Save waits for file inspection, but an empty or failed metadata search still permits a
manual entry. Cancel discards the draft and restores its invoking control. Leaving the
page preserves entered fields; late inspection and search responses cannot alter a
departed or discarded form. Fullscreen uses the controller file chooser and text keyboard.

IGDB candidates in Details and manual forms share a cover, title, year and single-line
platform list, with the action kept at the trailing edge. Long platform lists trim with
the full value available in a tooltip. Choosing a Details match preserves the current
section, refreshes metadata and artwork, and pins the selection. A claimed entry offers
an explicit same-game link; declining or a refused link leaves both mappings unchanged.
Returning to automatic matching clears the pin and restores Steam capsule precedence.
An unavailable assignment service hides the matching control; missing credentials alone
keep it available. Closing Details and reopening it starts at Overview.

Fullscreen Settings opens on **Appearance**, followed by **Controller**, **Library**,
**Platforms**, **Metadata & artwork**, **Plugins** and **Application**. Appearance has
directional adjustment rows, visible On/Off switches, shared theme and font pickers, and
a live cover preview. Text size moves in ten-point steps; interface scale moves in
five-point steps. Reset confirms the five fullscreen preferences and preserves shared
theme, typography and cover choices. The Controller guide keeps ten mappings and keyboard
help on one screen. Spending and recommendation history open from Library; background
operations open from Application.

Displayed 100% interface scale uses the original 85% baseline. Larger displays scale the
reference layout uniformly, so 4K retains the same cover density as 1080p. Smaller windows
use responsive typography. Safe margins stay proportional to the physical window, independently
of interface scale; text size remains a separate preference. Old interface-scale settings
restart at the new baseline once, while subsequent adjustments persist separately.

Fitted library and feed covers on both surfaces, and desktop merge portraits, extend each
artwork edge's sampled color into its adjacent gap. Fill mode clears the padding;
dimming applies to the image and padding together. Fullscreen shelf
arrows and dots fit within the cover row and preserve controller focus when clicked.
Activity's journal artwork and Settings' contours fill the canvas behind their safe margins.
Activity notes open on a bounded reading page with a fixed Back action and directional
scrolling. Closing returns focus to the invoking action; desktop keeps its note dialog.

Fullscreen Library and Application use the original adjustment rows, switches, section
rules and separate reading panes. Library sort, journal prompts, visibility, expansion
grouping, startup, tray and link choices share their saved values with desktop. Left and
Right set switches explicitly; controller focus stays on a row after saving. Library tools
opens from Library settings and Back returns to its action. Application includes diagnostics
help, the active backend's log directory, setup replay and update controls. Unsupported
startup registration is omitted from fullscreen and explained in desktop settings.
Windows startup registration reads and writes the same executable and background
arguments, including an explicit library override. Application settings retain
**Open logs folder** after session tracking recovers. If opening fails, the feedback
includes the resolved path for manual access; a successful retry clears that feedback.

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
Saving settings for an enabled, loaded provider acknowledges the queued refresh.
PlayStation history keeps its provider name in filters and game details. Grouped game
details retain each distinct provider's source explanation, including played history
that does not establish ownership, even when another store supplies the primary copy.
Device sign-in shows the provider's code and verification address only after you start it.
Leaving or canceling stops that attempt. Pending writes and installations prevent a service
restart; a completed restart reconnects and reloads the plugin catalogue.

Website **Install in Winnow** links start the named package immediately. Desktop shows
progress in Plugins settings; fullscreen opens a separate progress page and, on success,
the provider's settings page. Leaving either flow keeps the operation running without
taking navigation back. Progress, cancellation and retry share one state across surfaces.
An interrupted response retains its operation identity for retry. Setup pauses for a
handoff and **Resume setup** returns to the same saved step.

First-run setup keeps its saved cursor across desktop and fullscreen. Desktop uses a
bounded overlay with fixed navigation; IGDB Save, Remove and status stay outside the
field scroller. Fullscreen uses a separate page with controller hints. Its provider and
settings pages return to the same step when closed. Leaving a credential step or changing
presentation clears unsaved secrets. Steam consent and the controller keyboard keep their
own focus scope above setup.

The Theme step includes ordinary palette, font, layout and transparency choices on
desktop, and the shared TV appearance controls on fullscreen. Pending saves keep the
wizard in place; failed saves can be retried or skipped. Fullscreen appearance keeps
its adjustment and reset hints, with nested pickers above the setup page. Pickers keep
their own Select and Back hints, with Keyboard shown when the font-family field has focus.

Each data directory owns one Electron session. Additional launches restore its window
and deliver requests in order. Before the renderer is ready, the queue holds up to 64
requests and coalesces adjacent duplicates; after readiness, repeated requests each
dispatch. Malformed structured activations do not restore the window or enter the queue.
Separate library directories remain independent, including their backend and browser profile.
Shell activation IDs above JavaScript's safe integer range travel as canonical decimal
strings. The backend selects their current primary action, preserving the exact ownership
ID and the ordinary operation retry rules.

Before opening the library window, Electron waits up to 45 seconds for a healthy backend
and its initial presentation preferences, or a newly started companion's failure.
The preference read uses the remaining deadline so a slow saved-mode read cannot open
setup in the wrong presentation first. A deterministic companion startup refusal exits
without opening the library. A missing companion or timeout opens the recoverable connection
screen. After the window opens, preparation remains visible while primary snapshots wait
for discovery and the event handshake, with the same 45-second initial request deadline.
After a successful connection, requests during a disconnect wait up to 12 seconds for
reconnection. Canceling a queued request prevents it from being sent later.

Avalon reads existing authored Winnow JSON palettes from the library's `themes` folder,
including their fonts and opening preferences. Theme Studio provides reload, diagnostics,
folder access and safe export; saved file edits update the active palette automatically.
File errors stay visible, while bundled and local legibility warnings start collapsed.
A local replacement overrides the bundled palette and its audit findings together.
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
Cover and screenshot images share decoded pixels through leases, including thumbnails and
the lightbox. Their cache admits at most 128 image loads and runs six browser decodes at
once. Initial network waits do not hold those decode permits. Encoded strings waiting for a decode
have a separate 32 MiB budget; excess requests retain only their identity and reread the
backend's cached bytes under a permit before decoding. If those disk bytes have meanwhile
been removed, that bounded retry can wait for a download while holding its permit.
Completed encoded entries retain their existing
five-minute expiry. A 32 MiB decoded scrollback cache keeps recent pixels; visible images
remain valid after eviction. The last departing consumer cancels its pending request.
Detached images clear their sources.
Requests use the original display-width buckets, measured at the actual pixel density,
over the existing backend PNG/base64 transport. Avalon's saved Fit/Fill preference changes
the crop without changing cover bounds or fetching the image again, including desktop merge-review
portraits. Heroes and screenshots retain their own presentation. Missing or invalid saved
modes display as Fit without writing back; only a valid Fit or Fill selection is saved.

## Run from source

Install a current Node.js release compatible with Vite 7 (Node 22.12 or later) and the .NET 10 SDK.
From this directory:

```powershell
npm ci
dotnet build ../Winnow.Backend/Winnow.Backend.csproj
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

On Windows, a companion helper owns Electron's current-user activation mutex and pipe.
It runs before HTTP hosting or database locking and exits when the frontend closes. A
second Electron process forwards its typed activation and exits; Avalonia can remain open
beside Electron. The shared activation module sets a protected ACL owned by the current
user, denies network logons, and rejects activation from other accounts.

The helper uses an existing configured backend, the bundled companion, or a development
Debug/Release backend apphost. It never starts `dotnet run`. Set
`WINNOW_ACTIVATION_HELPER_PATH` to an absolute executable or DLL to select it independently,
including when testing a missing backend. An explicit unusable helper path is refused.
Without any usable helper host, Windows startup reports a fatal error with build guidance;
with a helper available, an unavailable backend retains the recoverable connection screen.

Application settings on desktop and fullscreen show the frontend package version and the
source commit embedded when the bundle was built. Development uses `package.json`, and a
packaged application uses its installed version. Builds outside a Git checkout show
`Unavailable` for the commit. Electron's runtime version is not the application version.

An invalid startup argument or unusable frontend data directory exits with code 2. A newly
started companion's refusal preserves exit 2 or 3, including malformed configuration,
invalid logging settings and an unsupported database schema. Backend configuration comes
from its installation directory. A failure during frontend initialization shows a diagnostic
message and exits with code 3, including a failed primary renderer load; cancellation
exits cleanly. Messages redact credentials and incidental paths. When the selected library
directory exists, initialization failures also write a bounded structured log in its `logs`
folder. It records the exception type, named call frames, available native error code,
build and run metadata. Arbitrary exception messages and file paths are omitted.
An unavailable logger or native alert does not replace the original exit status.

## What is available

Avalon's desktop caption shows **FETCHING DETAILS** and the remaining title count
while metadata enrichment runs. It is passive, disappears on completion or
disconnection, and continues tracking progress while fullscreen hides the caption.
Progress events refresh that count independently of the Library and feed. Returning
from fullscreen or closing a dialog shows the current count without changing focus.

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

Isolated Windows Jump List tasks carry their library directory in every activation. Existing
profile-based taskbar identities are preserved, and the legacy default `Hoard` directory
shares the canonical default identity. Recent games appear immediately with a fallback or
previously cached icon, then receive cover artwork as it becomes available. Icons contain
six square PNG frames from 16px through 128px, cropped from the cover's center. Their content
hash names keep unchanged artwork stable and retain old files that Windows may still use.
The cache lives in the selected library's `jump-list-icons` folder. Superseded or canceled
artwork requests cannot republish an old list.

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

Avalon fullscreen **Filter & sort** separates Browse from Refine games. Collection,
sort, installation and facet choices open their own pages; release-year buttons open
the controller keyboard. The count reflects staged changes. Apply, Clear and Cancel
stay visible, with A/B/Y hints: B returns from a choice page or discards the draft at
the overview, and Y applies the valid draft. Desktop keeps its immediate filter panel.
Built-in collections expose their explanations on both surfaces, including empty
collections. Missing selected facets remain removable and restrictive.

Library selection can hide every picked game after confirmation, and **Remove from
Derelict** appears only within that collection. Hidden games retain their history and
can be restored individually from Library tools. Desktop list rows show each distinct
store in its own chip. **STATS**, between Merges and Library, opens Gameplay initially
and preserves the selected Gameplay or Spending section on return;
fullscreen reaches it through Activity's Library summary.
Gameplay retains independent store and date choices on each surface. Its scope follows
visible ownerships and linked games, independent of Library search. Removing the selected
store returns to All stores. Custom dates include both local calendar days, including
daylight-saving transitions. Cancel, navigation and scope changes reject obsolete reads.
Current library and store-entry counts stay independent of the chosen period. Spending
shares the section toolbar and places its figures directly below source and coverage copy.
Fullscreen Library tools uses a single column of list editors with larger labels,
fields and actions; the page scrolls to keep the focused controller target visible.

The shared text keyboard uses five QWERTY rows, a wide Space key and an inverted-T
caret cluster. Case changes the letters and symbols; X deletes backward and RT invokes
Enter. Fullscreen shows the bundled D-pad, A, X, RT and B glyphs beside their action labels;
desktop retains text hints. B and Done close once and return focus to the original field. Activity and game
details journal editors use larger fullscreen fields and an explicit Edit note action. Expandable
filter and help sections participate in directional navigation.

Fullscreen file selection stays inside Winnow for artwork, manual executables, appearance
profile import/export, developer-theme folders, saved Steam pages and acquisition CSV export. The chooser
pages through folders, filters extensions without regard to case, and asks before replacing
an existing file, initially focusing Cancel. The chooser keeps D-pad, A and B glyph hints
visible, with Y Keyboard for filename entry and B Back on the replacement prompt. Choosing
a path never writes the file; the operation that opened the chooser owns the subsequent
read or write. Desktop keeps native file dialogs and the multi-file input for saved Steam
HTML. Fullscreen selects saved pages one at a time, then **Read selected pages** imports
the accumulated selection.
Installing a developer theme retains its native trust confirmation.

Fullscreen keeps LB/RB glyphs on either side of the centered main menu. Library, Activity
and Settings keep LT/RT glyphs beside their local sections.
The root footer uses the same bundled Xbox-style glyphs when a standard controller is
connected, with named Browse, Select, Back and contextual actions. Disconnecting restores
keyboard guidance. Controller families are not inferred from device names.
Bold selection reserves its width, so changing sections keeps the strip stationary; selected
sections use a neutral underline and focused actions use the accent. Repeated controller Menu
presses retain one quick menu and return to the original control on Back or Resume. Cursor
ownership clears on presentation changes and returns to the mouse when it moves. Native
fullscreen exit restores the previous normal or maximized window. Visible fallback cover
titles honor fullscreen text size without compounding repeated preference updates.

Startup, fullscreen entry and return to desktop keep the library covered while the library,
primary recommendations and layout settle. The dragon traces its contours for a complete
1.8-second circuit before a 180ms reveal; an OffscreenCanvas worker keeps it moving during
UI-thread work. Desktop respects system reduced motion and fullscreen uses its saved motion
preference. Reduced motion and hidden preparation skip the animation delay, while retaining
data readiness. A failed preparation offers Try again. Enter fullscreen and Back to desktop
remain available during preparation, even before a library context exists. Reentry shares an
unfinished read and refreshes completed reads without losing the page.
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
Native notification delivery requires a usable window handle; missing or destroyed windows
return unavailable, while a valid window hidden in the tray remains eligible. Spending reads
show a loading message and a retry action on failure. Leaving that screen cancels its read;
late results cannot repopulate the cancelled query.

Desktop Details edits notes inline and retains the draft when switching its section tabs.
Fullscreen uses its separate journal editor. A pending save blocks duplicate submissions
and closing the editor or its enclosing Details view. Activity preserves loaded pages and
the selected session when saving an older note. Rapid week changes queue the latest scope;
leaving the last reader cancels the request and late results cannot restore the old page.

Library and Details share an ambient launch status across desktop and fullscreen. A launcher
handoff shows Starting; a matching watcher observation confirms that the game is running.
Waiting disappears silently after 90 seconds, confirmation after three, and refusal after seven.
Fullscreen Details offers **Choose launch version** for games with multiple copies. Choosing
a version carries that copy's ownership through dispatch and feedback. Desktop's per-copy
actions remain in Details' Library section. Install and management actions do not show a
gameplay status, and uncertain retries retain their original operation ID.

Desktop tile actions, fullscreen X and Details primary actions use one command per
ownership. Concurrent requests share the pending result; changing views after an
interrupted response retries the same operation. A completed action allows a new intent.
Tile descriptions use the same collection names as the rail. Plugin source explanations
appear in Details and its per-copy Library section, including after an offline refresh.
Installation refreshes update Play/Install, Steam uninstall and the current folder while
retaining store links and an open metadata draft.
Pending actions retain their label and disable duplicate input. Completion and error
messages occupy a separate row so More and its store-link hit target remain stationary.
Fullscreen owned-copy facts and source explanations use the scaled 24px body size.
The More panel applies viewport scaling once, retaining its reference proportions at
4K while honoring interface scale, text size and percentage safe margins.

Fullscreen Play history starts on Lifetime; Right then Accept selects Tracked sessions.
Both trackers state the selected copy's compact playtime and unread-update count.
The total uses scaled 30px monospace type; axis dates match the series' UTC calendar.
Acknowledging updates refreshes that count and the marks while retaining the selected
range. Timeline series and locale formatters are reused so selecting a mark does not
rebuild every session label in a large history. Journal and Recorded hours render their
rows when opened; every entry remains available in the continuous history.
Refreshing Details preserves its local section and an open journal draft. Expansion
separation names both games, initially selects Keep relationship, and sends the child
identity only after confirmation.

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

Cached GOG patch notes appear under Updates for the selected GOG release. Desktop
uses a **GOG patch notes** expander; fullscreen opens a **Patch notes** reading page
with scrolling and Back hints. The text retains the backend's sanitized line breaks;
opening it does not fetch another store page or borrow another edition's notes.
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

The native activation, host and startup-contract suites require prebuilt backend and
fixture apphosts. From this directory, build
`dotnet build ../../tests/Winnow.Electron.Fixtures/Winnow.Electron.Fixtures.csproj`
before running them. Their default paths use the ordinary Debug output. For scratch
builds, set `WINNOW_BACKEND_PATH`, `WINNOW_ACTIVATION_HELPER_PATH` and
`WINNOW_ELECTRON_FIXTURE_PATH` to the corresponding absolute apphost paths. A separate
helper path lets startup-recovery tests deliberately remove the backend while retaining
frontend ownership. These suites never use `dotnet run` for the parent-bound helper.

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
Library refreshes wait for pending membership writes and their compensating changes,
so a reload cannot replace the latest list choice with an intermediate result. Cancelling
the waiting query remains immediate; unrelated queries continue normally.

`src/shared/theme.ts` is the theme contract; `src/renderer/theming` hosts packages and Studio;
`src/renderer/features` holds reusable backend feature screens. Theme packages can reuse
those screens through the public context. See the [implementation evidence](../../docs/spikes/2026-09-26-electron-implementation/README.md)
for the validation scope and native screenshots.

The [Rift integration evidence](../../docs/spikes/2026-09-28-rift-integration/README.md)
records the alternative design's desktop/fullscreen checks, browser captures and remaining
device-validation limits.
