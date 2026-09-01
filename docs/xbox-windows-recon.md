# Xbox on Windows as a fourth store — reconnaissance

**Status: desk recon, not a spike.** Everything in `docs/spikes/` was verified against real
files, a real database or a live endpoint. This was not. It is a survey of what a fourth
store would cost and where the load-bearing unknowns are, written so that whoever runs the
actual spike knows what to point the instruments at. Every claim below is labelled:

- **[SRC]** — sourced to vendor documentation or to a shipping open-source implementation, cited inline.
- **[INFER]** — a reasonable reading of the sources, not directly stated by them.
- **[VERIFY]** — must be measured on a real Windows machine with real Xbox games before any code is written. Section 11 collects these.

Two constraints on this recon worth stating plainly, because they bound its confidence:

- No Windows machine was available, so nothing on disk was inspected. Compare
  `docs/spikes/epic-gog-local-files.md`, which opened the actual Galaxy database and found
  three things the plan had wrong.
- The session's egress proxy blocks `gamesdb.gog.com`, `api-docs.igdb.com` and
  `*.xboxlive.com`, so no endpoint was probed live. The Epic spike's whole method — call it
  and see — was unavailable here.

---

## 1. The standing exclusion, and whether this recon overturns it

`game-library-design.md` §4.6 excludes PSN and Xbox in one breath and gives three reasons.
`ROADMAP.md` §3 reaffirms it and adds: *"Epic OAuth is not a precedent for these."*
`docs/spikes/epic-oauth.md` §1 already did the work of testing that claim for Epic and built
the table to do it with. The same table, for Xbox:

| §4.6 reason | PSN | Xbox / Microsoft Store | Verdict |
|---|---|---|---|
| No consumer API; every wrapper is reverse-engineered | True | **True.** `titlehub.xboxlive.com` is undocumented — Microsoft's own docs repo carries an open issue saying so [SRC]. The one *documented* ownership API, Collections v9, is a publisher service-to-service endpoint requiring a Partner Center identity [SRC] | **Trips, same as PSN** |
| A manual cookie extraction the user repeats every ~2 months | `npsso` from `ca.account.sony.com` | **Does not apply.** Sign-in is a standard MSA OAuth authorization-code flow at `login.live.com/oauth20_authorize.srf` with `Xboxlive.signin Xboxlive.offline_access`, redirecting to `login.live.com/oauth20_desktop.srf` [SRC] — the same shape as the Epic flow M4.6 already ships an embedded browser for | **Does not trip** |
| Documented account-ban risk | PSNAWP's own docs warn of temporary or permanent bans and recommend a throwaway account | **Nothing equivalent found.** No Xbox library tool's documentation carries such a warning, and no report of an Xbox ban attributed to Playnite's Xbox plugin surfaced. But Microsoft's enforcement pages prohibit "unauthorized third-party software" in broad terms [SRC], which is broader language than anything Epic publishes | **Ambiguous — weaker than PSN, worse than Epic** |

So the exclusion's *stated* reasons do not survive intact: two of the three are PSN-shaped
and do not describe Xbox. That is the same finding the Epic spike made, and it would be
intellectually dishonest to reach a different conclusion here just because the answer is
less convenient.

**But the reasons that actually matter for Xbox are not in §4.6 at all**, because §4.6 was
written about PSN and never looked at Xbox on its own terms. They are sections 6, 7 and 8
below, and they are worse than anything in that table:

1. **Game Pass is a subscription, and Winnow's entire domain model says "owned".**
2. **Playtime — the input the recommender is built on — is missing or partial by design.**
3. **Session detection, the M3a mechanism the whole flywheel depends on, does not work on MSIX games** without a Windows-specific mechanism Winnow does not have.

The honest summary is: *§4.6's argument against Xbox is wrong, and the conclusion is
probably still right, for reasons §4.6 never gave.* Section 12 states what would change that.

---

## 2. "Xbox on Windows" is three different things

Conflating them is the fastest way to design the wrong module. [SRC, INFER]

| Thing | What it is | Winnow's interest |
|---|---|---|
| **Microsoft Store MSIX/UWP games** | Games packaged and installed as AppX/MSIX packages, whether bought outright or streamed in via Game Pass. The install substrate. | This is what is *on disk* and what a local reader can see. |
| **The Xbox app (`Microsoft.GamingApp`) + Gaming Services** | The storefront/launcher and the system service that installs, licenses and launches those packages. | Owns install locations and the install/launch verbs. |
| **The Xbox Live account (MSA + XUID)** | The identity that holds entitlements, achievements, and title history — across console *and* PC. | The only route to owned-but-not-installed, and it does not distinguish PC from console without filtering. |

The fourth store is really *"Microsoft Store PC games, as entitled to an Xbox Live
account"*. Naming it `xbox` in `ExternalIdProviders` would be consistent with how the app
names things to users; naming it `winstore` would be consistent with GOG's identity graph
(section 9). They cannot both be right and the choice is not cosmetic — it is what goes in
the `external_ids.provider` CHECK constraint forever.

---

## 3. What identifies an Xbox PC game

Four different ids are in play, and picking the wrong one as `external_ids.provider_id` is
the kind of mistake that is expensive to undo after it is in users' databases.

| Id | Shape | Where it comes from | Suitability as `provider_id` |
|---|---|---|---|
| **PackageFamilyName (PFN)** | `Microsoft.HaloInfinite_8wekyb3d8bbwe` | Local package enumeration; also returned by `titlehub` as `pfn` [SRC] | **Best candidate.** It is the one id that appears on *both* sides — local disk and the remote library — which is exactly the join the resolver needs, and it is the id Playnite's Xbox plugin uses as its `GameId` [SRC] |
| **Xbox TitleId** | 8 hex digits [SRC] | `titlehub`, `MicrosoftGame.config` | Needed for the playtime call (section 7). Not present in a local package enumeration |
| **Store ProductId** | 12 alphanumeric, e.g. `9NBLGGH4R315` | `MicrosoftGame.config` `StoreId` [SRC] | The id a store-page deep link needs |
| **PackageFullName** | PFN plus version and architecture | Local enumeration | Version-bearing, so it changes on every game update. **Never store it** |

**Recommendation: `provider_id` = PackageFamilyName**, with TitleId and StoreId carried as
enrichment if needed. This mirrors the Epic decision in `epic-gog-local-files.md` §20, where
the id stored and the id looked up differed and needed `IStoreArtifactAliasSource` to
bridge; here they can be the same id, which is a real simplification.

---

## 4. Local ingest — what is on disk and whether it can be read

### 4.1 Install locations

- The Xbox app's default install root is `C:\XboxGames`, changeable per drive, one folder per drive [SRC].
- Each drive the Xbox app considers usable carries a hidden ~28-byte file named `.GamingRoot` at its root, which points at that drive's games folder [SRC]. **[VERIFY]** its exact binary layout — it is reported as a tiny binary blob containing a UTF-16 relative path, not text, and no vendor documentation of the format was found.
- Older and non-Xbox-app Store games live under `C:\Program Files\WindowsApps`, which is ACL'd to `NT SERVICE\TrustedInstaller` and is not readable by an unelevated process [SRC].

`.GamingRoot` is the honest discovery mechanism — the direct analogue of
`libraryfolders.vdf` — and it is the single highest-value thing to verify first, because
without it the reader is hardcoding `C:\XboxGames` and getting multi-drive users wrong.

### 4.2 Enumerating installed packages

Three routes, in decreasing order of cleanliness:

1. **`Windows.Management.Deployment.PackageManager.FindPackagesForUser("")`** — the documented API; returns `Package` objects carrying `Id.FamilyName`, `InstalledLocation.Path` and `SignatureKind` [SRC]. `SignatureKind == Store` plus `IsFramework == false` filters out sideloaded and framework packages [SRC].
2. **The AppModel registry repository** — `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages`, one subkey per installed package carrying `PackageRootFolder` and `DisplayName` [SRC].
3. **`HKLM\...\GamingServices\PackageRepository`** — referenced repeatedly in Microsoft's own Game Pass troubleshooting guidance as the store of Xbox-app install records, but its layout is undocumented. **[VERIFY]**

Route 1 has a **project-boundary cost that route 2 does not**. `PackageManager` is WinRT.
Consuming it from .NET needs a Windows-flavoured target framework
(`net10.0-windows10.0.x.y`), and **every project in this repo currently targets bare
`net10.0`** — including `Winnow.App` itself. A new `Winnow.Ingest.Xbox` could carry the
Windows TFM alone while the rest of the graph stays portable, but `Winnow.App` references
it, so the multi-targeting question lands in the composition root either way. The precedent
is `Winnow.Auth.WebView`, which already isolates a Windows-only dependency behind a
`Winnow.Core` interface (§5.1) — the same shape works here, with the reader behind an
interface and a no-op implementation off Windows.

Route 2 is a plain registry read from `net10.0` with `Microsoft.Win32.Registry`, exactly
what `GogInstalledGameRegistry` already does, and needs no TFM change at all. It is
undocumented-but-stable in practice. **This trade is a real decision and should be made
deliberately, not by whoever writes the first line of code.**

### 4.3 Telling a game from an app

Package enumeration returns Notepad, Calculator and the Xbox app itself alongside games.
Candidate discriminators, none verified:

- The presence of `MicrosoftGame.config` in the install folder — the GDK packaging manifest, which only games ship [SRC]. Requires reading inside the install directory (section 4.4).
- The `Windows.Full Trust` / gaming category in the appx manifest.
- Cross-referencing against the remote title list, which is what Playnite does: it enumerates local packages and keeps only those whose PFN matches a title from the account's library [SRC].

Playnite's approach sidesteps the classification problem entirely by making the *remote*
list authoritative — but that means **a signed-out user gets nothing at all**, which breaks
the pattern every other Winnow store follows (Steam, Epic and GOG all yield a library with
no sign-in). **[VERIFY]** whether `MicrosoftGame.config` presence is a sufficient local-only
discriminator.

### 4.4 The ACL problem

`WindowsApps` is not readable unelevated [SRC]. Two consequences:

- `PackageManager` still *reports* `InstalledLocation.Path` for such packages — the API answers, the directory listing does not. [INFER]
- **[VERIFY]** whether `C:\XboxGames\<Game>\Content\` is readable unelevated. This is the single most important unknown in the local story, because it decides whether `MicrosoftGame.config` can be read, and — via section 8 — whether sessions can be detected at all.

---

## 5. There is no local ownership file

Steam has `localconfig.vdf`. GOG has `galaxy-2.0.db`. Epic has `catcache.bin`. **No
equivalent for Xbox surfaced in this recon**, and its absence is structural rather than
accidental: Game Pass entitlement is a live licence check against Microsoft's servers, not a
list on disk. **[VERIFY]** by inspecting `%LOCALAPPDATA%\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalCache`
on a real machine — Microsoft's own troubleshooting advice tells users to delete its
contents to fix a missing library, which at minimum proves the app caches *something*
library-shaped there [SRC].

If that verification comes back empty, the consequence is severe and should be stated
before any work starts: **Xbox ingest with no sign-in yields only installed games.** Winnow
exists to surface *owned and unplayed*. A store that can only report what is already
installed contributes almost nothing to the product's actual premise — an Xbox user's
unplayed pile is precisely the games they never installed.

---

## 6. Remote ownership: one route, and what it costs

The only route to the full library is the Xbox Live account. Playnite's Xbox plugin is the
reference implementation and its flow is [SRC]:

```
1. MSA OAuth (public client, no secret)
   authorize:  https://login.live.com/oauth20_authorize.srf
   token:      https://login.live.com/oauth20_token.srf
   client_id:  38cd2fa8-66fd-4760-afb2-405eb65d5b0c
   redirect:   https://login.live.com/oauth20_desktop.srf
   scope:      Xboxlive.signin Xboxlive.offline_access

2. Xbox user token   POST https://user.auth.xboxlive.com/user/authenticate
3. XSTS token        POST https://xsts.auth.xboxlive.com/xsts/authorize
4. Library           GET  https://titlehub.xboxlive.com/users/xuid({xuid})/titles/titlehistory/decoration/detail
                          x-xbl-contract-version: 2
                          Authorization: XBL3.0 x={uhs};{token}
5. Playtime          POST https://userstats.xboxlive.com/batch
```

**One thing here is materially better than Epic, and it is worth naming because it cuts
against `ROADMAP.md` §3's "shipping storefront client credentials" row.** The MSA flow is a
*public* OAuth client: an authorization-code flow to a desktop redirect with **no client
secret**. Winnow's Epic integration had to embed Epic's own launcher secret, which the
roadmap correctly records as a real cost borne by the distributor. **Xbox has no equivalent
cost** — an Azure-registered public client id is not a secret, and rotating it does not
require Microsoft to have leaked anything. The registration would be Winnow's own, not a
launcher's, which is *better* provenance than either Epic or GOG.

Against that: the endpoints being called are undocumented, and Microsoft's enforcement
language about "unauthorized third-party software" is broad [SRC]. No ban attributable to a
library-reading tool was found, and Playnite's plugin has shipped for years, but "no reports
found" is not "verified safe" and this document will not pretend otherwise. It is a weaker
risk posture than Epic's, where Heroic's FAQ affirmatively says the opposite, and a stronger
one than PSN's, where the tooling's own authors warn you.

**Filtering to PC.** `titlehub` returns console titles too. Playnite filters on
`title.devices` containing `"PC"` and `title.type == "Game"` and a non-null `pfn` [SRC].
Console titles must be dropped, not imported as uninstallable ghosts — §4.6's "no consoles"
line is about *ingesting* console libraries and remains correct.

**Owned-but-never-played.** Playnite's code carries a comment that a game never started at
least once *"won't appear in user API data"* and handles it as a separate case [SRC]. This
is the crux and it is **[VERIFY]**: does `titlehistory` return entitlements the user has
never launched, or only played titles? If only played titles, then even a signed-in Xbox
user's unplayed pile is invisible, and — restating section 5's point in its strongest form —
**the fourth store cannot answer the one question Winnow is for.**

---

## 7. Playtime is partial, and that is not a bug that can be fixed

`titlehub`'s `titleHistory` object carries exactly three fields: `lastTimePlayed`, `visible`,
`can_hide`. **There is no minutes-played field anywhere in the titlehub model** [SRC].

Playtime comes from a separate call — `POST https://userstats.xboxlive.com/batch`, asking
for the stat named `MinutesPlayed` per title id [SRC]. `MinutesPlayed` is a **per-title
service-configuration stat that each publisher chooses whether to publish**, not a
platform-level counter like Steam's. Playnite's own source carries the comment *"No idea why
but this seems to be empty for some people…"* [SRC].

Concretely, for the fourth store:

- `LastPlayedAt` — **available** (`titleHistory.lastTimePlayed`).
- `PlaytimeMinutes` — **available for some titles, null for others**, decided by the publisher, not discoverable in advance.
- `AcquiredAt` — **no source found.** `titlehub` does not carry a purchase date. [VERIFY]

`CandidateOwnership` already models all three as nullable and the codebase already has the
discipline for it — `GogLibrarySource`'s registry-only path passes `PlaytimeMinutes: null`
with a comment explaining that zero would be a claim nothing supports. So the contract
absorbs this without change.

What does *not* absorb it is the recommender. `Winnow.Recommend` scores dormancy and
staleness over playtime and session history. An Xbox library arriving with a last-played
date, sometimes a playtime number, and no sessions at all (section 8) is the weakest input
any store has yet produced. It will not crash; it will rank badly and the "one sentence
reason" on each feed card will be thinner for Xbox games than for anything else.

---

## 8. Session detection is the real blocker

This is the finding that matters most, because M3a's session watching is what
`ROADMAP.md` §2 identifies as the entire point of the launcher.

`GameExecutableIndexBuilder` builds its map by **walking `ownerships.install_path` for
`.exe` files**, then `SessionWatcher` matches `Process.ProcessName` against that index and
attributes by executable path. `SystemProcessSource` deliberately avoids `MainModule`
(§5.2's rule). Three ways this fails on MSIX games:

1. **The directory walk returns nothing.** Under `WindowsApps` the scan hits `UnauthorizedAccessException`; the builder fails soft and the game contributes zero executables, so Tier 1 never fires. Under `XboxGames` it depends on the section 4.4 verification.
2. **Even with a name, attribution is fragile.** Many Store games run through a shared `gamelaunchhelper.exe` shim [VERIFY] — a name that would collide across every Game Pass title in the library, which `GameExecutableIndex` is not built to disambiguate.
3. **Path-based attribution is unavailable** when the process cannot be opened for module inspection.

Playnite's own manual is the corroborating evidence: it tells users that when playtime
tracking fails on a game, they should switch to *"folder tracking"* as an alternative mode
[SRC] — an admission that name-and-path process watching does not reliably work here.

**The correct mechanism exists and Winnow does not have it.** Win32's
`GetPackageFamilyName(hProcess, …)` returns the package family name for any process with
package identity, needs only `PROCESS_QUERY_LIMITED_INFORMATION`, and returns
`APPMODEL_ERROR_NO_PACKAGE` for ordinary Win32 processes [SRC]. That is precisely the
primitive needed: enumerate processes, ask each for its PFN, match against
`external_ids.provider_id` — and per section 3 the PFN is already the id Winnow would be
storing.

The cost is architectural rather than large. It is a new attribution tier in
`Winnow.Monitor` that is keyed on package identity rather than on a path, it is Windows-only
P/Invoke in a project whose `.csproj` currently carries no Windows dependency at all and
whose comments make a point of that, and it needs its own `IProcessSource` surface. Call it
a few hundred lines with tests. It is not optional: **without it, Xbox games launched from
Winnow record no sessions, and a store that generates no session data contributes nothing to
the flywheel that justifies the launcher.**

---

## 9. Identity resolution and enrichment

Better news, and the one place Xbox is genuinely well-served.

**GOG's identity graph already covers it.** `docs/spikes/epic-oauth.md`, under "What the
three games can and cannot reach", records a live gamesdb response whose release list reads
`epic / psn / xboxone / winstore / humble / beamdog / psx / psvita`. **`winstore` is a real
platform id in GOG's vocabulary**, verified in this repo's own spike output. So
`GET https://gamesdb.gog.com/platforms/winstore/external_releases/{id}` is very likely the
same two-hop bridge `EnrichmentLookupPlanner` already runs for Epic — Xbox id → gamesdb →
Steam or GOG id → IGDB. `GamesDbPlatforms` gains one constant.

**[VERIFY]:** what `external_id` gamesdb expects under `winstore` — PFN, StoreId, or
something else. The Epic case needed `IStoreArtifactAliasSource` precisely because the
stored id and the lookup id differed, and there is no reason to assume Xbox is luckier. Note
also that `xboxone` and `winstore` are *different* platforms in that vocabulary — console
and PC — and only `winstore` is the right one.

**IGDB.** One search result puts "Microsoft (Title ID)" at `external_game_source` id 18, but
`api-docs.igdb.com` is blocked from this session and the figure is uncorroborated. **[VERIFY]
against the live API before adding an `XboxExternalGameSourceId` to `IgdbOptions`.** If a
direct source exists, Xbox gets the same one-hop route Steam and GOG have and the gamesdb
bridge becomes a fallback rather than the primary.

---

## 10. Code inventory — everything that would change

Enumerated from the actual tree, not estimated. This is what a fourth store costs
*mechanically*, before any of sections 6–8.

### New

| Item | Notes |
|---|---|
| `src/Winnow.Ingest.Xbox/` | The reader. `GogLibrarySource` (1,208 lines across 10 files) is the right size comparison for a local-only reader; add the OAuth layer and Epic's ~40-file shape is the comparison instead |
| `tests/fixtures/xbox/` | Sanitized `MicrosoftGame.config`, a registry export, a `titlehub` response with a fake XUID. `CLAUDE.md`: sanitize any new fixture |
| A package-identity tier in `Winnow.Monitor` | Section 8. `GetPackageFamilyName` P/Invoke behind an interface |
| `src/Winnow.Data/Migrations/0013_xbox_provider.sql` | See below |

### Modified

| File | Change |
|---|---|
| `src/Winnow.Core/Domain/Constants.cs` | `ExternalIdProviders.Xbox`, added to `Stores` — which `WorkRepository:93` iterates, so that one line propagates correctly by design |
| `0013_*.sql` | Widen `external_ids.provider`'s CHECK. **SQLite cannot alter a CHECK**, so this is the 12-step rebuild — but `external_ids` has no inbound foreign keys and one index, so it is create/copy/drop/rename/reindex. `ownerships.store` is *not* CHECK-constrained and needs nothing. 0003's header documents the house style for this |
| `Winnow.App/Program.cs` | `services.AddXboxIngest()` |
| `Winnow.App/Services/LibrarySyncService.cs` | `LocalLibraryScan` is a 3-arity record — `Count`, `All`, `Scan()`, two log statements |
| `Winnow.App/ViewModels/StoreActions.cs` | Primary + secondary actions. Launch is `explorer.exe shell:AppsFolder\{PFN}!{AppId}` [SRC]; store page is `ms-windows-store://pdp/?ProductId={StoreId}`. **Note:** `GameLink` allowlists schemes and `IsClientScheme` assumes a URI — `shell:AppsFolder` is a *shell command*, not a URI, and does not fit the existing model. `GameLink`'s scheme allowlist and its `Uri.ToString()` lowercasing comment both need revisiting |
| `Winnow.App/ViewModels/GameLink.cs` | Above |
| `Winnow.App/ViewModels/LibraryViewModel.cs` | Two switch arms (`"the Xbox app"`) |
| `Winnow.App/ViewModels/Filters/FilterPanelViewModel.cs:460` | Display-name arm: `"xbox" => "Xbox"` |
| `Winnow.App/ViewModels/StoresViewModel.cs` (302 lines) | A fourth block of count/status/sign-in properties |
| `Winnow.App/Views/StoresView.axaml` (366 lines) | A fourth card. **Design-system review required** — the panel's three-card composition is authored, not generated |
| `Winnow.App/Services/EnrichmentLookupPlanner.cs` | A route for Xbox: direct if IGDB source 18 verifies, else the Epic-style gamesdb bridge |
| `Winnow.Enrich.GamesDb/Model/GamesDbRelease.cs` | `GamesDbPlatforms.WinStore = "winstore"` |
| `Winnow.Enrich.Igdb/IgdbOptions.cs` | `XboxExternalGameSourceId` + a switch arm, **only if section 9's [VERIFY] passes** |
| `Winnow.App/Services/SampleDataSeeder.cs` | Sample rows |
| `Winnow.slnx`, `Winnow.App.csproj`, `Winnow.Tests.csproj` | Project references |
| `CLAUDE.md`, `ROADMAP.md`, `game-library-design.md` §4.6 | §4.6 says Xbox "must not be added". **Adding it means amending §4.6 explicitly**, in the style ROADMAP §3 already uses — not quietly contradicting it |

Nothing in `Winnow.Resolve` changes: it is provider-agnostic and takes `CandidateOwnership`.
That boundary holding under a fourth store is a good sign about §5.1.

### Rough sizing

| Scope | Estimate |
|---|---|
| Local-only reader (installed games, no sign-in) + schema + UI + tests | ~1,200–1,800 lines. **Delivers little** — see section 5 |
| \+ MSA/XSTS sign-in, `titlehub`, `userstats`, DPAPI token store, sign-in UI | \+~2,000–2,500 lines. Epic's `Web/` subtree is the template and much of its shape (rate limiter, resilience handler, redaction, token store, interactive sign-in) is copyable |
| \+ package-identity session detection | \+~300–500 lines, and the first Windows-only code in `Winnow.Monitor` |
| \+ enrichment routing and the spikes to justify it | \+~300 lines and a real day of verification |

Call it **a milestone the size of M4.5 plus part of M3a** — bigger than GOG ingest, and
close to Epic-with-OAuth, with more unknowns than either had at the same stage.

---

## 11. What a real spike must verify

In priority order. Items 1–3 are gating: if they come back badly there is no point doing 4–9.

1. **Does `titlehub`'s `titlehistory` return owned-but-never-launched PC titles?** If no, the fourth store cannot surface an unplayed pile and everything else is moot (§6).
2. **Is `C:\XboxGames\<Game>\Content\` readable unelevated?** Decides `MicrosoftGame.config` access and, with item 3, whether sessions are detectable at all (§4.4, §8).
3. **What does a Game Pass game's process look like?** Its `ProcessName`, whether `GetPackageFamilyName` returns its PFN from an unelevated caller, and whether `gamelaunchhelper.exe` is a real shared shim (§8).
4. **`.GamingRoot`'s binary layout**, and whether multi-drive users are correctly discovered from it (§4.1).
5. **Does `MicrosoftGame.config` presence discriminate games from apps** across a real package list (§4.3)?
6. **`GET gamesdb.gog.com/platforms/winstore/external_releases/{id}` — with which id?** Measure coverage across a real library the way the Epic spike measured 67/67 (§9).
7. **IGDB `external_game_source` for Microsoft Store** — confirm or refute id 18 against the live API (§9).
8. **`MinutesPlayed` coverage** across a real library: what fraction of owned PC titles return a number (§7)?
9. **Is there a usable local cache** under `Microsoft.GamingApp`'s `LocalCache` (§5)?

Every one of these needs a Windows machine with Xbox games and an Xbox Live account. None
can be answered from this container.

---

## 12. Recommendation

**Do not schedule this now. Do not close the door either — and fix the reason on record.**

Three reasons to hold, in order of weight:

1. **It is the weakest data any store would contribute, at the highest cost.** No playtime for many titles, no acquisition date, no sessions without new Windows-only monitor work, and — pending item 1 — possibly no unplayed pile at all. `ROADMAP.md` §2 argues the launcher is justified because it acquires session data. Xbox is the one store where that argument does not close.
2. **The sequencing is wrong.** M5 (GDPR import), M6 (export) and M9 (install management) are queued and all three improve the *existing* three stores. M5 in particular is named as the biggest lever on cold-start quality. A fourth store adds breadth to a feed that is still shallow.
3. **Six of the nine spike items are gating or near-gating, and none can be answered here.** Committing before item 1 is answered would repeat exactly what §9's failure modes warn about.

Two reasons not to write it off, which §4.6 as written cannot express:

- **The sign-in objection genuinely does not apply.** No `npsso`-style manual step, no repeated extraction, and — unlike Epic and GOG — **no client secret to embed**. That is a better credential posture than either store Winnow already ships, and `ROADMAP.md` §3's row on shipping storefront credentials should say so rather than sweeping Xbox in with PSN.
- **Enrichment is nearly free.** `winstore` is already in the identity graph Winnow already calls.

**Concretely:**

- Amend `game-library-design.md` §4.6 to **split PSN from Xbox**. As written it justifies excluding Xbox with facts about Sony, and `epic-oauth.md` §1 already established that this class of reasoning does not transfer. Keep both excluded; give Xbox its own reasons — sections 5–8 above.
- Record the exclusion as **"held on evidence, revisit if item 1 passes"**, the same posture `ROADMAP.md` already uses for GOG sign-in, rather than "must not be added".
- If a Windows machine with Game Pass becomes available, spend **one session on items 1–3 only**. They are cheap, they are decisive, and a "no" on item 1 closes the question permanently with a real reason instead of an inherited one.

---

## Sources

Vendor documentation:
- [PackageManager.FindPackagesForUser](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.packagemanager.findpackagesforuser) · [GetPackageFamilyName (appmodel.h)](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagefamilyname)
- [MicrosoftGame.config overview](https://learn.microsoft.com/en-us/gaming/gdk/docs/features/common/game-config/microsoftgameconfig-overview) · [TitleId element](https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/microsoftgameconfig/elements/microsoftgameconfig-element-titleid)
- [Collections v9 publisherQuery](https://learn.microsoft.com/en-us/gaming/gdk/docs/store/commerce/service-to-service/microsoft-store-apis/xstore-v9-query-for-products) — the documented, publisher-only ownership API
- [Types of Xbox enforcement actions](https://support.xbox.com/en-US/help/family-online-safety/enforcement/types-of-xbox-enforcement-actions) · [Why your account was banned](https://support.microsoft.com/en-us/account-billing/learn-why-your-account-was-banned-or-suspended-from-xbox-live-87d8f88a-d45f-1955-d39f-deb3a64bd6cd)
- [titlehub is undocumented — MicrosoftDocs/xbox-live-docs #53](https://github.com/MicrosoftDocs/xbox-live-docs/issues/53)

Reference implementations:
- Playnite Xbox library plugin — [`XboxAccountClient.cs`](https://github.com/JosefNemec/PlayniteExtensions/blob/master/source/Libraries/XboxLibrary/Services/XboxAccountClient.cs) (OAuth/XSTS flow, `titlehub`, `userstats` `MinutesPlayed`), [`XboxLibrary.cs`](https://github.com/JosefNemec/PlayniteExtensions/blob/master/source/Libraries/XboxLibrary/XboxLibrary.cs) (PC filtering, PFN as game id)
- [xbox-webapi-python titlehub models](https://github.com/OpenXbox/xbox-webapi-python/blob/master/xbox/webapi/api/provider/titlehub/models.py) — the field list, and the absence of a minutes-played field
- [Playnite manual: playtime tracking modes](https://api.playnite.link/docs/manual/library/games/addingGames.html) — folder tracking as the fallback for games process watching cannot follow

Install layout and `.GamingRoot`:
- [How to install or move your Xbox PC games to any folder — PCWorld](https://www.pcworld.com/article/623123/how-to-install-or-move-your-xbox-pc-games-to-any-folder.html)
- [What is the .GamingRoot file — How-To Geek](https://www.howtogeek.com/872922/what-is-the-gamingroot-file/) · [XDA](https://www.xda-developers.com/what-does-the-gamingroot-file-do-on-windows-11/)
- [AppModel registry repository](https://chriskyfung.github.io/blog/windows/find-windows10-store-app-path-for-default-program)
- [Launching MSIX/AppX from the command line](https://www.andreasnick.com/106-starting-msix-and-appx-packages-with-powershell-from-the-command-line.html)

In-repo, verified elsewhere:
- `docs/spikes/epic-oauth.md` — the §4.6 rebuttal table this document reuses, and the live gamesdb response containing `winstore`
- `docs/spikes/epic-gog-local-files.md` §20 — the gamesdb identity graph and the stored-id/lookup-id split
