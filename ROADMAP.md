# Hoard — Roadmap (v2)

Supersedes §8 of `game-library-design.md` for sequencing. The hard constraints in §4 and the
module boundaries in §5.1 are unchanged and still binding. Where this document contradicts
§1's non-goals, the amendment is stated explicitly in §3 below — nothing is quietly dropped.

---

## 1. What Hoard is

**Hoard is the library that remembers.**

Every storefront lists your games. None of them retain the history that makes a library
legible: how long a game sat unopened before you tried it, whether you bounced off it once
or fought with it across six sessions, whether it has been patched three times since you
gave up, whether you are the kind of person who ever comes back.

Storefronts discard that. Hoard keeps it. That is the whole asset.

"Analytics tool" undersells it, correctly — analytics is a panel you open monthly and then
stop opening. But "launcher" oversells it in a different direction: Playnite already is a
mature open-source launcher with a plugin ecosystem, and a straight race against it is one
Hoard loses on maturity alone.

The position that is actually defensible is the intersection.

## 2. Why launcher + recommender, and not either alone

The two halves are not separate ambitions. They are a loop:

```
   launch games through Hoard
            |
   real session data accrues   <- nobody else has this
            |
   the feed gets genuinely good
            |
   the feed is the reason to launch through Hoard
```

This matters for sequencing. A recommender fed only by periodic library syncs sees **one
playtime number per game per sync** — the same impoverished view Steam has. A recommender
fed by session detection sees when you play, for how long, what you abandon mid-session, and
what you return to. The second is a different product.

So the launcher is not a feature bolted on for adoption. **It is the data-acquisition
strategy for the differentiator.** That is the argument for building it, and it is a stronger
one than "launchers get installed."

Two consequences fall out immediately, and both are lucky:

- **M3 was already the launcher.** The unimplemented process-monitor and session-detection
  milestone is the hard 80% of launching games. Actually *starting* a game is a URI handoff
  (`steam://rungameid/440`) and is nearly free; knowing when it stopped is the real work, and
  it was specified a year ago in §5.2. The launcher pivot costs far less than it appears to,
  provided M3 lands first.
- **M5 was already the cold-start fix.** The GDPR-export importer backfills historical
  playtime. A recommender on a library synced this morning has one snapshot per game and no
  sessions — nearly every interesting signal is degenerate. M5 is the single biggest lever
  against that, which promotes it from "export nicety" to "makes the feed work on day one."

## 3. Amendments to the design doc

| §1 non-goal | Status | Rationale |
|---|---|---|
| Recommendation engine (phase 2) | **Promoted to core** | It is the differentiator. Phase-2 placement assumed it needed a server; it does not — all inference is local over the user's own database. |
| Any hosted service, user accounts, multi-user | **Unchanged** | Still no server, still no Hoard account, still no telemetry. **Hoard has no accounts; Hoard links yours.** Epic/Steam OAuth authenticates you to *their* service and stores the token locally under DPAPI. That is third-party linking, not account creation, and the distinction is load-bearing. |
| 3D "games on a shelf" view (cut, §11) | **Still cut** | Full-screen gamepad mode (M10) is a 10-foot UI, not the 3D shelf. Avalonia handles it natively; §11's framework reasoning does not apply and is not reopened. |
| Shipping storefront client credentials | **Decided 2026-08-26: ship them built-in** | A sign-in button cannot ask the user for credentials, and there is no version where they supply their own: Epic issues no client that can read a personal library (an EOS portal app is rejected with `invalid_client`), and GOG has no public dev portal for this. So the only alternatives were "embed the launcher credentials" or "the feature does not exist". Heroic, Legendary and the Playnite plugins all embed them. Hoard is the party distributing them and that is a real cost; the realistic failure mode is Epic or GOG rotating a client and sign-in breaking until updated, not bans. The published Epic pair was verified live on 2026-08-26 rather than trusted. |
| PSN / Xbox (§4.6) | **Unchanged — still excluded** | See the note under M4.5. Epic OAuth is not a precedent for these. |

## 4. Phases

Numbering continues from §8. M0–M2 and M4 are shipped.

| # | Deliverable | Exit criteria | State |
|---|---|---|---|
| M4.5 | Epic OAuth ownership source + local fallback | Entitlements resolve when authed; unauthed degrades silently to local files with no loss of install state | **shipped** (sign-in unverified end to end) |
| M7 | Recommendation core (`Hoard.Recommend`) | Standalone scoring module, explainable output, sensible ranking on a cold library; not yet wired to UI | **shipped** (unwired by design) |
| M3a | Session detection (§5.2 mechanism A) | Process watching records sessions with true start/end; poll for discovery only, events for exit | **shipped** |
| M4.6 | Store sign-in UI (Epic) | A sign-in button in the app runs an embedded-browser OAuth flow that captures the code automatically; console flow survives as a documented fallback | next |
| — | GOG sign-in | **Held, on evidence.** Nothing to gain today; see below | not scheduled |
| M3b | Launch + journal prompt | Launching from Hoard records a session; journal prompt opt-in (§9 pitfall 7); `hoard-wrap` offered per-game, never globally | next |
| M8 | The Feed | Recommender surfaced as the app's primary view; every card states its reason in one sentence | after M3+M7 |
| M5 | GDPR export importer | Historical playtime backfills; feed measurably improves on a cold library | after M8 |
| M6 | Export (JSON + CSV) | Round-trips through the importer without loss | after M5 |
| M9 | Install / uninstall management | Install and uninstall delegate to the owning store client and reflect state back | after M6 |
| M10 | Full-screen mode + gamepad navigation | Whole app navigable on a controller at 10 feet | last |

### Why M4.6 jumped the queue

The console sign-in shipped in M4.5 is fragile in a way that is structural, not a bug: the
authorization code is single-use and expires in minutes, so every misstep between issuing it
and spending it — an environment variable that did not propagate, a prompt that hung, a
terminal that swallowed input — burns the code and needs a fresh one. Each of those cost a
real debugging round. An embedded browser reads the code the instant the provider issues it,
which removes the window entirely rather than making it easier to hit.

**GOG is held, and the reason corrects an error made when this section was first written.**
"GOG ingest found only 14 games" was recorded here as a shortfall. It is not one. Galaxy's
database holds **45 owned GOG releases, of which 31 are DLC** — verified directly against
`LibraryReleases` joined to `ReleaseProperties`, the same join `GalaxyLibraryReader` uses.
`GogLibrarySource` drops DLC by design, so 45 − 31 = the 14 base games it reports. The local
reader is reading the entire library correctly, and the missing-games premise was invented
rather than measured.

The authenticated GOG endpoint then carries **no playtime, no last-played, no title and no
DLC flag** — all four of which the local reader already has. So a GOG sign-in button would
add a login, a stored credential and an embedded browser in exchange for nothing.

One thing could reopen it: `GET gameplay.gog.com/.../sessions` exists and accepts GET
(PUT/DELETE answer 405), but no known client reads it and its payload is unverified. If it
carries session history, that is longitudinal data worth having and this gets rescheduled.
Until someone looks, it stays held.

### Why that order

**M3 before M8.** The feed's quality is bounded by its input data. Shipping the feed before
session detection means shipping it at its worst and teaching users it is mediocre. Session
data starts accruing the moment M3 lands, so M3 should land as early as possible even though
the feed is the visible prize — every week M3 is late is a week of history not collected.

**M9 delegates, never reimplements.** Hoard hands installation to the store's own client
(`steam://install/`, Galaxy, the Epic launcher). Writing our own downloaders —
Legendary-style — means owning CDN auth, chunked delivery, patching, and the support burden
for corrupted installs, for the sole benefit of avoiding a window appearing. Not worth it,
possibly not ever.

**M10 last, deliberately.** Full-screen gamepad mode is a second complete UI: focus
management, controller input, its own navigation model, its own layouts. It is the largest
surface area of the three asks and serves the narrowest slice of users (couch/HTPC). M3, M7
and M8 deliver to everyone. This is the right feature and the wrong thing to build third.

## 5. Phase 3 — recommending games you do not own

Deferred, and worth stating why beyond "needs catalog data" (it does — store catalogue
access we have not built).

There is a product-integrity tension to resolve first. Hoard's premise is *you own a thousand
games and have played forty*. An app that opens with that diagnosis and then sells you more
games is incoherent, and users will read it — correctly — as the moment the tool started
working for someone else.

The version that survives that objection is **wishlist intelligence**, not a purchase feed:
acting on titles the user has already flagged interest in, and being honest about sales and
taste-fit when asked. The core loop stays *play what you own*. Anything that inverts that
ratio is a mistake regardless of how well it converts.

## 6. Carried-over debt

Tracked so none of it silently becomes permanent:

- **Merge execution is not built.** The queue proposes and stores confirmations; nothing
  applies them. 23 cross-store pairs are pending on the user's library right now. The
  `ON DELETE CASCADE` hazard on collapsing two releases is documented and unresolved.
- **Cross-store dedup via `gamesdb.gog.com`** — **built for METADATA, not for dedup.**
  `Hoard.Enrich.GamesDb` now routes Epic titles to a Steam appid so they can be enriched
  (62 of 67). It deliberately writes no `external_ids` and no merge candidates: that table is
  keyed `(provider, provider_id)` globally, so putting a Steam appid on an Epic release would
  collide with the Steam release that already owns it. gamesdb also resolves *games*, not
  editions, so an Epic "Gold Edition" can land on the base game's record — right for the Work
  columns enrichment writes, wrong for a Release, and therefore never a merge. The original
  note follows.
- *(original)* — spiked and verified (`steam/224760` and
  `epic/Bluebird` resolve to the same `game_id`; 67/67 Epic titles resolved, 62 carrying
  Steam ids). Not built. This would collapse most of the merge queue automatically via hard
  ids rather than fuzzy title, which is exactly what §5.3 wants.
- **`SteamSyncService` is misnamed** — it ingests three stores. Rename to
  `LibrarySyncService` behind an `ILocalLibrarySource` in `Hoard.Core.Ingest`.
- **Session detection is Windows-only in practice.** `GameExecutableIndexBuilder` matches
  `*.exe`, so off Windows the index is empty and nothing is ever recorded — it warns once
  rather than failing silently. Widening the glob is NOT the fix: under Proton the resolved
  executable is the wine loader inside the runtime directory, not a path under the game's
  install root, so the install-prefix join cannot work there at all. Attribution would need
  `STEAM_COMPAT_DATA_PATH` from `/proc/<pid>/environ` — a different design.
- **IGDB cache has no `payload_version`.** Adding a field to the cached shape silently yields
  empty results for 30 days rather than refetching. A latent trap, not yet a bug.

## 7. The risk

This roadmap roughly triples Hoard's surface area. The realistic failure mode is not
technical — it is becoming a worse Playnite with an unfinished recommender attached.

The mitigation is ordering, and it is the reason M7 and M3 run ahead of everything visible:
**the feed must always be further along than the launcher.** If Hoard ever ships launcher
parity before the recommender is genuinely good, it has spent its differentiation budget on
catching up to a mature incumbent and has nothing left to be chosen for.
