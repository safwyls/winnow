# Architecture review evidence — 2026-09-10

This records how the accompanying [architecture and code review](../architecture-review-2026-09-10.md) was checked. It is measurement evidence, not a new specification.

## Environment and isolation

- Repository: `C:\Users\safwyl\source\winnow`.
- Baseline commit: `ae2af9aab4a9cc31da92b73980ad46ccd536e5fa`, branch `codex/titlebar-fetch-progress`.
- Windows build10.0.26200, .NET SDK10.0.400, .NET runtime10.0.11.
- Build/test outputs: `artifacts/review-2026-09-10/`, ignored scratch output.
- Main recommendation/artwork reproductions used a scratch C# executable referencing current source projects and the existing recommendation test harness. Data/identity experiments used the current existing Debug net10.0 repository assemblies and fresh file-backed temporary databases; the source under review was unchanged between those binaries and this review's Release build.
- Database experiments executed all32 numbered migration files in order or used the existing temp-database harness. No production host, live API or launcher files were needed.
- The malformed-configuration check launched the built executable from an isolated directory with an explicit throwaway `--data-dir` and `--no-sync`. Its process was identified by its exact review command line and stopped after the unhandled failure entered crash reporting.

The pre-existing app view edit, local Claude settings, TASK-180 and `docs/api/` were preserved. No review experiment edited product code or a real library. Scratch binaries/logs are local execution artifacts, not required repository deliverables.

## Build, restore and tests

Executed from the repository root:

```powershell
dotnet build -c Release -p:BaseOutputPath=C:\Users\safwyl\source\winnow\artifacts\review-2026-09-10\bin\ -warnaserror

dotnet test -c Release --no-build -p:BaseOutputPath=C:\Users\safwyl\source\winnow\artifacts\review-2026-09-10\bin\ --logger trx --results-directory artifacts/review-2026-09-10/TestResults --blame-hang-timeout 5m --blame-hang-dump-type mini

./scripts/Verify-Migrations.ps1 -BaselineRef HEAD
./scripts/Test-MigrationHashes.ps1
./scripts/Test-TestResultSummary.ps1

dotnet restore --force-evaluate --no-cache -warnaserror
```

The initial sandboxed build could not read the user's NuGet configuration. Running the same requested verification with approved access succeeded; it was not left blocked.

| Check | Observed result |
| --- | --- |
| Windows Release build | Passed; 0 warnings, 0 errors;21.66 seconds |
| `Winnow.Tests` | 3,957 passed |
| `Winnow.Ui.Tests` | 288 passed |
| `Winnow.Recommend.Tests` | 160 passed |
| `Winnow.Covers.Tests` | 153 passed |
| `Winnow.Plugins.Tests` | 35 passed |
| `Winnow.Plugin.SteamGridDb.Tests` | 42 passed |
| `Winnow.Monitor.Linux.Tests` | 2 explicitly skipped on Windows |
| Test total | 4,635 passed;0 failed;2 skipped |
| Migration integrity | All32 migrations verified against HEAD |
| Migration mutation script | Passed matching hashes, CRLF normalization, modified SQL, unrecorded SQL, changed baseline and missing SQL cases |
| Test-result summary script | Passed |
| Fresh no-cache restore | Passed all29 projects with configured auditing and warnings as errors |

TRX files remain under the scratch TestResults directory. These measurements establish the state of existing automated coverage. New review findings were deliberately not implemented, so there was no product-code change to retest.

## Identity and persistence experiments

These are normalized minimal recipes for executed experiments. Use a fresh initialized SQLite database per independent case and real repositories constructed with `new SqliteConnectionFactory(tempDatabasePath, false)`. Inspect rows through a separate connection after each awaited operation. Fake IDs below have no relation to an actual account.

### R01: undo after reparenting

Seed works1=A,2=B,3=C, then:

```csharp
var links = new IdentityLinkRepository(factory);
await links.LinkAsync(new IdentityLinkRequest {
    ParentWorkId = 2, ChildWorkIds = new long[] { 1 }
});
await links.LinkAsync(new IdentityLinkRequest {
    ParentWorkId = 3, ChildWorkIds = new long[] { 2 }
});
await links.RetractLinkAsync(1);
var resolution = await links.GetResolutionAsync();
```

Observed live links after undo: `1→2, 2→3`. `SameGame.Resolve(1)=2`, `Resolve(2)=3`, `Resolve(3)=3`. Before undo, the second link had correctly flattened the graph to `1→3, 2→3`.

A second independent history used X31/Y32→P30, then X31/Y32→Q33, then X31→R34. Retracting the middle act threw:

```text
UNIQUE constraint failed: identity_links.child_work_id
```

That failing whole-act undo rolled back and left `31→34, 32→33`. It demonstrates failure after partial supersession, **not** partial mutation of that failed transaction.

Inspect live graph rows with:

```sql
SELECT child_work_id,parent_work_id,kind
FROM identity_links
WHERE retracted_at IS NULL
ORDER BY child_work_id;
```

An additional expansion-admission experiment seeded base70 ← expansion71, then supplied metadata proposing addon72→71. The scanner emitted a proposal the repository's depth rule rejects. This is a proposal/write-contract mismatch; the experiment did not force that invalid proposal into storage.

### R02: value/provenance and pin/work atomicity

No caller unit of work surrounds these operations. That is essential: the claimed defect is that public standalone operations lack an atomic guarantee.

For a work10, reject the provenance statement:

```sql
CREATE TRIGGER review_fail_source
BEFORE INSERT ON work_field_sources
WHEN NEW.work_id = 10
BEGIN
    SELECT RAISE(ABORT,'injected source failure');
END;
```

Then call:

```csharp
await new WorkFieldSourceRepository(factory)
    .SetFieldAsync(10, "summary", "User edit");
```

Observed after the exception: `works.summary='User edit'`; zero `work_field_sources` rows for `work_id=10 AND field='summary'`.

For a separate work11, seed `works.igdb_id=111` and an uncleared pin111. Reject the later work projection write:

```sql
CREATE TRIGGER review_fail_pin_projection
BEFORE UPDATE ON works
WHEN OLD.id = 11
BEGIN
    SELECT RAISE(ABORT,'injected work update failure');
END;
```

Then call:

```csharp
await new WorkIgdbPinRepository(factory).PinAsync(
    new WorkIgdbPinAssignment {
        WorkId = 11, IgdbId = 222, Name = "Replacement"
    });
```

Observed after the exception: work mapping111, live pin222, old pin111 cleared. Trigger names/error text above are normalized for reproduction; their original names/text were not retained. The injected statement failures establish the transaction gap, not the probability of a particular physical disk failure.

### R03: manual correction after pinning

Create a manual draft with title, IGDB333 and Steam123. Pin that same work to IGDB333 with year2001. Update the manual entry to a new title, IGDB444, Steam456 and year2020 through `ManualEntryRepository.UpdateAsync`.

Observed:

```text
updated = true
works.igdb_id = 444
works.first_release_year = 2020
live work_igdb_pins.igdb_id = 333
first_release_year source = igdb
external_ids = igdb:333, igdb:444, steam:123, steam:456
```

This directly establishes contradictory pin/mapping/provenance and retained old assertions. An incorrect future hard join is a source-supported consequence, not a separately executed live ingest incident.

### R10: confirmed account identity without a complete inventory

Seed two Steam ownerships and only local account observations:

| Ownership | Account | Source | Minutes |
| --- | --- | --- | --- |
|20, game A |11111, Mine |steam_local |10 |
|21, game B |22222, housemate |steam_local |10 |

Set `library.account_scope=own` and `steam.owned_account_ref=11111`. There is no authoritative complete inventory and no row attesting Mine's ownership of B.

Call `GetOwnershipBucketsAsync(new BucketThresholds(120,6000,6))`. Observed: only ownership20 is returned. The premise that Mine also owns B is deliberately unknown to the database; the defect is inferring B's absence from positive local evidence about A.

The state is reachable because `SteamSignInService` can confirm account identity before a complete owned-library fetch. This experiment does not assume an unconfirmed account activates Own scope and did not query a real user's entitlement.

### R09: unread pushes before and after last play

Seed a played ownership with130 minutes, last play2025-01-01, and correlated build/announcement pairs dated2024 and2026. Leave acknowledgement unset. Query the derived ownership buckets.

Observed: `stale_but_patched` with `update_count=2`. Only the2026 push occurred since play. The separately reported missing production acknowledgement wiring was established through source inspection, not a UI click experiment.

### R11: unsupported future migration journal

Initialize all32 current migrations, add a synthetic applied journal entry named `Winnow.Data.Migrations.9999_future_schema.sql`, then run the current initializer.

Observed: initialization succeeded. No unknown future DDL was applied. The experiment establishes absence of a compatibility guard, not corruption under a particular future schema.

## Recommendation and artwork experiments

A scratch C# executable referenced current Data, Recommend and Covers projects plus the existing `RecommendHarness` and temp-database helper. It ran separate initialized databases per recommendation scenario. Fixed harness request time was used for scoring.

Observed console output:

```text
Cold shelfware: candidates=1, shelves=0, flatFeedCards=1
Installed sibling: candidates=1, shelves=0, cards=0
Dismissed linked child, all accounts: cards=0
Dismissed linked child, own accounts: dismissedParentReturned=True
Expired cover marker: diskMissing=False, pipelineMissing=True
Repeat transient-null cover lease: cache calls=1
```

### R20: production shelf cold start

Seed one never-played, uninstalled owned game without taste facets/history. Compare `Engine.GetShelvesAsync(request)` and `Engine.GetFeedAsync(request)`. Both see the eligible game, but only the flat API returns a card. Production FeedService calls the shelf API.

### R19: installed sibling

Seed an uninstalled Steam parent and an installed Epic child with no play history; link them as same-game. The resolved game is counted as one candidate, but no Ready to play shelf/card is produced. This isolates installation being read from the primary ownership instead of the available grouped copy.

### R21: dismissal hidden by account filtering

Seed an Epic parent and a Steam child, each with300 minutes and last play two years before the request. Link them. Record a NotInterested verdict against the child release; in all-account scope the flat feed returns no cards.

Give the child an other-account Steam Web membership. Set Mine's confirmed account and add another Steam game with Mine's Steam Web membership so Own filtering is active. Switch to Own and reload with the same verdict request.

Observed: the visible Epic parent returns. The hidden child is absent from the candidate-derived resolution map. This case uses a complete-looking Steam Web membership to isolate feedback identity; the separate R10 experiment addresses the validity of inventory evidence itself.

### R22: missing marker and retained null lease

For pipeline expiry, create a disk cache with a one-day negative TTL and a fake cover source returning null. Fetch once, then set the negative marker's modification time to two days ago. The disk lookup expires/removes the marker and reports false; the same pipeline still reports true because its memory entry has no expiry.

For lease retry, acquire a lease from a fake ICoverCache that counts calls and returns null. Call the same retained lease twice. The cache is called once. No successful image decode is required to establish the retry defect.

R37's queue/disposal stress risks were source inspected but were **not** stress reproduced by these two tests.

## R06: coherent GOG snapshot experiment

This is an executed synthetic Python/SQLite filesystem experiment modeling `GalaxyDatabaseSnapshot`'s copy ordering. It does not invoke the C# class or access Galaxy.

```python
import pathlib, shutil, sqlite3, tempfile

root = pathlib.Path(tempfile.mkdtemp(prefix="winnow-review-gog-"))
live, snapshot = root / "live.db", root / "snapshot.db"
db = sqlite3.connect(live)
db.execute("PRAGMA journal_mode=WAL")
db.execute("PRAGMA wal_autocheckpoint=0")
db.execute("CREATE TABLE a(value TEXT)")
db.execute("CREATE TABLE b(value TEXT)")
db.execute("INSERT INTO a VALUES ('old')")
db.execute("INSERT INTO b VALUES ('old')")
db.commit()
db.execute("PRAGMA wal_checkpoint(TRUNCATE)")

# Copy the old main database, as the production copy does first.
shutil.copyfile(live, snapshot)

# Source commit and checkpoint before sidecars are copied.
db.execute("UPDATE a SET value='committed'")
db.execute("UPDATE b SET value='committed'")
db.commit()
db.execute("PRAGMA wal_checkpoint(TRUNCATE)")
db.execute("UPDATE a SET value='newest'")
db.commit()

for suffix in ("-wal", "-shm"):
    shutil.copyfile(str(live) + suffix, str(snapshot) + suffix)

copied = sqlite3.connect(
    "file:" + snapshot.as_posix() + "?mode=ro", uri=True)
print("quick_check:", copied.execute("PRAGMA quick_check").fetchone()[0])
print("snapshot:", copied.execute("SELECT * FROM a,b").fetchall())
print("live:", db.execute("SELECT * FROM a,b").fetchall())
copied.close()
db.close()
```

Observed:

```text
quick_check: ok
snapshot: [('newest', 'old')]
live: [('newest', 'committed')]
```

The only committed source states were old/old, committed/committed and newest/committed. The accepted snapshot invents newest/old. Structural SQLite checks cannot prove that separately copied main/WAL files describe one source transaction state.

## R26: bootstrap configuration boundary

Create `artifacts/review-2026-09-10/bad-config/appsettings.json` containing intentionally invalid JSON:

```text
{ invalid-json-for-isolated-startup-boundary-check
```

With that directory as the working directory, launch:

```powershell
dotnet C:\Users\safwyl\source\winnow\artifacts\review-2026-09-10\bin\Release\net10.0\Winnow.dll --data-dir C:\Users\safwyl\source\winnow\artifacts\review-2026-09-10\bad-config\data --no-sync
```

Observed: an unhandled JSON configuration exception through `Host.CreateApplicationBuilder`, ending at `Program.Main:69`. Host construction precedes the application's startup try/catch. The app never reached data-directory selection or UI startup.

The process remained in crash reporting and was stopped after exact command-line verification. No normal exit code was observed; this record does not invent one. The task requires an executable-level regression for the documented2/3 exit contract.

## Unexecuted validation

Steam/Epic cache-account defects, IGDB in-flight mapping races, list/filter/fullscreen wiring issues, plugin feed latency and session restart lifecycle were traced in source. They are not claimed as real-account, hardware or crash-frequency measurements.

This review did not perform live API calls or sign-in, production library interactions, physical controller/ten-foot verification, Linux process tests, installer smoke runs, pixel/screenshot QA, or website build/deployment. Follow-up tasks specify the missing deterministic regressions; existing platform/release tasks retain broader environment verification.
