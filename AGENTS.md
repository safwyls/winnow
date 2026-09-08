# Winnow — how work is done in this repository

Local-first desktop app that surfaces forgotten games in large Steam/Epic/GOG libraries
("your library has unread mail"). No server, no accounts.

The product, the assembly, the binary and the mascot (a dragon) are all **Winnow**.

## Where to read

One document owns each domain. Read the one that governs what you are about to change; none
of them defers to another, and none of them outranks another.

| Domain | Document |
|---|---|
| How work is done here: naming, layout, build, run, test, commit, delegation, Backlog | `AGENTS.md` (this file) |
| Product scope, phase order, exit criteria, what is excluded and what is deferred | `ROADMAP.md` |
| The build spec: architecture, module boundaries, external services, entity resolution, schema, derived buckets, session detection | `game-library-design.md` |
| The visual spec: palette, type, layout, dormancy, components, copy, accessibility, themes | `design-system.md` |
| Token values | `src/Winnow.App/Themes/tokens.axaml` |
| The scoring model: signals, weights, thresholds, cold start, explainability | `docs/recommendation-engine.md` |
| Where each filter value comes from | `docs/facet-provenance.md` |
| Orientation for a new reader: what it is, how to install, run and build | `README.md` |
| Evidence: how something was measured | `docs/spikes/` |
| Per-domain agent charters | `.codex/agents/` (Codex), `.claude/agents/` (Claude Code) |

If a document is wrong, edit it to the current truth in the same commit as the change that
made it wrong, and append the sentence it used to say to `docs/decisions.md`. Do not leave a
correction sitting next to the text it corrects.

## The name, and the word it is not

**"hoard" survives as an English word and must not be replaced.** Four places use it
deliberately and a search-and-replace over them is a regression:

- `design-system.md` §2 "a library about your own hoard", §9 "what a hoard of them looks
  like", §11.3 "look like the whole hoard"
- `src/Winnow.App/Views/ActionBarView.axaml` — the same sentence as the last of those

Anything hyphenated or possessive — `Winnow-launched`, `Winnow's own`, `Winnow-owned`, "a
Winnow theme" — is the product and is already renamed.

## Compatibility shims that must not be removed

Each one is load-bearing for an install that predates the 2026-08-28 rename.

- `WinnowDataLocation` (`src/Winnow.App/Services/`) moves `%LOCALAPPDATA%\Hoard` to
  `%LOCALAPPDATA%\Winnow` once, sidecars and subdirectories included, and falls back to
  reading the legacy directory in place if the move cannot be completed. It must never end up
  pointing at an empty new directory.
- `DatabaseInitializer.RenameLegacyJournalEntries` re-points DbUp's `SchemaVersions` rows
  from `Hoard.Data.Migrations.*` to `Winnow.Data.Migrations.*`.
- `WinnowThemes.LegacyDefaultId` maps the stored `appearance.theme = hoard` onto the `winnow`
  theme, after the catalogue is consulted so an authored theme may still claim the old id.

## Layout

- `src/Winnow.Core` — domain records, repository interfaces, ingest contract. No IO, BCL only.
- `src/Winnow.Data` — SQLite via Microsoft.Data.Sqlite + Dapper; DbUp migrations as embedded
  `Migrations/NNNN_*.sql`, append-only, never edit shipped ones. Derived buckets are queries,
  never stored columns.
- `src/Winnow.Ingest.Steam`, `src/Winnow.Ingest.Epic`, `src/Winnow.Ingest.Gog` — read-only
  readers over each launcher's local files. Parse VDF with ValveKeyValue, never a hand-rolled
  parser. Emit `CandidateOwnership`; never write works or releases.
- `src/Winnow.Resolve` — maps candidates to Work and Release. Hard external-id joins
  auto-merge; fuzzy matches queue for user confirmation and never auto-merge.
- `src/Winnow.Enrich.*` — external metadata clients. Rate-limited, cached, soft-failing.
- `src/Winnow.Covers`, `src/Winnow.Covers.Igdb` — cover fetch and disk cache.
- `src/Winnow.Monitor` — process watching and session recording.
- `src/Winnow.Recommend` — the scoring model. No IO beyond repositories; references
  `Winnow.Core` only.
- `src/Winnow.Auth.WebView` — embedded sign-in. References Avalonia and `Winnow.Core` only.
- `src/Winnow.App` — Avalonia 11 UI plus the generic-host composition root. Assembly name is
  `Winnow`, to match `avares://Winnow/...`. The UI reads the database and raises commands; it
  never calls ingest or enrichment directly.
- `tests/Winnow.Tests` — xUnit on temp-file SQLite databases. Parser tests use the sanitized
  real fixtures in `tests/fixtures/steam/`.
- `tests/Winnow.Ui.Tests` — isolated Avalonia headless pointer and keyboard tests with real
  fonts and templates. Use temporary data and never start the production host.

## Agent instructions and writing

`AGENTS.md` is the shared entry point. `CLAUDE.md` imports it. Codex loads the TOML roles in
`.codex/agents/`; Claude Code uses the matching Markdown roles in `.claude/agents/`. Keep
paired role instructions equivalent when editing them. Roles inherit the selected model;
do not pin a model unless the user requests it.

Each agent writes the documentation, UI copy and comments required by its work. There is no
separate prose author and no prose handoff required to finish a change.

- Write clear, concise sentences with familiar words and active verbs. Lead with the result
  or fact; add the explanation needed to understand it. Use lists for actual steps or parallel
  items, and omit filler, sales language and repeated summaries.
- Verify behavior in the source before documenting it. State limitations and distinguish
  measured results from assumptions. Preserve an existing document's structure and voice.
- Keep rules in the document that owns the domain, historical rationale in `docs/decisions.md`,
  measurement methods in `docs/spikes/`, and delivery status in `ROADMAP.md` or Backlog.
- Comments explain constraints or intent that the code cannot show. Avoid narrating the next
  line or describing the history of a diff. UI copy follows `design-system.md`.
- Report what changed, what was checked and any remaining limitation. Scale detail to the work.

The frontend-design skill is available in `.agents/skills/` for Codex and `.claude/skills/`
for Claude Code. Keep both copies aligned. Winnow's visual spec and tokens govern app changes.
Historical plans and decision records provide context, not additional active agent instructions.

The Codex Backlog hook checks direct `apply_patch` edits; the Claude hook checks direct file
edits. Neither guards every possible shell or tool write. The Backlog CLI rule below applies
to all editing methods. If `backlog` is missing from PATH, check the installed npm executable
(on Windows, `%APPDATA%\npm\backlog.cmd`) before installing anything. If it cannot run,
report the limitation and leave Backlog files untouched.

## Conventions

- Use domain agents for bounded work that benefits from delegation. Keep small changes local.
  Give each agent its scope, owned files and relevant charter; concurrent agents must preserve
  one another's edits. The coordinating agent owns integration and verification.
- `Directory.Build.props` sets nullable, implicit usings and `TreatWarningsAsErrors`.
- Build and test with `dotnet build` and `dotnet test` from the repository root.
- CI runs Windows Release restore/build/test on pushes and pull requests, with SDK analyzers
  and direct/transitive NuGet auditing enabled; warnings fail the gate. Windows CI
  and Linux session checks are required by `main` branch protection. Changes reach `main`
  through an up-to-date pull request; this also applies to administrators. No additional
  approving reviewer is required. Force pushes and branch deletion are disabled. Migration integrity
  uses `scripts/Verify-Migrations.ps1`. For a new migration, append its SHA-256 to
  `src/Winnow.Data/Migrations/hashes.json`: UTF-8 text without BOM, CRLF normalized to LF,
  all other whitespace retained. Never replace an existing entry. Verify with
  `./scripts/Verify-Migrations.ps1 -BaselineRef HEAD`; mutation tests are in
  `scripts/Test-MigrationHashes.ps1`.
- Windows CI prints completed tests and retains TRX plus hang diagnostics. A five-minute
  test inactivity timeout captures a mini dump so a stalled host can be investigated.
- A separate Ubuntu CI job runs `tests/Winnow.Monitor.Linux.Tests` against real native
  processes and a synthetic Proton environment. Those tests explicitly skip on non-Linux hosts.
- Release packaging lives in `packaging/`; `docs/releases.md` owns build and publication
  instructions. Tag releases must pass the reusable CI gate and both installer smoke checks
  before creating a draft release. Smoke scripts install only on disposable GitHub runners.
- Run with `dotnet run --project src/Winnow.App`. `-- --seed-sample` seeds demo data.
- **For any run where you might click something, pass `-- --data-dir <path>`** to redirect the
  database, sidecars, covers, themes and WebView2 profile to a throwaway directory. Otherwise
  clicks write to the real library. An unusable path is refused at startup with exit code 2;
  it never falls back silently. Setting `%LOCALAPPDATA%` does not work, because
  `Environment.GetFolderPath` uses the Windows shell API and ignores it.
- A startup failure — migrations, hosted services, framework initialization — is caught,
  logged and shown to the user (on the console when there is one, otherwise a message box),
  and leaves exit code 3, distinct from the `--data-dir` refusal's 2.
- If the app is running it holds a lock on the output assemblies. Build to a scratch path
  instead: `dotnet test -p:BaseOutputPath=C:\Temp\winnow-verify\`.
- Commit at milestone boundaries. The database lives at `%LOCALAPPDATA%\Winnow\winnow.db`.
- Never write to any Steam, Epic or GOG file. Copy before reading anything live.
- Sanitize any new fixture with fake account ids.

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
