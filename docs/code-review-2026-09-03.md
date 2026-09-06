# Winnow full repository code review — 2026-09-03

## Scope and method

This review targets the **current working tree** of `safwyls/winnow` at merge commit
`b20e263` plus the uncommitted changes present on 2026-09-03 (the tree was dirty at review
time; roughly forty files under `backlog/`, `docs/`, and `src/Winnow.App` were modified,
consistent with in-flight feed-card and settings work).

What was done:

- `dotnet build Winnow.slnx` → **succeeds, 0 warnings, 0 errors** (warnings are errors here).
- `dotnet test Winnow.slnx` → **2,990 tests pass, 0 fail, 0 skip** (Winnow.Tests 2,775,
  Winnow.Recommend.Tests 145, Winnow.Covers.Tests 70).
- Every P1 and most P2 findings from [code-review-2026-08-28.md](code-review-2026-08-28.md)
  were re-verified against the code as it stands.
- New code that did not exist at the last review was examined directly: the identity-link
  merge queue (`0018`/`0019` migrations, `IdentityLinkRepository`, `MergeQueueViewModel`),
  the Steam WebView session capture, the cover lease pipeline, and the update-poll cascade.
- Sweeps for common hazard classes: SQL string interpolation, `async void`, `.Result`/`.Wait()`,
  `DateTime.Now`, `Process.Start`, plaintext secrets, single-instance guards, HTTP timeouts.

## Executive verdict

The tree has improved dramatically since 2026-08-28. **All eight of the previous release
gates have been addressed**, and the build/test regression (F01) that made the last review
blocker-grade is green. The highest-risk prior findings — the rename migration (F02),
fill-only enrichment (F03), the network-gated first paint (F04), the unbound OAuth bridge
(F05), sweep starvation (F06/F07), the writer-lock pileup (F08), the phantom play facts
(F10), missing-coverage recommendations (F15), and the never-executed merge (F09, redesigned
as link relations) — are all demonstrably fixed, most with comments naming the finding they
close and tests behind them.

What remains is a smaller set of weaknesses, none of which is a confirmed unrecoverable-loss
class defect:

- the Steam Web API key and IGDB client secret are stored **plaintext** in the database
  while the README claims credentials use DPAPI (N01);
- the main window's startup path is `async void` with no error boundary (N02);
- there is still no single-instance guard (N03, carried F39);
- feed impressions are still recorded at generation, not display (N04, carried F16);
- the update poller has no per-app failure budget, so persistently failing appids lead every
  batch forever (N05, partial F11).

No evidence was found of: injected SQL (all interpolation is over internal table/column
constants), committed secrets (the one `LauncherClientSecret` is Epic's public launcher
credential, embedded by design), storefront writes, `WebClient`/raw sockets, or `DateTime.Now`
in persisted logic.

## Severity model

| Level | Meaning |
|---|---|
| P0 | Active or near-certain unrecoverable loss/security compromise. None found. |
| P1 | Release blocker: plausible data loss/security issue, core promise violated, or severe correctness failure. |
| P2 | Important defect/debt likely to cause wrong results, poor reliability, or major scaling trouble. |
| P3 | Lower-risk hardening, maintainability, consistency, or hygiene concern. |

## New findings

### N01 — Steam API key and IGDB secret are plaintext at rest, contradicting the documented DPAPI claim (P2)

**Evidence.** The Epic and Steam *sessions* are properly DPAPI-protected
(`DpapiEpicSecretProtector`, `DpapiSteamSecretProtector`, both refusing a plaintext fallback).
But the Steam Web API key is written raw to the settings table —
[StoreConnections.cs:104](../src/Winnow.App/Services/StoreConnections.cs#L104) →
`_settings.SetAsync("steam.api_key", value, ct)` — and read raw by
`SettingsTableApiKeySource`. The IGDB/Twitch client secret and the cached Twitch access
token are likewise plaintext (`igdb.client_secret` in
[CredentialSources.cs:16](../src/Winnow.Enrich.Igdb/Credentials/CredentialSources.cs#L16);
token written unencrypted at
[TwitchTokenProvider.cs:219](../src/Winrich.Igdb/../src/Winnow.Enrich.Igdb/Auth/TwitchTokenProvider.cs#L219)).
Meanwhile the README states *"credentials use DPAPI"*.

**Impact.** A Steam Web API key grants read access to a user's full owned-games list for the
key holder; the IGDB client secret is a long-lived bearer credential. Both are recoverable
from `winnow.db` by anything with the user's disk profile — including other processes and
cloud-sync copies of `%LOCALAPPDATA%`. The documentation overpromises what the product
protects, which is its own defect: the privacy story is part of the product.

**Remediation.** Route both through the existing DPAPI protector pattern (with their own
versioned entropy constants), migrate plaintext rows on first read, and re-word the README
to say precisely which credentials are protected. Keep the "no plaintext fallback, refuse
rather than degrade" rule the session stores already follow.

### N02 — `MainWindow.OnOpened` is `async void` with no error boundary (P2)

**Evidence.** [MainWindow.axaml.cs:244](../src/Winnow.App/Views/MainWindow.axaml.cs#L244)
declares `protected override async void OnOpened(EventArgs e)` and awaits
`library.LoadCommand.ExecuteAsync(null)`, the merge queue load, `display.LoadAsync()` and
the feed, all with no surrounding `try`/`catch`.

**Impact.** Any exception thrown by the library query, the queue load, or the feed compute
escapes an `async void` on the UI thread and takes the process down at startup — precisely
when the database is most likely to be in a fresh or newly-migrated state. This is the
same class as the fixed F36, one layer up.

**Remediation.** Wrap the body in a try/catch that logs and leaves the shell standing on
whatever last succeeded, mirroring the error boundary `Program.cs` already puts around its
startup task. Every other `async void` in the tree is an event handler in a view code-behind;
this is the only one that sequences load-bearing startup work.

### N03 — No single-instance guard (P2, carried F39)

**Evidence.** A sweep for `Mutex`/`SingleInstance` across `src/` finds nothing. Two copies of
Winnow can run against the same data directory.

**Impact.** Two process watchers double-record sessions (inflating playtime facts), two
snapshot schedulers race the SQLite writer, and two update pollers double the external
request traffic against services that are rate-limited by volunteer goodwill.

**Remediation.** A named mutex acquired before the host starts; the second instance shows a
sentence and exits. Cheap, and it removes a whole family of "two copies" explanations from
every future bug report about counts drifting.

### N04 — Feed impressions are still recorded at generation, not display (P2, carried F16)

**Evidence.** [FeedService.cs](../src/Winnow.App/Services/FeedService.cs) `ComputeAsync`
calls `RecordSurfacedAsync` immediately after the engine returns, before any UI visibility
check exists anywhere in the feed path. Backlog task-10 ("Record feed impressions when a
card is actually shown") is `To Do`.

**Impact.** Rotation memory ("recently surfaced") is fed by cards the user never saw, so the
feed's own fairness mechanism is working from fiction. This was a P1 last time; nothing
about the code has changed, and it is now tracked, which is why it is documented here as P2
carried debt rather than a fresh blocker.

**Remediation.** As written in task-10's acceptance criteria: record on viewport entry, not
on generation.

### N05 — Update poller has no per-app failure budget (P2, partial F11)

**Evidence.** [UpdateSignalPoller.cs](../src/Winnow.Enrich.Updates/UpdateSignalPoller.cs)
deliberately does not stamp `last-polled` on `NewsOutcome.Unavailable` (documented in the
comment at the `Unavailable` arm) so transient outages don't cost an app its slot. But the
due ordering is `LastPolledAt` ascending, failures leave the state untouched, and there is
no failure count, backoff, or attempt budget anywhere in the store.

**Impact.** The 2026-08-28 review's starvation mechanism is unchanged for the *app-specific*
failure case: an appid whose news endpoint persistently errors sits at the front of the due
order forever and spends its share of `MaxAppsPerBatch` on every pass, cap-starving the apps
behind it. The outage-wide case (all apps fail together) is harmless, and the comment shows
the trade was considered — but nothing bounds the per-app case.

**Remediation.** Persist `last_attempt_at` and a bounded failure count alongside
`LastPolledAt`; order by `last_attempt_at` for apps in a failure streak, so a persistently
failing appid slips backward instead of leading every batch. A test with one poison appid
and several healthy ones proves it.

### N06 — No CI gate exists (P3, carried F43)

**Evidence.** No `.github/workflows/` directory. The previous review's F01 — a solution that
did not compile — happened with no gate to catch it, and the remediation for that finding
("add CI") was the only release-gate item that was never done.

**Impact.** The 2026-08-28 regression proved the failure mode is real, and this repo has
unusually testable gates (`TreatWarningsAsErrors`, `SchemaDisciplineTests`, 2,990 tests)
that a five-line workflow would enforce on every push.

**Remediation.** `dotnet build` + `dotnet test` on the solution, on `main` and PRs.

### N07 — No rolling diagnostic log (P3, carried F41)

**Evidence.** Logging goes to console/trace providers only; no file sink exists anywhere in
`Winnow.App`'s logging setup.

**Impact.** The failure modes this product cares about (a sign-in that hangs, a migration
refusal, a sweep that truncated) are logged with beautiful, actionable sentences — to a
console a desktop user never sees. When a user reports "it didn't work", there is nothing to
ask for.

**Remediation.** A size-capped, rotating file in the data directory, with the documented
no-secrets discipline already enforced by hand in the logging call sites.

### N08 — Cover downloads have no response-size bound (P3, partial F28)

**Evidence.** [SteamCapsuleSource.cs:72](../src/Winnow.Covers/SteamCapsuleSource.cs#L72)
does `ReadAsByteArrayAsync` with no maximum; the covers client's 30-second timeout is the
only bound. (The transport-failure-vs-missing-negative confusion from F28's second half is
fixed — failures are now logged and left uncached, verified.)

**Impact.** A misbehaving or hostile CDN response can spike memory before decode. Low
likelihood given the endpoints involved and TLS, which is why this is hardening, not a hole.

**Remediation.** Enforce a `MaxBytes` cap (a cover capsule has a known size class — anything
over a few MB is not an image this app wants) and treat over-cap as a transport failure,
not a negative cache entry.

### N09 — Most Enrich HTTP clients set no explicit timeout (P3)

**Evidence.** Only the covers client sets `client.Timeout` (30s, in
`CoverCacheServiceCollectionExtensions`). The Steam, SteamWeb, IGDB, GamesDb, Steam-news and
Epic clients rely on the `HttpClient` default of 100 seconds; their resilience handlers
match on `TimeoutException`, but no per-attempt timeout option was found configuring one.

**Impact.** A wedged connection can hold a background slot for the default timeout, and retry
policies operate on a budget measured in minutes rather than seconds. Everything is
off the UI thread now (F04 verified fixed), so this is throughput/reliability debt, not jank.

**Remediation.** An explicit `Timeout` (and ideally a Polly per-attempt timeout) per named
client, tuned to what each endpoint owes.

### N10 — The library tile set is built on the UI thread between awaits (P3, F13 residual)

**Evidence.** `LibraryViewModel.LoadAsync`
([LibraryViewModel.cs:616](../src/Winnow.App/ViewModels/LibraryViewModel.cs#L616)) now uses
bulk reads (the N+1 half of F13 is fixed), but it is invoked from `OnOpened` on the
dispatcher, and the per-row resolution, tile construction, filter rebuild and count work
all run synchronously on the UI thread between the awaited queries.

**Impact.** On a several-thousand-title library the first paint stalls behind tile
construction. Not the N+1 death of the last review; a smoothness debt.

**Remediation.** Build the tile collection off-thread and marshal the finished set once,
the same pattern `RefreshLibraryAsync` already uses for its dispatch hop.

### N11 — The bulk link act has no partial-failure boundary (P3)

**Evidence.** `MergeQueueViewModel.LinkAllAsync` loops the checked cards; per card, the link,
the candidate rejections and the expansion refusals are separate transactions
([MergeQueueViewModel.cs:783](../src/Winnow.App/ViewModels/MergeQueueViewModel.cs#L783)). A
throw from card 3 of 8 leaves cards 1–2 applied and marked resolved but no dock/undo run is
shown, and the method's caller shows nothing at all — the exception surfaces through the
command.

**Impact.** The sweep's admission/reconcile pass eventually retires the orphaned proposals,
so the database converges; but for the rest of the session the queue's visual state (what is
"resolved", what the undo dock thinks happened) diverges from the database, and the user's
last answer may look lost.

**Remediation.** Wrap each card in its own try/catch, collect the failures, and show the dock
with both counts; or stage the whole batch and apply it in one transaction.

### N12 — Hygiene: real account data and rename leftovers in the working tree (P3)

**Evidence.** `gdpr/` contains the real exported Steam account pages
(`sceadwesAccount.html` and history) — correctly gitignored, and the fixtures under
`tests/fixtures/steam/` are sanitized copies, but the real exports live in the repo
directory. `tests/Hoard.Recommend.Tests/` survives from before the rename holding only
`obj/` build artifacts (the assembly-info files there still say `Hoard`).

**Impact.** The gdpr tree is one `git add -f`, one careless zip of the folder, or one sync
tool away from leaving the machine; the stale test directory is a trap for any future
"why does a Hoard test project exist" question.

**Remediation.** Move the account exports out of the repository entirely (they are working
data, not fixtures), and delete `tests/Hoard.Recommend.Tests`.

### N13 — Raw build history is still only polled in cascade (P3, carried F12 — accepted design)

**Evidence.** The poller fetches build info only when news changed or while a watch window
is open, gated by `CascadeMaxAnnouncementAgeDays`. The comments now document this as the
deliberate cost model, including the reasoning for the gate.

**Impact.** A release whose news feed is empty or untagged never collects raw build pushes,
so the two-independent-signals corpus the design doc promises is one-sided for that
population. Because the choice is now documented in the code, this is recorded as a known
limitation rather than a defect — but the limitation is invisible to a user reading the
`Patched` bucket's meaning.

**Remediation.** If the corpus matters for future heuristic retuning, add the low-frequency
independent build baseline the last review described; otherwise, state the gap in the
design doc so it stops looking like an oversight.

## Status of the 2026-08-28 findings

Verified against the current tree. ✓ = fixed and re-verified; ~ = partially fixed, residual
captured above; ✗ = still open, captured above; ? = not re-verified this pass.

| ID | Severity | Status | Notes |
|---|---:|---|---|
| F01 | P1 | ✓ | Build and all 2,990 tests green |
| F02 | P1 | ✓ | Staging-sibling copy, file-by-file validation, single-rename promotion, usability-based `Choose`, sidecar-mixing refusal |
| F03 | P1 | ✓ | `COALESCE(stored, incoming)` with explanatory comment; regression tests |
| F04 | P1 | ✓ | Window opens on existing data; startup pipeline is a background `Task.Run`; snapshot scheduler documented local-only |
| F05 | P1 | ✓ | Per-attempt cryptographic state, exact-origin message check (`AcceptsMessageFrom`), popup/navigation classification, scheme+host+port redirect matching |
| F06 | P1 | ✓ | Persisted sweep cursor, wrap-around resume, truncation never stamped complete |
| F07 | P1 | ✓ | `SoftMatchAdmission` + `ResolveAndReconcileAsync` retire non-proposable pendings, preserve terminal decisions |
| F08 | P1 | ✓ | Whole-table preload, pure-CPU scoring with no writer held, batched writes |
| F09 | P1 | ✓ | Redesigned: destructive merge retired (0019), standing merges replayed into links, link/retract with acts |
| F10 | P1 | ✓ | One coherent winning play tuple; per-account union that never crosses accounts |
| F11 | P1 | ~ | N05: transient-outage half fixed and documented; per-app failure budget absent |
| F12 | P1 | ~ | N13: cascade-only build polling, now documented as design |
| F13 | P1 | ~ | N+1 fixed via bulk reads; UI-thread tile building remains (N10) |
| F14 | P1 | ? | Cover ownership redesigned around leases/LRU/`CoverPresenter`; looks fixed, not exhaustively verified |
| F15 | P1 | ✓ | `UpdateCoverage` gates the probably-done penalty; `ScoreBounds` bounds hidden penalties |
| F16 | P1 | ✗ | Open as N04; tracked as backlog task-10 |
| F17 | P1 | ✓ | Pending saves tracked and drained in `Dispose` |
| F18 | P2 | ? | Unit-of-work leases exist; atomicity-by-caller not re-verified |
| F19 | P2 | ? | Not re-verified |
| F20 | P2 | ✓ | Moot/absorbed: merge candidate rows are now proposals only, canonicality handled in the resolver preload |
| F21 | P2 | ? | Not re-verified |
| F22 | P2 | ? | Not re-verified |
| F23 | P2 | ? | Not re-verified |
| F24 | P2 | ~ | `AchievementQueryRepository` + UI wiring now exist; depth not verified |
| F25 | P2 | ~ | Roots normalized through `GetFullPath`; `manifest.InstallDir` still joined without an escape check |
| F26 | P2 | ✓ | `SourceSetIdFor` computed per call; negatives reopen when a source's capability changes |
| F27 | P2 | ? | Lease design presumably covers it; not exhaustively verified |
| F28 | P2 | ~ | Transport-failure-vs-missing fixed; response size unbounded (N08) |
| F29 | P2 | ? | Not re-verified |
| F30 | P2 | ~ | Still Windows-only; now documented in the README as a product limitation |
| F31 | P2 | ✓ | `SqliteEpicCatalogCache` persists the catalog cache |
| F32 | P2 | ✓ | `ScoreBounds` models subtractive history; shortlist is bound-aware |
| F33 | P2 | ✓ | Coverage-tier inference replaced by the same facts/bounds machinery |
| F34 | P2 | ? | Not re-verified |
| F35 | P2 | ? | Not re-verified |
| F36 | P2 | ✓ | Startup is a background task with catch-all and bounded drain — but see N02 for the window's own `OnOpened` |
| F37 | P2 | ? | Not re-verified |
| F38 | P2 | ? | Not re-verified |
| F39 | P2 | ✗ | Open as N03 |
| F40 | P2 | ~ | Sessions DPAPI-protected, no plaintext fallback; API key and IGDB secret plaintext (N01) |
| F41 | P2 | ✗ | Open as N07 |
| F42 | P2 | ✓ | Pre-migration backup with policy, quick_check gates before and after, prune only after readback verifies |
| F43 | P2 | ✗ | Open as N06 |
| F44 | P3 | ? | Not re-verified |
| F45 | P3 | ~ | Explicit `DateTimeKind.Unspecified` handling now exists at the read sites checked |
| F46 | P3 | ✓ | `checksums.txt` + `SchemaDisciplineTests.No_shipped_migration_has_been_edited` |
| F47 | P3 | ? | Not re-verified |
| F48 | P3 | ? | Not re-verified |
| F49 | P3 | ~ | Comment/naming discipline is now exemplary (findings cited by ID in comments); spot-checked only |
| F50 | P2 | ✓ | Single `BrightFloor = 0.68` authority with the calibration history documented |

## Strengths worth preserving

- **The remediation discipline is unusual.** Fixed findings are fixed at the named seams,
  with comments citing the finding ID and explaining the failure mode, and tests behind
  them. `WebView2AuthPrompt`'s "THE ORIGIN CHECK" comment is the tone the whole tree now has.
- **Data-loss paranoia in the right places.** Staged-and-validated directory promotion,
  backup-before-migration with refusal-on-no-backup, quick_check before and after, checksums
  on shipped migrations, and a standing-merge replay into links before the journal was
  dropped — all verified above.
- **Secret hygiene at the boundary.** No plaintext fallback for sessions (refuse rather than
  degrade), redacted `ToString` on every credential type, URIs-not-logged in the auth path.
  The plaintext API-key gap (N01) is the exception, not the rule.
- **Safe SQL discipline.** Every interpolated string in the data layer interpolates internal
  table/column constants; all values are parameters.
- **Cost modeling in the update poller** (eliminate/cascade/stagger) and the cover negative
  cache are the kind of measured design that keeps this app a good citizen of volunteer-run
  endpoints.

## Release gates

Nothing here is release-blocking to the degree of the last review's list, but before calling
the tree shippable:

1. Close the DPAPI gap and correct the README's claim (N01) — the privacy story is the product.
2. Put an error boundary around `OnOpened` (N02) — one try/catch removes the startup-crash class.
3. Add the single-instance mutex (N03).
4. Add CI: build + test + schema-discipline on every push (N06).
5. Fix impressions-on-display (N04, task-10) or consciously defer it with the rotation caveat
   written into the recommendation doc.

## Follow-ups for the next pass

- Re-verify the `?` rows above, especially F18/F19 (transaction atomicity and play-record
  idempotency), F25's `InstallDir` escape, F29 (metadata cache versioning), and F35/F47/F48
  (accessibility), which this pass did not re-examine.
- Decide N13 (independent build polling) as a design question, not a bug.
