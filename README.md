# Winnow

**A local-first game library manager that surfaces the games you own, meant to play, and
forgot existed.** No server, no account, no telemetry.

Large PC libraries accumulate games you meant to play and forgot about. Storefronts don't
track the signals that matter: how long a game sat unopened, whether you bounced off it,
whether it's been patched since you last tried. Winnow does.

---

## For users

### What it does

- **One library across Steam, Epic and GOG.** Read from local launcher files, with optional
  sign-in where a store's API knows things its files don't.
- **A feed that says why.** Every recommendation carries a sentence — *"You put 2.8 hours into
  this in 2021 and it has had an update since, most recently 'PATCH NOTES – S06.05.02'."* Not
  a genre tag, not a star rating. The reason is the product.
- **Buckets that mean something.** *Never played* means zero recorded minutes and no
  last-played date. Other buckets distinguish brief trials, longer starts and patched returns.
- **Patch tracking.** Games updated since you last played them.
- **Launch and session tracking.** Click Play; the game starts and nothing else happens.
  Winnow records when you actually played, which storefronts don't retain.
- **Nine bundled themes and transparency**, including Bottle green, SilkCircuit and Rosé Pine with Dawn variants, plus a drop-in JSON theme format.

### Install and run

Release packages include the .NET runtime. When a beta release is published, download it
from [GitHub Releases](https://github.com/safwyls/winnow/releases):

- **Windows x64:** run the `-setup.exe` installer, or extract the `.zip` and run `Winnow.exe`.
  The installer runs per user and preserves library data when removed. Windows packages
  are currently unsigned. Embedded sign-in requires the Evergreen WebView2 Runtime.
- **Linux x64:** the `.deb` targets Ubuntu 24.04 with a desktop session. Install it with
  `sudo apt install ./Winnow-<version>-linux-x64.deb`, then launch Winnow from the app menu
  or run `winnow`. The `.tar.gz` is a portable alternative: extract it and run `./winnow`.
  Portable builds need the native libraries listed in [release instructions](docs/releases.md).

To run from source, install the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```powershell
git clone https://github.com/safwyls/winnow.git
cd winnow
dotnet run --project src/Winnow.App
```

Linux builds have limited storefront integration: Epic/GOG discovery targets Windows
launcher locations, embedded sign-in uses Windows WebView2, and credential persistence
requires the Windows DPAPI protector. Linux native session detection and Steam compatibility
path attribution have Ubuntu smoke coverage; actual Wine/Proton game compatibility varies.

Diagnostics are saved under `%LOCALAPPDATA%\Winnow\logs` (or the selected `--data-dir`).
Five rolling files retain roughly 5 MiB. Logs omit identity values, paths, credentials and
exception messages while retaining operation names, counts, timings and exception types.

The window opens after the local scan. Titles, cover art and update signals fill in behind it;
startup time depends on the library and device.

### First run

A new library opens a setup wizard for IGDB, Steam, Epic, GOG, theme, application preferences
and library preferences. **Continue**, **Back**, **Skip this step** and **Skip setup** let you
choose how much to configure. Closing Winnow partway through resumes that step next time;
finishing or skipping setup keeps it closed. Existing libraries continue straight into Winnow.
You can reopen it with **Settings → Application → Run setup again** on desktop or fullscreen.

Preferences save as you change them. Credential forms have an explicit **Save** action;
skipping a step discards unsaved secret fields and preserves saved choices. Saving
IGDB credentials applies them immediately and queues a background metadata refresh. Steam and Epic offer their existing
optional connections; GOG uses local Galaxy discovery without a sign-in step.

The first run does the most work — scanning launcher files, creating a record per game, then
fetching titles and covers. Give it a minute or two.

**`Patched` grows over the first week.** The update poller spreads its sweep across seven
days, so a game enters that bucket when its slot comes up.

### Fullscreen and controllers

Choose the **Fullscreen** icon beside the settings cog, press **F11**, or press the controller's
**Menu / Start** button. Fullscreen has its own For you, Library, Activity, Settings and
game-detail screens, designed for a TV. Press F11 or choose **Exit fullscreen** in the
controller's quick menu to restore the previous window state. Desktop and fullscreen retain
separate browsing positions, filters and layout preferences. Theme selection is shared and
updates both surfaces immediately. Fullscreen text size ranges from 70% to 140%, with −/+
buttons for mouse adjustment and left/right adjustment on a controller.
Fullscreen Appearance also offers **Interface scale** from 80% to 120% in 5% steps. This adjusts
text, covers and controls together; text size remains a separate readability adjustment.

Enable **Start in fullscreen** in **Settings → Application** on either surface to use the
TV interface on the next launch. It defaults off. Windows sign-in and explicit background
launches still start quietly in the notification area.

Fullscreen Appearance includes **Fit ultrawide displays** to use the full screen width
without stretching text. Controller input hides the cursor; moving or clicking the mouse
restores it. In Filter & sort, **Y** applies and closes and **B** cancels. D-pad/left-stick
up/down crosses library page edges; left/right in Activity changes weeks.

- **D-pad / left stick:** navigate games and controls.
- **A / south button:** select; on an editable text field, open the on-screen keyboard.
- **B / east button:** back; closes text entry before the underlying dialog.
- **LB / RB:** switch main screens at the root.
- **LT / RT:** switch local sections or shelves in Home, Library, Activity, Settings and game details.
- **X / west button:** play the selected installed game.
- **Y / north button:** the contextual action shown in the footer.
- **View / Back button:** search from browsing.
- **Menu / Start:** open the quick menu.
- **Right stick up / down:** scroll long content.

While the on-screen keyboard is open, **X** backspaces and **RT** presses Enter.
Enter closes the keyboard and sends Enter to a single-line field, or adds a newline to
a multiline field; **B** closes the keyboard without submitting.

Windows supports XInput controllers; Linux supports controllers exposed through readable
`/dev/input/js*` devices. Battery status appears when XInput supplies it. Input pauses while
Winnow is inactive, and held buttons must be released after reconnecting or returning from
a game. A fullscreen file browser supports controller selection. External launchers and
third-party authentication challenges retain their own input requirements.
Physical-controller compatibility and readability at your seating distance need device validation.

### Connecting platforms

`SETTINGS › PLATFORMS` (the gear at the foot of the rail) shows what each platform contributes.

| Platform | From local files | Adds when signed in |
|---|---|---|
| Steam | Installed games and locally recorded playtime/last played | Full owned list and play facts through an API key or browser sign-in; history and account-page imports through the supported connection |
| Epic | Cached owned titles and install state | Account inventory and acquisition dates |
| GOG | Galaxy ownership, available play facts and install state, with registry installation fallback | No GOG sign-in integration |

Steam purchase and licence import is in the Steam entry on that screen. Steam offers two ways
to connect, a Web API key and a browser sign-in, and they are alternatives rather than a
fallback pair — the screen explains the trade.

Epic sign-in opens Epic's own page in an embedded browser. A console flow (`--epic-login`) is
available as an alternative.

### Where your data lives

| | Path |
|---|---|
| Database | `%LOCALAPPDATA%\Winnow\winnow.db` |
| Cover cache | `%LOCALAPPDATA%\Winnow\covers\` |
| Your themes | `%LOCALAPPDATA%\Winnow\themes\` |

Built-in integrations make read-only requests to IGDB, Steam's public endpoints,
`gamesdb.gog.com` and `api.steamcmd.net`; the bundled SteamGridDB plugin requests its API and CDN.
Enabled third-party plugins may contact additional services. **Winnow reads launcher files
and does not write to them.**

Winnow encrypts stored credentials with Windows DPAPI (`CurrentUser` scope): Epic and Steam
sign-in sessions, the Steam Web API key, the optional Epic OAuth client secret, and the IGDB
client secret and cached access token, plus plugin credentials. Legacy plaintext credentials migrate on first read;
cleanup retries if an earlier migration was interrupted. A system that cannot encrypt refuses
to persist new credentials. It leaves legacy user-entered secrets untouched but unused, and
clears legacy machine-minted tokens. This migration updates settings rows; it does not scrub
old database backups or guarantee removal of historical bytes from disk. Public client ids
remain readable.

*Upgrading from Hoard?* The first launch moves `%LOCALAPPDATA%\Hoard\` to
`%LOCALAPPDATA%\Winnow\` automatically when it can complete safely; otherwise Winnow uses
the existing library in place.

### Optional: IGDB

Winnow works without it; a keyless Steam endpoint covers most titles. IGDB adds years,
publishers and genres. Open **Settings → Metadata & artwork → IGDB metadata** on desktop or
fullscreen. **Get IGDB credentials** opens the [Twitch developer console](https://dev.twitch.tv/console/apps).
Register a Confidential application and generate a client secret; see
[IGDB's setup instructions](https://api-docs.igdb.com/#account-creation) for the registration steps.
Enter the client ID and secret, then choose **Save credentials**. Changes take effect immediately. Saving
stores the secret through the platform secret protector; it does not validate it with Twitch.
The secret stays masked, and the field is cleared after saving. A background metadata refresh
runs after any startup sync or current credential refresh finishes, with progress in the titlebar.

**Remove saved credentials** removes only the pair stored by Winnow. Environment or local
configuration credentials remain available as a fallback, taking effect immediately.
If secure storage is unavailable, Winnow refuses to save the secret and explains the failure.

For development, environment variables remain supported:

```powershell
setx Igdb__ClientId     "your-client-id"
setx Igdb__ClientSecret "your-client-secret"
```

**Then open a new terminal** — environment variables are read at shell startup. Or use
`src/Winnow.App/appsettings.local.json` (gitignored).

### Optional: SteamGridDB

SteamGridDB ships as an enabled provider plugin. Existing credentials and cached artwork migrate
automatically from the earlier built-in integration.

Open **Settings → Plugins → SteamGridDB** to add your own
[SteamGridDB API key](https://www.steamgriddb.com/profile/preferences/api).
Saving protects the key on supported devices and queues background hero-artwork enrichment.
SteamGridDB matches known Steam app IDs, including Steam copies linked to other stores.
Games without that ID continue using the existing artwork sources. Cached artwork remains
available offline; Winnow also accepts `SteamGridDb__ApiKey` as an environment variable.

**Settings → Metadata & artwork** lets you reorder **High-resolution Steam heroes**, **SteamGridDB**
and **IGDB**, plus additional active artwork plugins, for backdrops. Saved per-game backgrounds always take priority. The order takes
effect on desktop and fullscreen without restarting; it does not change metadata field sources.

### Provider plugins

Plugins can import game libraries, add metadata and artwork, and supply recommendation shelves.
Open **Settings → Plugins → Open plugins folder**, copy a package into its own
directory, restart, enable it and restart again. Third-party plugins run trusted code with
Winnow's permissions; install packages from authors you trust. Settings are available on
desktop and fullscreen. See [plugin authoring and installation](docs/plugins.md) for the SDK,
supported data contracts, compatibility and current limits.

### Writing a theme

Drop a `.json` file in `%LOCALAPPDATA%\Winnow\themes\`. A complete theme is eight colours and
a few numbers; everything else is derived:

```json
{
  "schemaVersion": 1,
  "id": "bottle-green",
  "name": "Bottle green",
  "seeds": {
    "ground": "#0A140E", "surface": "#17291D", "text": "#F1F0E6",
    "flare": "#FF4D93", "volt": "#B4F24B", "amber": "#FFA83D",
    "azure": "#6FB8E8", "danger": "#E04B45"
  },
  "structure": { "edge": 1.75, "wellDepth": 0.5 },
  "defaults": { "transparency": 40, "backdrop": "acrylic", "layout": "floating" }
}
```

Winnow reports each theme's measured contrast so you can see the impact on readability. Broken
themes are skipped with a diagnostic. The app writes an annotated example on first run.

---

## For developers

### Stack

Avalonia 11 · .NET 10 · SQLite (Microsoft.Data.Sqlite + Dapper) · DbUp · CommunityToolkit.Mvvm.

### Module map

```
Winnow.Core           Domain records, repository interfaces, ingest contracts.
Winnow.Data           SQLite, Dapper, DbUp migrations, and the bucket queries.
Winnow.Ingest.*       Steam / Epic / GOG readers over local launcher files.
Winnow.Resolve        Candidates to Work and Release, with a confirmation queue.
Winnow.Enrich.*       IGDB, Steam store, steamcmd, GamesDB.
Winnow.PluginSdk      Public library, metadata, artwork and feed contracts.
Winnow.Plugins        Plugin discovery, activation, dependency loading and HTTP.
plugins/             Separately packaged providers, including SteamGridDB.
Winnow.Covers[.Igdb]  Cover art pipeline and disk cache.
Winnow.Monitor        Process watching and session recording.
Winnow.Recommend      The scoring model and the shelves.
Winnow.Auth.WebView   WebView2 host for embedded sign-in.
Winnow.App            Avalonia UI and the composition root. Assembly name `Winnow`.
```

What each module is allowed to do, and the boundaries between them, are in
[`game-library-design.md`](game-library-design.md) §5.1.

### Build and test

```powershell
dotnet build
dotnet test
```

GitHub Actions runs restore, dependency auditing, an analyzer-enabled Release build and all
tests on Windows for pushes to `main` and pull requests. Advisory warnings fail the restore,
including advisories on transitive packages. Test results are retained for seven days.
The workflow also verifies migration hashes against the previous push or pull-request base.
The contribution workflow requires the Windows and Linux session checks through an up-to-date
pull request. Repository settings enforce branch protection separately from the workflow file.

The separate **Release builds** workflow packages Windows and Linux x64 applications.
Branch/PR and manual runs keep installer artifacts; a `vX.Y.Z[-prerelease]` tag also runs
the CI gate and creates a draft GitHub Release with SHA-256 checksums after both package
smoke checks pass. See [release instructions](docs/releases.md) for builds and publication.

To check migration integrity locally:

```powershell
./scripts/Verify-Migrations.ps1 -BaselineRef HEAD
./scripts/Test-MigrationHashes.ps1
```

### Working on the UI

**The XAML previewer renders populated views.** In Rider, open any `.axaml` under
`src/Winnow.App/Views/` and choose *Editor and Preview*; the same works in Visual Studio's
Avalonia previewer. Every view assigns itself a design-time view model when it detects the
previewer, so the details modal, the cover wall, the feed and the settings screens all draw
with data rather than empty frames.

The data is a fabricated eight-game library in
[`src/Winnow.App/Design/`](src/Winnow.App/Design/PreviewData.cs) — real domain records folded
through the same code the SQLite read model uses, with no database, filesystem or network
touch. One game per rail bucket, a two-store game for the chip treatment, an unread patch,
and GOG patch notes for the expander. `tests/Winnow.Ui.Tests/DesignTimePreviewTests.cs`
attaches the preview data to every previewable view, so a change that breaks the preview
fails a test.

For click-through rather than pictures, run the app against a throwaway library:

```powershell
dotnet run --project src/Winnow.App -- --data-dir C:\Temp\winnow-play --seed-sample
```

`--data-dir` redirects the database, covers and sign-in state away from your real library;
`--seed-sample` fills it with demo games. `WINNOW_UI_CAPTURE_DIR=<dir>` makes the UI tests
drop rendered frames there, and `dotnet run -- --theme=<id> --open-library` style flags
(DEBUG builds) land the window on a state worth screenshotting.

No network calls: parser tests run against sanitized captures of real launcher files in
`tests/fixtures/`, and every HTTP client is tested against canned responses. Fixtures carry
fake account ids — sanitize anything you add.

If you have the app running, build to a scratch path so it doesn't fight the file lock:

```powershell
dotnet test -p:BaseOutputPath=C:\Temp\winnow-verify\
```

### Documentation by task

Read the page for the work you are doing. Current choices and the reasons needed to use them
are stated together; earlier plans and reviews are optional evidence.

| | |
|---|---|
| [`AGENTS.md`](AGENTS.md) | Contribution workflow, module layout, safe development runs and verification. |
| [`ROADMAP.md`](ROADMAP.md) | Delivered capabilities, deferred scope, remaining validation and carried debt. |
| [`game-library-design.md`](game-library-design.md) | The build spec: architecture, constraints, schema, entity resolution. |
| [`design-system.md`](design-system.md) | The visual spec. |
| [`docs/recommendation-engine.md`](docs/recommendation-engine.md) | The scoring model, every threshold, and why. |
| [`docs/facet-provenance.md`](docs/facet-provenance.md) | Where filter values and metadata come from. |
| [`docs/plugins.md`](docs/plugins.md) | Install or write a provider plugin. |
| [`docs/releases.md`](docs/releases.md) | Build, check and publish release packages. |
| [`docs/spikes/`](docs/spikes/) | Optional dated measurements and verification evidence. |

### Current limits

Settings → Library offers an acquisition CSV export with title, store, acquisition date,
licence and price paid. Missing values stay blank; prices are stored cents without a currency.

Full JSON export/import and Steam collection import remain deferred. Identity links apply
immediately and can be undone; install/uninstall actions delegate to the owning launcher.
Fullscreen has its own UI, with physical-controller and TV-distance validation still open.
[`ROADMAP.md`](ROADMAP.md) lists remaining scope and validation.

### A note on shipped credentials

`BuiltInEpicCredentialSource` supplies the launcher client credentials needed for Epic sign-in.
Users can sign in without registering a developer application. A configured user-supplied
client pair takes priority over the bundled pair.

---

## Licence

Not yet chosen.
