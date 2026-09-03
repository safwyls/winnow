# Migration plan: one source of truth per domain

Status: **executed 2026-09-02.** This file is now a record of the migration rather than a
proposal. Steps 0 to 13 and 15 to 17 were applied; step 14 was cancelled by the user, who
chose to keep `notes.md` as it is, so it sits outside the destination set. Where the shipping
code disagreed with a disposition below, the code won and the difference is recorded in
`docs/decisions.md`. Read that file for the outcome; this one is kept for the inventory.

Scope: `README.md`, `CLAUDE.md`, `AGENTS.md`, `ROADMAP.md`, `game-library-design.md`,
`design-system.md`, `notes.md`, `docs/spikes/`, `.claude/agents/`, `.codex/agents/`.

Problem being solved: normative content is spread across ten locations behind a conditional
precedence chain (ROADMAP supersedes design §8 and amends §1, but §4 and §5.1 still bind,
and spikes override both). Several documents carry superseded text next to its correction.
An agent has to reconcile all of that per task, and reconciles it inconsistently.

## How to read this

- **Phase 1 and Phase 2 share one table per source file.** Phase 1 asks for file, anchor,
  verbatim claim and overlap; Phase 2 asks for a classification of the same claim. Two
  four-hundred-row tables would double the reading cost and let the two drift apart, so the
  class is a fifth column. Phase 2 below carries the tie-breaks, the rule/rationale splits,
  the UNRESOLVED list and the counts.
- **Quotes are verbatim** from the source, trimmed to the operative sentence. An ellipsis
  marks a trim. Where a table *is* the claim (the palette, the type scale, the dormancy ramp)
  the row names the table and quotes its governing sentence.
- **IDs are stable** and every one is accounted for in a Phase 6 step. The coverage map at
  the end of Phase 6 is the check.
- Conflict references (K-nn) point into Phase 3. UNRESOLVED references (U-nn) point into the
  Phase 2 UNRESOLVED list. Neither is resolved here.

Classification key:

| | |
|---|---|
| **RULE** | Currently binding. An agent must obey it on future work |
| **DECISION** | Why something is the way it is. Historical, not binding |
| **FINDING** | An empirical result: a spike, a measurement, an observed fact |
| **DEAD** | Superseded, reversed, or describing state that no longer exists |
| **UNRESOLVED** | Cannot be classified from the documents alone. Adjudication needed |

---

# PHASE 1 + 2 — Inventory and classification

## 1.1 `CLAUDE.md` (103 lines)

| ID | Anchor | Verbatim claim | Also claimed in | Class |
|---|---|---|---|---|
| CL-01 | preamble | "Local-first desktop app that surfaces forgotten games in large Steam/Epic/GOG libraries ("your library has unread mail"). No server, no accounts." | AGENTS.md, README, ROADMAP §1/§3, design §1 | RULE |
| CL-02 | §The name | "The product, the assembly, the binary and the mascot (a dragon) are all **Winnow**." | AGENTS.md, design header, ROADMAP §1 | RULE |
| CL-03 | §The name | "It was called Hoard until 2026-08-28." | AGENTS.md, ROADMAP §1, design header, README | DECISION |
| CL-04 | §The name | ""hoard" survives as an English word and must not be replaced." | AGENTS.md, docs-writer charters | RULE |
| CL-05 | §The name | "The premise of the app is *winnowing a hoard* ... so the common noun is load-bearing, not a leftover." | AGENTS.md, ROADMAP §1, design header | DECISION |
| CL-06 | §The name | "Four places use it deliberately and a search-and-replace over them is a regression: `design-system.md` §2 ..., §9 ..., §11.3 ...; `src/Winnow.App/Views/ActionBarView.axaml`" | AGENTS.md, backlog TASK-34 | RULE |
| CL-07 | §The name | "Anything hyphenated or possessive ... is the product and is already renamed." | AGENTS.md | RULE |
| CL-08 | §The name | "`WinnowDataLocation` ... moves `%LOCALAPPDATA%\Hoard` to `%LOCALAPPDATA%\Winnow` once, sidecars and subdirectories included, and **falls back to reading the legacy directory in place** if the move cannot be completed. It must never end up pointing at an empty new directory." | AGENTS.md, ROADMAP §1, README | RULE |
| CL-09 | §The name | "`DatabaseInitializer.RenameLegacyJournalEntries` re-points DbUp's `SchemaVersions` rows from `Hoard.Data.Migrations.*` to `Winnow.Data.Migrations.*`." | AGENTS.md, ROADMAP §1 | RULE |
| CL-10 | §The name | "DbUp keys applied scripts by embedded-resource name, which carries the root namespace, so without this every shipped migration replays against a populated database and `0001` dies on `table works already exists` before the window opens." | AGENTS.md | DECISION |
| CL-11 | §The name | "`WinnowThemes.LegacyDefaultId` maps the stored `appearance.theme = hoard` onto the `winnow` theme, after the catalogue is consulted so an authored theme may still claim the old id." | AGENTS.md | RULE |
| CL-12 | §Authority | "`ROADMAP.md` ... Supersedes the design doc's §8 milestones and amends its §1 non-goals; read it BEFORE the design doc so you know which parts of §1 still bind." | AGENTS.md, README table, ROADMAP header, design §8, docs-writer charters | RULE (target: DEAD) |
| CL-13 | §Authority | "`game-library-design.md` ... §4 hard constraints and §5.1 module boundaries are non-negotiable; §9 lists the known failure modes." | AGENTS.md, README, ROADMAP header, winnow-reviewer | RULE |
| CL-14 | §Authority | "Flare (#FF5C8A) marks ONLY unread updates" | AGENTS.md, avalonia-ui charters; design-system §2 gives `#FF4D93` | RULE; the hex is DEAD (K-01) |
| CL-15 | §Authority | "all numbers render in IBM Plex Mono `tnum`" | AGENTS.md, design-system §3, avalonia-ui, winnow-reviewer | RULE |
| CL-16 | §Authority | "Root `tokens.axaml` is the design RECORD; the compiling copy is `src/Winnow.App/Themes/tokens.axaml` — change tokens there." | AGENTS.md | RULE |
| CL-17 | §Authority | "Fonts are static OFL cuts (Avalonia 11 has no variable-axis API)" | AGENTS.md; contradicted by design-system §3 | RULE + FINDING (K-02) |
| CL-18 | §Authority | "`docs/spikes/` — empirical verification results that OVERRIDE spec guesses" | AGENTS.md, README, docs-writer charters | RULE (target: DEAD) |
| CL-19 | §Layout | "`src/Winnow.Core` — domain records, repository interfaces, ingest contract. No IO, BCL only." | AGENTS.md, README module map | RULE |
| CL-20 | §Layout | "`src/Winnow.Data` — SQLite via Microsoft.Data.Sqlite + Dapper; DbUp migrations as embedded `Migrations/NNNN_*.sql` (append-only, never edit shipped ones)." | AGENTS.md, README, design §3/§6, data-layer | RULE |
| CL-21 | §Layout | "Derived buckets are queries, never stored columns." | AGENTS.md, README, design §5.1/§6.1/§9, data-layer, winnow-reviewer, recommendation-engine | RULE |
| CL-22 | §Layout | "`src/Winnow.Ingest.Steam` — read-only readers over Steam's local files (ValveKeyValue, never hand-rolled VDF). Emits `CandidateOwnership`; must never write works/releases." | AGENTS.md, README, design §4.1/§5.1/§9, steam-ingest, winnow-reviewer | RULE |
| CL-23 | §Layout | "`src/Winnow.Resolve` — maps candidates to Work/Release. Hard joins only auto-merge; fuzzy matches queue for user confirmation, never auto-merge." | AGENTS.md, README, design §5.1/§5.3/§9, winnow-reviewer | RULE |
| CL-24 | §Layout | "`src/Winnow.App` — Avalonia 11 UI + generic-host composition root (assembly name `Winnow` to match `avares://Winnow/...`)." | AGENTS.md, README | RULE |
| CL-25 | §Layout | "UI reads the DB and raises commands; never calls ingest/enrichment directly." | AGENTS.md, README, design §5, avalonia-ui, winnow-reviewer | RULE |
| CL-26 | §Layout | "`tests/Winnow.Tests` — xUnit on temp-file SQLite dbs; parser tests use the sanitized real fixtures in `tests/fixtures/steam/`." | AGENTS.md, README, steam-ingest, recommendation-engine | RULE |
| CL-27 | §Conventions | "Domain agents live in `.claude/agents/` — delegate work by domain and pass their charter." | AGENTS.md says `.Codex/agents/` | RULE (K-03) |
| CL-28 | §Conventions | "`Directory.Build.props`: nullable, implicit usings, TreatWarningsAsErrors." | AGENTS.md, recommendation-engine, `Directory.Build.props` | RULE |
| CL-29 | §Conventions | "Build/test: `dotnet build`, `dotnet test` from repo root." | AGENTS.md, README, winnow-reviewer, recommendation-engine | RULE |
| CL-30 | §Conventions | "Run: `dotnet run --project src/Winnow.App` (`-- --seed-sample` seeds demo data)." | AGENTS.md, README | RULE |
| CL-31 | §Conventions | "For any run where you might click something, pass `-- --data-dir <path>` to redirect the database, sidecars, covers, themes and WebView2 profile to a throwaway directory" | absent from AGENTS.md | RULE |
| CL-32 | §Conventions | "clicks write to the real library otherwise (this has already happened)" | absent from AGENTS.md | DECISION |
| CL-33 | §Conventions | "An unusable path is refused at startup with exit code 2; it never falls back silently." | absent from AGENTS.md | RULE |
| CL-34 | §Conventions | "Setting `%LOCALAPPDATA%` does not work, `Environment.GetFolderPath` uses the Windows shell API and ignores it." | absent from AGENTS.md | FINDING |
| CL-35 | §Conventions | "Commits at milestone boundaries" | AGENTS.md | RULE |
| CL-36 | §Conventions | "DB lives at `%LOCALAPPDATA%\Winnow\winnow.db`." | AGENTS.md, README | RULE |
| CL-37 | §Conventions | "Never write to any Steam-owned file." | AGENTS.md, README, design §4.1, steam-ingest, winnow-reviewer | RULE |
| CL-38 | §Conventions | "Sanitize any new fixture (fake account ids)." | AGENTS.md, README, steam-ingest, winnow-reviewer | RULE |
| CL-39 | §Backlog.md | "**For every user request in this project, run `backlog instructions overview` before answering or taking action.**" | AGENTS.md | RULE |
| CL-40 | §Backlog.md | "Before task lifecycle actions, read the matching detailed guide: `backlog instructions task-creation` ... `task-execution` ... `task-finalization`" | AGENTS.md | RULE |
| CL-41 | §Backlog.md | "Use `backlog <command> --help` before running unfamiliar commands." | AGENTS.md | RULE |
| CL-42 | §Backlog.md | "Do not edit Backlog task, draft, document, decision, or milestone markdown files directly. Use the `backlog` CLI" | AGENTS.md | RULE |

## 1.2 `AGENTS.md` (98 lines)

`AGENTS.md` lines 1-74 are a near-verbatim copy of `CLAUDE.md` lines 1-79. Rather than
restate forty duplicate rows, this table carries one row for the duplication and one row per
divergence. The duplicated claims are inventoried above as CL-01..CL-30 and CL-35..CL-42.

| ID | Anchor | Verbatim claim | Also claimed in | Class |
|---|---|---|---|---|
| AG-01 | whole file | Lines 1-74 duplicate `CLAUDE.md` lines 1-79 word for word except AG-02 and AG-03; lines 76-98 duplicate the Backlog block | CLAUDE.md | DEAD (duplicate) |
| AG-02 | §Conventions | "Domain agents live in `.Codex/agents/`" (CLAUDE.md says `.claude/agents/`; the directory on disk is `.codex/agents/`, lower case) | CL-27 | DEAD (K-03) |
| AG-03 | §Conventions | AGENTS.md omits the `--data-dir` paragraph, the exit-code-2 sentence and the `%LOCALAPPDATA%` finding CLAUDE.md carries | CL-31, CL-33, CL-34 | DEAD (omission, K-04) |

## 1.3 `README.md` (206 lines)

| ID | Anchor | Verbatim claim | Also claimed in | Class |
|---|---|---|---|---|
| RM-01 | preamble | "A local-first game library manager ... No server, no account, no telemetry." | CL-01, ROADMAP §3, design §1 | RULE |
| RM-02 | preamble | "Storefronts don't track the signals that matter: how long a game sat unopened, whether you bounced off it, whether it's been patched since you last tried. Winnow does." | ROADMAP §1, design §1 | DECISION |
| RM-03 | What it does | "**One library across Steam, Epic and GOG.** Read from local launcher files, with optional sign-in where a store's API knows things its files don't." | design §1 goals, ROADMAP §4 | RULE |
| RM-04 | What it does | "Every recommendation carries a sentence ... Not a genre tag, not a star rating. The reason is the product." | `docs/recommendation-engine.md`, recommendation-engine charter, ROADMAP M8 | RULE |
| RM-05 | What it does | "*Never played* is under 2 hours (Steam's refund window). *Bounced off* is above that" | contradicts design §6.1 | DEAD (K-05) |
| RM-06 | What it does | "**Launch and session tracking.** Click Play; the game starts and nothing else happens." | design §5.2, ROADMAP M3b | RULE |
| RM-07 | Install and run | "Requires the [.NET 10 SDK]" | design §3, CLAUDE.md | RULE |
| RM-08 | Install and run | "Windows only in practice — Epic and GOG discovery uses the registry, credentials use DPAPI, and session detection is Windows-shaped. It builds elsewhere; it will find less." | ROADMAP §6, spikes | FINDING |
| RM-09 | Install and run | "The window opens as soon as the local scan finishes, about a second." | ROADMAP §6 `SteamSyncService` split | FINDING |
| RM-10 | First run | "**`Patched` grows over the first week.** The update poller spreads its sweep across seven days" | design-system §10.4, `docs/spikes/update-signals.md` | FINDING |
| RM-11 | Connecting platforms | Platform contribution table: Steam local "Installed games, playtime, last played", signed-in "Full owned list *(needs an API key)*"; Epic local "Owned titles, install state", signed-in "Playtime and acquisition dates"; GOG local "Everything Winnow needs" | `docs/spikes/epic-gog-local-files.md`, ROADMAP GOG note | FINDING |
| RM-12 | Connecting platforms | "Epic sign-in opens Epic's own page in an embedded browser. A console flow (`--epic-login`) is available as an alternative." | ROADMAP M4.6 exit criterion | RULE |
| RM-13 | Where your data lives | Path table: db `%LOCALAPPDATA%\Winnow\winnow.db`, covers `\covers\`, themes `\themes\` | CL-36 | RULE |
| RM-14 | Where your data lives | "Nothing leaves the machine except read-only requests to IGDB, Steam's public endpoints, `gamesdb.gog.com` and `api.steamcmd.net`." | design §4.2-§4.5, ROADMAP §4.7 conditions | RULE |
| RM-15 | Where your data lives | "Epic refresh tokens are encrypted at rest with DPAPI (`CurrentUser` scope), but Steam Web API keys and IGDB client secrets are stored as plaintext rows in the local database." | ROADMAP §4.7 second amendment condition 2 | FINDING (K-06) |
| RM-16 | Where your data lives | "Encrypting them the same way is tracked as future work." | no matching item in ROADMAP §6 or the backlog | UNRESOLVED (U-01) |
| RM-17 | Where your data lives | "**Winnow never writes to any Steam, Epic or GOG file.**" | CL-37, design §4.1, spikes | RULE |
| RM-18 | Optional: IGDB | "Winnow works without it; a keyless Steam endpoint covers most titles." | `docs/spikes/steam-store-tags.md` | FINDING |
| RM-19 | Optional: IGDB | "`setx Igdb__ClientId` / `Igdb__ClientSecret` ... Or use `src/Winnow.App/appsettings.local.json` (gitignored)." | design §4.4 | RULE |
| RM-20 | Optional: IGDB | "**Then open a new terminal** — environment variables are read at shell startup." | — | FINDING |
| RM-21 | Writing a theme | "Drop a `.json` file in `%LOCALAPPDATA%\Winnow\themes\`. A complete theme is eight colours and a few numbers; everything else is derived", with the example carrying `schemaVersion`, `seeds`, `structure`, `defaults` | design-system §14.1 | RULE |
| RM-22 | Writing a theme | "Winnow reports each theme's measured contrast ... Broken themes are skipped with a diagnostic. The app writes an annotated example on first run." | design-system §14.3, §16.6 | RULE |
| RM-23 | Stack | "Avalonia 11 · .NET 10 · SQLite (Microsoft.Data.Sqlite + Dapper) · DbUp · CommunityToolkit.Mvvm. No EF Core — the SQL is meant to stay legible." | design §3, §3.1, data-layer | RULE |
| RM-24 | Module map | Ten-module map with per-module musts: Core "BCL only, no IO"; Data "Derived buckets are QUERIES, never columns"; `Ingest.*` "Read-only over launcher files. Emit CandidateOwnership"; Resolve "Hard id joins auto-merge; fuzzy matches queue"; `Enrich.*` "Rate-limited, cached, soft-failing"; `Covers[.Igdb]` "first source that answers wins"; Monitor "Process watching and session recording"; Recommend "No IO beyond repositories"; `Auth.WebView` "References Avalonia + Core only"; App "Assembly name `Winnow`" | CL-19..CL-25, design §5.1 (five rows only) | RULE, superset (K-07) |
| RM-25 | Key constraints | "**Four-layer identity: Work → Release → Ownership → PlayRecord.** Never collapse Release into Work. Skyrim SE is not Skyrim; the achievement sets differ." | design §5.3, §9, data-layer | RULE |
| RM-26 | Key constraints | "**Never auto-merge on a fuzzy title.** Hard external-id joins only ... This is the failure that makes people leave." | CL-23, design §5.3, §9 | RULE |
| RM-27 | Key constraints | "**A source's silence is not an answer.** A field a source cannot provide arrives `null`, never `false` or `0`." | unique to README | RULE |
| RM-28 | Key constraints | "**Never write to a Steam, Epic or GOG file.** Copy before reading anything live." | CL-37; the "copy before reading" half is otherwise only in `docs/spikes/epic-gog-local-files.md` §11 | RULE |
| RM-29 | Build and test | "**1,737 tests.**" | ROADMAP M5 says "2,111 tests passing" | DEAD (K-08) |
| RM-30 | Build and test | "No network calls: parser tests run against sanitized captures ... and every HTTP client is tested against canned responses." | CL-26, enrichment-api, steam-ingest | RULE |
| RM-31 | Build and test | "If you have the app running, build to a scratch path ... `dotnet test -p:BaseOutputPath=C:\Temp\winnow-verify\`" | — | RULE |
| RM-32 | Documentation, in precedence order | Six-row precedence table naming ROADMAP, the design doc, design-system, spikes, `docs/recommendation-engine.md`, CLAUDE.md | CL-12, CL-13, CL-18 | RULE (target: DEAD) |
| RM-33 | Documentation | "`docs/spikes/` outranks the specs because several assumptions in the specs turned out to be wrong" | CL-18 | DECISION |
| RM-34 | What isn't built | "Merge *execution* (the queue records intent; nothing applies it), GDPR export import, JSON/CSV export, install management, and full-screen gamepad navigation." | ROADMAP §6, §4 | FINDING; the "GDPR export import" item is DEAD (K-09) |
| RM-35 | Shipped credentials | "`BuiltInEpicCredentialSource` carries Epic's launcher client id and secret ... They sit at the lowest priority in the credential chain, so a user-supplied pair always wins." | ROADMAP §3, `docs/spikes/epic-oauth.md` | RULE |
| RM-36 | Licence | "Not yet chosen." | — | FINDING |

## 1.4 `ROADMAP.md` (414 lines)

| ID | Anchor | Verbatim claim | Also claimed in | Class |
|---|---|---|---|---|
| RD-01 | header | "Supersedes §8 of `game-library-design.md` for sequencing. The hard constraints in §4 and the module boundaries in §5.1 are unchanged and still binding." | CL-12, CL-13, RM-32, design §8 note | RULE (target: DEAD) |
| RD-02 | header | "Where this document contradicts §1's non-goals, the amendment is stated explicitly in §3 below — nothing is quietly dropped." | design §1 | RULE (target: DEAD) |
| RD-03 | §1 | "**Winnow is the library that remembers.**" | README preamble | DECISION |
| RD-04 | §1 | "Renamed from Hoard on 2026-08-28, mascot included ... the app's premise is **winnowing a hoard**, so the old name named the problem and the new one names the work." | CL-03, CL-05, design header | DECISION |
| RD-05 | §1 | "The three compatibility shims the rename needed ... each is load-bearing for an install that predates the rename rather than tidy-up that can be deleted." | CL-08..CL-11 | DECISION |
| RD-06 | §1 | "Storefronts discard that. Winnow keeps it. That is the whole asset." | RM-02, design §1 | DECISION |
| RD-07 | §1 | ""Analytics tool" undersells it ... But "launcher" oversells it in a different direction: Playnite already is a mature open-source launcher ... The position that is actually defensible is the intersection." | — | DECISION |
| RD-08 | §2 | "So the launcher is not a feature bolted on for adoption. **It is the data-acquisition strategy for the differentiator.**" | — | DECISION |
| RD-09 | §2 | "**M3 was already the launcher.** ... Actually *starting* a game is a URI handoff (`steam://rungameid/440`) and is nearly free" | design-system §10.3, design §5.2 | DECISION |
| RD-10 | §2 | "**M5 was already the cold-start fix.** Historical playtime backfill ... gives the recommender a real longitudinal series on install day." | recommendation-engine charter, `docs/recommendation-engine.md` §6 | DECISION |
| RD-11 | §3 amendments | "Recommendation engine (phase 2) | **Promoted to core** ... Phase-2 placement assumed it needed a server; it does not — all inference is local" | design §1 non-goals (struck through), §8 Phase 2 | RULE + DECISION (split) |
| RD-12 | §3 amendments | "Any hosted service, user accounts, multi-user | **Unchanged** ... **Winnow has no accounts; Winnow links yours.**" | CL-01, RM-01, design §1 | RULE |
| RD-13 | §3 amendments | "3D "games on a shelf" view (cut, §11) | **Still cut** ... §11's framework reasoning does not apply and is not reopened." | design §11 | RULE + DECISION (split) |
| RD-14 | §3 amendments | "Shipping storefront client credentials | **Decided 2026-08-26: ship them built-in** ... Heroic, Legendary and the Playnite plugins all embed them." | RM-35, `docs/spikes/epic-oauth.md` §10 | RULE + DECISION (split); the §1 non-goal it amends does not exist (K-10) |
| RD-15 | §3 amendments | "PSN / Xbox (§4.6) | **Unchanged — still excluded** ... Epic OAuth is not a precedent for these." | design §4.6, §9 | RULE |
| RD-16 | §3 amendments | "§4.7 no-scraping rule | **Amended 2026-08-28** | M5's saved-HTML importer gains an embedded-WebView peer route" | design §4.7 | DEAD (superseded in part by RD-24, K-11) |
| RD-17 | §3 amendments | "§4.7 no-scraping rule | **Amended again 2026-08-30** | Condition 1 of the 2026-08-28 amendment ... is superseded ... Eight binding conditions below." | design §4.7 | RULE |
| RD-18 | §4 | "Numbering continues from §8. M0–M2 and M4 are shipped." | design §8 | FINDING |
| RD-19 | §4 table | Twelve-row phase table with exit criteria and state: M4.5 shipped, M7 "**shipped** (unwired by design)", M3a shipped, M4.6 shipped, M11 "**shipped** — unplanned", GOG sign-in "not scheduled", M3b "**shipped** — `winnow-wrap` (§5.2 B) deliberately deferred", M8 "**shipped** — no dismiss/snooze yet", M5 "**built** ... exit criterion half-proven", M6 "**deferred** 2026-08-31; exit criterion to be restated", M9 "after M6", M10 "last" | design §8 milestone table | FINDING (state) + RULE (exit criteria and order) |
| RD-20 | §4 M11 note | "Recorded as a milestone because it is a system now ... **not** because it was planned." | design-system §14-§16 | DECISION |
| RD-21 | §4 M11 note | "**A measurement discipline for colour.** `Colorimetry` walks AA ceilings per theme, per layout, per slider position, and the Appearance screen prints the number live." | design-system §14.3, §16.7 | RULE |
| RD-22 | §4 M11 note | "The cost is equally plain: **it is polish shipped ahead of M3b and M8**" | — | DECISION |
| RD-23 | §4 M4.6 note | "the authorization code is single-use and expires in minutes ... An embedded browser reads the code the instant the provider issues it" | `docs/spikes/epic-oauth.md`, `docs/spikes/embedded-auth.md` | DECISION |
| RD-24 | §4 GOG note | "Galaxy's database holds **45 owned GOG releases, of which 31 are DLC** ... The local reader is reading the entire library correctly, and the missing-games premise was invented rather than measured." | `docs/spikes/epic-gog-local-files.md` §13/§15 | FINDING; supersedes the "only 14 games" claim (DEAD) |
| RD-25 | §4 GOG note | "The authenticated GOG endpoint then carries **no playtime, no last-played, no title and no DLC flag**" | `docs/spikes/embedded-auth.md` §5/§6 | FINDING |
| RD-26 | §4 GOG note | "`GET gameplay.gog.com/.../sessions` exists and accepts GET (PUT/DELETE answer 405) ... Until someone looks, it stays held." | `docs/spikes/embedded-auth.md` | FINDING + RULE (the reopening condition) |
| RD-27 | §4 M5 note | "there is no downloadable archive. Valve's Privacy Dashboard is a set of login-gated live pages ... The export-file mechanism cannot backfill historical playtime because the data it was supposed to contain does not exist in that form." | `docs/spikes/steam-gdpr-export.md` §1-§3 | FINDING; design §5.4 is DEAD as a result (K-09) |
| RD-28 | §4 M5 note | "**`IPlayerService/ClientGetLastPlayedTimes`** for `first_playtime` per app. One call, existing key." | `docs/spikes/steam-gdpr-export.md` §4 | RULE |
| RD-29 | §4 M5 note | "**`ISaleFeatureService/GetUserYearInReview`** for years 2022 onward ... This is the actual cold-start fix. Auth verified 2026-08-28" | `docs/spikes/steam-gdpr-export.md` §4 | RULE + FINDING |
| RD-30 | §4 M5 note | "**A saved-HTML importer** for the account licenses and purchase-history pages only ... Explicitly not building a general importer over the ~100 dashboard pages." | `docs/spikes/steam-gdpr-export.md` §8 | RULE |
| RD-31 | §4 M5 note | "The exit criterion is unchanged ... The mechanism changed, not the goal." | design §8 M5 | DECISION |
| RD-32 | §4 M5 note | "The observation-identity foundations (review findings F10/F19) made play records and snapshots idempotent on their full fact." | `docs/code-review-2026-08-28.md` | FINDING |
| RD-33 | §4.7 first amendment | Condition 1: "**Ephemeral session.** The WebView uses an in-private, in-memory profile. Cookies are never persisted to disk." | superseded by RD-36 | DEAD |
| RD-34 | §4.7 first amendment | Conditions 2-4 as first written (two-page allowlist, manual route is an equal peer, one parser) | narrowed and extended by RD-37..RD-42 | DEAD (superseded text retained beside its correction, K-11) |
| RD-35 | §4.7 first amendment | "The spirit of §4.7 is that Winnow must never hold or exfiltrate the user's session or impersonate their browser." | design §4.7 | DECISION |
| RD-36 | §4.7 second amendment, cond. 1 | "**User-present sign-in, ephemeral off-the-record browser.** ... Winnow never sees the password; Steam Guard works normally; the profile is torn down afterwards." | `docs/spikes/steam-web-session-auth.md` §5 | RULE |
| RD-37 | §4.7 second amendment, cond. 2 | "**Exactly two secrets at rest.** The minted access token and the refresh token, and nothing else, written as one DPAPI-encrypted blob ... A host that cannot encrypt refuses to store rather than degrading to plaintext" | RM-15 (K-06) | RULE |
| RD-38 | §4.7 second amendment, cond. 3 | "**A closed list of three unattended request kinds.** ... No authenticated HTML page is ever fetched without the user present." | — | RULE |
| RD-39 | §4.7 second amendment, cond. 4 | "**Reading is bounded by what, not by how much.** ... The script is fixed at build time; it is not a general query interface." | `docs/spikes/steam-web-session-auth.md` §7.1 | RULE |
| RD-40 | §4.7 second amendment, cond. 5 | "**Purchase history needs its own permission.** ... Declining leaves the sign-in fully functional" | — | RULE |
| RD-41 | §4.7 second amendment, cond. 6 | "**Peers, on both axes.** The Web API key and the WebView sign-in are peer connection methods, neither a fallback for the other." | RM-12, `docs/spikes/steam-web-session-auth.md` §8 | RULE |
| RD-42 | §4.7 second amendment, cond. 7 | "**One parser, one importer, one credential seam.** Sign-in is a credential source, not a second Steam integration." | — | RULE |
| RD-43 | §4.7 second amendment, cond. 8 | "**Legibility.** A session that cannot renew must say so before it dies. Silent degradation to no-remote-data is a defect, not a graceful fallback." | RM-27 (adjacent principle) | RULE |
| RD-44 | §4.7 second amendment | "**What this costs, stated plainly.** A refresh token is not as reliable as an API key ... one contrary community report exists against the `finalizelogin` route (node-steam-session issue #56, 2026-05-20, unresolved)." | `docs/spikes/steam-web-session-auth.md` | DECISION + FINDING |
| RD-45 | §4.7 second amendment | "The token lives about a day (24h 22m, measured 2026-08-30) ... the refresh token, roughly 207 days with remember-me" | `docs/spikes/steam-web-session-auth.md` §1 | FINDING |
| RD-46 | §4 Why that order | "**M3 before M8.** ... every week M3 is late is a week of history not collected." | — | DECISION |
| RD-47 | §4 Why that order | "**M9 delegates, never reimplements.** Winnow hands installation to the store's own client (`steam://install/`, Galaxy, the Epic launcher)." | design-system §10.3 | RULE |
| RD-48 | §4 Why that order | "**M10 last, deliberately.** Full-screen gamepad mode is a second complete UI" | RD-13 | DECISION |
| RD-49 | §5 | "There is a product-integrity tension to resolve first ... An app that opens with that diagnosis and then sells you more games is incoherent" | — | DECISION |
| RD-50 | §5 | "The version that survives that objection is **wishlist intelligence**, not a purchase feed ... The core loop stays *play what you own*." | recommendation-engine charter | RULE |
| RD-51 | §6 debt | "**Merge execution is not built.** The queue proposes and stores confirmations; nothing applies them ... The `ON DELETE CASCADE` hazard on collapsing two releases is documented and unresolved." | RM-34, backlog TASK-5/TASK-64 | FINDING |
| RD-52 | §6 debt | "**Cross-store dedup via `gamesdb.gog.com`** — **built for METADATA, not for dedup.** ... It deliberately writes no `external_ids` and no merge candidates: that table is keyed `(provider, provider_id)` globally" | `docs/spikes/epic-gog-local-files.md` §20 | RULE + DECISION (split) |
| RD-53 | §6 debt | "*(original)* — spiked and verified ... Not built. This would collapse most of the merge queue automatically" | superseded by RD-52 in the same bullet | DEAD (K-12) |
| RD-54 | §6 debt | "**`SteamSyncService` — settled 2026-08-28.** Split, not just renamed ... `LocalLibrarySyncService : ILocalLibrarySync` handles the three local scans; `RemoteOwnershipSyncService : IRemoteOwnershipSync` handles entitlement backfill at 6 hours." | RM-24 | RULE + DECISION (split) |
| RD-55 | §6 debt | "Both live in `Winnow.App.Services`, not `Winnow.Core.Ingest` as originally intended — `LibrarySyncReport` carries `ResolveResult`, and Core cannot reference Resolve." | CL-19, design §5.1 | DECISION |
| RD-56 | §6 debt | "The no-network guarantee is enforced by `LocalLibrarySyncContractTests`." | `tests/Winnow.Tests/LocalLibrarySyncContractTests.cs` | RULE (enforced) |
| RD-57 | §6 debt | "**Session detection is Windows-only in practice.** ... Widening the glob is NOT the fix: under Proton the resolved executable is the wine loader" | RM-08, design §5.2 | FINDING + RULE (split) |
| RD-58 | §6 debt | "**IGDB cache has no `payload_version`.** Adding a field to the cached shape silently yields empty results for 30 days rather than refetching." | design §4.4 | FINDING |
| RD-59 | §6 debt | "**Account stats query surface exists with no UI in front of it.** ... The ownership columns `acquired_at` / `license_type` / `price_paid_cents` are still read by nothing; M6 export remains their intended first consumer." | design-system §10.5, design §7 | FINDING |
| RD-60 | §6 debt | "The fact tables cannot distinguish two identical same-day transactions, so an exact repeat purchase on one day is undercounted by one." | backlog TASK-44 | FINDING |
| RD-61 | §6 debt | "**`OwnershipRepository.UpsertAsync` could overwrite an imported `acquired_at`** ... Worth an enforcing test when the field is next touched." | — | RULE (deferred) |
| RD-62 | §6 debt | "**$0.00 purchase rows are skipped, not recorded as zero.** ... whether to store a zero or omit entirely is a user decision, untested either way." | backlog TASK-40 | UNRESOLVED (U-02) |
| RD-63 | §6 debt | "**Saved-file licenses route captures one page per file.** ... Multi-file merge in the loader is the fix if coverage ever matters." | `docs/spikes/steam-gdpr-export.md` §8 | FINDING |
| RD-64 | §6 debt | "**Single-entry rail sections (deferred 2026-08-29).** ... Constraint: whatever replaces them must preserve the rail's stated grammar. Everything above the divider is a subset of ALL GAMES; below it, content precedes work queue precedes configuration." | design-system §12.1, §13 gap 3 | RULE (the grammar) + DECISION (the deferral) |
| RD-65 | §6 debt | "**Account stats presentation is a first pass; cleanup shelved (deferred 2026-08-29).** ... Candidates: per-transaction averages, per-year averages, percentage breakdowns ... cost per hour played and spend on games never launched" | — | DECISION |
| RD-66 | §6 debt | "**Account-scope filter has two deliberate under-reaches (accepted 2026-08-30).** ... two surfaces still read the ownership-level `playtime_snapshots` series, which has no per-account form" | backlog TASK-53/54 | FINDING |
| RD-67 | §6 debt | "Erring visible is the decision: hiding a game the user owns is worse than showing one they do not." | RM-27 (adjacent) | RULE + DECISION (split) |
| RD-68 | §6 debt | "Migration 0015's seed rows are stamped `source = 'ownerships.account_ref'` and excluded from absence evidence" | — | RULE |
| RD-69 | §7 | "This roadmap roughly triples Winnow's surface area. The realistic failure mode ... is becoming a worse Playnite with an unfinished recommender attached." | RD-07 | DECISION |
| RD-70 | §7 | "**the feed must always be further along than the launcher.**" | RD-46 | RULE |

## 1.5 `game-library-design.md` (615 lines)

| ID | Anchor | Verbatim claim | Also claimed in | Class |
|---|---|---|---|---|
| GD-01 | §0 | "This is a build specification, not a proposal. ... Read section 4 before writing any ingest code." | CL-13, steam-ingest, winnow-reviewer | RULE |
| GD-02 | §0 | "Items marked **[VERIFY]** were not confirmed during design. Confirm them empirically before building on them. Do not treat them as established." | CL-18, steam-ingest charter | RULE |
| GD-03 | §1 | "Large PC game libraries (1,000+ titles) decay into three piles ... **Games the owner intends to play but has forgotten exist**. Pile 3 is the target." | RM-01, RD-03 | DECISION |
| GD-04 | §1 Goals | Six goals: unified library with correct deduplication; playtime and last-played including longitudinal history; "Update-aware staleness detection (the differentiating feature)"; per-platform achievement tracking with a unified read surface; user-authored lists and collections; "First-class data export" | RM-03, design-system §1, §12, §7 | RULE |
| GD-05 | §1 Non-goals | "PlayStation and Xbox integration (see §4.6)" | RD-15, §4.6, §9 | RULE |
| GD-06 | §1 Non-goals | "Any hosted service, user accounts, or multi-user features" | RD-12, CL-01, RM-01 | RULE |
| GD-07 | §1 Non-goals | "Co-op / friend library matching (requires a server; phase 2)" | §8 Phase 2 | RULE |
| GD-08 | §1 Non-goals | "~~Recommendation engine (phase 2)~~ — **SUPERSEDED. See `ROADMAP.md` §3.**" | RD-11 | DEAD (struck text kept beside its correction, K-13) |
| GD-09 | §1 Non-goals | "3D "games on a shelf" browsing view (cut; see §11)" | RD-13, §11 | RULE |
| GD-10 | §1 Non-goals | "Mobile" | — | RULE |
| GD-11 | §2 | "Evaluated Electron vs. Avalonia. **Avalonia wins.** ... this application is a **background daemon with a UI attached**." | RM-23, avalonia-ui | DECISION |
| GD-12 | §2 | "The last two rows were the entire Electron case, and both were tied to the 3D shelf view. **With the shelf cut, nothing argues for Electron.**" | §11, RD-13 | DECISION |
| GD-13 | §2 Accepted cost | "If phase 2's hosted service acquires a web portal, Avalonia XAML transfers nothing to it ... Registered, accepted, not relitigated." | — | DECISION |
| GD-14 | §3 | Tech-stack table: .NET latest LTS (.NET 10); Avalonia 11+ with XAML; `CommunityToolkit.Mvvm`; SQLite via `Microsoft.Data.Sqlite`; **Dapper**; "**FluentMigrator or DbUp**"; **ValveKeyValue** "do not hand-roll"; SteamKit2 "Only if the Web API proves insufficient; not needed for v1"; `HttpClient` + **Polly**; AngleSharp "GDPR export import (§5.4)"; `System.Text.Json` source-generated contexts; Serilog rolling file sink; in-process `PeriodicTimer`; IGDB v4; Velopack **[VERIFY]** | RM-23, CL-20, data-layer, enrichment-api | RULE; the migrations row is DEAD (K-14) and the AngleSharp rationale is DEAD (K-09) |
| GD-15 | §3 | "**Deliberately excluded from v1:** Postgres, any vector store, any server framework, any LLM dependency. All are phase 2. Do not add them speculatively." | RD-12 | RULE |
| GD-16 | §3.1 | "**EF Core does not play well with NativeAOT** ... Dapper plus an explicit migration runner keeps AOT viable and the schema legible." | RM-23, data-layer | DECISION |
| GD-17 | §3.1 | "If the implementer prefers EF Core's migrations story, that is defensible — but then drop NativeAOT and publish trimmed self-contained instead. Do not attempt both." | — | DEAD (K-15) |
| GD-18 | §3.1 | "Trimmed self-contained publish is the safe default; treat AOT as an optimisation to attempt after M2, not a day-one constraint." | — | RULE |
| GD-19 | §4.1 | "Reading local files is the **primary** playtime source, not the Web API." | CL-22, RM-11, steam-ingest | RULE |
| GD-20 | §4.1 | Path table: `libraryfolders.vdf`, `appmanifest_<appid>.acf`, `localconfig.vdf`, `cloudstorage/cloud-storage-namespace-1.json` | `docs/spikes/steam-local-files.md`, steam-ingest | RULE |
| GD-21 | §4.1 | Steam install roots for Windows, Linux (including Flatpak) and macOS | `docs/spikes/steam-local-files.md` | RULE |
| GD-22 | §4.1 | "The collections JSON path changed in 2025. Older guides point at `sharedconfig.vdf` or a Chromium LevelDB store in `htmlcache`. Both are **dead**." | steam-ingest, `docs/spikes/steam-local-files.md` §4 | RULE |
| GD-23 | §4.1 | "The Steam client does not flush config changes to disk immediately ... Treat a running Steam client as an eventually-consistent writer." | steam-ingest, spike §3 note 7 | RULE |
| GD-24 | §4.1 | "Never write to these files while Steam is running ... v1 is **read-only** against all Steam files." | CL-37, RM-17, steam-ingest, winnow-reviewer | RULE |
| GD-25 | §4.1 | "Parse with ValveKeyValue. Both text and binary KeyValues appear in Steam's config tree; hand-rolled parsers break on the binary variants." | CL-22, §9, steam-ingest, winnow-reviewer | RULE |
| GD-26 | §4.1 | "`localconfig.vdf` playtime fields **[VERIFY]** — confirm exact key names (`Playtime`, `LastPlayed`, `playtime_two_weeks`)" | resolved by `docs/spikes/steam-local-files.md` §3 | DEAD (K-16) |
| GD-27 | §4.2 | "Used for enrichment and friends data only. Key is user-supplied, stored locally." | RM-15, enrichment-api | RULE |
| GD-28 | §4.2 | "`IPlayerService/GetOwnedGames` — pass `include_appinfo=1`, `include_played_free_games=1`, and `skip_unvetted_apps=false`." | enrichment-api | RULE |
| GD-29 | §4.2 | "`rtime_last_played` is returned **only when the API key belongs to the queried account**. ... Do not architect around the Web API for the local user's own playtime." | GD-19 | RULE |
| GD-30 | §4.2 | "`GetPlayerSummaries` accepts up to **100 SteamIDs per call** ... Not needed in v1" | — | FINDING |
| GD-31 | §4.2 | "Since June 2025, Steam throttles profile endpoints aggressively, returning HTTP 429 with `Retry-After` of 60–120s. Reported figures are third-party estimates — **[VERIFY]** ... implement exponential backoff and 429 handling from the first commit regardless. Polly policies, applied at the `HttpClient` level, not per call site." | enrichment-api, winnow-reviewer | RULE (the [VERIFY] half is open, U-03) |
| GD-32 | §4.2 | "Nominal budget is 100,000 calls/day. Cache aggressively." | enrichment-api | RULE |
| GD-33 | §4.3 | "`store.steampowered.com/api/appdetails` is limited to roughly **200 requests per 5 minutes per IP**, and accepts **one appid per request** ... never put it in a user-facing path." | enrichment-api, §9, design-system §7 | RULE |
| GD-34 | §4.3 | "Cache appdetails responses for **at least 24 hours**. Set a descriptive `User-Agent`." | enrichment-api | RULE |
| GD-35 | §4.3 | "**User-defined store tags are not in `appdetails`.** ... require either the store page HTML or `IStoreService`/`IStoreBrowseService`. **[VERIFY]** which endpoint is currently viable. IGDB genres/themes are the fallback" | resolved by `docs/spikes/steam-store-tags.md` | DEAD (K-17) |
| GD-36 | §4.3 | "Valve rate-limits traffic that resembles scraping. If throttled persistently, the documented remedy is contacting `webapi@valvesoftware.com`." | `docs/spikes/steam-store-tags.md` | RULE |
| GD-37 | §4.4 | "Auth is Twitch client-credentials ... Tokens are long-lived (~60 days); cache and refresh, don't re-mint per request." | enrichment-api, RM-19 | RULE |
| GD-38 | §4.4 | "Rate limit: **4 requests/second** per credential. Enforce with a shared Polly rate-limit policy, not ad-hoc `Task.Delay`." | enrichment-api, winnow-reviewer | RULE |
| GD-39 | §4.4 | "Queries use Apicalypse (POST body as `text/plain`, not query params)" | enrichment-api | RULE |
| GD-40 | §4.4 | "**`external_games` / `external.steam` maps Steam appids directly to IGDB IDs.** This is the high-precision join and the backbone of entity resolution." | §5.3, CL-23 | RULE |
| GD-41 | §4.4 | "**`game_versions` endpoint exposes release editions** ... This is exactly the abstraction the Release layer needs — do not reinvent it." | §5.3, RM-25 | RULE |
| GD-42 | §4.5 | "**Build push:** appinfo `depots.branches.public.timeupdated` ... Available from local SteamCMD or `GET https://api.steamcmd.net/v1/info/{appid}` ... **[VERIFY]** availability, and keep local SteamCMD as fallback." | `docs/spikes/update-signals.md` §1-§2 | RULE with a DEAD clause (K-18) |
| GD-43 | §4.5 | "**Announcements:** `ISteamNews/GetNewsForApp`, filtered to community announcements." | `docs/spikes/update-signals.md` §3 | DEAD in part (K-19) |
| GD-44 | §4.5 | "**Only flag a "major update" when both fire within the same window.** Store both raw signals in `update_events` so the heuristic can be retuned without re-fetching." | enrichment-api, §9, design-system §5.2 | RULE |
| GD-45 | §4.6 | "PSN and Xbox are **out of scope and must not be added**." + three reasons (no consumer API; manual `npsso` extraction; PSNAWP ban warning) | RD-15, §9 | RULE + DECISION (split) |
| GD-46 | §4.7 | "Steam exposes transaction history at `store.steampowered.com/account/store_transactions` ... Neither has an API or export. **Do not scrape either page.**" | RD-16, RD-17 amend this | RULE, twice amended (K-11) |
| GD-47 | §4.7 | "Bundles appear as a **single line item for N games**. Per-game attribution is underdetermined" | RD-59 | FINDING |
| GD-48 | §4.7 | "**Third-party keys (Humble, Fanatical, etc.) never appear in Steam's spending data at all**" | — | FINDING |
| GD-49 | §4.7 | "The sanctioned path is the GDPR export (§5.4), which includes an `ExternalLicenses` file covering third-party keys." | contradicted by RD-27 | DEAD (K-09) |
| GD-50 | §4.7 | "Price is an **opt-in, clearly-labelled estimate**, not a core feature." | RD-59 | RULE |
| GD-51 | §5 | "Background services run as `IHostedService` implementations under the generic host, with the Avalonia UI resolving view models from the same DI container." | CL-24, avalonia-ui | RULE |
| GD-52 | §5 | "UI never calls an ingest or enrichment component directly; it reads the database and raises commands." | CL-25, RM-24, avalonia-ui, winnow-reviewer | RULE |
| GD-53 | §5 | The architecture mermaid diagram naming every service and its edges | RM-24 | DECISION |
| GD-54 | §5.1 | Module boundary table: `Ingest.*` must not "Write to `works`/`releases` directly"; `Resolve.*` must not "Auto-merge below confidence threshold"; `Enrich.*` must not "Block any user-facing path"; `Monitor.*` must not "Assume any specific launcher is present"; `Score.*` must not "Store derived values as source of truth" | CL-19..CL-25, RM-24, every agent charter | RULE |
| GD-55 | §5.2 A | "Two tiers. **Polling is for discovery only — never for exit detection.**" | RD-19 M3a exit criterion | RULE |
| GD-56 | §5.2 A | "*Tier 1 — discovery (polled, 5s):* Enumerate via `Process.GetProcesses()`. Map executables to releases using `installdir` from `appmanifest_*.acf` cross-referenced with `libraryfolders.vdf` paths" | RD-57 | RULE |
| GD-57 | §5.2 A | "**Filter on `Process.ProcessName` against the known-executables set before resolving any full path.**" | — | RULE |
| GD-58 | §5.2 A | "On Linux, read `/proc/*/comm` for the name filter and `/proc/<pid>/exe` only for candidates." | RD-57 says the Linux path cannot work as specified | RULE, contested (K-20) |
| GD-59 | §5.2 A | "*Tier 2 — exit (event-driven, no polling):* ... retain the `Process` object, set `EnableRaisingEvents = true`, and subscribe to `Exited`. ... Retaining the handle also pins the PID against reuse" | — | RULE |
| GD-60 | §5.2 A | "Read `Process.StartTime` for the true wall-clock start ... The interval governs when the app *notices*, not what it *records*." | — | RULE |
| GD-61 | §5.2 A | "Consequently 5s is a UI-responsiveness setting, not an accuracy one ... Do not drop to 1s expecting better data" | — | RULE + DECISION (split) |
| GD-62 | §5.2 A | "Known noise sources, all of which must be handled: launchers spawn child processes ...; some games relaunch through a second executable; Proton/Wine wraps everything in a process tree; match on the tree, not a single PID; Debounce: ignore sessions under 60s by default (configurable)" | RD-57 | RULE |
| GD-63 | §5.2 B | "**B. Launch-option wrapper (opt-in, exact)** The user sets `winnow-wrap %command%` ... Offer it in the UI as an upgrade for individual games, not as a global requirement." | RD-19 (M3b: "deliberately deferred") | RULE, unbuilt (K-21) |
| GD-64 | §5.2 | "**Journal prompt:** on session end, if enabled, show a small unintrusive window ... Must be fully disableable in settings, and must default to a state the user explicitly opted into." | §9 pitfall 7, design-system §6 | RULE |
| GD-65 | §5.3 | "**Four-layer model — do not collapse it to two:** Work / Release / Ownership / PlayRecord" | RM-25, §9, data-layer, CL-23 | RULE |
| GD-66 | §5.3 | "1. **Hard join (auto-merge):** IGDB `external_games` lookup by Steam appid / GOG id / Epic catalog id. High precision. Merge without asking." | contradicted for Epic by `docs/spikes/epic-gog-local-files.md` §19 | RULE with a DEAD clause (K-22) |
| GD-67 | §5.3 | "2. **Soft match (queue, never auto):** normalised title + release year within ±1, publisher match, cover perceptual hash. Produce a confidence score and write to `merge_candidates` with `status='pending'`." | CL-23, §5.1 | RULE |
| GD-68 | §5.3 | "3. **User confirmation** clears the queue in a dedicated UI. Batch it" | design-system §6 | RULE |
| GD-69 | §5.3 | "**Non-negotiable:** never auto-merge on fuzzy title similarity. ... Precision over recall, always, with a human in the loop." | RM-26, CL-23, §9, winnow-reviewer | RULE |
| GD-70 | §5.4 | "**GDPR export import** ... The user requests their data from `help.steampowered.com/en/accountdata` ... receives it, and points the app at the file." | contradicted by RD-27 | DEAD (K-09) |
| GD-71 | §5.4 | "The export reportedly includes a **playtime breakdown** ... plus **`ExternalLicenses`**" | contradicted by RD-27 | DEAD (K-09) |
| GD-72 | §5.4 | "**[VERIFY] before building the parser:** obtain a current export and confirm what files it actually contains." | resolved by `docs/spikes/steam-gdpr-export.md` | DEAD |
| GD-73 | §5.4 | "Build it after M2, not before — the snapshot pipeline must work standalone." | RD-19 | RULE |
| GD-74 | §6 | The SQLite schema block: `works`, `releases`, `external_ids` "PRIMARY KEY(provider, provider_id)", `ownerships`, `play_records`, `playtime_snapshots`, `sessions`, `session_notes`, `achievements`, `achievement_unlocks`, `update_events`, `lists`, `list_items`, `merge_candidates`, `metadata_cache`, `settings` | data-layer, RD-52 (the `external_ids` key) | RULE |
| GD-75 | §6 | "Migrations checked into the repo and applied on startup." | CL-20, RM-23, data-layer | RULE |
| GD-76 | §6.1 | "Computed as queries, not stored columns" + the six-bucket table (Never played, Bounced, Stale but patched, Retired, Active, Dead) | CL-21, RM-24, §9, data-layer | RULE |
| GD-77 | §6.1 | "**Never played means never opened.** Zero minutes *and* no last-played date, nothing else." | contradicts RM-05 | RULE (K-05) |
| GD-78 | §6.1 | "classifying it as "Never played" was tried (the refund-line rule, reverted 2026-08-29) and abandoned because a game the user demonstrably launched reading as "Never played" was confusing." | RM-05 still states the reverted rule | DEAD + DECISION (split) |
| GD-79 | §6.1 | "`bounced_floor` defaults to 120 minutes (Steam's refund window), which is the floor for Bounced." | recommendation-engine charter, design-system §5.2 | RULE |
| GD-80 | §6.1 | "**Precedence**, in the order the query tests: never-played ..., retired, stale-but-patched, bounced, active." | recommendation-engine charter | RULE |
| GD-81 | §6.1 | "Retired outranks stale so a 200-hour game is never resurfaced. Stale outranks bounced ... Only never-opened outranks staleness" | design-system §5.2 | DECISION |
| GD-82 | §6.1 | "`retired_floor` still cannot be a flat number in the long run ... **[VERIFY]** whether a HowLongToBeat data source is available and licensable ... Both are query parameters, not columns" | §10 open questions | RULE + open [VERIFY] (U-04) |
| GD-83 | §6.2 | "Never compute a blended cross-platform completion percentage ... Render per-release rows nested under the Work. The unified view is a query, not a stored merge." | data-layer, design-system §10.5 | RULE |
| GD-84 | §7 | "Launch feature, not an afterthought. ... JSON (full fidelity, versioned schema, round-trippable via an import path); CSV (flattened, one row per ownership); No account or network required; Schema version in every export" | GD-04, RD-19 (M6 deferred) | RULE, deferred (K-23) |
| GD-85 | §8 | "**Sequencing here is superseded by `ROADMAP.md` (v2).** ... The exit criteria below remain accurate — only the order and the phase list changed. §4's hard constraints are untouched." | RD-01, CL-12, RM-32 | DEAD (the table) + RULE (the exit criteria) |
| GD-86 | §8 | The M0-M6 milestone table with exit criteria | RD-19 | RULE (exit criteria) |
| GD-87 | §8 | "**M0–M2 is the minimum interesting product.**" | — | DECISION |
| GD-88 | §8 Phase 2 | "Sync server, Steam OpenID accounts, co-op library matching, recommendations." | recommendations contradicted by RD-11 | DEAD in part (K-13) |
| GD-89 | §8 Phase 2 | "Surface coverage honestly ("14 of your 47 friends have public libraries") rather than silently dropping people." | RM-27 (adjacent) | RULE (conditional on a phase not in scope) |
| GD-90 | §9 | Nine ranked pitfalls: stale Steam path docs; auto-merging fuzzy titles; `appdetails` in onboarding; treating `timeupdated` as major update; collapsing Release into Work; storing derived buckets as columns; journal prompt on by default; hand-rolling VDF; adding PSN/Xbox | every other rule above, winnow-reviewer checklist | RULE (restatement) |
| GD-91 | §10 | Six open questions: localconfig key names; steamcmd.net vs local SteamCMD; which endpoint returns weighted tags; contents of a current GDPR export; HowLongToBeat licensability; auto-update mechanism | resolved by spikes except the last two | DEAD in part (K-24) |
| GD-92 | §10 | "Resolve these empirically. Do not proceed on assumptions from training data or blog posts" | GD-02, CL-18 | RULE |
| GD-93 | §11 | "**Shelf view cut.** ... if the shelf is ever reinstated, it does **not** justify revisiting the framework choice on its own." | RD-13, GD-09, GD-12 | RULE + DECISION (split) |
| GD-94 | §11 | "Cover thumbnails in the library view remain in scope and come from IGDB covers and Steam's `library_600x900` portrait capsule." | design-system §4, RM-24 | RULE |

## 1.6 `design-system.md` §1-§12 (lines 1-1034)

| ID | Anchor | Verbatim claim | Also claimed in | Class |
|---|---|---|---|---|
| DS-01 | header | "**Applies to:** Avalonia 11+ desktop client, dark-only for v1" | §14.1.1 ("No light theme, deliberately") | RULE |
| DS-02 | header | "**Companion files:** `tokens.axaml` (drop-in ResourceDictionary), `mock-library.html` (visual target)" | CL-16, avalonia-ui charters | RULE |
| DS-03 | §1 | "This is a **game library that happens to be analytically sharp** ... Cover art is the primary interface. Data lives inside the art, not beside it." | RM-01 | DECISION |
| DS-04 | §1 | "**The art is the chart.** Dormancy is rendered as desaturation of the cover itself. ... No sparkline, no bar, no second visual language competing with the art." | §5.1, §11.4, avalonia-ui | RULE |
| DS-05 | §1 | "**Patched-since-played is an unread badge.** A hot pink dot in the tile corner." | §5.2, CL-14 | RULE |
| DS-06 | §1 | "The consequence: **your library has unread mail.**" | CL-01, RM-01 | DECISION |
| DS-07 | §2 | Palette table, twelve tokens: `Well #050D0E`, `Ground #0F1C1E`, `Surface #16282A`, `SurfaceRaised #1D3437`, `Line #2B4A4C`, `Text #F0EDE7`, `TextDim #8FA5A0`, `Flare #FF4D93`, `Volt #4DE8C2`, `Amber #FFB63D`, `Azure #57A8F0`, `Danger #E04B45` | `tokens.axaml`, CL-14 (different Flare hex) | RULE (K-01) |
| DS-08 | §2 Discipline | "`Flare` ... appears **only** on unread-update markers and the bucket that counts them. The instant it becomes a generic accent, the badge stops meaning anything" | CL-14, §10.2, §11.3, avalonia-ui, winnow-reviewer | RULE + DECISION (split) |
| DS-09 | §2 Discipline | "`Volt` carries selection and recency. `Amber` carries "you've been here a lot." `Azure` is the neutral one ... `Danger` is the close button's hover fill" | §12.3 (Danger also on the delete confirm), §14.3 (Amber on the AA figure) | RULE (K-25) |
| DS-10 | §2 Discipline | "**Hierarchy is carried by temperature as well as lightness.** ... Do not "fix" this by neutralising either one." | — | RULE |
| DS-11 | §2 Discipline | "Never tint cover art with brand colour. The art is content; the interface stays out of it except through the saturation ramp in §5." | §5.1 | RULE |
| DS-12 | §2 | "**Palette revised — the violet family is gone.** `Ground #16112A`, `Surface #1F1838` ... were a deep indigo-violet stage" | — | DEAD + DECISION (split) |
| DS-13 | §2 | "`Flare` moved 6° hotter (`#FF5C8A` → `#FF4D93`) ... `Azure` moved 8° toward cyan (`#5B9DFF` → `#57A8F0`)" | CL-14 and the avalonia-ui charters still carry `#FF5C8A` | FINDING (K-01) |
| DS-14 | §3 | "Three roles, three families. All SIL OFL — bundle them, don't rely on system fonts." Display Bricolage Grotesque 700; Body Plus Jakarta Sans 400-600; Data IBM Plex Mono 400-500 | avalonia-ui, CL-17 | RULE |
| DS-15 | §3 | "It has `wdth` and `opsz` axes; use `wdth` 100–110 for headers, never above 120." | contradicted by CL-17 | DEAD (K-02) |
| DS-16 | §3 | "**Every number is Plex Mono with tabular figures.** Non-negotiable in list view ... (`FontFeatures="tnum"`)" | CL-15, avalonia-ui, winnow-reviewer | RULE |
| DS-17 | §3 Scale | Seven-row type scale: Display L 22/26 wdth 105; Display S 12/15 wdth 110 +0.06em uppercase; Body L 15/22; Body 13/18; Label 11/14 +0.04em uppercase; Data 12/16 tnum; Data S 10/12 tnum | `tokens.axaml`; the wdth values inherit DS-15's conflict | RULE (K-02) |
| DS-18 | §4 | "**Grid is the default view. List is a toggle**, remembered per-session." | §6, §14.7 | RULE |
| DS-19 | §4 | "4px base unit. Spacing: `4 · 8 · 12 · 16 · 24 · 32 · 48`." | §15.3 | RULE |
| DS-20 | §4 | "**Tile geometry.** 2:3 portrait, matching Steam's `library_600x900` capsule and IGDB covers. Default 148×222, gutter 16px. Density slider spans 108×162 → 200×300; the grid reflows on available width" | GD-94, §5.4 | RULE |
| DS-21 | §4 | "**Radius:** 6px on tiles, 4px on controls." | §15.3 | RULE |
| DS-22 | §4 | "**Elevation.** Tiles get a real drop shadow on hover ... This is the one place shadow is permitted; everywhere else, elevation is the `Surface → SurfaceRaised` step." | §14.2 (amended to a relative claim) | RULE |
| DS-23 | §5.1 | Dormancy ramp table mapping idle months to saturation/brightness: `<1mo 1.00/1.00`, `6mo 0.72/0.91`, `1y 0.50/0.83`, `2y 0.34/0.74`, `3y+ 0.22/0.68` | avalonia-ui charters (0.60 floor), `docs/spikes/avalonia-dormancy-rendering.md` (0.60 floor) | RULE (K-26) |
| DS-24 | §5.1 | "Clamp at `0.22 / 0.68` — never fully grey." | contradicted by avalonia-ui charters and the dormancy spike | RULE (K-26) |
| DS-25 | §5.1 | "A **−6° hue rotation** is part of the floor, composed as `saturate() → hue-rotate(-6deg) → brightness()`." | `docs/spikes/avalonia-dormancy-rendering.md` (the two-endpoint lerp cannot express it independently) | RULE (K-27) |
| DS-26 | §5.1 | "**Brightness floor revised.** This was `0.60` until the ramp was first seen on real cover art ... **Saturation, not brightness, is what carries the dormancy signal.**" | — | DEAD + DECISION (split) |
| DS-27 | §5.1 | "**Hover restores full saturation over 140ms.** ... This is the single most important interaction in the app" | avalonia-ui, §8 | RULE |
| DS-28 | §5.2 | "10px `Flare` dot, top-right, 8px inset, with a 2px `Ground`-coloured ring ... Optional soft outer glow at 30% opacity." | — | RULE |
| DS-29 | §5.2 | "Present only when a major update landed after the user's last session (both signals from §4.5 of the design doc — build push *and* announcement)." | GD-44 | RULE |
| DS-30 | §5.2 | "Never on never-opened games; an unplayed game has nothing to be behind on." | GD-77, `docs/spikes/update-signals.md` §4 | RULE |
| DS-31 | §5.2 | "**"Never-opened" here means zero recorded playtime, not the `Never played` bucket.**" | GD-77/GD-78 (the bucket definition since reverted, K-28) | RULE, premise stale |
| DS-32 | §5.2 | "The update poller's eligibility filter draws the same line, for the same reason, and must keep drawing it on playtime rather than on a bucket name." | `docs/spikes/update-signals.md` §4 | RULE |
| DS-33 | §5.2 | "Clicking the badge opens the patch notes for the updates you missed." | §10.3, `docs/spikes/update-signals.md` | RULE |
| DS-34 | §5.3 | "Bottom third of the tile, gradient scrim to `Ground` at 92%. Title in Body L, playtime and idle time in Data S. Store badge bottom-left. A single primary action, `Play`, in `Volt`." | — | RULE, amended by DS-35 |
| DS-35 | §5.3 | "**Store badge became a chip row (TASK-70.6, 2026-09-01).** ... A multi-store tile additionally carries a compact one-letter-per-store mark at rest on the front, which fades out over 140ms" | §6, §11.2 amendment | RULE |
| DS-36 | §5.3 | "Do not show more than four facts. The tile is a decision surface, not a detail view." | §10 | RULE |
| DS-37 | §5.4 | "Avalonia has no CSS `filter`. Two viable approaches, in preference order: 1. **Shader effect** ... **[VERIFY]** ... 2. **Pre-computed bitmap variants** ... Fall back to (2) if (1) is unavailable" | `docs/spikes/avalonia-dormancy-rendering.md` settles it: option 1 does not exist | DEAD (K-29) |
| DS-38 | §5.4 | "Do not attempt per-frame pixel manipulation on the UI thread." | dormancy spike | RULE |
| DS-39 | §5.4 | "Covers must be virtualized and decoded off-thread at display resolution, not full size." | avalonia-ui charters | RULE |
| DS-40 | §5.4 | "The panel is `Views/CoverWall.cs`, not `ItemsRepeater`: `UniformGridLayout` charges every item in a row for a trailing gutter ... Its remarks carry the measurements." | avalonia-ui charters, dormancy spike's 2026-08-24 note | RULE + FINDING (split) |
| DS-41 | §6 | "**Rail bucket.** Display S name, Data count. Selected: `SurfaceRaised` fill, 2px `Volt` left edge. The `Patched since` bucket is the only one carrying a `Flare` dot next to its count." | §12.1, DS-08 | RULE |
| DS-42 | §6 | "Zero-count buckets render at 40% opacity rather than hiding, so the rail never reflows." | §11.2 | RULE |
| DS-43 | §6 | "**List view.** Same data, no art dependency: title, store, playtime, idle, unread dot. 44px rows, `Surface` ground, `Line` rules, `Volt` selection edge." | §14.7 (rows moved to `PaneGround`) | RULE, amended (K-30) |
| DS-44 | §6 | "**Merge confirm queue.** Two covers side by side at 200×300 ... Actions are `Same game` / `Different games` — never "Merge"/"Cancel"" | §7 copy table, GD-68, avalonia-ui | RULE |
| DS-45 | §6 | "**Each member states its store (TASK-70.8, 2026-09-01).** ... Pair layout ... a `WrapPanel` so three chips (123.1px) never clip at 200px. Roster rows ... the chips lead the metadata line" | §11.2 amendment | RULE |
| DS-46 | §6 | "**The card has a maximum width of 840px and is centred.** ... 840 clears that with slack ..., sits on §4's 4px grid, and is twice the 420px feed card measure." | §13 gap 5's 720px provisional measure | RULE + DECISION (split) (K-31) |
| DS-47 | §6 | "**Session journal prompt.** 400×220 frameless, bottom-right, `SurfaceRaised` ... Appears at most once per session, never steals focus." | GD-64 | RULE |
| DS-48 | §7 | "Plain and specific. The app knows something faintly embarrassing about the user ... and must never be smug about it." | avalonia-ui charters | RULE + DECISION (split) |
| DS-49 | §7 | Copy table: `Patched since`, `Never played`, `Bounced off`, `Played out`, `Won't run`, `3 updates since you played`, `How was that?`, `Same game` | avalonia-ui charters, GD-76; contradicted by notes.md | RULE (K-32) |
| DS-50 | §7 | Three empty states: "Nothing's been patched since you last played. This fills up on its own."; "You've played everything you own. Genuinely rare."; "Reading your Steam library. Covers and metadata fill in over the next few minutes — you can browse now." | §12.1 has a fourth | RULE |
| DS-51 | §7 | "Render placeholder tiles with the title set in Bricolage on a `Surface` field — never a spinner, never an empty grid." | avalonia-ui, §13 gap 1 | RULE |
| DS-52 | §8 | "**The saturation ramp is decorative-redundant.** ... A user who can't perceive the fade loses nothing." | §10.2, §11.3, avalonia-ui | RULE |
| DS-53 | §8 | "Visible keyboard focus everywhere: 2px `Volt` outline, 2px offset." | contradicted by §10.7 and §13 gap 6 | RULE, contested (K-33) |
| DS-54 | §8 | "Full keyboard grid navigation (arrows, `/` to search, `Enter` to launch)." | §12.3 | RULE |
| DS-55 | §8 | "Reduced motion disables the hover saturation animation — state snaps instead of fading." | §12.5, avalonia-ui | RULE |
| DS-56 | §8 | "`TextDim` on `Surface` measures **5.88:1**, and on `SurfaceRaised` ... **5.04:1**. Do not dim further." + `Text` 13.1:1, `Azure` 6.03:1, `Volt` on `Ground` 11.3:1 | §13 gap 7, §14.3, §14.7 | RULE + FINDING (split) |
| DS-57 | §8 | "Provide a settings toggle to disable the dormancy ramp entirely" | dormancy spike ("force α = 1") | RULE |
| DS-58 | §8 | "The caption buttons are real buttons ... `Danger` is never the only thing distinguishing close: it has its own glyph and its own tooltip." | §9, DS-09 | RULE |
| DS-59 | §9 | "The app draws its own title bar. Avalonia's `ExtendClientAreaToDecorationsHint` with `ExtendClientAreaChromeHints="NoChrome"`" | — | RULE |
| DS-60 | §9 | "**The caption takes the rail's colour — `Surface`, the same ink and the same alpha.**" | amended three times below | RULE for flush only (K-34) |
| DS-61 | §9 | "**Amended (§14).** This section used to read: *"`Well` is one step darker than `Ground`, not lighter ..."*" | — | DEAD (superseded text retained verbatim) |
| DS-62 | §9 | "`CaptionFill` *is* `ChromeSurface`, at every position on the slider." + "`ThemeContrastTests.The_caption_is_the_rail` asserts both halves." | §16.5 | RULE (enforced) |
| DS-63 | §9 | "**Amended again (§15) ...** Under the **floating layout** the caption does *not* take the rail's ink. It takes `Well`" | superseded in turn by DS-65 | DEAD (K-34) |
| DS-64 | §9 | "`FloatingLayoutTests.The_caption_is_the_ground` holds for floating" | §15.6, §16.3 | RULE (enforced) |
| DS-65 | §9 | "**Amended a third time (§16) ...** **Floating: the caption paints nothing at all past `SOLID`** and the ground shows through it." | §16.5 | RULE |
| DS-66 | §9 | "`Well` survives, one step below `Ground`, on the two surfaces where a tone under the art field is still the point: the scrollbar track and the detail modal's scrim." | §15.2 adds a third use | RULE |
| DS-67 | §9 | "The mark at the left is two 2:3 capsules ... Nothing else lives in the caption — no menu, no search, no status. It is a lip, not a toolbar." | §15.8, CL-06 (the "hoard" sentence) | RULE |
| DS-68 | §9 | "Drag uses `BeginMoveDrag` ... the title bar therefore tests both the framework's count and its own press clock (500ms, 8px, in *screen* coordinates) ... deliberately not also wired to `DoubleTapped`" | — | RULE + DECISION (split) |
| DS-69 | §9 | "`OffScreenMargin` is applied to the window's root panel. ... without it the caption and the first column of tiles are clipped the moment the window is maximised." | — | RULE |
| DS-70 | §9 | "The middle button says what it will do, not what state the window is in" | — | RULE |
| DS-71 | §9 | "**Scrollbars** keep Fluent's `ScrollBar` theme ... `Application.Resources` outranks `Application.Styles` in Avalonia's lookup ... the stepper arrows are hidden, and the resting thumb is widened ... to 4px" | — | RULE + FINDING (split) |
| DS-72 | §9 | "**The thumb is neutral, never `Volt`**" | DS-09 | RULE |
| DS-73 | §9.1 | "Measured on this window: the band is exactly **8px** on the right and 8px on the bottom." | §15.3 | FINDING |
| DS-74 | §9.1 | "**`ScrollBarEdgeInset` (`0,0,10,10`) is the rule that follows: no interactive control may sit inside the 8px the OS owns**" | §15.4 retires it under floating | RULE, layout-conditional (K-35) |
| DS-75 | §9.1 | "interior ones — the rail's, the detail modal's — opt out with `ScrollViewer.inner`" | §11.1 | RULE |
| DS-76 | §9.1 | "The rule is about which edge a control is on, never about which control it is." | §15.4 | RULE |
| DS-77 | §9.1 | "Two alternatives were weighed and rejected. **Widening the thumb** ... **Hooking `WM_NCHITTEST`** ..." | — | DECISION |
| DS-78 | §10 | "It stays a **modal over the library**, opened by `Enter` or a double click, dismissed by `Escape` or a click on the scrim." | §12.4 | RULE + DECISION (split) |
| DS-79 | §10.1 | "**Two columns, split by what they are about** ... The divider spans the right column only" + the four-block order (what is this / my history / get me in / the rest) | — | RULE |
| DS-80 | §10.2 | "**The rule is §5.1's dormancy ramp turned on its side** — `Volt` at the last-played end fading to `Line` at today" | §5.1 | RULE |
| DS-81 | §10.2 | "**Marks are `Flare`**, legal here and only here in the panel ... Capped at 14" | DS-08 | RULE |
| DS-82 | §10.2 | "**The rail is normalised, never scaled to duration.**" | — | RULE + DECISION (split) |
| DS-83 | §10.2 | "**Everything it draws is restated in words underneath** (§8: the encoding is decorative-redundant)." | DS-52 | RULE |
| DS-84 | §10.2 | "**No last-played date, no rail.** Two different absences, kept apart by the copy" | §10.4 | RULE |
| DS-85 | §10.2 | "**It is deliberately not a playtime chart.** ... On a real library that table holds **one reading per game** — measured, 611 of 616" | recommendation-engine cold-start tiers | DECISION + FINDING (split) |
| DS-86 | §10.3 | "`steam://run/<appid>` when the game is on disk, `steam://install/<appid>` when it is not — and the button is **named for which one it is**" | RD-47 | RULE |
| DS-87 | §10.3 | "No appid means no primary action at all, never an inert button." | — | RULE |
| DS-88 | §10.3 | "The folder goes through the launcher's directory entry point as a path — never a `file:` URI" | DS-89 | RULE |
| DS-89 | §10.3 | "**Every outbound target is built by `GameLink.Create` and nothing else.** Three schemes are allowed — `https`, `http`, `steam` ... **A target that fails validation is a null link, and a null link renders no button**" | RM-27 (adjacent) | RULE |
| DS-90 | §10.4 | Copy table, eight rows, including "No updates recorded in that stretch."; "Checked 12 times since 23 Aug 2026 — up 1h 7m."; "Steam has no date for your last session."; "You've never opened this." | §7, notes.md (K-32) | RULE |
| DS-91 | §10.4 | "**"No updates recorded in that stretch"** and not "nothing has shipped": update polling is staggered across days ... the interface may only claim the one it can support." | RM-10, GD-44 | RULE + DECISION (split) |
| DS-92 | §10.5 | "`acquired_at`, `license_type`, `price_paid_cents`, `platform`, `edition_note` ... are absent from the markup entirely rather than bound and hidden: a row that can never appear is dead weight impersonating a feature." | RD-59 (they are populated now for imported rows) | RULE, premise stale (K-36) |
| DS-93 | §10.5 | "**Achievements are not here.** ... §6.2's rule stands regardless: never a blended cross-platform completion figure." | GD-83 | RULE |
| DS-94 | §10.6 | "Titles, summaries, install paths and appids are `SelectableTextBlock`, not `TextBlock`." + "`tokens.axaml`'s text styles select on `:is(TextBlock)`, not `TextBlock`." | — | RULE + FINDING (split) |
| DS-95 | §10.6 | "The four worth stopping on keep it; everything else sets `Focusable="False"`." | §11.1 tab order | RULE |
| DS-96 | §10.6 | "**Focused text gets a raised field, not a ring.**" | §10.7 | RULE |
| DS-97 | §10.7 | "§8's global `FocusAdorner` did not deliver a visible ring in this panel — measured on the running window" | §13 gap 6 | FINDING |
| DS-98 | §10.7 | "**The ring is a brush swap on a border whose thickness never changes.** ... It is set on `PART_ContentPresenter` rather than on the Button" | §14.7, §13 gap 6 | RULE (K-33) |
| DS-99 | §10.7 | "The launch button is the one place the ring is not `Volt` ... it is `VoltInk`" | §13 gap 6 | RULE |
| DS-100 | §10.7 | "**No flyout anywhere in this panel, deliberately** — an adorner needs an adorner layer and a popup is its own root" | §12.3 | RULE + DECISION (split) |
| DS-101 | §10.7 | "**Tab order follows the tree, not `TabIndex`.** Avalonia's tab navigation walks declaration order and ignores `TabIndex` on a non-focusable container — measured, not assumed." | §11.1 | RULE + FINDING (split) |
| DS-102 | §11 | "Steam's library filter is the reference and not the template. ... **Two things the reference gets right, kept.** A count beside every option ... And one surface you scan rather than a menu you drill into." | §11.2 | DECISION + RULE (split) |
| DS-103 | §11.1 | "`Filters` opens a **276px column to the right of the grid**, on the rail's own `Surface`. It is not a drawer over the art and not a popover." | §14.2 (`ChromeSurface`), §16 (now a pane) | RULE |
| DS-104 | §11.1 | "**Its left edge is the window's other chrome boundary.** ... 1px `Line`, `Surface` behind it ... **the panel is a peer of the rail rather than a second column of it.**" | §14.7, §16 use this as the argument for the tier change | RULE |
| DS-105 | §11.1 | "**Its header is 48px, the command bar's height**, so the rule under `FILTERS` continues the rule under the command bar straight across the window." | §15.7, §15.8 (a join, then a continuation again) | RULE |
| DS-106 | §11.1 | "**The rail is still not duplicated** ... the rail owns the bucket axis; the panel owns every other one; neither offers the other's." | §11.3, §12.2 | RULE |
| DS-107 | §11.1 | "**the cut bar (§11.3) now carries it alone** — the bucket is a chip there beside the panel's own" | §11.3 | RULE |
| DS-108 | §11.1 | "**Its right edge is the window's, so §9.1 applies to it and did not before.** ... the column went 264 → 276 to pay for the gutter" | DS-74, §15.4 | RULE (K-35) |
| DS-109 | §11.1 | "**Tab order follows the window in reading order** — rail, command bar, grid, panel — which means the panel is last in the file as well as last on screen." | DS-101 | RULE |
| DS-110 | §11.1 | "The grid narrows rather than being covered." | §12.5 | RULE |
| DS-111 | §11.2 | "The number beside an option is **what you would get if you ticked it** — computed with every *other* group's selections applied, this group's own selections lifted" | — | RULE |
| DS-112 | §11.2 | "An option whose residual count is **0 renders its zero and stops being a click target and a tab stop**, at the 40% opacity §6 already gives a zero-count bucket. An option that is *ticked* stays live whatever its count says" | DS-42 | RULE |
| DS-113 | §11.2 | "**Order freezes on the first counts.** A long group leads with its commonest options and then holds that order for the session." | — | RULE + DECISION (split) |
| DS-114 | §11.2 | "Counts are taken **per tile, not per release** ... which is why the panel tallies its own sets rather than calling `FacetSnapshot.CountsFor`" | amended by DS-115 | RULE |
| DS-115 | §11.2 | "**Grid grain changed (TASK-70.6, 2026-09-01).** The grid is now one tile per game rather than one per ownership ... the per-store figures sum to more than All Games by exactly the number of extra store memberships." | DS-35, DS-45 | RULE + FINDING (split) |
| DS-116 | §11.3 | "One strip under the command bar, present only when the grid has stopped showing the whole library" | §15.8 (moved inside the library pane) | RULE |
| DS-117 | §11.3 | "**`926 → 136` is the signature of this screen.** It is the only arrow in the interface ... Plex Mono, tabular; the total in `TextDim`, the result in `Volt`." | DS-16 | RULE |
| DS-118 | §11.3 | "*a library that has been cut down and does not say so is the most expensive confusion this screen can produce* ... then 136 of 926 games look like the whole hoard." | CL-06 (a deliberate "hoard" site) | DECISION |
| DS-119 | §11.3 | "**Chips are `Volt`-edged, never `Flare`.** ... There is deliberately **no "has updates" group** anywhere in the panel" | DS-08, §11.4 | RULE |
| DS-120 | §11.3 | "**Every chip says who set it** ... three families and only two edges" + the three-row table (the open list; a rule the list brought; a rule you set) | §12.2 | RULE |
| DS-121 | §11.3 | "The distinction is never carried by the edge alone: each chip's tooltip says it in words (§8)" | DS-52 | RULE |
| DS-122 | §11.3 | "**The open list leads the bar, ahead of the bucket.**" | §12.2 | RULE |
| DS-123 | §11.3 | "The bar carries at most four actions at once, and membership actions and list metadata are mutually exclusive" | §12.3 | RULE |
| DS-124 | §11.4 | Drawn groups: "`genre` · `theme` · `game mode` · `store tag` · `features` · `controller` · `store` · `on disk` · `release year`" | `docs/facet-provenance.md` | RULE |
| DS-125 | §11.4 | "**Every group here is a group a live list can store.** That is the rule. `FacetKinds` also holds player perspective, which `LibraryFilter` has no field for — so it is not drawn" | — | RULE |
| DS-126 | §11.4 | "**A dimension with no data draws nothing.** ... When none of the metadata-backed groups are present the panel says so in a sentence instead." | §13 gap 1 | RULE |
| DS-127 | §11.4 | "**A dimension whose one option is true of every title draws nothing.**" | — | RULE |
| DS-128 | §11.4 | "**Release year is two Plex Mono fields, not a slider and not a histogram.** ... A drawn year distribution was considered and cut" | DS-04 | RULE + DECISION (split) |
| DS-129 | §11.4 | "A release with no year does not match a bounded range — an absent fact is not evidence." | RM-27 | RULE |
| DS-130 | §12 | "**A list is one the user fills by hand. A live list is one that holds a rule and finds its own members.** Never "smart", never "dynamic collection" ... The action on the cut bar is **`Save as live list`**." | §7 | RULE |
| DS-131 | §12.1 | "The kinds are told apart **by heading, not by a coloured mark**. ... the rail already has exactly one dot" | DS-41 | RULE + DECISION (split) |
| DS-132 | §12.1 | "**the name is body type, not Display S caps.** Bucket names are the application's own vocabulary and are shouted; a list name is the user's own sentence and is not." | DS-17 | RULE |
| DS-133 | §12.1 | "Both kinds recount on every library load." | §11.2 | RULE |
| DS-134 | §12.1 | "`LISTS` is the heading that always exists ... `LIVE LISTS` appears only once there is one." + the empty-state sentence | RD-64 (rail grammar) | RULE |
| DS-135 | §12.2 | "**A manual list is one more AND term** over the library, not a separate screen" | — | RULE |
| DS-136 | §12.2 | "**A live list adds no term at all.** Opening one pours its saved rules back into the rail and the panel" | §11.3 | RULE |
| DS-137 | §12.2 | "Editing an open live list turns the cut bar into `Update list` / `Revert`" | §12.3 | RULE |
| DS-138 | §12.2 | "**So a list is a context, not a switch.** You are in exactly one at a time, and selecting `All games`, a bucket, or another list *leaves* the one you were in and takes its contribution with it." | §11.3, §12.4 | RULE |
| DS-139 | §12.2 | "**The panel stays open on the way out.**" | — | RULE |
| DS-140 | §12.2 | "**Clicking the bucket you are on does not clear it while a live list is open.**" | §12.4 | RULE |
| DS-141 | §12.2 | "**The rail carries the same distinction.** The `Volt` edge means *this is where you are*, and exactly one row ever has it ... a bucket in force takes the selection fill with a `TextDim` edge" | DS-41, DS-120 | RULE |
| DS-142 | §12.2 | "A checked box is `Volt` whoever ticked it ... The bar carries provenance; the panel carries state." | DS-120 | RULE + DECISION (split) |
| DS-143 | §12.2 | "A manual list opens in **`List order`**, a sort row that exists only while one is open ... `Move up` and `Move down` go dead at the ends" | — | RULE |
| DS-144 | §12.3 | "Naming a live list, picking a list to add to, renaming one and confirming a delete all happen in **the same strip**, replacing the cut bar while they are up." | §10.7, §11.3 | RULE |
| DS-145 | §12.3 | "Avalonia's global `FocusAdorner` does not render inside a popup — a popup is its own root and has no adorner layer" | DS-100, §13 gap 6 | FINDING |
| DS-146 | §12.3 | "`Enter` confirms, `Escape` cancels, and focus follows the prompt into its field. The save prompt opens with the rules read out as a suggested name" | §12.4 | RULE |
| DS-147 | §12.3 | "**`Add to list` is one control for both views.** ... The picked set is derived from the selection in the view model rather than in the pointer handler" | DS-54 | RULE |
| DS-148 | §12.3 | "Deleting asks first, and the question says what survives ... It is the only destructive act in the application, and `Danger` appears on its confirm button and nowhere else on the strip." | DS-09 (K-25) | RULE |
| DS-149 | §12.4 | "Outermost first: the panel closes; then an unsaved edit ... reverts; then the filters clear — *unless* a live list is open ...; then the open list closes ...; then the bucket clears. One key, and no press is ever a no-op" | §12.2 | RULE |
| DS-150 | §12.4 | "**Every letter key yields to a focused text field.**" | — | RULE |
| DS-151 | §12.5 | "Nothing here animates except the 120ms fill cross-fade the rail rows already had, and every `Transitions` value is set **through a style, never as a local value on an element**." | DS-55 | RULE + DECISION (split) |
| DS-152 | §12.5 | "The command bar's search box became a **star-sized column among Auto ones**, and the window's default width went from 1180 to 1280." | §15.8 | RULE |

## 1.7 `design-system.md` §13-§16 (lines 1035-2163)

| ID | Anchor | Verbatim claim | Also claimed in | Class |
|---|---|---|---|---|
| DS-153 | §13 gap 1 | "**Indeterminate progress has no rule.** ... **If an animated indicator ever ships, §8 needs the rule first.**" + "sign-in shows a Volt-edged status field saying where to look, plus Cancel" | DS-51, DS-55 | RULE (the gate) + FINDING (the interim) |
| DS-154 | §13 gap 2 | "**No colour role for "optional, and deliberately not connected."** ... Used a `Line`/`TextDim` pill; it wants to be a named component in §6." | DS-09 | UNRESOLVED (U-05) |
| DS-155 | §13 gap 3 | "**§6 has no single-row rail section that opens a screen.** REVIEW/`SAME GAME?` works because the row states a question. SOURCES/PLATFORMS is mildly redundant" | RD-64, backlog TASK-60 | UNRESOLVED (U-06) |
| DS-156 | §13 gap 4 | "**§7's copy table has no rows for connection state or credential consent.** ... "Not signed in" vs "Disconnected", "Session expired" vs "Error" ... are all decisions the table should own" | DS-49 | UNRESOLVED (U-07) |
| DS-157 | §13 gap 5 | "**No reading-measure rule.** ... Used 12/18 capped at 720px. That belongs to the system, not to one file." | DS-46 (840px card, "§13 gap 5's provisional 720px prose measure was read and does not govern") | UNRESOLVED (U-08) (K-31) |
| DS-158 | §13 gap 6 | "**§8 and §10.7 disagree about focus.** ... **The two sections should be reconciled** — right now which one is authoritative depends on which you read first." | DS-53, DS-98, §14.7 | UNRESOLVED (U-09) (K-33) |
| DS-159 | §13 gap 7 | "**RESOLVED in §14.** There is no rule for translucency, and Mica needed one." + the interim rule "translucency is confined to the caption strip, and nothing else in the application is translucent" and its 85%-over-white table | §14.2, §14.3 | DEAD (the interim rule and its conclusion) |
| DS-160 | §14 | "**Dark-only is still true; one-palette is not.** Four themes ship, the default is unchanged, and a transparency **slider** sits beside them. Both settings live on the rail's `SETTINGS › APPEARANCE` screen and persist in `settings`." | DS-01, RM-21 | RULE |
| DS-161 | §14.1 | "**The role is the invariant; the colour is not.** ... It may never change what a job means, and it may never spend one job's colour on a second one." | DS-08, RM-21 | RULE |
| DS-162 | §14.1 | "**`Flare` is the load-bearing case.** ... no theme's `Volt`, `Amber`, `Azure` or `Danger` may equal it. `ThemeContrastTests` asserts that per theme, along with a minimum hue separation from `Danger` (24°) and from `Volt` (60°)." | DS-08, `tests/Winnow.Tests/ThemeContrastTests.cs` | RULE (enforced) |
| DS-163 | §14.1 | "every theme's `Volt` is its own room at full voltage ... And every theme's `Flare` is the one hue that room cannot produce." | DS-07 | RULE |
| DS-164 | §14.1.1 | "The four themes that shipped first — Winnow, Cold storage, Nightshift, Phosphor — differed in **hue and value and nothing else** ... Two were withdrawn." | — | DEAD + DECISION (split) |
| DS-165 | §14.1.1 | Four-axis separation table (temperature, chroma strategy, value structure, material) + "**The test of the set is that a thumbnail of the rail alone identifies the theme, with no label.**" | — | RULE |
| DS-166 | §14.1.1 | "**No light theme, deliberately.** ... A light theme is not this table with the steps reversed; it is a second pass over all three" | DS-01 | RULE + DECISION (split) |
| DS-167 | §14.1.1 | The four shipped themes and their one-sentence identities (Winnow default, Nightshift, Tungsten, Box art) | RM-21, `docs/screenshots/appearance/` | RULE |
| DS-168 | §14.1.1 | "**Two costs, stated rather than hidden.** Tungsten ... `Volt` (brass, 43°) and `Amber` (ember, 16°) sit 27° apart ... Box art ... `Volt` and `Azure` sit 29° apart" | DS-162 (the asserted separations are Flare-to-Danger and Flare-to-Volt only) | FINDING |
| DS-169 | §14.2 | "**Amended (§16): there are two tiers, not three, and the table below describes the middle one as though it still exists.**" | §16 | DEAD (the table) + RULE (the amendment) (K-37) |
| DS-170 | §14.2 | "Which surface may admit the desktop is a **token**, not a rule somebody has to remember." + the five-grounds table (`ShellGround`, `WallGround`, `PaneGround`, `TileGround`, `ChromeSurface`, `ChromeGround` retired, `CaptionFill`, `ChromeRaised`, `ChromeRaisedHalf`, `ChromeFieldOnGround`, `ChromeFieldOnSurface`) | §16.1 | RULE, partly DEAD (K-37) |
| DS-171 | §14.2 | "**Popovers keep an opaque fill.** A flyout is its own popup root and never receives the window's backdrop" | §14.7 | RULE |
| DS-172 | §14.2 | "**`ChromeRaised` is a veil, not an ink** ... the veil is `Text` and the only free parameter is its strength ... grows to 10%" | DS-22 | RULE + DECISION (split) |
| DS-173 | §14.3 | "**The previous measurement was right and the conclusion was wrong.** ... What it actually proves is narrower: *an ink chosen for an opaque ground cannot have alpha subtracted from it.*" | DS-159 | DECISION |
| DS-174 | §14.3 | "**Dark Mica cannot produce translucency at any alpha.** ... back-solved ..., the backdrop is `#201F1E` **whether the wallpaper under the window is orange rock or blue sky**." | §14.6 | FINDING |
| DS-175 | §14.3 | "**So the hint order changed to `[AcrylicBlur, Mica, None]`.**" + the reversal of the earlier Mica-over-acrylic decision | §14.6 (the head of the list is now the user's choice) | DEAD as stated, superseded by DS-183 (K-38) |
| DS-176 | §14.3 | "**a slider, 0 to 100, stored as a whole percent under the same `appearance.transparency` key** ... A stored `true` migrates to 25; a stored `false` to 0." | RM-21 | RULE |
| DS-177 | §14.3 | "**Zero is a real position, not an off state dressed as one.** ... the label under that end of the track is a word (`SOLID`)" | §16.8 | RULE |
| DS-178 | §14.3 | "~~**The far end admits 70% desktop.** `MinChromeAlpha` is `0.30`.~~ **Retired (§16)** ... The far end admits **85% on the window's ground** and **35% on every pane**" | §16.1 | DEAD + RULE (split) |
| DS-179 | §14.3 | "**Alpha falls linearly** across the whole track: `1 → 0.30`. **The inks finish in the first quarter** (`InkRampSpan = 0.25`) and then hold." | §16.3, §16.4 | RULE (the ink ramp) + DEAD (the `1 → 0.30` figure) |
| DS-180 | §14.3 | AA measurement table + "the AA ceiling lands at 27% (Winnow), 30% (Nightshift), 30% (Tungsten), 26% (Box art)" | superseded by §16.6 | DEAD (K-39) |
| DS-181 | §14.3 | "**Over a dark desktop the number never gets worse.** ... `ThemeContrastTests` asserts that at every position on the slider, for every theme." | §16.6 | RULE (enforced) |
| DS-182 | §14.3 | "**Requested is not active.** ... the test names the levels that count (acrylic, blur, Mica) rather than testing "not `None`". When the answer is no, transparency is treated as zero and the settings screen says so in words." | §14.6 | RULE (enforced) |
| DS-183 | §14.6 | "the head of the hint list is the user's choice: acrylic asks `[AcrylicBlur, Mica, None]`, Mica asks `[Mica, AcrylicBlur, None]`. **Acrylic stays the default**" | DS-175 | RULE |
| DS-184 | §14.6 | "**A substitution is a third answer, not the second one.** ... the material that came back is reported **by name** and the screen says so in an `Amber` field." | DS-09 (K-25) | RULE |
| DS-185 | §14.6 | "**the field may open up, the tiles may not.** Covers sit solid on an open field" | §14.4 | RULE |
| DS-186 | §14.6 | "The clause that used to follow — *"the list view, the merge queue, Stores and Appearance are text sitting directly on it, so they take `PaneGround` and stay solid at every setting"* — **is withdrawn**" | §14.7 | DEAD |
| DS-187 | §14.6 | "**The wall admits exactly half the desktop the chrome does.** `MinWallAlpha` is `0.65`" + the §16 amendment "the constant survives and the relation it was stated in does not ... the ground admits 85%, a pane admits 35%" | §16.1 | RULE (the constant) + DEAD (the relation) |
| DS-188 | §14.6 | "the constraint is not contrast — it is **polarity**. §5.1's ramp is dark capsules on a dark field and only reads that way while the field stays *darker* than the capsules on it." + the four-row derivation table | DS-23 | RULE + DECISION (split) |
| DS-189 | §14.6 | "**Measured on the running window, this is not only a white-wallpaper argument.** ... At half reach the field lands at luminance **0.020–0.024** — under the dormant capsule's **0.031**" | §16.6 | FINDING |
| DS-190 | §14.6 | "**The Appearance screen prints both numbers** ... in Plex Mono `tnum`, so the relation is visible rather than asserted. It is a ratio and not a second slider on purpose" | RD-21, DS-16 | RULE + DECISION (split) |
| DS-191 | §14.6 | "Both preferences persist beside theme and transparency, under `appearance.backdrop` (`acrylic` / `mica`, unset reads as acrylic) and `appearance.wall` (unset reads as *off*)" | RM-21 | RULE |
| DS-192 | §14.4 | "Each tile therefore paints `TileGround` under its art stack, opaque in every theme and every setting ... That is a fact of construction, not a measurement that could drift." | §14.6, §14.7 | RULE |
| DS-193 | §14.4 | "at the far end of the slider with the wall open and dimming on, 187,192 pixels in the wall region differ ... Not one pixel inside a tile changed." | — | FINDING |
| DS-194 | §14.7 | "**Amended (§16): the argument below was right and did not go far enough.** ... `MinFieldAlpha` is **no longer a half** ... the ceilings in the table are measured against a chrome tier that no longer exists." | §16.1 | DEAD (the constants) + RULE (the identity) |
| DS-195 | §14.7 | "**The verdict that opened this: half a translucent window is worse than none of it.**" | §15.8 | DECISION |
| DS-196 | §14.7 | Per-theme AA ceiling table for chrome / pane / selected pane row / pane `Text` / input placeholder / input `Text` | superseded in part by §16.6 | FINDING, partly DEAD (K-39) |
| DS-197 | §14.7 | "So `PaneGround` **is** `WallGround` — the same alpha, the same ink, and the same *setting*. It answers `appearance.wall` rather than opening on its own" | §16.1 | RULE |
| DS-198 | §14.7 | "**Three surfaces did not move** ... `TileGround` stays opaque ... The popovers stay opaque ... **polarity does not reach the panes**" | DS-171, DS-192 | RULE |
| DS-199 | §14.7 | "**The list view was a fourth ground hiding inside the third.** ... The rows take `PaneGround` now and the column-header strip takes `ChromeSurface`" | DS-43 (K-30) | RULE |
| DS-200 | §14.7 | "Its row fills ... take `ChromeRaised` and a new `ChromeRaisedHalf` ... Walked, the elevation never once inverts" | DS-172 | RULE |
| DS-201 | §14.7 | "A field is a **child** of the bar or panel it sits in, so the two alphas **stack**" + `fieldAlpha = 1 − (1 − MinWallAlpha) / (1 − containerAlpha)` | §16.1 | RULE |
| DS-202 | §14.7 | "~~**`MinFieldAlpha` is `0.50`**~~ — **it is `0` now (§16.1)** ... `ThemeContrastTests` asserts the identity rather than the constant" | §16.1 | DEAD (the constant) + RULE (the assertion) |
| DS-203 | §14.7 | "**And it follows the wall's setting rather than the slider's.**" | DS-197 | RULE |
| DS-204 | §14.7 | "**One more thing was carried over and should not have been: the ink.** ... A field cut into an un-walked ground must be un-walked too" | §16.4 | RULE |
| DS-205 | §14.7 | "the field's alpha finishes in the first quarter, on `InkRampSpan` ... Slider zero is still bit-for-bit opaque, and nothing jumps leaving it." | DS-179, §16.3 | RULE |
| DS-206 | §14.7 | "**A field is found by its border, and lit by its ring.** ... `Line` draws it and `Volt` says it has the caret." | DS-09 | RULE |
| DS-207 | §14.7 | "**Focus stays §10.7's brush swap on a border whose thickness never changes** (§13 gap 6 records that §8 and §10.7 disagree; §10.7 is what the rest of the app follows)" | DS-53, DS-98, DS-158 | RULE, and the only place §13 gap 6 is answered (K-33) |
| DS-208 | §14.7 | "**The year field's watermark was `TextFaint`, which measures 4.13 / 3.69 / 3.58 / 4.12 on the *opaque* ground: under AA at `SOLID`** ... It is `TextDim` now" | DS-56 | FINDING + RULE (split) |
| DS-209 | §14.5 | "`<SolidColorBrush x:Key="X">#16282A</SolidColorBrush>` and `<SolidColorBrush x:Key="X" Color="#16282A"/>` look identical and are not ... a folded brush is a token the theme system silently cannot reach. Measured, not assumed: the first build had thirty-five of them" | CL-16 | RULE + FINDING (split) |
| DS-210 | §15 | "**A second arrangement, behind a setting, default off.** ... **It is structure, and the two settings it sits beside are not.**" | §15.5 | RULE + DECISION (split) |
| DS-211 | §15.1 | Region table: caption flush; command bar and cut bar "~~Flush~~ → **inside the library card**"; rail, wall, merge/Stores/Appearance and filter panel are cards; detail modal full bleed | §15.8 | RULE |
| DS-212 | §15.1 | "**The original text.** *"The command bar was the one judgement call, and it is settled: flush ..."*" | §15.8 | DEAD (superseded text retained verbatim) |
| DS-213 | §15.2 | "**`ShellGround` is inked `Well`** under this layout ... the deepest tone is the *right* one for the space behind everything" | DS-66, §16.1 | RULE + DECISION (split) |
| DS-214 | §15.2 | "The order that follows is one direction and holds in every theme: `Well < Ground < Surface`" | §16.4 | RULE |
| DS-215 | §15.2 | "**§5.1's polarity is untouched.** The wall island is `WallGround` exactly as before" | DS-188 | RULE |
| DS-216 | §15.3 | "**The gap is 8px.** ... **it is exactly the width of the resize band §9.1 measures.**" | DS-19, DS-73 | RULE + DECISION (split) |
| DS-217 | §15.3 | "**One pane owns each gap.** ... the rail gives up its right margin, the library pane owns both of its own gutters, and the filter panel gives up its left one." | — | RULE + FINDING (split) |
| DS-218 | §15.3 | "**The radius is 8px**, above the tile's 6 and the control's 4." | DS-21 | RULE |
| DS-219 | §15.3 | "**The rail's column becomes `Auto` with the pane carrying its own 220.** ... The column widens; the rail does not narrow." | DS-103 | RULE |
| DS-220 | §15.4 | "**§9.1 is retired here, not kept** ... It is dropped under this layout and kept under the other" | DS-74, DS-108 | RULE (K-35) |
| DS-221 | §15.5 | "Its own `LAYOUT` section on the Appearance screen, **under THEME and above TRANSPARENCY.**" | DS-160 | RULE + DECISION (split) |
| DS-222 | §15.5 | "**It is drawn the way THEME is drawn and not the way the qualifiers are** ... the miniature is not an illustration of the setting — it is the setting at 1/8 scale." + "A layout card is repainted from whichever theme is up" | — | RULE |
| DS-223 | §15.5 | "Persisted under `appearance.layout` (`flush` / `floating`; unset reads as flush). The debug capture flag is `--layout=flush|floating`, session-only and sealed against writing" | DS-191, CL-31 | RULE |
| DS-224 | §15.6 | "~~**Nothing §14 measured moved.**~~ **Superseded (§16.7)** ... `Colorimetry.AaCeiling` therefore walks **both** layouts and reports the worse" | §16.7 | DEAD + RULE (split) |
| DS-225 | §15.6 | "**The token count went four to two.** ... `FloatingLayoutTests` asserts every other token is bit-for-bit identical between the two." | §15.8 | RULE (enforced) |
| DS-226 | §15.6 | "**The panes never composite TWICE, and that is the whole construction.**" + the §16.3 amendment "The *once* is asserted directly in `FloatingLayoutTests`" | §16.3 | RULE (enforced) |
| DS-227 | §15.7 | "~~At `SOLID` the ground is one field; past it, it is a field with brighter slots cut in it.~~ **Repealed (§16.5).**" | §16.5 | DEAD |
| DS-228 | §15.7 | "**The gap tone does almost no work under the library pane.** Measured, `Well`-against-`Ground` comes out at 1.13:1 in Winnow and 1.02–1.06:1 in the other three ... **Tungsten is the weakest of the four**" | §16.3 | FINDING |
| DS-229 | §15.7 | "**§11.1's rule across the window is now a join rather than a continuation.**" + "**Mostly repaid by §15.8** ... The scanline is still y=92" | DS-105, §15.8 | FINDING |
| DS-230 | §15.8 | "**Those controls are not window chrome.** ... They are the library pane's **header**, so they are inside its card — in *both* layouts" | DS-116, DS-211 | RULE |
| DS-231 | §15.8 | "**One top edge.** The rail, the library and the filter panel now all begin on the same scanline immediately under the caption." | DS-105 | RULE |
| DS-232 | §15.8 | "**A visibility rule became a fact of composition.** ... no arrangement of those four panes can put a settings screen under the library's controls." | — | RULE + DECISION (split) |
| DS-233 | §15.8 | "**The cut bar goes with it, under the command bar and above the art.** ... Both bars keep their 1px rule in both layouts" | DS-116 | RULE |
| DS-234 | §15.8 | "**`Filters` still toggles a sibling island from inside the library pane.** Slightly odd, and left alone" | DS-103 | DECISION |
| DS-235 | §15.8 | "`ChromeGround` is retired: the pane paints its ground once and the bars sit on it" | DS-170, §16.4 | RULE |
| DS-236 | §15.8 | "Nothing the Appearance screen reports moved: the AA ceiling is 27 / 31 / 30 / 26 before and after." + "**Those figures are the last ones this document reports for the three-tier window (§16).**" | §16.6 | DEAD (K-39) |
| DS-237 | §16 | "**Asked for on aesthetic grounds and it re-derived four constants.**" + the three-tier table it replaces | §14.2 | DECISION |
| DS-238 | §16 | "**The rail and the filter panel are content columns by the same test** — §11.1 calls the panel "a peer of the rail" ... Nothing about them is chrome except a token name." | DS-104, §14.7 | RULE + DECISION (split) |
| DS-239 | §16.1 | "`alpha = 1 − (1 − MinWallAlpha) / (1 − containerAlpha)`" + the forced table: `ShellGround` admits 85% painting `MinShellAlpha 0.15`; any pane admits 35% painting `MinPaneAlpha 0.588`; any input field admits 35% painting `MinFieldAlpha 0` | DS-201, DS-202 | RULE |
| DS-240 | §16.1 | "`MinWallAlpha` is still `0.65` and **its derivation is untouched** ... the constant now names an *admission* rather than a paint." | DS-187 | RULE |
| DS-241 | §16.2 | "the bar is the mirror image of the one `MinWallAlpha` is held to — **the restructure may not cost the user range they already have.**" + the ground-alpha walk table and "`0.15` ... **chosen**" | §16.6 | RULE + DECISION (split) |
| DS-242 | §16.2 | "**A second route lands within a point and a half of it.** ... Transmittances compose by **multiplying** ... `√(1.00 × 0.70) = 0.837`, an alpha of `0.163`." | — | DECISION |
| DS-243 | §16.3 | "`ShellGround` used to be a **step** ... That is a fact about a ground that fades in proportion. It is not a fact about this one, **because the layer above it finishes early.**" + the linearity table | DS-179, DS-226 | RULE + DECISION (split) |
| DS-244 | §16.3 | "**The ground's ink bleeds into the panes, and it was measured** ... the worst tone difference is **1.06 to 1.11:1**" | DS-228 | FINDING |
| DS-245 | §16.4 | "§14.3's ink ramp is a **chrome** compensation ... There is no chrome. ... `TranslucentSurface` is **below `Ground`** in three of the four themes" | DS-179 | RULE + FINDING (split) |
| DS-246 | §16.4 | "§14.2's recess ... is therefore carried by the **ink** now rather than by the alpha: `Surface` over `Ground`, both unwalked, at one shared alpha" | DS-214 | RULE |
| DS-247 | §16.4 | "**`TranslucentSurface` is retired with the tier it belonged to** ... The field stays on the record and in the theme format so that no user theme needs editing; nothing reads it." | RM-21 | RULE |
| DS-248 | §16.5 | "**Floating: the caption paints nothing at all and the ground shows through it.** ... **§15.7's first cost is repealed.**" | DS-65, DS-227 | RULE |
| DS-249 | §16.5 | "**Flush: §9's amendment stands exactly as written.** ... the caption is still `ChromeSurface`, same ink and same alpha" | DS-60, DS-62 | RULE |
| DS-250 | §16.5 | "**over a bright wallpaper it does not** [hold] — the ground is the most open surface in the window, so the caption and the gaps are the brightest band in it, together." | DS-67, §15.7 | FINDING + DECISION (split) |
| DS-251 | §16.6 | The ten-row measurement table: reported AA ceiling before 27/31/30/26, after **30/31/31/31**; selected rail row after 40/54/47/41; rail labels 56/69/61/57; caption on the ground 30/31/31/31; caption in flush 56/69/61/57; pane `TextDim` 63/71/68/74; selected list row 48/56/53/56; polarity floor 29→34, 46→47, 38→41, 44→44 | DS-180, DS-196, DS-236, §16.9 | FINDING (current) |
| DS-252 | §16.6 | "**The mark was never about the rail. It is about whichever surface is most open and carries text**" | §16.7 | RULE + DECISION (split) |
| DS-253 | §16.6 | "**Polarity clears the mark by 4 to 16 points**, so `MinWallAlpha` survives at `0.65` untouched." | DS-187, DS-240 | FINDING |
| DS-254 | §16.7 | "So `Colorimetry.AaCeiling` walks **both** layouts and reports the worse. The mark means one thing whichever layout is up" | DS-224, RD-21 | RULE (enforced) |
| DS-255 | §16.8 | "**Two panes at the same tier can still be in different states.** `appearance.wall` still gates the art field and the screens beside it, while the rail and the filter panel follow the slider alone." | DS-191, DS-197 | RULE + DECISION (split) |
| DS-256 | §16.8 | "**The typed text in the filter panel's fields lost four points.** ... it now runs out at **96% and 97%** on Winnow and Box art" | DS-239 | FINDING |
| DS-257 | §16.8 | "**The caption gives up seven points of its own range** in the floating layout — 38% to 31% on Nightshift" | DS-251 | FINDING |
| DS-258 | §16.9 | "**Every `compare--*` sheet in `docs/screenshots/appearance/` was captured before this, and several of them print numbers on their captions that are now wrong.** They are left as they are" + the eight-row "A sheet says / It is now" mapping table | `docs/screenshots/appearance/*` | DEAD (the sheets) + RULE (leave them) |
| DS-259 | §16.9 | "**The window itself is the record that is kept current** ... the screen measures the running window and reports the worst case live" | DS-190, RD-21 | RULE + DECISION (split) |

## 1.8 `notes.md` (26 lines)

`notes.md` opens with "Here is where notes/observations will be recorded with the intent that
they be addressed down the line with Claude". It does not say whether its items are binding
requirements or an unprioritised wish list. Three of them contradict shipped design-system
rules. Every row here is therefore UNRESOLVED, and Phase 6's step that moves them is blocked
on one adjudication (U-10). See K-32 and K-40.

| ID | Anchor | Verbatim claim | Also claimed in | Class |
|---|---|---|---|---|
| NT-01 | Features | "Option to ignore games not associated with your account (games associated with accounts on the same system show up currently)" | RD-66, backlog TASK-53 (appears already built) | UNRESOLVED (U-10) |
| NT-02 | Features | "Option to hide games from Winnow" | — | UNRESOLVED (U-10) |
| NT-03 | Features | "Option to enable/disable explicit 18+ content" | — | UNRESOLVED (U-10) |
| NT-04 | Features | "Option to manually find and assign metadata through IGDB" | GD-67 (the merge queue is the confirmation surface) | UNRESOLVED (U-10) |
| NT-05 | Features | "Loading indicator when metadata is being fetched" | contradicts DS-51 and DS-153 | UNRESOLVED (U-10) (K-40) |
| NT-06 | Features | "Gamepad compatible full-screen mode (with a clock, controller battery aware if possible)" | RD-19 M10, RD-48 | UNRESOLVED (U-10) |
| NT-07 | Features | "Open folder button to custom theme location" | RM-21, DS-88 | UNRESOLVED (U-10) |
| NT-08 | Features | "Add to list from details view" | DS-147 | UNRESOLVED (U-10) |
| NT-09 | Features | "Merge Feed and All Games into a single section" | RD-64 (rail grammar), DS-134 | UNRESOLVED (U-10) |
| NT-10 | Features | "Open patch notes in a contained webview" | DS-33, DS-89, RD-38 | UNRESOLVED (U-10) |
| NT-11 | Bugs | "Drop the over-explanatory text blurbs throughout the interface. Explanations should be short, straightforward, and unambiguous. No more than a few words." | contradicts DS-49, DS-50, DS-157 | UNRESOLVED (U-10) (K-40) |
| NT-12 | Bugs | "Transparency block has wayyyyy too much information. Users dngaf about this stuff. It's great for our design docs but don't put it in the UI" | contradicts DS-190, DS-259, RD-21 | UNRESOLVED (U-10) (K-40) |
| NT-13 | Bugs | "Patched Since > Patched" | contradicts DS-49 | UNRESOLVED (U-10) (K-32) |
| NT-14 | Bugs | "Bounced Off > Bounced ? Not sure on this one, it reads like you quit it but it's currently capturing games in active play" | contradicts DS-49, GD-76 | UNRESOLVED (U-10) (K-32) |
| NT-15 | Bugs | "Stores > Platforms" | DS-155, backlog TASK-60 (In Progress) | UNRESOLVED (U-10) |
| NT-16 | Bugs | "The flip side of the card and details should have some sort of alternate art dim underneath the information" | DS-34, DS-192 | UNRESOLVED (U-10) |
| NT-17 | Bugs | "Option to add games manually for things outside the supported platforms" | GD-54 (`Ingest.*` emits candidates) | UNRESOLVED (U-10) |
| NT-18 | Bugs | "Details button does not work on flip side of cards" | — | UNRESOLVED (U-10) |
| NT-19 | Bugs | "Corner radius on same game, settings panes is not consistent with the other panes" | DS-21, DS-218 | UNRESOLVED (U-10) |

## 1.9 `docs/spikes/` (nine documents)

Spikes are evidence documents. Their normative weight comes entirely from CL-18 and RM-32,
which say they override the specs. Inventoried here are the claims that carry that override,
not every measurement in the files. Every row is a FINDING; the column on the right names the
spec claim it displaces, which is what makes it load-bearing.

| ID | Anchor | Verbatim claim | Displaces / also claimed in | Class |
|---|---|---|---|---|
| SP-01 | `steam-local-files.md` §3 | "Per-app keys (exact casing ...): `Playtime` PascalCase **minutes**, total; `LastPlayed` PascalCase **Unix epoch seconds**; `Playtime2wks` ... NOT `playtime2wks`, NOT `playtime_two_weeks` (the spec's candidate name)" | GD-26 | FINDING |
| SP-02 | `steam-local-files.md` §3 | "**`LastPlayed` sentinel `"86400"`** (= 1970-01-02) appears on many old entries ... Treat any value below a sanity floor (e.g. < 315532800, year 1980) as "unknown", not as a real date." | — | FINDING |
| SP-03 | `steam-local-files.md` §3 | "**Key order inside an app block is not stable.** ... Never parse positionally." + "App blocks may contain NO playtime keys at all ... Skip blocks lacking `Playtime`" + "**False-match hazard:** `UserLocalConfigStore/apptickets` is ALSO a map keyed by appid" | GD-25 | FINDING |
| SP-04 | `steam-local-files.md` §3 | "**Multiple accounts:** ... Enumerate all of them ... Attribute playtime per account (`CandidateOwnership` should carry the steam3id)." | RD-66, GD-20 | FINDING |
| SP-05 | `steam-local-files.md` §4 | "Top level is an **array of `[key, entry]` pairs** — NOT an object map." + tombstones (`is_deleted`) + "**Id alphabet includes `+`, `/` and `*`**" + "v1 should ingest static membership (`added` minus `removed`) and record but not evaluate `filterSpec`." | GD-20, GD-22 | FINDING |
| SP-06 | `steam-local-files.md` summary | "appmanifest field `LastUpdated` (implied casing) | on disk it is `lastupdated`; parse keys case-insensitively" | GD-20 | FINDING |
| SP-07 | `steam-local-files.md` summary | "§4.1's core claims all held: paths are correct, the 2025 collections path is live" | GD-20, GD-21, GD-22 | FINDING (confirms) |
| SP-08 | `steam-store-tags.md` §4.3 amendments | "It is `IStoreBrowseService/GetItems`; plain `IStoreService` has no tag method" + "`GetItems` is **keyless** and batches 100+ appids — the one-appid/35-hour math does not apply" + "Tag names need a second call, `IStoreService/GetTagList`" | GD-35, GD-33 | FINDING |
| SP-09 | `steam-store-tags.md` Recommendation | "Store `(tagid, weight, rank)` — keep rank, since weight is only within-app comparable." + "**Store page HTML scraping is not recommended in any form.**" + "Store the two vocabularies **separately** — do not blend" | GD-35, `docs/facet-provenance.md` | FINDING |
| SP-10 | `update-signals.md` §4.5 amendments | "steamcmd.net demo was erroring — `[VERIFY]` | **Alive and correct.**" + "Keep local SteamCMD as fallback | **Drop it.** 250 MB + an open non-TTY output bug" | GD-42 | FINDING |
| SP-11 | `update-signals.md` §4.5 amendments | ""filtered to community announcements" | Use **`tags=patchnotes`** — 527 → 34 items vs 74 for the feeds filter" + "**`GetNewsForApp` needs no API key.**" | GD-43 | FINDING |
| SP-12 | `update-signals.md` §4.5 amendments | "**403 from `GetNewsForApp` means "no feed for this appid", not throttling.** Cache it; do not back off" + "Correlation window must be **±7 days**" | GD-44, GD-31 | FINDING |
| SP-13 | `update-signals.md` §4.5 amendments | "Store the news item `url` on the event row; design-system §5.2's badge click needs it" + "Never-opened games are ineligible for the badge, so **do not poll them**" | DS-33, DS-30, DS-32 | FINDING |
| SP-14 | `avalonia-dormancy-rendering.md` §Findings | "**Built-in Effect API — NOT AVAILABLE.** ... there is **no public API for authoring custom effects** ... §5.4's option 1 as written is therefore unavailable → per the doc's own rule, use option 2." | DS-37 | FINDING |
| SP-15 | `avalonia-dormancy-rendering.md` §Recommendation | "**Primary — two-layer continuous cross-fade** ... α = (S − 0.22) / 0.78 from the §5.1 saturation column" with "one **floor variant**: saturation 0.22, brightness 0.60" | DS-23, DS-24 (floor since revised to 0.68) | FINDING (K-26) |
| SP-16 | `avalonia-dormancy-rendering.md` §Recommendation | "**Fallback/escalation trigger:** move to approach (2) ... only if profiling shows the doubled bitmap memory is unacceptable ... or design later demands a matrix path the two-endpoint lerp can't express (e.g. hue cool-shift independent of saturation)." | DS-25 (the −6° rotation is exactly that case) | FINDING (K-27) |
| SP-17 | `avalonia-dormancy-rendering.md` 2026-08-24 note | "**Superseded in part, 2026-08-24 — the ItemsRepeater recommendation only.** ... The cover wall is now `src/Winnow.App/Views/CoverWall.cs` ... Do not reintroduce the package." | DS-40, avalonia-ui charters | FINDING; the §Token-file verifications row still recommends ItemsRepeater (K-41) |
| SP-18 | `avalonia-dormancy-rendering.md` §Token-file verifications | "`TextBlock.FontFeatures` ... **introduced in 11.1.0** ... Syntax: `FontFeatures="+tnum"`" + "`TextBlock.LetterSpacing` ... device pixels — convert the `+0.06em` tokens to px" | DS-16, DS-17 | FINDING |
| SP-19 | `epic-gog-local-files.md` §1 | "`%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests\*.item` **AUTHORITATIVE for installed titles**" + "`LauncherInstalled.dat` **DEAD. Do not use.**" + "`catcache.bin` **AUTHORITATIVE for the owned library**" + "do not hardcode the path ... `HKCU\SOFTWARE\Epic Games\EOS` → `ModSdkMetadataDir`" | GD-14/§5 (no paths given), RM-11 | FINDING |
| SP-20 | `epic-gog-local-files.md` §11 | "`galaxy-2.0.db` is WAL; `immutable=1` silently returns stale data and `mode=ro` writes `-wal`/`-shm` into the store's directory. Copy first" | GD-24, RM-28 | FINDING |
| SP-21 | `epic-gog-local-files.md` §12 | "Galaxy's library contains **other stores' releases marked owned**. Filter `substr(releaseKey,1,4)='gog_'` or double-count the Steam library" | RD-24 | FINDING |
| SP-22 | `epic-gog-local-files.md` §19 | "§4.4 "`external_games` maps Steam appid / **GOG id / Epic catalog id**" | **GOG id: true** (13/14 coverage). **Epic catalog id: false** — 0/73." | GD-66 | FINDING (K-22) |
| SP-23 | `epic-gog-local-files.md` §20 | "**A better Epic join: GOG's own cross-store identity graph — VERIFIED, 67/67**" | RD-52, GD-66 | FINDING |
| SP-24 | `epic-gog-local-files.md` §8, §14 | "Epic has **no per-game playtime and no last-played on disk**" + "GOG **does** have playtime (minutes) and last-played (UTC), including for uninstalled games" | RM-11, RD-25 | FINDING |
| SP-25 | `epic-gog-local-files.md` §17 | "Local GOG titles are installer-locale (Polish for GWENT); Galaxy's `GamePieces.title` is canonical" | — | FINDING |
| SP-26 | `epic-gog-local-files.md` §22 | "**Verdict: do not build it** [Epic OAuth] — `catcache.bin` already supplies the library" | superseded by `epic-oauth.md` and RD-19 (M4.5 shipped) | DEAD (K-42) |
| SP-27 | `epic-oauth.md` header | "This supersedes sections 21–22 of `epic-gog-local-files.md` ... Where the two disagree, this document wins" | SP-26 | RULE (a precedence claim inside the evidence layer) |
| SP-28 | `epic-oauth.md` §1 | "§4.6 reason ... Documented account-ban risk | **Nothing comparable exists.** ... **Does not trip**" | GD-45, RD-15 | FINDING |
| SP-29 | `epic-oauth.md` §10 | Shipped client credentials sit at the lowest priority in the chain | RM-35, RD-14 | FINDING |
| SP-30 | `epic-oauth.md` §7 | "**The unit of `totalTime` — UNVERIFIED, and the one thing verification must settle**" | RM-11 ("Playtime and acquisition dates") | UNRESOLVED (U-11) |
| SP-31 | `embedded-auth.md` §1 | "**Hosting** | **WebView2 via `NativeControlHost`.** ... **CONFIRMED by a running app**" + "**GOG** | **Do not build this yet.**" | RD-19 (GOG held), RD-26 | FINDING |
| SP-32 | `embedded-auth.md` §9 | "section 9 lists every UNVERIFIED item in one place so none of them can be quietly promoted to fact later" | GD-02 | RULE (a method rule) |
| SP-33 | `steam-gdpr-export.md` §1 | "**There is no downloadable archive: VERIFIED** ... It describes no archive, no ZIP, no email delivery, and no file." | GD-70, GD-71, GD-49, RD-27 | FINDING (K-09) |
| SP-34 | `steam-gdpr-export.md` §4 | "**Where the historical playtime actually is, Steam Replay / Year in Review: VERIFIED**" + "Auth question resolved: VERIFIED 2026-08-28" | RD-28, RD-29 | FINDING |
| SP-35 | `steam-gdpr-export.md` §Recommended scope | "**Do not build:** a general "GDPR export importer" that walks ~100 dashboard pages." + "A parser written against saved HTML should treat markup as hostile and versioned: fail soft per-page, never abort the import" | RD-30, GD-70 | FINDING |
| SP-36 | `steam-gdpr-export.md` §8 | "Selectors for those two pages are now VERIFIED; the `help.steampowered.com/en/accountdata/*` pages remain unverified." | RD-30, RD-63 | FINDING |
| SP-37 | `steam-web-session-auth.md` §1-§2 | The `webapi_token` is a JWT resolving the account by its `sub` claim, mintable from any store page, accepted by the three endpoints Winnow uses; "a bad token returns a hard 401, where a bad API key returns a silent 200 with an empty envelope" | RD-17, RD-45 | FINDING |
| SP-38 | `steam-web-session-auth.md` §8 | "Recommended architecture sketch" naming sign-in as a peer credential source | RD-41, RD-42 | FINDING |

## 1.10 `.claude/agents/` (seven charters)

| ID | Anchor | Verbatim claim | Also claimed in | Class |
|---|---|---|---|---|
| AC-01 | avalonia-ui | "Before any work, read `design-system.md` in full, `tokens.axaml` ... and `mock-library.html` (the visual target). Also read `game-library-design.md` §2 and §5" | DS-02, CL-16 | RULE |
| AC-02 | avalonia-ui | "`Flare` (#FF5C8A) appears ONLY on unread-update markers and the bucket counting them." | DS-07, DS-08, CL-14 | RULE with a DEAD hex (K-01) |
| AC-03 | avalonia-ui | "Every number renders in IBM Plex Mono with tabular figures (`FontFeatures="tnum"`)." | CL-15, DS-16 | RULE (duplicate) |
| AC-04 | avalonia-ui | "Fonts ... are bundled as AvaloniaResource. Never rely on system fonts." | DS-14, CL-17 | RULE (duplicate) |
| AC-05 | avalonia-ui | "Dormancy ramp clamps at saturation 0.22 / brightness 0.60 — never fully grey." | contradicts DS-24 | DEAD (K-26) |
| AC-06 | avalonia-ui | "The cover wall is `Views/CoverWall.cs` ... Do NOT reintroduce `Avalonia.Controls.ItemsRepeater`" + the measured consequence | DS-40, SP-17 | RULE (duplicate) |
| AC-07 | avalonia-ui | "Bitmaps decode off-thread at display resolution." | DS-39 | RULE (duplicate) |
| AC-08 | avalonia-ui | "Accessibility floor (design-system.md §8) is not optional" | DS-52..DS-58 | RULE (duplicate) |
| AC-09 | avalonia-ui | "Copy follows the §7 table exactly" | DS-49 | RULE (duplicate) |
| AC-10 | avalonia-ui | "Placeholder tiles during metadata backfill: title set in Bricolage on a Surface field. Never a spinner, never an empty grid." | DS-51 | RULE (duplicate) |
| AC-11 | avalonia-ui | "When Avalonia API details matter ... verify against current docs (Context7 / avaloniaui.net) rather than training memory." | GD-92 | RULE |
| AC-12 | data-layer | "Before any work, read `game-library-design.md` §3.1 ..., §6 ..., §6.1 ..., and §6.2" | GD-16, GD-74, GD-76, GD-83 | RULE |
| AC-13 | data-layer | "Derived buckets (Never touched / Bounced / Stale-but-patched / Retired / Dead) are QUERIES, not stored columns." | GD-76 names the bucket `Never played` | RULE with a DEAD label (K-43) |
| AC-14 | data-layer | "Migrations are append-only versioned .sql files in `src/Winnow.Data/Migrations/`, embedded resources, run via DbUp on startup. Never edit a shipped migration." | CL-20, GD-75, RM-24 | RULE (duplicate) |
| AC-15 | data-layer | "Bucket queries get tests against seeded fixture data covering edge cases (zero playtime, boundary thresholds, update-after-last-played windows)." | winnow-reviewer item 9, `tests/Winnow.Tests/BucketQueryTests.cs` | RULE |
| AC-16 | data-layer | "Write SQL that stays legible — that is the whole reason Dapper was chosen over EF." | GD-16, RM-23 | RULE + DECISION (split) |
| AC-17 | enrichment-api | "Before any work, read `game-library-design.md` §4.2–§4.5 ... and §5.1 ... respect them exactly." | GD-27..GD-44, GD-54 | RULE |
| AC-18 | enrichment-api | Six non-negotiables restating Polly-at-HttpClient, IGDB 4 rps and token caching, appdetails 200/5min and cache >=24h, Steam 429 backoff and `GetOwnedGames` params, both update signals in `update_events`, keys never logged or committed | GD-28..GD-44 | RULE (duplicate) |
| AC-19 | enrichment-api | "Enrichment must never block a user-facing path (§5.1)." + "Test HTTP clients against canned response fixtures; no live API calls in tests." | GD-54, RM-30 | RULE (duplicate) |
| AC-20 | enrichment-api | The charter says nothing about `IStoreBrowseService/GetItems`, `tags=patchnotes`, the 403 semantics or dropping local SteamCMD | SP-08, SP-10, SP-11, SP-12 | DEAD (omission, K-44) |
| AC-21 | recommendation-engine | "`model: fable`" front-matter pin | `.codex` peer has no model pin | RULE |
| AC-22 | recommendation-engine | "§6.1's precedence rules and the Never-played/Bounced refund line at 120 minutes are the vocabulary your output must speak." | GD-79, GD-80 | RULE |
| AC-23 | recommendation-engine | "The moat is the data **nobody else retains**." + the six named signals (shelf time, bounce shape, return latency, patch-since-bounce, cross-store ownership, session cadence) | `docs/recommendation-engine.md` §3, RD-08 | RULE + DECISION (split) |
| AC-24 | recommendation-engine | "**Local only.** ... **§5.1 boundary.** ... **Derived, never truth.** ... **Explainability is mandatory** ... **Owned-but-unplayed is priority 1.** ... **No auto-merge, no identity decisions.**" | RD-12, GD-54, CL-21, RM-04, RD-50, GD-69 | RULE (duplicate) |
| AC-25 | recommendation-engine | The three cold-start tiers (Tier 0 one sync, Tier 1 weeks, Tier 2 months) + "The feed must be good at Tier 0 and get better, never blank-until-ready." | `docs/recommendation-engine.md` §6 | RULE |
| AC-26 | recommendation-engine | "Note that the M5 GDPR-export importer backfills historical playtime and is therefore the single biggest cold-start lever available." | contradicted by RD-27, SP-33 | DEAD (K-09) |
| AC-27 | recommendation-engine | "The user's real library is ~1,000 releases with known shape (616 Steam local, 841 Steam owned, 67 Epic, 14 GOG); test against realistic distributions" | RD-24, DS-85 | FINDING |
| AC-28 | recommendation-engine | "Every threshold is a named, documented parameter with a defensible default." + the four named anti-patterns | GD-82, `docs/recommendation-engine.md` §5 | RULE |
| AC-29 | recommendation-engine | "Write your reasoning into `docs/recommendation-engine.md` as you go ... That document is a deliverable, not notes." | RM-32 | RULE |
| AC-30 | recommendation-engine | "**Do not wire into the UI or the composition root.** This module gets hooked into Winnow deliberately, later." | contradicted by RD-19 (M8 shipped: "Recommender surfaced as the app's primary view") | DEAD (K-45) |
| AC-31 | steam-ingest | "Before any work, read `game-library-design.md` §4.1 ..., §5.1 ..., and §9 ... older blog posts and Stack Overflow answers contradict them and are WRONG." | GD-01, GD-19, GD-90 | RULE |
| AC-32 | steam-ingest | Six non-negotiables restating ValveKeyValue-only, read-only, eventually-consistent reads, the 2025 collections path, candidates-only, sanitized fixtures | GD-22..GD-25, GD-54, CL-38 | RULE (duplicate) |
| AC-33 | steam-ingest | "This machine has a live Steam install at `C:\Program Files (x86)\Steam` — use it to verify key names and formats empirically before coding against them, per the plan's [VERIFY] rules." | GD-02, GD-21, SP-01 | FINDING + RULE (split) |
| AC-34 | steam-ingest | The charter names no Epic or GOG paths although its description claims Epic/GOG ownership | SP-19, SP-20 | DEAD (omission, K-44) |
| AC-35 | winnow-reviewer | "You review completed work packages against the project's two authority documents: `game-library-design.md` ... and `design-system.md`" | contradicts CL-12/CL-18/RM-32, which name four to six | DEAD (K-46) |
| AC-36 | winnow-reviewer | The ten-point review checklist | GD-54, GD-25, GD-24, GD-69, GD-76, GD-38, DS-08, DS-16, CL-38, CL-29 | RULE (duplicate) |
| AC-37 | winnow-reviewer | "Report findings ranked by severity, each with file:line and the violated spec section. Verify claims by reading the code — do not trust summaries. State plainly when something passes; do not manufacture findings." | — | RULE |
| AC-38 | docs-writer | "You are the ONLY agent permitted to author non-code text in this repository" + the model pin `claude-opus-4-6` | the five delegation blocks (AC-42) | RULE |
| AC-39 | docs-writer | "Before writing anything, read `CLAUDE.md` in full." + the three naming rules + "Authority order: `ROADMAP.md` supersedes ... `docs/spikes/` empirical results override spec guesses." | CL-04, CL-06, CL-12, CL-18 | RULE (duplicate) |
| AC-40 | docs-writer | House style: "Plain declarative sentences." / "Documents argue with themselves where the code changed its mind" / "Record decisions with dates and evidence" / "Never oversell" / "**Never use emdashes**, separate ideas with commas, semicolons or periods." / "Brevity, and clarity are important." | the corpus violates the em-dash rule throughout | RULE, systematically violated (K-47) |
| AC-41 | docs-writer | "a comment states a constraint the code cannot show ... return "no comment needed" rather than writing filler." + "You never modify code semantics." | — | RULE |
| AC-42 | five charters | "## Non-code text is delegated, always ... If you cannot spawn agents from your context, leave the text as a clearly marked `TODO(docs-writer)`" (in avalonia-ui, data-layer, enrichment-api, recommendation-engine, steam-ingest; a variant in winnow-reviewer) | absent from every `.codex` peer | RULE (duplicated six times) |

## 1.11 `.codex/agents/` (seven charters)

| ID | Anchor | Verbatim claim | Also claimed in | Class |
|---|---|---|---|---|
| AX-01 | all seven | Each `.toml` carries `name`, `description` and a `developer_instructions` string that duplicates its `.claude/agents/*.md` peer's body | AC-01..AC-41 | DEAD (duplicate) |
| AX-02 | all seven | No `.codex` charter carries the "Non-code text is delegated, always" block | AC-42 | DEAD (omission, K-48) |
| AX-03 | docs-writer.toml | "Always runs on Codex-opus-4-6" (the `.claude` peer says `claude-opus-4-6`, and `.toml` has no model field to act on it) | AC-38 | DEAD (K-48) |
| AX-04 | docs-writer.toml | "Before writing anything, read `AGENTS.md` in full." + "Check AGENTS.md's list before touching any sentence containing the word." | AC-39 points at CLAUDE.md | DEAD (K-48) |
| AX-05 | recommendation-engine.toml | No model pin, where the `.claude` peer pins `model: fable` | AC-21 | DEAD (K-48) |
| AX-06 | avalonia-ui.toml | The instruction string is stored with literal `\r` escapes throughout, where the other six use plain newlines | — | DEAD (formatting defect) |
| AX-07 | docs-writer.toml | The file is untracked in git (`?? .codex/agents/docs-writer.toml` at the time of writing) | — | FINDING |

---

# PHASE 2 — Classification notes

The class of every claim is the fifth column of the Phase 1 tables. This section carries the
three things that do not fit in a column: the splits, the tie-breaks applied, and the
UNRESOLVED list.

## 2.1 Rule / rationale splits

Where a claim is both a rule and its own justification, the tie-break in the brief applies:
the rule text goes to RULE, the reasoning to DECISION. Sixty-one rows are split this way and
are marked "(split)" in their class column. They produce two destinations each in Phase 6:
the imperative half to the domain document, the reasoning half to the decisions log.

Split rows: RD-11, RD-13, RD-14, RD-52, RD-54, RD-57, RD-67 · GD-45, GD-61, GD-78, GD-85,
GD-93 · DS-08, DS-12, DS-26, DS-40, DS-46, DS-48, DS-56, DS-68, DS-71, DS-78, DS-82, DS-85,
DS-91, DS-94, DS-100, DS-101, DS-102, DS-113, DS-115, DS-128, DS-131, DS-142, DS-151, DS-164,
DS-166, DS-169, DS-172, DS-178, DS-188, DS-190, DS-194, DS-202, DS-208, DS-209, DS-210,
DS-213, DS-216, DS-217, DS-221, DS-224, DS-232, DS-238, DS-241, DS-243, DS-245, DS-250,
DS-252, DS-255, DS-259 · AC-16, AC-23, AC-33 · CL-17.

Where a row splits, the class column names the primary class first. Counts in the report
below count each row once, by its primary class.

## 2.2 Tie-breaks applied

- **A claim that is currently true but only as evidence is FINDING, not RULE.** A measured
  number ("the band is exactly 8px") is a FINDING; the sentence that follows it ("no
  interactive control may sit inside the 8px") is a RULE. Where one sentence does both the
  row is split.
- **A superseded claim is DEAD even when the document that supersedes it is a different
  file.** GD-70/GD-71 read as live prose in the design doc; ROADMAP and the GDPR spike make
  them false. They are DEAD and conflict K-09 records that nothing in the design doc says so.
- **A claim about state ("shipped", "not built", "23 pairs pending") is FINDING.** It is not
  binding and it is not a reason; it is an observation with a date.
- **An exit criterion is a RULE.** RD-19's phase table mixes state with exit criteria; the
  criteria bind future work, so the row's primary class is RULE.
- **A duplicate is DEAD only when it is a copy with no independent authority.** AGENTS.md's
  body and the seven `.codex` charters are copies. README's restatements of spec rules are
  not marked DEAD, because README is a document the project intends to keep; its rows are
  RULE and Phase 6 relocates them.
- **A rule that the corpus systematically violates is still a RULE**, and the violation is a
  conflict. AC-40's em-dash rule is the case.

## 2.3 UNRESOLVED

Eleven items cannot be classified from the documents. Each blocks at least one Phase 6 step.

| ID | Rows | The question | Why it cannot be answered from the documents |
|---|---|---|---|
| U-01 | RM-16 | Is "encrypting them the same way" tracked work or an aspiration? | README says "tracked as future work"; ROADMAP §6, which is the register of carried debt, does not list it, and no backlog task matches |
| U-02 | RD-62 | Store `$0.00` purchase rows as zero, or omit them? | The document says explicitly it "is a user decision, untested either way" |
| U-03 | GD-31 | Are the 429 / `Retry-After` figures still the ones to code against? | Marked `[VERIFY]`; no spike measures them. The backoff rule binds regardless; only the numbers are open |
| U-04 | GD-82 | Is a licensable HowLongToBeat source available, and does `retired_floor` become per-genre? | Marked `[VERIFY]`; no spike, and `docs/recommendation-engine.md` sets its own thresholds without answering it |
| U-05 | DS-154 | What colour role names "optional, and deliberately not connected"? | §13 records the gap and the interim pill; §14-§16 never return to it |
| U-06 | DS-155 | What is the pattern for a single-row rail section that opens a screen? | §13 gap 3 and ROADMAP §6 both record it as deferred with "no better shape is known" |
| U-07 | DS-156 | Which connection-state and consent strings does §7's copy table own? | §13 gap 4 records that this screen's copy was written from the auth spikes rather than from §7 |
| U-08 | DS-157 | What is the reading measure? | §13 gap 5 sets 720px provisionally; §6 reads it and rules it non-governing for a card, leaving prose unspecified |
| U-09 | DS-158 | Is §8's adorner or §10.7's brush swap the focus rule? | §13 gap 6 states the disagreement; §14.7 asserts §10.7 "is what the rest of the app follows" without amending §8 |
| U-10 | NT-01..NT-19 | Are `notes.md` items binding requirements, a backlog, or a wish list? | The file's own preamble says only that they will "be addressed down the line". Three contradict shipped rules |
| U-11 | SP-30 | What is the unit of Epic's `totalTime`? | The spike marks it UNVERIFIED and says verification must settle it; README already states "Playtime and acquisition dates" as delivered |

---

# PHASE 3 — Conflicts register

Fifty conflicts. Each is a place where two documents disagree, where precedence is
conditional, or where one document carries both a claim and its reversal. **None is resolved
here.** The "settled by" column names the evidence that would decide it, not a decision.

## 3.1 Cross-file disagreements

| K | Subject | Text A | Text B | Settled by |
|---|---|---|---|---|
| K-01 | The `Flare` hex | "Flare (#FF5C8A) marks ONLY unread updates" — CLAUDE.md §Authority, AGENTS.md, `.claude/agents/avalonia-ui.md`, `.codex/agents/avalonia-ui.toml` | "`Flare` \| `#FF4D93`" — design-system §2 table, and §2's revision note "`Flare` moved 6° hotter (`#FF5C8A` → `#FF4D93`)" | The `flare` seed in `src/Winnow.App/Themes/tokens.axaml` for the `winnow` theme. Whichever value the shipping default theme carries is the fact; the other four documents are stale |
| K-02 | Variable font axes | "Fonts are static OFL cuts (Avalonia 11 has no variable-axis API)" — CLAUDE.md, AGENTS.md | "It has `wdth` and `opsz` axes; use `wdth` 100–110 for headers, never above 120" — design-system §3, and `wdth 105` / `wdth 110` in the §3 scale | The files in `src/Winnow.App/Assets/Fonts/` and its README. If the cuts are static, §3's axis instruction and the scale's `wdth` values are unfollowable and must be restated as named cuts |
| K-03 | Where agent charters live | "Domain agents live in `.claude/agents/`" — CLAUDE.md §Conventions | "Domain agents live in `.Codex/agents/`" — AGENTS.md §Conventions | Both directories exist on disk (`.claude/agents/`, `.codex/agents/`, the second lower-case). The question is which tool the project is committing to, which is K-48 |
| K-04 | The `--data-dir` rule | CLAUDE.md carries the `--data-dir` paragraph, the exit-code-2 sentence and the `%LOCALAPPDATA%` finding | AGENTS.md omits all three, so an agent reading only AGENTS.md will click on the real library | Nothing empirical. This is a copy that drifted; the adjudication is which file is the source |
| K-05 | What `Never played` means | "*Never played* is under 2 hours (Steam's refund window)" — README §What it does | "**Never played means never opened.** Zero minutes *and* no last-played date, nothing else ... the refund-line rule, reverted 2026-08-29" — design §6.1 | `tests/Winnow.Tests/BucketQueryTests.cs` and `LibraryBucketRulesTests.cs`. The query is the fact; README states the reverted rule |
| K-06 | Plaintext credentials | "Steam Web API keys and IGDB client secrets are stored as plaintext rows in the local database" — README | "A host that cannot encrypt refuses to store rather than degrading to plaintext" — ROADMAP §4.7 second amendment, condition 2 | Whether condition 2 was scoped to the two session secrets only, or states a policy the API key and IGDB secret also fall under. The documents can be read both ways |
| K-07 | How many modules the boundary spec governs | Five rows: `Ingest.*`, `Resolve.*`, `Enrich.*`, `Monitor.*`, `Score.*` — design §5.1 | Ten modules with per-module musts — README §Module map; five projects — CLAUDE.md §Layout; seventeen directories under `src/` | `src/` on disk. The three lists disagree on cardinality and on names (see also K-50) |
| K-08 | Test count | "**1,737 tests.**" — README §Build and test | "2,111 tests passing" — ROADMAP §4, M5 row, dated 2026-08-29 | `dotnet test`. Both are point-in-time counts in documents that do not date them the same way |
| K-09 | The GDPR export | "The sanctioned path is the GDPR export (§5.4)" — design §4.7; "**GDPR export import** ... points the app at the file" — §5.4; "AngleSharp \| GDPR export import (§5.4)" — §3; "GDPR export import" listed as not built — README; "the M5 GDPR-export importer" — recommendation-engine charter | "there is no downloadable archive ... the data it was supposed to contain does not exist in that form" — ROADMAP §4 M5 note; "**There is no downloadable archive: VERIFIED**" — `docs/spikes/steam-gdpr-export.md` §1 | Already settled empirically by the spike. The conflict is that four documents still describe the mechanism as live and none of them says otherwise |
| K-10 | An amendment with no target | "Shipping storefront client credentials \| **Decided 2026-08-26: ship them built-in**" — ROADMAP §3, a table whose column header is "§1 non-goal" | design §1's non-goal list has six entries and none of them is about shipping credentials | Whether the row amends an unwritten policy or was mis-filed. Only the author can say |
| K-11 | The no-scraping rule | "**Do not scrape either page.**" — design §4.7 | Amendment 1 (2026-08-28, four binding conditions, condition 1 since superseded) and amendment 2 (2026-08-30, eight binding conditions) — ROADMAP §3 and §4 | Nothing empirical. This is a three-layer chain in which the base document still reads as an absolute prohibition |
| K-13 | Recommendations in or out of scope | "~~Recommendation engine (phase 2)~~ — **SUPERSEDED**" struck in design §1; "Phase 2 (explicitly out of scope now): Sync server, Steam OpenID accounts, co-op library matching, **recommendations**" — design §8, not struck | "Promoted to core" — ROADMAP §3; M7 and M8 shipped — ROADMAP §4 | Already settled by ROADMAP. §8's Phase 2 list was never updated to match §1's strike-through |
| K-14 | The migration runner | "Migrations \| FluentMigrator or DbUp" — design §3 | DbUp named as the runner in CLAUDE.md, AGENTS.md, README, the data-layer charters, and `RenameLegacyJournalEntries`'s reliance on DbUp's `SchemaVersions` | The `src/Winnow.Data` package references. The choice was made; §3 still offers it |
| K-15 | EF Core | "If the implementer prefers EF Core's migrations story, that is defensible" — design §3.1 | "No EF Core — the SQL is meant to stay legible" — README; "No EF Core." — both data-layer charters | The package references. §3.1 leaves a door open that three other documents have closed |
| K-20 | Linux session attribution | "On Linux, read `/proc/*/comm` for the name filter and `/proc/<pid>/exe` only for candidates" — design §5.2 | "under Proton the resolved executable is the wine loader inside the runtime directory, not a path under the game's install root, so the install-prefix join cannot work there at all" — ROADMAP §6 | Already settled by the ROADMAP entry. §5.2 still prescribes a mechanism the debt register says cannot work |
| K-21 | `winnow-wrap` | "Two mechanisms, both shipped" and the full §5.2 B specification — design §5.2 | "M3b ... **shipped** — `winnow-wrap` (§5.2 B) deliberately deferred" — ROADMAP §4 | `src/` has no wrapper project. §5.2's opening sentence is false as written |
| K-22 | The Epic hard join | "IGDB `external_games` lookup by Steam appid / GOG id / **Epic catalog id**" — design §5.3, §4.4 | "**Epic catalog id: false** — 0/73. IGDB stores Epic *offer* and *page* ids, not catalog item ids" — `docs/spikes/epic-gog-local-files.md` §19 | Already settled by the spike. The design doc still names a join that resolves nothing |
| K-23 | Export | "Launch feature, not an afterthought" — design §7; "First-class data export" — design §1 goals | "M6 \| Export (JSON + CSV) \| **deferred** 2026-08-31; exit criterion to be restated" — ROADMAP §4 | Whether the goal survives the deferral. The ROADMAP defers the milestone without amending the goal |
| K-25 | `Danger` and `Amber` discipline | "`Danger` \| Destructive affordance. Today: the window close button, nothing else" — design-system §2 | "`Danger` appears on its confirm button and nowhere else on the strip" — §12.3; the AA figure "turning `Amber`" — §14.3; the substitution notice "in an `Amber` field" — §14.6 | The §2 table's "Today:" qualifier was never updated. Whether the delete confirm and the two measurement fields are exceptions or the rule is now broader |
| K-26 | The dormancy floor | "Clamp at `0.22 / 0.68`" and the ramp table's `3y+ 0.22 / 0.68` — design-system §5.1, with the revision note explaining the change from 0.60 | "Dormancy ramp clamps at saturation 0.22 / brightness **0.60**" — both avalonia-ui charters; "one **floor variant**: saturation 0.22, brightness **0.60**" — `docs/spikes/avalonia-dormancy-rendering.md` | The floor variant actually generated in the cover pipeline. Three documents say 0.60 and the spec says 0.68 |
| K-27 | The −6° hue rotation | "A **−6° hue rotation** is part of the floor, composed as `saturate() → hue-rotate(-6deg) → brightness()`" — design-system §5.1 | The shipped two-endpoint cross-fade "is an exact linear interpolation between the two endpoints"; the spike names "hue cool-shift independent of saturation" as a reason to escalate to a matrix path — `avalonia-dormancy-rendering.md` | Whether the floor bitmap is generated with the hue rotation baked in. If it is, the two agree; if not, §5.1 states an encoding the app does not draw |
| K-29 | Dormancy rendering approach | "Two viable approaches, in preference order: 1. **Shader effect** ... 2. **Pre-computed bitmap variants** ... Fall back to (2) if (1) is unavailable" — design-system §5.4 | "**Built-in Effect API — NOT AVAILABLE** ... §5.4's option 1 as written is therefore unavailable" — the dormancy spike | Already settled. §5.4 still presents a preference order whose first option does not exist |
| K-32 | Bucket copy | "`Patched since`", "`Bounced off`", "Stores" as a settings segment — design-system §7, §13 gap 3 | "Patched Since > Patched"; "Bounced Off > Bounced ?"; "Stores > Platforms" — notes.md §Bugs | The user. TASK-60 is already In Progress for the third of these, which suggests the note is binding, and that inference is not documentation |
| K-40 | How much the UI explains | "the Appearance screen reports the worst case live and marks where it crosses AA" — design-system §14.3; "The Appearance screen prints both numbers" — §14.6; "**The window itself is the record that is kept current**" — §16.9; the 720px prose measure — §13 gap 5 | "Transparency block has wayyyyy too much information ... It's great for our design docs but don't put it in the UI"; "Drop the over-explanatory text blurbs throughout the interface ... No more than a few words." — notes.md §Bugs | The user. §14.3's whole argument for the live figures is that a number nobody can check should not ship, so this is a genuine trade rather than an error |
| K-41 | ItemsRepeater, inside one spike | "Do not reintroduce the package." — `avalonia-dormancy-rendering.md`, 2026-08-24 note | "For the cover grid: use **ItemsRepeater + UniformGridLayout** for v1" — the same file's §Token-file verifications table, unamended | The note is dated later and the app follows it. The table is stale text inside the document that is meant to be evidence |
| K-42 | Epic OAuth | "**Verdict: do not build it**" — `epic-gog-local-files.md` §22 | "This supersedes sections 21–22 of `epic-gog-local-files.md` ... Where the two disagree, this document wins" — `epic-oauth.md`; M4.5 shipped — ROADMAP §4 | Already settled, but by a precedence claim made *inside the evidence layer*, which is the same pattern the migration is removing |
| K-43 | Bucket names | "Never touched / Bounced / Stale-but-patched / Retired / Dead" — both data-layer charters | "Never played", "Bounced off", "Played out", "Won't run" — design §6.1 and design-system §7 | The strings in the UI resources. The charter uses a vocabulary the copy table forbids |
| K-44 | Charters stale against spikes | enrichment-api names `appdetails`, IGDB and `ISteamNews` "filtered to community announcements" only; steam-ingest names no Epic or GOG path although its description claims them | `IStoreBrowseService/GetItems` is the tag route; `tags=patchnotes` is the announcement filter; local SteamCMD is dropped; the Epic and GOG paths are in `epic-gog-local-files.md` | The spikes. The charters were written before them and never revised |
| K-45 | Is the recommender wired? | "**Do not wire into the UI or the composition root.** This module gets hooked into Winnow deliberately, later." — both recommendation-engine charters | "M8 \| The Feed \| Recommender surfaced as the app's primary view ... **shipped**" — ROADMAP §4 | Already settled by M8. The charter forbids what the product now depends on |
| K-46 | How many authority documents | "the project's two authority documents" — both winnow-reviewer charters | Four in CLAUDE.md, six in README's precedence table | The migration itself: after it there is one per domain, and the reviewer's list becomes the list of domains |
| K-47 | The em-dash rule | "**Never use emdashes**, separate ideas with commas, semicolons or periods." — both docs-writer charters | Every document in scope uses them heavily, including the ones docs-writer is said to have authored | The user. Either the rule is new and the corpus predates it, or the rule is not enforced |
| K-48 | Two agent-config trees | `.claude/agents/*.md`: seven charters, five carrying the delegation block, `recommendation-engine` pinned to `model: fable`, docs-writer pinned to `claude-opus-4-6` and pointed at CLAUDE.md | `.codex/agents/*.toml`: the same seven, none carrying the delegation block, no model pins, docs-writer pointed at AGENTS.md and claiming "Codex-opus-4-6", `avalonia-ui.toml` stored with literal `\r` escapes, `docs-writer.toml` untracked in git | Whether Codex is still used on this project. If it is, one tree must be generated from the other; if not, one tree is deleted |
| K-49 | Two `tokens.axaml` | "Root `tokens.axaml` is the design RECORD; the compiling copy is `src/Winnow.App/Themes/tokens.axaml` — change tokens there." — CLAUDE.md, AGENTS.md | The two files differ (md5 `a52b1325…` against `00f5b74c…`) | A diff of the two. Whether the root copy is an out-of-date record or holds tokens the app has not adopted |
| K-50 | `Score.*` | "`Score.*` \| Derive staleness buckets from stored facts" — design §5.1, a module row in the boundary table | No `Winnow.Score` project exists. Bucket derivation lives in `Winnow.Data` queries and scoring in `Winnow.Recommend` | `src/` on disk. The boundary table names a module that was never built, so the rule that governs it has no addressee |

## 3.2 Conditional precedence

| K | The chain | Where it is stated | Settled by |
|---|---|---|---|
| K-12 | ROADMAP §6's gamesdb bullet holds its own reversal: "built for METADATA, not for dedup" followed by "*(original)* — spiked and verified ... This would collapse most of the merge queue automatically" | ROADMAP §6 | The bullet says "The original note follows", so both are deliberate. Whether the original still describes intended future work or is history |
| K-16 | "`localconfig.vdf` playtime fields **[VERIFY]** — confirm exact key names (`Playtime`, `LastPlayed`, `playtime_two_weeks`)" stands unamended in design §4.1 while `steam-local-files.md` §3 gives the answer and says the third name is wrong | design §4.1 / spike | Already settled empirically; the spec was never edited |
| K-17 | "**[VERIFY]** which endpoint is currently viable" stands in design §4.3 while `steam-store-tags.md` answers it and adds that the one-appid arithmetic in §4.3 does not apply to the answer | design §4.3 / spike | Already settled empirically; the spec was never edited |
| K-18 | "keep local SteamCMD as fallback" stands in design §4.5 while `update-signals.md` says "**Drop it.** 250 MB + an open non-TTY output bug" | design §4.5 / spike | Already settled empirically; the spec was never edited |
| K-19 | "filtered to community announcements" stands in design §4.5 while `update-signals.md` says "Use **`tags=patchnotes`** — 527 → 34 items vs 74 for the feeds filter" | design §4.5 / spike | Already settled empirically; the spec was never edited |
| K-24 | design §10 lists six open questions; the spikes answer four of them and the document still asks all six | design §10 | Already settled for four; U-03 and U-04 remain genuinely open |
| K-33 | Focus has three statements and one explicit refusal to choose: §8's 2px Volt adorner floor; §10.7's brush swap at fixed thickness; §13 gap 6 "which one is authoritative depends on which you read first"; §14.7 "§10.7 is what the rest of the app follows" | design-system §8, §10.7, §13, §14.7 | The user, or a measurement of what the running window actually draws in each surface. §14.7 answers it in passing inside a section about input fields |
| K-34 | The caption's ink has an original statement and three amendments, two of which reverse each other, and the current answer depends on the layout: flush `ChromeSurface`, floating no fill at all | design-system §9 (four layers), §15.2, §16.5 | Nothing. Both current halves are tested (`ThemeContrastTests.The_caption_is_the_rail`, `FloatingLayoutTests.The_caption_is_the_ground`). The conflict is that a reader must walk four amendments to learn a two-line rule |
| K-35 | `ScrollBarEdgeInset` binds in flush, is retired in floating, and §11.1 records the filter panel changing sides of it when it moved | design-system §9.1, §11.1, §15.4 | Nothing empirical. The rule is correct and its statement is spread across three sections, two of which are amendments |
| K-37 | §14.2's five-grounds table is prefaced by an amendment saying the table "describes the middle one as though it still exists", and the table is left in place | design-system §14.2, §16 | Nothing. The current tier map is §16.1's three-row table; §14.2's eleven-row table is a superseded document that a reader meets first |
| K-38 | §14.3 fixes the backdrop hint order as `[AcrylicBlur, Mica, None]`; §14.6 makes the head of that list a user setting | design-system §14.3, §14.6 | Nothing. §14.6 says §14.3 "settled a **default**, not an only option", which is a reinterpretation of a sentence that does not read that way |
| K-39 | Four sets of AA ceiling figures are live in one document: 27/30/30/26 (§14.3), the six-row per-surface table (§14.7), 27/31/30/26 (§15.8), 30/31/31/31 (§16.6), plus §16.9's mapping from the committed screenshots' captions | design-system §14.3, §14.7, §15.8, §16.6, §16.9 | `Colorimetry.AaCeiling` on the running build. Only §16.6 is current, and the other three are not marked as history where a reader lands on them |

## 3.3 A document containing its own reversal

| K | Document | The claim | Its reversal, in the same file |
|---|---|---|---|
| K-28 | design-system §5.2 | "**"Never-opened" here means zero recorded playtime, not the `Never played` bucket.** Since that bucket became everything under the refund line (design doc §6.1) ..." | The premise is false: design §6.1 reverted the refund-line bucket on 2026-08-29. The rule §5.2 states is still right; the argument it gives for it describes a bucket definition that no longer exists |
| K-30 | design-system §6 / §14.7 | "**List view.** ... 44px rows, `Surface` ground" | "The rows take `PaneGround` now and the column-header strip takes `ChromeSurface`" |
| K-31 | design-system §13 gap 5 / §6 | "No reading-measure rule ... Used 12/18 capped at 720px. That belongs to the system" | "§13 gap 5's provisional 720px prose measure was read and does not govern: this is a two-column comparison, not a paragraph" |
| K-36 | design-system §10.5 | "`acquired_at`, `license_type`, `price_paid_cents` ... are all in the schema and all **empty for every row** this data source produces" | ROADMAP §6 records migration 0014/0015 and the saved-HTML importer populating exactly those columns. The detail view's absence rule may still be right; its stated reason is not |
| K-05 | README / design §6.1 | (also a cross-file conflict, listed above) | design §6.1 records the reversal explicitly; README still carries the reverted rule as a headline feature |
| K-12 | ROADMAP §6 | (also listed above) | the bullet deliberately retains the pre-reversal note |

---

# PHASE 4 — Target structure

## 4.1 The destination set

Nine destinations. One document is authoritative per domain; no document defers to another.

| # | File | Domain it owns, exclusively | It must not contain |
|---|---|---|---|
| 1 | `AGENTS.md` | How work is done in this repository: naming, layout, build, run, test, commit, delegation, Backlog workflow, and the pointer to each domain document | Product scope, visual values, API behaviour, any precedence claim |
| 2 | `ROADMAP.md` | Product scope, phase order, exit criteria, what is deliberately excluded and what is deferred | Architecture, hard constraints, module boundaries, rationale (that goes to 7) |
| 3 | `game-library-design.md` | The build spec: architecture, module boundaries, external-service behaviour, entity resolution, the schema, derived buckets, session detection | Sequencing, milestones, visual values, rationale |
| 4 | `design-system.md` | The visual spec: palette, type, layout, the dormancy encoding, components, copy, accessibility floor, themes, translucency, layouts | Architecture, precedence, superseded measurements, amendment history |
| 5 | `src/Winnow.App/Themes/tokens.axaml` | Token values | Anything the design system does not state |
| 6 | `README.md` | Orientation: what the product is, how to install, run and build it, where things live, where to read further | Any rule. Every normative sentence in it moves to 1, 3 or 4 and README links instead |
| 7 | `docs/decisions.md` | Append-only. Why things are the way they are, what was reversed, what a document used to say | Anything binding. No agent instruction points at it, and no document says "see decisions.md" for a rule |
| 8 | `docs/spikes/*.md` | Evidence. Unchanged files, each gaining a one-line banner | Any claim that a spec section does not already carry. A spike stops being able to override anything |
| 9 | `.claude/agents/*.md` | Per-domain agent charters, thinned: a pointer to the governing document plus the domain rules that exist nowhere else | Restatements of spec rules, palette values, thresholds, endpoint parameters |

Deleted, each with a named destination:

| Deleted | Every claim in it goes to |
|---|---|
| `CLAUDE.md` (as a document) | Becomes a single line, `@AGENTS.md`, so Claude Code loads the one file. Its content moves to `AGENTS.md` |
| `notes.md` | Backlog tasks via `backlog task create`, one per item. Blocked on U-10 |
| `tokens.axaml` (repo root) | `src/Winnow.App/Themes/tokens.axaml`. Blocked on K-49 |
| `.codex/agents/*.toml` | Either generated from `.claude/agents/*.md` by a script, or deleted. Blocked on K-48 |
| `game-library-design.md` §8 | `ROADMAP.md`'s phase table (the exit criteria) and `docs/decisions.md` (the sequencing history) |

## 4.2 The rules the target documents follow

1. **One document per domain, named in `AGENTS.md`.** No document says it supersedes, amends,
   outranks or is read before another. `AGENTS.md` lists which file owns which domain, and
   that list is the whole of the routing.
2. **A wrong section is edited, not amended.** If a spike, a measurement or a decision makes a
   section false, the section is rewritten to the current truth in the same commit, and the
   sentence it used to say is appended to `docs/decisions.md`. The words "supersedes",
   "amended", "superseded", "retired", "the original text" and "as first written" do not
   appear outside that log. Enforcement E-24.
3. **RULEs are imperative, present tense, no rationale inline.** "Parse VDF with
   ValveKeyValue." not "Parse with ValveKeyValue, because binary KeyValues appear in Steam's
   config tree and hand-rolled parsers break on them." The reason goes to the log; the log
   entry names the rule it explains, and the rule does not name the log entry.
4. **Findings live in the spec, evidence lives in the spike.** The current key names are in
   §4.1; `steam-local-files.md` stays as the record of how they were learned. A spike is never
   the place to look up a rule.
5. **`README.md` states no rules.** It describes and links. A reader who follows only README
   builds and runs the app; a reader who is about to change it is sent to `AGENTS.md`.
6. **The decisions log is write-only for agents.** Nothing instructs an agent to read it. It
   exists so that a human can ask "why is this like this" and so that deleting rationale from
   a spec is not deleting it from the repository.

## 4.3 The agent-config question

`AGENTS.md`, `.codex/agents/` and `.claude/agents/` currently hold content `CLAUDE.md` does
not. Exhaustively, that content is:

| Source | What it has that `CLAUDE.md` does not | Disposition |
|---|---|---|
| `AGENTS.md` | Nothing additive. One divergence (`.Codex/agents/`, AG-02) and three omissions (AG-03) | Becomes the single file; the divergence is dropped and the omissions are the CLAUDE.md text |
| `.claude/agents/avalonia-ui.md` | The `CoverWall`/`ItemsRepeater` prohibition with its measured consequence (AC-06); the instruction to verify Avalonia APIs against current docs (AC-11); the delegation block (AC-42) | AC-06 to `design-system.md` §5.4 (it is already there, DS-40) and the charter keeps a pointer; AC-11 and AC-42 stay in the charter |
| `.claude/agents/data-layer.md` | The requirement that bucket queries carry seeded edge-case tests (AC-15) | To `game-library-design.md` §6.1 as a rule, and to Phase 5 as E-08 |
| `.claude/agents/enrichment-api.md` | Nothing not in design §4.2-§4.5, and it is stale against three spikes (AC-20) | Thinned to a pointer |
| `.claude/agents/recommendation-engine.md` | The signal inventory and its argument (AC-23); the three cold-start tiers (AC-25); the measured library shape (AC-27); the threshold discipline and anti-patterns (AC-28); the `model: fable` pin (AC-21) | AC-23/25/28 already live in `docs/recommendation-engine.md`, which becomes that module's domain document; the charter keeps the pin and a pointer |
| `.claude/agents/steam-ingest.md` | The live Steam install path used for empirical verification (AC-33) | Stays in the charter: it is a fact about this machine, not about the product |
| `.claude/agents/winnow-reviewer.md` | The ten-point checklist (AC-36) and the reporting discipline (AC-37) | The checklist becomes a pointer to Phase 5's enforcement table, so a check that has a test is not also a prose checklist item; AC-37 stays |
| `.claude/agents/docs-writer.md` | The exclusive-authorship rule (AC-38), the house style including the em-dash rule (AC-40), and the comment doctrine (AC-41) | All three stay in the charter. AC-40 is blocked on K-47 |
| `.codex/agents/*.toml` | Nothing additive. Seven copies, six divergences, all defects (AX-01..AX-07) | Generated or deleted; blocked on K-48 |

Note that `docs/recommendation-engine.md` and `docs/facet-provenance.md` are outside the
brief's file list but are cited as normative by `README.md` (RM-32) and by the
recommendation-engine charter (AC-29). Phase 6 step 13 treats `docs/recommendation-engine.md`
as the domain document for `Winnow.Recommend` and does not otherwise inventory it. This is
declared in the report as a file not fully accounted for.

---

# PHASE 5 — Enforcement

Prior art: `LocalLibrarySyncContractTests` (a stated guarantee turned into a test),
`ThemeContrastTests` and `FloatingLayoutTests` (a spec's numbers walked per theme and per
slider position), `TreatWarningsAsErrors` in `Directory.Build.props`. All new tests go in
`tests/Winnow.Tests` unless named otherwise. **No test is written by this plan.**

## 5.1 Automated checks

| E | Check | Project / mechanism | Applies to |
|---|---|---|---|
| E-01 | `ArchitectureBoundaryTests` — assembly reference graph: `Winnow.Core` references only BCL; `Winnow.Recommend` references only `Winnow.Core`; `Winnow.Auth.WebView` references only Avalonia and `Winnow.Core`; no `Winnow.Ingest.*` references a write repository | Winnow.Tests; `Assembly.GetReferencedAssemblies()` plus a Mono.Cecil type-reference scan of the built assemblies | CL-19, CL-24, GD-51, GD-54, RM-24, AC-24 |
| E-02 | `UiIngestIsolationTests` — no type in `Winnow.App` outside the composition-root namespace references a type in `Winnow.Ingest.*`, `Winnow.Enrich.*` or `Winnow.Covers.*` | Winnow.Tests; Cecil scan with a namespace allowlist | CL-25, GD-52, AC-01 |
| E-03 | `IngestWriteSurfaceTests` — ingest assemblies emit `CandidateOwnership` and reference no `works`/`releases` write path | Winnow.Tests; Cecil scan plus a fixture run asserting zero rows written to `works`/`releases` | CL-22, GD-54, AC-32 |
| E-04 | `NoStoreFileWriteTests` — no write-mode file API is reachable from `Winnow.Ingest.*` except the documented copy-to-temp helper | Winnow.Tests; Cecil scan for `File::Create/WriteAll*/AppendAll*/Delete/Move` and `FileStream` write ctors | CL-37, RM-17, RM-28, GD-24, AC-32 |
| E-05 | `VdfParsingTests` — `Winnow.Ingest.Steam` references ValveKeyValue and declares no type whose name matches `*VdfParser`/`*KeyValueReader` | Winnow.Tests; reference check plus type-name scan | CL-22, GD-25, GD-90, AC-32 |
| E-06 | `ShippedMigrationsAreImmutable` — a checked-in `Migrations/checksums.txt` carries one SHA-256 per embedded migration resource; the test recomputes every hash and fails on a mismatch. Adding a migration appends a line; editing one fails the build | Winnow.Tests, extending `MigrationTests.cs`; embedded-resource enumeration plus SHA-256 | CL-20, RM-24, GD-75, AC-14 |
| E-07 | `DerivedBucketColumnTests` — migrate a temp database and assert no table has a column matching `bucket\|staleness\|score\|is_(never\|bounced\|retired\|stale)` | Winnow.Tests; `PRAGMA table_info` over every table | CL-21, GD-54, GD-76, RM-24, AC-13, AC-24 |
| E-08 | `BucketQueryTests` extensions — `NeverPlayedRequiresNoLastPlayedDate`, `BouncedFloorIsOneTwentyMinutes`, `RetiredOutranksStale`, `StaleOutranksBounced`, and one seeded case per boundary | Winnow.Tests, existing `BucketQueryTests.cs` / `LibraryBucketRulesTests.cs` | GD-76, GD-77, GD-79, GD-80, GD-82, AC-15, AC-22 |
| E-09 | `AutoMergeRequiresHardIdTests` — every resolver path that is not a hard external-id join returns a pending `merge_candidates` row and writes no merge | Winnow.Tests, beside `SoftMatchResolverTests.cs` | CL-23, RM-26, GD-67, GD-69, GD-90, AC-24 |
| E-10 | `IdentityLayerTests` — a release always has a work; merge execution never collapses two releases into one row; achievements are keyed per release | Winnow.Tests; repository round-trip over a seeded db | RM-25, GD-65, GD-74, GD-83, AC-12 |
| E-11 | `NullNotZeroTests` — feed each reader a fixture with the field absent and assert the candidate carries `null`, never `0` or `false` | Winnow.Tests; one case per reader per optional field | RM-27, SP-24 |
| E-12 | `LocalLibrarySyncContractTests` (existing) plus `EnrichmentNeverOnStartupPath` — no enrichment or remote client is resolvable from the first-paint path | Winnow.Tests; existing contract test extended with a container-graph assertion | GD-33, GD-54, RD-56, AC-19 |
| E-13 | `HttpClientPolicyTests` — every typed client resolved from the container has the rate-limit and retry handlers in its chain; `NoAdHocDelayTests` asserts no `Task::Delay` in `Winnow.Enrich.*` outside the policy assembly | Winnow.Tests; `IHttpClientFactory` handler inspection plus a Cecil scan | GD-31, GD-38, AC-18, AC-36 |
| E-14 | `SteamWebRequestShapeTests` — `GetOwnedGames` carries `include_appinfo=1`, `include_played_free_games=1`, `skip_unvetted_apps=false`; `appdetails` sends one appid and a descriptive User-Agent; IGDB sends `Client-ID` and bearer on every request | Winnow.Tests, beside the existing `SteamWeb`/`Igdb` fixtures | GD-28, GD-34, GD-37, GD-39, AC-18 |
| E-15 | `MajorUpdateRequiresBothSignals` and `NeverOpenedGamesAreNotPolled` | Winnow.Tests, existing `Updates/` folder | GD-44, DS-29, DS-30, DS-32, SP-13 |
| E-16 | `GameLinkTests` — only `https`, `http` and `steam` survive `GameLink.Create`; `file:`, `javascript:`, `data:`, relative targets and control characters return null; a null link renders no button | Winnow.Tests, beside `GameLaunchTests.cs` | DS-86, DS-87, DS-88, DS-89 |
| E-17 | `ThemeContrastTests` (existing, covers palette uniqueness and hue separation) plus `FlareUsageTests` — a scan of `src/Winnow.App/**/*.axaml` for `Flare` resource references, failing outside an allowlist of the badge, the `Patched since` rail row and the gap rail's marks | Winnow.Tests; existing test plus a new markup scan | CL-14, DS-08, DS-28, DS-41, DS-81, DS-119, DS-162, AC-02, AC-36 |
| E-18 | `NumericTypographyTests` — the `Data` and `Data S` styles set IBM Plex Mono and `FontFeatures="+tnum"`, and every `TextBlock` carrying a numeric-formatted binding uses one of them | Winnow.Tests; token-dictionary assertion plus an axaml scan. The second half is best-effort: a number rendered from a formatted string in a view model is not detectable | CL-15, DS-16, DS-117, AC-03, AC-36 |
| E-19 | `ThemeContrastTests` (existing) — per theme and per slider position: the AA walk, the dark-desktop monotonicity, the polarity floor, the field identity, the caption identity in flush | Winnow.Tests, existing | DS-56, DS-161, DS-162, DS-163, DS-181, DS-182, DS-187, DS-188, DS-192, DS-197, DS-201, DS-202, DS-205, DS-239, DS-240, DS-249 |
| E-20 | `FloatingLayoutTests` (existing) — token parity between layouts, composite-exactly-once, the caption on the ground, `Colorimetry.AaCeiling` walking both layouts | Winnow.Tests, existing | DS-64, DS-65, DS-213, DS-220, DS-224, DS-225, DS-226, DS-248, DS-254, RD-21 |
| E-21 | `DeliberateHoardTests` — the three `design-system.md` sentences and the `ActionBarView.axaml` sentence are present verbatim; every other occurrence of "hoard" in tracked text is inside an allowlist (`Hoard.Data.Migrations`, `%LOCALAPPDATA%\Hoard`, `LegacyDefaultId`, `docs/decisions.md`) | Winnow.Tests; file read plus a repository-wide scan | CL-04, CL-06, CL-07, DS-118, AC-39 |
| E-22 | `WinnowDataLocationTests` and `DataMigrationSafetyTests` (existing) plus `LegacyJournalRenameTests` and `LegacyThemeIdTests` — the move never lands on an empty directory, the journal re-point makes shipped migrations no-ops on a populated legacy database, and `appearance.theme = hoard` resolves after the catalogue | Winnow.Tests; existing tests extended | CL-08, CL-09, CL-11 |
| E-23 | `DataDirectoryOverrideTests` (existing) plus `UnusablePathExitsWithCodeTwo` | Winnow.Tests, existing file | CL-31, CL-33 |
| E-24 | `DocumentationConsistencyTests` — (a) none of "supersedes", "superseded", "amended", "retired (§", "the original text", "as first written" appears outside `docs/decisions.md`; (b) every `§n[.n]` cross-reference resolves to a heading in the file it names; (c) `README.md` contains none of "never", "must not", "non-negotiable"; (d) no document names another as outranking it | Winnow.Tests; markdown read over the tracked document set. This is the check that keeps the migration from undoing itself | The whole of Phase 4.2, and by construction CL-12, CL-18, RM-32, RM-33, RD-01, RD-02, GD-85, AC-35, AC-39 |
| E-25 | `TokenSourceTests` — exactly one `tokens.axaml` exists in the repository, at `src/Winnow.App/Themes/` | Winnow.Tests; file enumeration | CL-16, DS-02 |
| E-26 | `BuildPropertyTests` — `Directory.Build.props` still sets `Nullable`, `ImplicitUsings` and `TreatWarningsAsErrors` | Winnow.Tests; msbuild property read. The enforcement itself is the compiler | CL-28 |
| E-27 | `FixtureSanitisationTests` — no fixture under `tests/fixtures/` contains a 17-digit id outside the known-fake range | Winnow.Tests; file scan | CL-38, RM-30, AC-32 |
| E-28 | `NoLiveHttpTests` — no test-reachable code path constructs an `HttpClient` with a real `HttpClientHandler` | Winnow.Tests; container inspection plus a Cecil scan of the test assembly. Best-effort: a socket opened by a transitive dependency is not caught | RM-30, AC-19 |
| E-29 | `CopyStringTests` — the bucket, merge-action and empty-state strings in the UI resources equal the §7 and §10.4 tables verbatim | Winnow.Tests; resource read against a table checked into the test | DS-49, DS-50, DS-90, DS-130, AC-09 |
| E-30 | `RailGrammarTests` — the rail's sections appear in the stated order and every section above the divider is a subset of ALL GAMES | Winnow.Tests, beside `LibraryViewModelTests.cs` | DS-41, DS-42, DS-134, DS-141, RD-64 |
| E-31 | `EscapeLadderTests` and `FilterCountTests` — the `Escape` order in §12.4, the residual-count rule, the zero-count tab-stop rule, and frozen order | Winnow.Tests; existing `FilterPanelViewModelTests.cs` and `ListsViewModelTests.cs` extended | DS-111, DS-112, DS-113, DS-114, DS-115, DS-138, DS-149, DS-150 |
| E-32 | A `PreToolUse` hook in `.claude/settings.json` refusing writes to `backlog/**/*.md` | Not a test. The Backlog rules are instructions to an agent, and a hook is the only mechanism that can enforce them | CL-42 |

## 5.2 Unenforceable, remains prose

Every RULE id not named in 5.1 is unenforceable by an automated check. They fall into five
kinds, and the reason differs by kind.

| Kind | Ids | Why no check is possible |
|---|---|---|
| **Reading instructions to an agent** | CL-01, CL-02, CL-03, CL-05, CL-13, CL-17, CL-26, CL-27, CL-29, CL-30, CL-35, CL-36, CL-39, CL-40, CL-41 · GD-01, GD-02, GD-92 · AC-01, AC-11, AC-12, AC-17, AC-21, AC-29, AC-31, AC-33, AC-37, AC-38, AC-40, AC-41, AC-42 · SP-27, SP-32 | A test can assert an outcome, never that a document was read. The only mechanism that reaches these is a hook (E-32's pattern), and only for the ones that name a tool call |
| **Scope and product-shape rules** | RM-01, RM-03, RM-04, RM-06, RM-07, RM-12, RM-13, RM-14, RM-19, RM-21, RM-22, RM-23, RM-31, RM-35 · RD-11..RD-15, RD-17, RD-19, RD-28, RD-29, RD-30, RD-36..RD-43, RD-47, RD-50, RD-52, RD-54, RD-61, RD-64, RD-67, RD-68, RD-70 · GD-04..GD-10, GD-14..GD-19, GD-27, GD-29, GD-30, GD-32, GD-36, GD-40, GD-41, GD-45, GD-46, GD-50, GD-73, GD-84, GD-86, GD-89, GD-93, GD-94 | "PSN and Xbox must not be added" is enforceable only by the absence of a thing, and a test asserting the absence of every unbuilt feature is a test of nothing. Several of these become enforceable the moment the feature exists: RD-37 (two secrets at rest) and RD-38 (the closed request list) are the strongest candidates for a `SteamSessionContractTests` in the shape of `LocalLibrarySyncContractTests`, and the plan recommends them if the user wants a sixth enforced rule |
| **Visual and typographic values** | DS-01, DS-04, DS-05, DS-07, DS-09..DS-11, DS-14, DS-17..DS-28, DS-31, DS-33..DS-36, DS-38..DS-40, DS-43..DS-48, DS-51..DS-55, DS-57..DS-63, DS-66..DS-76, DS-78..DS-84, DS-91..DS-110, DS-116, DS-120..DS-129, DS-131..DS-137, DS-139..DS-148, DS-151, DS-152, DS-160, DS-165..DS-167, DS-170..DS-172, DS-176..DS-180, DS-183..DS-186, DS-190, DS-191, DS-198..DS-200, DS-203, DS-204, DS-206..DS-211, DS-214..DS-219, DS-221..DS-223, DS-230..DS-235, DS-238, DS-241, DS-243, DS-245..DS-247, DS-250, DS-252, DS-255, DS-258, DS-259 · AC-04, AC-07, AC-08, AC-10 | A number in a token dictionary can be asserted; a claim about what a window *reads as* cannot. Where a rule reduces to a number the design system already asserts it through `ThemeContrastTests` and `FloatingLayoutTests` (E-19, E-20). Geometry, ordering, radius and motion values are assertable in principle by inspecting the resolved visual tree, and the cost of that harness is not justified by the failure rate: nothing in the repository's history shows a token drifting silently. Reassess if one does |
| **Behavioural rules with no observable surface yet** | RD-26, RD-56 (already enforced), GD-20, GD-21, GD-22, GD-23, GD-42, GD-43, GD-47, GD-48, GD-55..GD-64, GD-68, GD-81 | The session-detection rules (GD-55..GD-62) are enforceable through `SessionWatcherHarness`, which exists; the plan does not add tests there because the harness already covers discovery, exit and debounce, and the remaining rules (Proton tree matching, `/proc` reads) have no platform to run on in CI. Stated as prose, with the platform gap named |
| **Editorial and process** | CL-32 (a DECISION), RM-24 (covered by E-01/E-07 in part; the `Covers`/`Monitor`/`Enrich` musts are prose), RM-28's second clause, AC-16, AC-23, AC-25, AC-28, AC-36 | "Write SQL that stays legible" and "every threshold is a named parameter with a defensible default" are review criteria. They stay in the reviewer's charter, which is where a human applies them |

---

# PHASE 6 — Sequenced steps

Sixteen steps. Each is independently committable and leaves the repository consistent.
**BLOCKED** means the step cannot start until the named Phase 3 conflict or Phase 2
UNRESOLVED item is adjudicated. Steps 1 to 15 touch no file under `src/` or `tests/` except
where a step says so; step 16 is the only one that adds tests, and it adds exactly the list in
Phase 5.

### Step 0 — Open the decisions log
- **Files:** `docs/decisions.md` (new).
- **Content:** a header, the append-only policy, and nothing else. Every later step appends.
- **Verify:** the file exists; no other document links to it; `AGENTS.md` does not mention it.
- **If wrong:** nothing breaks. If the policy line is missing, later steps start appending
  rationale in a shape nobody can follow.
- **Rows:** none.

### Step 1 — One agent-config file — **BLOCKED on K-03, K-04, K-48**
- **Files:** `AGENTS.md` (becomes the source), `CLAUDE.md` (becomes the single line
  `@AGENTS.md`), `docs/decisions.md`.
- **Work:** merge the two files, taking CLAUDE.md's text wherever they diverge (AG-02, AG-03);
  strip the authority section (CL-12, CL-13, CL-18) and replace it with a domain routing list;
  move CL-10, CL-32 and the rename rationale (CL-03, CL-05) to the log; leave the naming
  rules, the layout, the conventions and the Backlog block as imperative statements.
- **Verify:** open a fresh Claude Code session and confirm CLAUDE.md's import resolves and the
  naming rules are in context; run `dotnet build` (nothing should change) and confirm the
  four "hoard" sites are still listed.
- **If wrong:** every agent loses the `--data-dir` rule and starts writing to the real library,
  which the document says has already happened once. This is the highest-consequence step.
- **Rows:** CL-01..CL-42, AG-01..AG-03 (45).

### Step 2 — Fold spike findings into the build spec
- **Files:** `game-library-design.md` (§4.1, §4.3, §4.5, §5.3, §5.4, §10), `docs/decisions.md`.
- **Work:** replace GD-26 with the verified key names and units (SP-01..SP-07); replace GD-35
  with `IStoreBrowseService/GetItems` and `GetTagList` (SP-08, SP-09); rewrite GD-42/GD-43 to
  drop local SteamCMD and to filter on `tags=patchnotes`, and add the 403 and ±7-day rules
  (SP-10..SP-13); correct GD-66's Epic clause (SP-22, SP-23); delete GD-70/GD-71/GD-72 and
  state the M5 mechanism (SP-33..SP-36); add the Epic and GOG paths and the WAL hazard
  (SP-19..SP-21, SP-24, SP-25); reduce GD-91 to the two questions still open (U-03, U-04).
  Each deleted sentence is appended to the log with the spike that killed it.
- **Verify:** every `[VERIFY]` marker left in the spec has a matching row in Phase 2's
  UNRESOLVED table; `dotnet test` still green (no code changed, so this is a smoke check).
- **If wrong:** an agent codes against `playtime_two_weeks` or reinstates local SteamCMD. Both
  are recoverable and both are caught by the existing parser tests.
- **Rows:** GD-26, GD-35, GD-42, GD-43, GD-66, GD-70, GD-71, GD-72, GD-91, GD-92 ·
  SP-01..SP-25, SP-28..SP-31, SP-33..SP-38 (45).

### Step 3 — Sequencing moves to the roadmap, and only there
- **Files:** `game-library-design.md` (delete §8), `ROADMAP.md` (absorb the exit criteria,
  delete the header's precedence paragraph), `docs/decisions.md`.
- **Verify:** no document says "supersedes"; the phase table carries every exit criterion that
  §8's table carried; M0-M2 and M4 appear with their criteria and their shipped state.
- **If wrong:** an exit criterion is lost and a shipped milestone stops being checkable.
- **Rows:** GD-85, GD-86, GD-87, GD-88, RD-01, RD-02, RD-18, RD-19 (8).

### Step 4 — Non-goals stated once — **BLOCKED on K-10, K-13**
- **Files:** `game-library-design.md` §1, `ROADMAP.md` §3, `docs/decisions.md`.
- **Work:** rewrite §1's non-goal list with no struck text; delete ROADMAP §3's amendment
  table, moving each rationale to the log and each surviving rule to §1; K-10 must be settled
  first (the credentials row amends a non-goal that does not exist) and K-13 (§8's Phase 2
  list still names recommendations).
- **Verify:** §1 reads as a list of six current exclusions with no history in it; the phrase
  "phase 2" appears in exactly one place.
- **If wrong:** the recommender's status becomes ambiguous again, which is the conflict that
  started this.
- **Rows:** GD-03..GD-10, RD-11..RD-15, RD-49, RD-50 (15).

### Step 5 — The Steam account-page rule stated once — **BLOCKED on K-06, K-11**
- **Files:** `game-library-design.md` §4.7, `ROADMAP.md` (delete the two amendment sections),
  `docs/decisions.md`.
- **Work:** §4.7 becomes one rule with the eight binding conditions inline and no history.
  The first amendment's four conditions, the superseded condition 1, and both "why this is an
  amendment" arguments go to the log. K-06 decides whether condition 2 governs the API key and
  the IGDB secret as well as the two session secrets.
- **Verify:** a reader who opens §4.7 cold can state what Winnow may fetch, when, and what it
  may store, without opening another file. `docs/spikes/steam-web-session-auth.md` still holds
  the evidence and asserts nothing.
- **If wrong:** the strictest reading is lost and someone persists a cookie jar. Condition 2 is
  the one worth a test (Phase 5 §5.2 names it).
- **Rows:** GD-46..GD-50, RD-16, RD-17, RD-33..RD-45 (20).

### Step 6 — Carried debt sorted — **BLOCKED on U-01, U-02**
- **Files:** `ROADMAP.md` §6, `game-library-design.md`, `docs/decisions.md`, backlog (CLI).
- **Work:** each of the eighteen debt items splits three ways: the rule half to the spec
  (RD-52's no-`external_ids` rule, RD-64's rail grammar, RD-67's err-visible rule, RD-68's
  seed-row exclusion), the state half to a backlog task, the reasoning to the log. RD-53's
  retained original note is resolved by K-12. U-01 and U-02 decide whether two items exist at
  all.
- **Verify:** every debt item is either a backlog task with an id, a rule in the spec, or a log
  entry, and no item is in two places. `backlog task list --plain` shows the new tasks.
- **If wrong:** debt silently becomes permanent, which is the section's own stated purpose.
- **Rows:** RD-51..RD-68 (18).

### Step 7 — Roadmap rationale to the log
- **Files:** `ROADMAP.md` (§1, §2, §5, §7, the M11/M4.6/GOG/M5 notes), `docs/decisions.md`.
- **Work:** ROADMAP keeps scope, the phase table and the standing constraints. Every "why"
  paragraph moves. The GOG hold keeps its one binding sentence (the reopening condition,
  RD-26) in the roadmap and its arithmetic in the log.
- **Verify:** ROADMAP fits on a screen and a half and contains no paragraph beginning "Why".
- **If wrong:** the reasoning is not lost (it is in the log) but the roadmap stops explaining
  itself to a new reader, which the user may not want. This step is the most reversible.
- **Rows:** RD-03..RD-10, RD-20..RD-32, RD-46, RD-47, RD-48, RD-69, RD-70 (26).

### Step 8 — Build spec consolidation
- **Files:** `game-library-design.md` (§0, §2, §3, §4.1-§4.6, §5, §6, §7, §9, §11),
  `docs/decisions.md`.
- **Work:** every RULE restated imperatively with its reason moved out; §2's framework
  comparison, §3.1's Dapper argument and §11's shelf reasoning go wholesale to the log,
  leaving one-line rules; §5.1's boundary table is corrected against `src/` (K-50 names the
  `Score.*` row that has no module); §9's pitfall list is deleted as a restatement, with each
  pitfall folded into the rule it restates.
- **Verify:** §5.1's rows equal the projects that exist; §9 is gone and nothing it said is
  gone; `winnow-reviewer`'s checklist still has a target for each of its ten items.
- **If wrong:** a boundary rule loses its addressee. Step 16's E-01 catches the ones that
  matter.
- **Rows:** GD-01, GD-02, GD-11..GD-25, GD-27..GD-34, GD-36..GD-41, GD-44, GD-45,
  GD-51..GD-65, GD-67..GD-69, GD-73..GD-84, GD-89, GD-90, GD-93, GD-94 (67).

### Step 9 — Design system §1-§8 — **BLOCKED on K-01, K-02, K-05, K-26, K-27, K-29**
- **Files:** `design-system.md` §1-§8, `docs/decisions.md`.
- **Work:** the palette table carries one hex per token (K-01); §3's axis instruction is
  restated as named cuts or kept, per K-02; §5.1's floor is one number (K-26) and the hue
  rotation either is or is not part of what the app draws (K-27); §5.2's argument is rewritten
  against the current bucket definition (K-28); §5.4 states the shipped approach and drops the
  preference order (K-29). Every revision note moves to the log.
- **Verify:** open the running app beside the document and check the palette, the floor and the
  badge rule; `ThemeContrastTests` still passes unchanged.
- **If wrong:** the visual spec contradicts the shipping build in the one place that is most
  visible. Six adjudications is a lot for one step; it can be split per conflict if the user
  prefers to settle them one at a time.
- **Rows:** DS-01..DS-58 (58).

### Step 10 — Design system §9-§12 — **BLOCKED on K-30, K-33, K-34, K-35, K-36**
- **Files:** `design-system.md` §9-§12, `docs/decisions.md`.
- **Work:** §9's caption rule becomes two sentences (flush, floating) with the four amendments
  in the log (K-34); §9.1's inset rule states both layouts in one place (K-35); §10.5's reason
  is corrected (K-36); the list view's ground is stated once (K-30); focus is stated once
  (K-33), which also closes §13 gap 6.
- **Verify:** `FloatingLayoutTests` and `ThemeContrastTests` still pass; a reader can state the
  caption's ink for either layout from one paragraph.
- **If wrong:** the two layouts' assertions drift apart and the tests stop matching the words.
- **Rows:** DS-59..DS-152 (94).

### Step 11 — The seven §13 gaps — **BLOCKED on U-05, U-06, U-07, U-08, U-09**
- **Files:** `design-system.md` (delete §13), `docs/decisions.md`, backlog (CLI).
- **Work:** gap 7 is resolved and becomes a log entry; gap 6 is closed by step 10; gaps 1-5
  become backlog tasks carrying the interim choice, so the provisional answer is visible where
  work happens rather than in a spec section that reads as authoritative.
- **Verify:** `backlog task list --plain` shows five tasks; §13 no longer exists; nothing else
  in the document references "§13".
- **If wrong:** an open question becomes invisible and gets re-decided per screen, which is
  what §13 was written to prevent.
- **Rows:** DS-153..DS-158 (6).

### Step 12 — Design system §14-§16 collapse — **BLOCKED on K-37, K-38, K-39**
- **Files:** `design-system.md` §14-§16 (rewritten as one section), `docs/decisions.md`.
- **Work:** the largest single edit. One tier table (§16.1's three rows), one set of AA figures
  (§16.6), one backdrop rule (§14.6's user choice), one polarity constant with its derivation,
  one caption rule per layout. Every superseded table, every struck constant and every "amended"
  paragraph moves to the log verbatim, including §16.9's screenshot mapping, which is a
  decisions-log entry by nature.
- **Verify:** run the app, open `SETTINGS › APPEARANCE`, and check every number the screen
  prints against the document; `ThemeContrastTests` and `FloatingLayoutTests` unchanged and
  green.
- **If wrong:** the Appearance screen and the spec disagree, and the screen is the one that is
  measured live, so the document is the thing that is wrong. Low blast radius, high confusion
  cost.
- **Rows:** DS-159..DS-259 (101).

### Step 13 — README stops stating rules — **BLOCKED on K-05, K-07, K-08**
- **Files:** `README.md`, `game-library-design.md`, `docs/decisions.md`.
- **Work:** RM-24's module map becomes the spec's §5.1 (K-07 decides the cardinality); RM-27
  and RM-28's copy-before-reading clause move to the spec as rules; RM-05 is corrected (K-05);
  RM-29's count is either removed or generated (K-08); RM-32's precedence table becomes a
  plain list of where to read further; RM-34's GDPR line is deleted.
- **Verify:** `DocumentationConsistencyTests`' README clause (E-24c) passes; a new reader can
  still install, run and build from README alone.
- **If wrong:** README keeps teaching the reverted refund-line rule to every new reader, which
  is the most-read wrong sentence in the repository.
- **Rows:** RM-01..RM-36 (36).

### Step 14 — `notes.md` becomes backlog tasks — **BLOCKED on U-10, K-32, K-40**
- **Files:** `notes.md` (deleted), backlog (CLI).
- **Work:** one `backlog task create` per item, carrying the verbatim note in the description.
  U-10 decides whether they are requirements; K-32 and K-40 decide three that contradict
  shipped rules. NT-15 already has TASK-60 and is only linked.
- **Verify:** nineteen items are accounted for by a task id or an explicit decision not to
  track them; `notes.md` is gone and nothing links to it.
- **If wrong:** user-recorded intent is deleted. Nothing else in the repository holds these,
  so this step must not run before U-10 is answered.
- **Rows:** NT-01..NT-19 (19).

### Step 15 — Charters thinned, spikes marked as evidence — **BLOCKED on K-47, K-48**
- **Files:** `.claude/agents/*.md` (seven), `.codex/agents/*.toml` (generated or deleted),
  `docs/spikes/*.md` (nine, banner only), `docs/decisions.md`.
- **Work:** each charter keeps its pointer, its domain-specific content from Phase 4.3, and
  the delegation block; every restatement of a spec rule is deleted, which removes AC-05's
  wrong floor, AC-13's wrong bucket name, AC-26's dead GDPR reference, AC-30's dead wiring
  prohibition and AC-35's two-authority claim by construction. Each spike gains one line:
  "Evidence. The current rule is in `<file>` §`<n>`." SP-27's internal precedence claim and
  SP-26's dead verdict are struck, and K-41's stale table row is corrected.
- **Verify:** no charter contains a hex value, a threshold or an endpoint parameter; each spike
  banner names a section that exists (E-24b).
- **If wrong:** a charter goes on teaching a wrong number to whichever agent loads it, which is
  how AC-05 has been shipping a 0.60 floor against a 0.68 spec.
- **Rows:** AC-01..AC-42, AX-01..AX-07, SP-26, SP-27, SP-32 (52).

### Step 16 — Enforcement
- **Files:** `tests/Winnow.Tests/*` (the Phase 5 list), `Migrations/checksums.txt`,
  `.claude/settings.json` (E-32).
- **Work:** land E-01 through E-32. E-24 last, because it will fail until steps 1-15 are done.
- **Verify:** `dotnet test` green; then deliberately break one rule per test (add a `bucket`
  column, edit a shipped migration, put `Flare` on a new control) and confirm the matching test
  fails.
- **If wrong:** a test asserts something the documents do not say, and the documents become
  subordinate to the tests. Each test's name should quote the rule it enforces.
- **Rows:** none.

### Step 17 — Delete the second `tokens.axaml` — **BLOCKED on K-49**
- **Files:** `tokens.axaml` (repo root, deleted), `AGENTS.md` (the "design RECORD" sentence).
- **Verify:** `E-25` passes; the app still builds and the four themes still render.
- **If wrong:** a token that lives only in the root copy is lost. The diff must be read before
  the delete, which is what K-49 asks for.
- **Rows:** none (CL-16 is carried by step 1).

## 6.1 Coverage map

Every Phase 1 row appears in exactly one step.

| Step | Rows | Count |
|---|---|---|
| 0 | — | 0 |
| 1 | CL-01..CL-42, AG-01..AG-03 | 45 |
| 2 | GD-26, GD-35, GD-42, GD-43, GD-66, GD-70, GD-71, GD-72, GD-91, GD-92; SP-01..SP-25, SP-28..SP-31, SP-33..SP-38 | 45 |
| 3 | GD-85..GD-88, RD-01, RD-02, RD-18, RD-19 | 8 |
| 4 | GD-03..GD-10, RD-11..RD-15, RD-49, RD-50 | 15 |
| 5 | GD-46..GD-50, RD-16, RD-17, RD-33..RD-45 | 20 |
| 6 | RD-51..RD-68 | 18 |
| 7 | RD-03..RD-10, RD-20..RD-32, RD-46..RD-48, RD-69, RD-70 | 26 |
| 8 | GD-01, GD-02, GD-11..GD-25, GD-27..GD-34, GD-36..GD-41, GD-44, GD-45, GD-51..GD-65, GD-67..GD-69, GD-73..GD-84, GD-89, GD-90, GD-93, GD-94 | 67 |
| 9 | DS-01..DS-58 | 58 |
| 10 | DS-59..DS-152 | 94 |
| 11 | DS-153..DS-158 | 6 |
| 12 | DS-159..DS-259 | 101 |
| 13 | RM-01..RM-36 | 36 |
| 14 | NT-01..NT-19 | 19 |
| 15 | AC-01..AC-42, AX-01..AX-07, SP-26, SP-27, SP-32 | 52 |
| 16 | — | 0 |
| 17 | — | 0 |
| | **Total** | **610** |

## 6.2 Blocked steps, and what unblocks each

| Step | Blocked on |
|---|---|
| 1 | K-03, K-04, K-48 |
| 4 | K-10, K-13 |
| 5 | K-06, K-11 |
| 6 | U-01, U-02 |
| 9 | K-01, K-02, K-05, K-26, K-27, K-29 |
| 10 | K-30, K-33, K-34, K-35, K-36 |
| 11 | U-05, U-06, U-07, U-08, U-09 |
| 12 | K-37, K-38, K-39 |
| 13 | K-05, K-07, K-08 |
| 14 | U-10, K-32, K-40 |
| 15 | K-47, K-48 |
| 17 | K-49 |

Unblocked and startable today: steps 0, 2, 3, 7, 8, 16. Between them they carry 146 of the
610 rows, and step 2 alone retires five of the twelve conditional-precedence conflicts by
folding the spikes into the spec.

Twenty-three conflicts block nothing and are recorded for completeness: K-09, K-12, K-14, K-15, K-16,
K-17, K-18, K-19, K-20, K-21, K-22, K-23, K-24, K-25, K-28, K-31, K-41, K-42, K-43, K-44,
K-45, K-46, K-50 are resolved as a side effect of the step that touches their rows, except
where the step is itself blocked. Any of them can be escalated to a blocker if the user
disagrees with the disposition Phase 1 records.

---

# Report

## Totals

| | |
|---|---|
| **Claims inventoried** | **610** |

| Class | Count | Share |
|---|---|---|
| RULE | 405 | 66% |
| FINDING | 80 | 13% |
| DEAD | 55 | 9% |
| DECISION | 43 | 7% |
| UNRESOLVED | 27 | 4% |

Sixty-one RULE rows are rule-and-rationale pairs and split into two destinations (Phase 2.1).

| | |
|---|---|
| **Conflicts registered** | **50** (K-01..K-50) |
| of which cross-file disagreements | 34 |
| of which conditional precedence | 12 |
| of which a document reversing itself | 4 (K-05 and K-12 also appear above) |
| **UNRESOLVED items** | **11** (U-01..U-11), covering 27 rows |
| **Steps** | **17**, of which **12 are blocked** |
| **Enforcement checks proposed** | **32** (E-01..E-32), of which 5 extend tests that exist |

## Per-file counts

| File | Rows | RULE | DECISION | FINDING | DEAD | UNRESOLVED |
|---|---|---|---|---|---|---|
| `CLAUDE.md` | 42 | 37 | 4 | 1 | 0 | 0 |
| `AGENTS.md` | 3 | 0 | 0 | 0 | 3 | 0 |
| `README.md` | 36 | 22 | 2 | 9 | 2 | 1 |
| `ROADMAP.md` | 70 | 31 | 20 | 14 | 4 | 1 |
| `game-library-design.md` | 94 | 70 | 8 | 3 | 13 | 0 |
| `design-system.md` | 259 | 208 | 9 | 17 | 20 | 5 |
| `notes.md` | 19 | 0 | 0 | 0 | 0 | 19 |
| `docs/spikes/` | 38 | 2 | 0 | 34 | 1 | 1 |
| `.claude/agents/` | 42 | 35 | 0 | 1 | 6 | 0 |
| `.codex/agents/` | 7 | 0 | 0 | 1 | 6 | 0 |
| **Total** | **610** | **405** | **43** | **80** | **55** | **27** |

## Files not fully accounted for

Four files carry normative weight and are outside the brief's list. None is inventoried; each
is named here rather than silently skipped.

1. **`docs/recommendation-engine.md` (63KB).** Cited as authoritative by README's precedence
   table (RM-32) and named a deliverable by the recommendation-engine charter (AC-29). It
   holds the scoring model, every threshold and the explanation contract, which is a domain
   with no other owner. Phase 4 proposes it as `Winnow.Recommend`'s domain document. It needs
   its own inventory pass before step 15 thins that charter.
2. **`docs/facet-provenance.md` (14KB).** Owns where every filter value comes from, which
   design-system §11.4 (DS-124) depends on and does not restate. Same treatment needed.
3. **`docs/code-review-2026-08-28.md` (51KB) and `docs/stabilization-2026-08-28.md` (9KB).**
   Referenced by ROADMAP §6 (RD-32 cites findings F10/F19) and by backlog TASK-34. They are
   historical review records, so they are decisions-log material by nature, but they carry
   release gates and a re-review cadence that may still bind.
4. **`mock-library.html`.** Named a companion file and "the visual target" by design-system's
   header (DS-02) and by both avalonia-ui charters (AC-01). It is a 12KB HTML mock, not
   inventoried, and it is a second visual source of truth beside `design-system.md` and
   `tokens.axaml`. Whether it still governs anything is a fiftieth-first conflict this plan
   did not open.

Within the brief's list, everything is accounted for. `docs/spikes/` was inventoried for the
claims that carry override weight rather than for every measurement in 260KB of evidence; the
un-inventoried remainder is by construction non-normative once step 15 lands, and step 2 is
the step that decides which of it is not.

## What this plan does not do

- It resolves no conflict. Fifty are registered and twelve steps wait on them.
- It writes no test. Phase 5 names thirty-two and their mechanisms.
- It proposes no change under `src/`. The only `src/` change anywhere in the plan is deleting
  the duplicate `tokens.axaml` at the repository root, which is not under `src/`, and step 17
  is blocked on reading the diff first.
- It deletes no text without a destination. Four files disappear; each row in them lands in a
  named file, a backlog task, or `docs/decisions.md`.
