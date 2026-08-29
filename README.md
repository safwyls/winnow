# Winnow

**A local-first game library manager that surfaces the games you own, meant to play, and
forgot existed.** No server, no account, no telemetry.

Large PC libraries accumulate games you meant to play and forgot about. Storefronts don't
track the signals that matter: how long a game sat unopened, whether you bounced off it,
whether it's been patched since you last tried. Winnow does.

---

## For users

### What it does

- **One library across Steam, Epic and GOG.** Read from local launcher files, with
  optional sign-in where a store's API knows things its files don't.
- **A feed that says why.** Every recommendation carries a sentence — *"You put 2.8 hours
  into this in 2021 and it has had an update since, most recently 'PATCH NOTES – S06.05.02'."*
  Not a genre tag, not a star rating. The reason is the product.
- **Buckets that mean something.** *Never played* is under 2 hours (Steam's refund window).
  *Bounced off* is above that — you tried it and stopped.
- **Patch tracking.** Games updated since you last played them.
- **Launch and session tracking.** Click Play; the game starts and nothing else happens.
  Winnow records when you actually played, which storefronts never retain.
- **Themes and transparency**, including a drop-in JSON theme format.

### Install and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/safwyls/winnow.git
cd winnow
dotnet run --project src/Winnow.App
```

Windows only in practice — Epic and GOG discovery uses the registry, credentials use DPAPI,
and session detection is Windows-shaped. It builds elsewhere; it will find less.

The window opens as soon as the local scan finishes, about a second. Titles, cover art and
update signals fill in behind it.

### First run

The first run does the most work — scanning launcher files, creating a record per game, then
fetching titles and covers. Give it a minute or two.

**`Patched` grows over the first week.** The update poller spreads its sweep across seven
days, so a game enters that bucket when its slot comes up.

### Connecting stores

`SETTINGS › STORES` (the gear at the foot of the rail) shows what each platform contributes.

| Platform | From local files | Adds when signed in |
|---|---|---|
| Steam | Installed games, playtime, last played | Full owned list *(needs an API key)* |
| Epic | Owned titles, install state | Playtime and acquisition dates |
| GOG | Everything Winnow needs | Not needed |

Epic sign-in opens Epic's own page in an embedded browser. A console flow (`--epic-login`)
is available as an alternative.

### Where your data lives

| | Path |
|---|---|
| Database | `%LOCALAPPDATA%\Winnow\winnow.db` |
| Cover cache | `%LOCALAPPDATA%\Winnow\covers\` |
| Your themes | `%LOCALAPPDATA%\Winnow\themes\` |

Nothing leaves the machine except read-only requests to IGDB, Steam's public endpoints,
`gamesdb.gog.com` and `api.steamcmd.net`. Credential protection is uneven today: Epic refresh
tokens are encrypted at rest with DPAPI (`CurrentUser` scope), but Steam Web API keys and IGDB
client secrets are stored as plaintext rows in the local database. Anyone with access to
`winnow.db` can read those two. Encrypting them the same way is tracked as future work.
**Winnow never writes to any Steam, Epic or GOG file.**

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

**Then open a new terminal** — environment variables are read at shell startup.
Or use `src/Winnow.App/appsettings.local.json` (gitignored).

### Writing a theme

Drop a `.json` file in `%LOCALAPPDATA%\Winnow\themes\`. A complete theme is eight colours
and a few numbers; everything else is derived:

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

Winnow reports each theme's measured contrast so you can see the impact on readability.
Broken themes are skipped with a diagnostic. The app writes an annotated example on first
run.

---

## For developers

### Stack

Avalonia 11 · .NET 10 · SQLite (Microsoft.Data.Sqlite + Dapper) · DbUp · CommunityToolkit.Mvvm.
No EF Core — the SQL is meant to stay legible.

### Module map

```
Winnow.Core           Domain records, repository interfaces, ingest contracts. BCL only, no IO.
Winnow.Data           SQLite, Dapper, DbUp migrations. Derived buckets are QUERIES, never columns.
Winnow.Ingest.*       Steam / Epic / GOG. Read-only over launcher files. Emit CandidateOwnership.
Winnow.Resolve        Candidates → Work/Release. Hard id joins auto-merge; fuzzy matches queue.
Winnow.Enrich.*       IGDB, Steam store, steamcmd, GamesDB. Rate-limited, cached, soft-failing.
Winnow.Covers[.Igdb]  Cover art pipeline; first source that answers wins.
Winnow.Monitor        Process watching and session recording.
Winnow.Recommend      The scoring model and shelves. No IO beyond repositories.
Winnow.Auth.WebView   WebView2 host for embedded sign-in. References Avalonia + Core only.
Winnow.App            Avalonia UI and the composition root. Assembly name `Winnow`.
```

### Key constraints

- **Four-layer identity: Work → Release → Ownership → PlayRecord.** Never collapse Release
  into Work. Skyrim SE is not Skyrim; the achievement sets differ.
- **Never auto-merge on a fuzzy title.** Hard external-id joins only; everything else queues
  for confirmation. This is the failure that makes people leave.
- **Derived buckets are queries, not stored columns.** Thresholds get tuned; stored values rot.
- **A source's silence is not an answer.** A field a source cannot provide arrives `null`,
  never `false` or `0`.
- **Migrations are append-only.** Embedded `NNNN_*.sql`; never edit a shipped one.
- **Never write to a Steam, Epic or GOG file.** Copy before reading anything live.

### Build and test

```powershell
dotnet build
dotnet test
```

**1,737 tests.** No network calls: parser tests run against sanitized captures of real
launcher files in `tests/fixtures/`, and every HTTP client is tested against canned
responses. Fixtures carry fake account ids — sanitize anything you add.

If you have the app running, build to a scratch path so it doesn't fight the file lock:

```powershell
dotnet test -p:BaseOutputPath=C:\Temp\winnow-verify\
```

### Documentation, in precedence order

| | |
|---|---|
| [`ROADMAP.md`](ROADMAP.md) | Current scope and phase order. Supersedes the design doc's milestones. |
| [`game-library-design.md`](game-library-design.md) | The build spec. §4 constraints and §5.1 boundaries are non-negotiable. |
| [`design-system.md`](design-system.md) | The visual spec, and the measurements behind it. |
| [`docs/spikes/`](docs/spikes/) | **Empirical findings that override both.** |
| [`docs/recommendation-engine.md`](docs/recommendation-engine.md) | The scoring model, every threshold, and why. |
| [`CLAUDE.md`](CLAUDE.md) | Module map, conventions, and traps that already cost real debugging. |

`docs/spikes/` outranks the specs because several assumptions in the specs turned out to be
wrong — verified findings are in the spike documents.

### What isn't built

Merge *execution* (the queue records intent; nothing applies it), GDPR export import,
JSON/CSV export, install management, and full-screen gamepad navigation. See `ROADMAP.md`
§6 for carried debt with the reasoning intact.

### A note on shipped credentials

`BuiltInEpicCredentialSource` carries Epic's launcher client id and secret — the same
approach Legendary, Heroic and Playnite use. They sit at the lowest priority in the
credential chain, so a user-supplied pair always wins. See `docs/spikes/epic-oauth.md`.

---

## Licence

Not yet chosen.
