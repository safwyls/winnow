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
- **Buckets that mean something.** *Never played* means you haven't opened it. *Started*
  means you're past Steam's two-hour refund window.
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

The window opens as soon as the local scan finishes, about a second. Titles, cover art and
update signals fill in behind it.

### First run

The first run does the most work — scanning launcher files, creating a record per game, then
fetching titles and covers. Give it a minute or two.

**`Patched` grows over the first week.** The update poller spreads its sweep across seven
days, so a game enters that bucket when its slot comes up.

### Fullscreen and controllers

Choose **Fullscreen** at the foot of the rail, press **F11**, or press the controller's
**Menu / Start** button. Fullscreen has its own For you, Library, Activity, Settings and
game-detail screens, designed for a TV. Press F11 or choose **Exit fullscreen** in the
controller's quick menu to restore the previous window state. Desktop and fullscreen retain
separate browsing positions, filters and appearance preferences.

Fullscreen Appearance includes **Fit ultrawide displays** to use the full screen width
without stretching text. Controller input hides the cursor; moving or clicking the mouse
restores it. In Filter & sort, **Y** applies and closes and **B** cancels. D-pad/left-stick
up/down crosses library page edges; left/right in Activity changes weeks.

- **D-pad / left stick:** navigate games and controls.
- **A / south button:** select; on an editable text field, open the on-screen keyboard.
- **B / east button:** back; closes text entry before the underlying dialog.
- **LB / RB:** switch main screens; switch local sections on a game page.
- **LT / RT:** page through games or activity.
- **X / west button:** play the selected installed game.
- **Y / north button:** the contextual action shown in the footer.
- **View / Back button:** search from browsing.
- **Menu / Start:** open the quick menu.
- **Right stick up / down:** scroll long content.

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
| Steam | Installed games, playtime, last played | Full owned list *(needs an API key)* |
| Epic | Owned titles, install state | Acquisition dates |
| GOG | Everything Winnow needs | Not needed |

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

Nothing leaves the machine except read-only requests to IGDB, Steam's public endpoints,
`gamesdb.gog.com` and `api.steamcmd.net`. **Winnow reads launcher files and does not write to
them.**

Winnow encrypts stored credentials with Windows DPAPI (`CurrentUser` scope): Epic and Steam
sign-in sessions, the Steam Web API key, the optional Epic OAuth client secret, and the IGDB
client secret and cached access token. Legacy plaintext credentials migrate on first read;
cleanup retries if an earlier migration was interrupted. A system that cannot encrypt refuses
to persist new credentials. It leaves legacy user-entered secrets untouched but unused, and
clears legacy machine-minted tokens. This migration updates settings rows; it does not scrub
old database backups or guarantee removal of historical bytes from disk. Public client ids
remain readable.

*Upgrading from Hoard?* The first launch moves `%LOCALAPPDATA%\Hoard\` to
`%LOCALAPPDATA%\Winnow\` automatically.

### Optional: IGDB

Winnow works without it; a keyless Steam endpoint covers most titles. IGDB adds years,
publishers and genres. Get a client ID and secret from
[dev.twitch.tv](https://dev.twitch.tv/console/apps):

```powershell
setx Igdb__ClientId     "your-client-id"
setx Igdb__ClientSecret "your-client-secret"
```

**Then open a new terminal** — environment variables are read at shell startup. Or use
`src/Winnow.App/appsettings.local.json` (gitignored).

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
tests on Windows for every push and pull request. Advisory warnings fail the restore,
including advisories on transitive packages. Test results are retained for seven days.
The workflow also verifies migration hashes against the previous push or pull-request base.
Repository administrators can require the `Windows build, tests and migration integrity`
check in branch protection; the workflow file itself does not configure that setting.

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

### Where to read further

One document owns each domain, and [`AGENTS.md`](AGENTS.md) carries the full list.

| | |
|---|---|
| [`AGENTS.md`](AGENTS.md) | How work is done here: layout, conventions, and traps that already cost real debugging. Start here before changing anything. |
| [`ROADMAP.md`](ROADMAP.md) | Scope, phase order, exit criteria, and carried debt. |
| [`game-library-design.md`](game-library-design.md) | The build spec: architecture, constraints, schema, entity resolution. |
| [`design-system.md`](design-system.md) | The visual spec. |
| [`docs/recommendation-engine.md`](docs/recommendation-engine.md) | The scoring model, every threshold, and why. |
| [`docs/decisions.md`](docs/decisions.md) | Why things are the way they are, and what was reversed. |
| [`docs/spikes/`](docs/spikes/) | Evidence: how a thing was measured. |

### What isn't built

Settings → Library offers an acquisition CSV export with title, store, acquisition date,
licence and price paid. Missing values stay blank; prices are stored cents without a currency.

Merge *execution* (the queue records intent; nothing applies it), full JSON export/import and install
management remain deferred. Fullscreen has its own UI; physical controller and TV-distance
validation remain open. [`ROADMAP.md`](ROADMAP.md) §5 lists the
carried debt against its backlog tasks.

### A note on shipped credentials

`BuiltInEpicCredentialSource` carries Epic's launcher client id and secret — the same approach
Legendary, Heroic and Playnite use. They sit at the lowest priority in the credential chain,
so a user-supplied pair always wins. The reasoning is in
[`docs/decisions.md`](docs/decisions.md).

---

## Licence

Not yet chosen.
