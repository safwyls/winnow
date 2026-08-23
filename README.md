# Hoard

A local-first game library manager that surfaces the games you own, meant to play,
and forgot existed. No server, no account, no telemetry.

Current state: **M0–M2 complete** — Steam ingest, IGDB/Steam enrichment, cover art,
entity resolution with a merge confirm queue, playtime snapshots, and update-aware
staleness detection ("Patched since").

## Running it

Requires the **.NET 10 SDK** (`dotnet --list-sdks` should show `10.0.x`).

```powershell
cd c:\Users\safwyl\source\hoard
dotnet run --project src/Hoard.App
```

The window opens as soon as the local Steam scan finishes (about a second).
Everything else — titles, cover art, update signals — fills in behind it, which is
deliberate: §7 of the design system promises a browsable library immediately.

### First run vs. later runs

The **first** run on an empty database does the most work: it scans Steam's local
files, creates a record per game, then fetches titles and covers. Give it a minute
or two before judging what you see. Later runs are near-instant and only fetch what
changed.

`Patched since` **grows over the first week.** The update poller deliberately
spreads its sweep across 7 days to stay light on Valve's endpoints and on the
volunteer `api.steamcmd.net` service, so a game only enters the bucket once its
slot comes up. Day one shows a fraction of what week two will.

### Flags

| Flag | Effect |
|---|---|
| *(none)* | Normal run: scan, enrich, poll, sweep |
| `--no-sync` | Open the UI against the existing database and write nothing. Best for looking at the interface without waiting |
| `--seed-sample` | Fill an *empty* database with ~40 fake titles spanning every bucket, plus merge candidates. Debug builds only |
| `--open-queue` | Land on the merge confirm queue instead of the library. Debug builds only |

Pass them after `--`:

```powershell
dotnet run --project src/Hoard.App -- --no-sync
```

### What to look for

- **The grid** — dormant games are desaturated and cooled; hovering a tile restores
  it to full colour over 140ms. That fade *is* the dormancy encoding.
- **`Patched since`** in the rail — games that got a real update after you last
  played. Tiles carry a pink dot. That colour appears nowhere else in the app.
- **`Bounced off`** — the pile the whole product exists to surface: games you
  started, put down, and forgot.
- **`Same game?`** under REVIEW — pairs the resolver could not tell apart. Nothing
  merges unless you answer. (Answers are recorded, but the record-merging step
  itself is not built yet.)
- **Keyboard**: `/` focuses search, arrows move the selection, `Esc` clears.

## Where your data lives

| | Path |
|---|---|
| Database | `%LOCALAPPDATA%\Hoard\hoard.db` |
| Cover cache | `%LOCALAPPDATA%\Hoard\covers\` |

Nothing leaves the machine except read-only requests to IGDB, Steam's public
endpoints, and `api.steamcmd.net`. Hoard **never writes to any Steam file.**

To start over, delete the database (the cover cache can stay — it will be reused):

```powershell
Remove-Item "$env:LOCALAPPDATA\Hoard\hoard.db*"
```

## IGDB (optional)

Hoard works without it — a keyless Steam endpoint covers most titles. IGDB is the
better source and adds years, publishers and genres, which also sharpens duplicate
detection. Get a client ID and secret from [dev.twitch.tv](https://dev.twitch.tv/console/apps),
then:

```powershell
setx Igdb__ClientId     "your-client-id"
setx Igdb__ClientSecret "your-client-secret"
```

Open a **new** terminal afterwards. Credentials are read from the environment or
from `src/Hoard.App/appsettings.local.json` (gitignored); they are never logged and
never committed.

## Tests

```powershell
dotnet test
```

474 tests. Parser tests run against sanitized captures of real Steam files in
`tests/fixtures/`; every HTTP client is tested against canned responses, so the
suite makes no network calls.

## Layout

See [CLAUDE.md](CLAUDE.md) for the module map and conventions, `game-library-design.md`
for the build specification, `design-system.md` for the visual one, and
`docs/spikes/` for empirically verified findings that override both where they
disagree.
