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
| PSN / Xbox (§4.6) | **Unchanged — still excluded** | See the note under M4.5. Epic OAuth is not a precedent for these. |

## 4. Phases

Numbering continues from §8. M0–M2 and M4 are shipped.

| # | Deliverable | Exit criteria | State |
|---|---|---|---|
| M4.5 | Epic OAuth ownership source + local fallback | Entitlements resolve when authed; unauthed degrades silently to local files with no loss of install state | **in flight** |
| M7 | Recommendation core (`Hoard.Recommend`) | Standalone scoring module, explainable output, sensible ranking on a cold library; not yet wired to UI | **in flight** |
| M3 | Launch + session detection | Launching from Hoard records a session with real start/end on both detection paths; journal prompt opt-in | next |
| M8 | The Feed | Recommender surfaced as the app's primary view; every card states its reason in one sentence | after M3+M7 |
| M5 | GDPR export importer | Historical playtime backfills; feed measurably improves on a cold library | after M8 |
| M6 | Export (JSON + CSV) | Round-trips through the importer without loss | after M5 |
| M9 | Install / uninstall management | Install and uninstall delegate to the owning store client and reflect state back | after M6 |
| M10 | Full-screen mode + gamepad navigation | Whole app navigable on a controller at 10 feet | last |

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
- **Cross-store dedup via `gamesdb.gog.com`** — spiked and verified (`steam/224760` and
  `epic/Bluebird` resolve to the same `game_id`; 67/67 Epic titles resolved, 62 carrying
  Steam ids). Not built. This would collapse most of the merge queue automatically via hard
  ids rather than fuzzy title, which is exactly what §5.3 wants.
- **`SteamSyncService` is misnamed** — it ingests three stores. Rename to
  `LibrarySyncService` behind an `ILocalLibrarySource` in `Hoard.Core.Ingest`.
- **IGDB cache has no `payload_version`.** Adding a field to the cached shape silently yields
  empty results for 30 days rather than refetching. A latent trap, not yet a bug.

## 7. The risk

This roadmap roughly triples Hoard's surface area. The realistic failure mode is not
technical — it is becoming a worse Playnite with an unfinished recommender attached.

The mitigation is ordering, and it is the reason M7 and M3 run ahead of everything visible:
**the feed must always be further along than the launcher.** If Hoard ever ships launcher
parity before the recommender is genuinely good, it has spent its differentiation budget on
catching up to a mature incumbent and has nothing left to be chosen for.
