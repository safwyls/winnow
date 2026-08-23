# Hoard — game library manager (Avalonia, .NET 10)

Local-first desktop app that surfaces forgotten games in large Steam/Epic/GOG libraries
("your library has unread mail"). No server, no accounts.

## Authority documents — read before changing anything they govern
- `game-library-design.md` — the build spec. §4 hard constraints and §5.1 module
  boundaries are non-negotiable; §9 lists the known failure modes.
- `design-system.md` + `tokens.axaml` — visual spec. Flare (#FF5C8A) marks ONLY unread
  updates; all numbers render in IBM Plex Mono `tnum`.
- `docs/spikes/` — empirical verification results that OVERRIDE spec guesses
  (e.g. exact `localconfig.vdf` key names/units, Avalonia dormancy rendering approach).

## Layout
- `src/Hoard.Core` — domain records, repository interfaces, ingest contract. No IO, BCL only.
- `src/Hoard.Data` — SQLite via Microsoft.Data.Sqlite + Dapper; DbUp migrations as embedded
  `Migrations/NNNN_*.sql` (append-only, never edit shipped ones). Derived buckets are
  queries, never stored columns.
- `src/Hoard.Ingest.Steam` — read-only readers over Steam's local files (ValveKeyValue,
  never hand-rolled VDF). Emits `CandidateOwnership`; must never write works/releases.
- `src/Hoard.Resolve` — maps candidates to Work/Release. Hard joins only auto-merge;
  fuzzy matches queue for user confirmation, never auto-merge.
- `src/Hoard.App` — Avalonia 11 UI + generic-host composition root (assembly name `Hoard`
  to match `avares://Hoard/...`). UI reads the DB and raises commands; never calls
  ingest/enrichment directly.
- `tests/Hoard.Tests` — xUnit on temp-file SQLite dbs; parser tests use the sanitized
  real fixtures in `tests/fixtures/steam/`.

## Conventions
- Domain agents live in `.claude/agents/` — delegate work by domain and pass their charter.
- `Directory.Build.props`: nullable, implicit usings, TreatWarningsAsErrors.
- Build/test: `dotnet build`, `dotnet test` from repo root. Run: `dotnet run --project
  src/Hoard.App` (`-- --seed-sample` seeds demo data).
- Commits at milestone boundaries; DB lives at `%LOCALAPPDATA%\Hoard\hoard.db`.
- Never write to any Steam-owned file. Sanitize any new fixture (fake account ids).
