# Winnow — game library manager (Avalonia, .NET 10)

Local-first desktop app that surfaces forgotten games in large Steam/Epic/GOG libraries
("your library has unread mail"). No server, no accounts.

## The name, and the word it is not

The product, the assembly, the binary and the mascot (a dragon) are all **Winnow**. It was
called Hoard until 2026-08-28.

**"hoard" survives as an English word and must not be replaced.** The premise of the app is
*winnowing a hoard* — the dragon's pile of a thousand unplayed games — so the common noun is
load-bearing, not a leftover. Four places use it deliberately and a search-and-replace over
them is a regression:

- `design-system.md` §2 "a library about your own hoard", §9 "what a hoard of them looks
  like", §11.3 "look like the whole hoard"
- `src/Winnow.App/Views/ActionBarView.axaml` — the same sentence as the last of those

Anything hyphenated or possessive — `Winnow-launched`, `Winnow's own`, `Winnow-owned`, "a
Winnow theme" — is the product and is already renamed.

**Three compatibility shims exist because the rename crossed the user's data.** None are
decoration; each one is load-bearing for an install that predates the rename:

- `WinnowDataLocation` (`src/Winnow.App/Services/`) moves `%LOCALAPPDATA%\Hoard` to
  `%LOCALAPPDATA%\Winnow` once, sidecars and subdirectories included, and **falls back to
  reading the legacy directory in place** if the move cannot be completed. It must never end
  up pointing at an empty new directory.
- `DatabaseInitializer.RenameLegacyJournalEntries` re-points DbUp's `SchemaVersions` rows
  from `Hoard.Data.Migrations.*` to `Winnow.Data.Migrations.*`. DbUp keys applied scripts by
  embedded-resource name, which carries the root namespace, so without this every shipped
  migration replays against a populated database and `0001` dies on `table works already
  exists` before the window opens.
- `WinnowThemes.LegacyDefaultId` maps the stored `appearance.theme = hoard` onto the
  `winnow` theme, after the catalogue is consulted so an authored theme may still claim the
  old id.

## Authority documents — read before changing anything they govern
- `ROADMAP.md` — current scope, phase order and identity. Supersedes the design doc's §8
  milestones and amends its §1 non-goals; read it BEFORE the design doc so you know which
  parts of §1 still bind.
- `game-library-design.md` — the build spec. §4 hard constraints and §5.1 module
  boundaries are non-negotiable; §9 lists the known failure modes.
- `design-system.md` + `tokens.axaml` — visual spec. Flare (#FF5C8A) marks ONLY unread
  updates; all numbers render in IBM Plex Mono `tnum`. Root `tokens.axaml` is the design
  RECORD; the compiling copy is `src/Winnow.App/Themes/tokens.axaml` — change tokens there.
  Fonts are static OFL cuts (Avalonia 11 has no variable-axis API); see
  `src/Winnow.App/Assets/Fonts/README.md`.
- `docs/spikes/` — empirical verification results that OVERRIDE spec guesses
  (e.g. exact `localconfig.vdf` key names/units, Avalonia dormancy rendering approach).

## Layout
- `src/Winnow.Core` — domain records, repository interfaces, ingest contract. No IO, BCL only.
- `src/Winnow.Data` — SQLite via Microsoft.Data.Sqlite + Dapper; DbUp migrations as embedded
  `Migrations/NNNN_*.sql` (append-only, never edit shipped ones). Derived buckets are
  queries, never stored columns.
- `src/Winnow.Ingest.Steam` — read-only readers over Steam's local files (ValveKeyValue,
  never hand-rolled VDF). Emits `CandidateOwnership`; must never write works/releases.
- `src/Winnow.Resolve` — maps candidates to Work/Release. Hard joins only auto-merge;
  fuzzy matches queue for user confirmation, never auto-merge.
- `src/Winnow.App` — Avalonia 11 UI + generic-host composition root (assembly name `Winnow`
  to match `avares://Winnow/...`). UI reads the DB and raises commands; never calls
  ingest/enrichment directly.
- `tests/Winnow.Tests` — xUnit on temp-file SQLite dbs; parser tests use the sanitized
  real fixtures in `tests/fixtures/steam/`.

## Conventions
- Domain agents live in `.claude/agents/` — delegate work by domain and pass their charter.
- `Directory.Build.props`: nullable, implicit usings, TreatWarningsAsErrors.
- Build/test: `dotnet build`, `dotnet test` from repo root. Run: `dotnet run --project
  src/Winnow.App` (`-- --seed-sample` seeds demo data).
- Commits at milestone boundaries; DB lives at `%LOCALAPPDATA%\Winnow\winnow.db`.
- Never write to any Steam-owned file. Sanitize any new fixture (fake account ids).

<!-- BACKLOG.MD GUIDELINES START -->
<!-- backlog.md-instructions-version: 1.50.1 -->
<CRITICAL_INSTRUCTION>

## Backlog.md Workflow

This project uses Backlog.md for task and project management.

**For every user request in this project, run `backlog instructions overview` before answering or taking action.**

Use the overview to decide whether to search, read, create, or update Backlog tasks.

Before task lifecycle actions, read the matching detailed guide:
- `backlog instructions task-creation` before creating or splitting tasks
- `backlog instructions task-execution` before planning, changing status or assignee, adding a plan or implementation notes, or implementing task work
- `backlog instructions task-finalization` before checking acceptance criteria, writing final summaries, or moving tasks to terminal statuses

Use `backlog <command> --help` before running unfamiliar commands. Help shows options, fields, and examples.

Do not edit Backlog task, draft, document, decision, or milestone markdown files directly. Use the `backlog` CLI so metadata, relationships, and history stay consistent.

</CRITICAL_INSTRUCTION>
<!-- BACKLOG.MD GUIDELINES END -->
