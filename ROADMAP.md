# Winnow — Roadmap

Product scope, phase order, exit criteria, and what is deliberately excluded or deferred.
Architecture and hard constraints are in `game-library-design.md`; the reasoning behind the
choices recorded here is in `docs/decisions.md`.

---

## 1. What Winnow is

**Winnow is the library that remembers.**

Every storefront lists your games. None of them retain the history that makes a library
legible: how long a game sat unopened before you tried it, whether you bounced off it once or
fought with it across six sessions, whether it has been patched three times since you gave up,
whether you are the kind of person who ever comes back. Storefronts discard that. Winnow keeps
it. That is the whole asset.

Winnow is a launcher and a recommender, and the two halves are one loop: launching through
Winnow accrues real session data, session data makes the feed good, and the feed is the reason
to launch through Winnow. **The launcher is the data-acquisition strategy for the
differentiator**, which is why session detection ranks ahead of everything visible.

## 2. Standing constraints

These bind every phase below and do not expire.

- **No server, no Winnow account, no telemetry.** All inference is local, over the user's own
  database. Signing in to a storefront links the user's account there; it does not create one
  here.
- **The feed must always be further along than the launcher.** Shipping launcher parity before
  the recommender is genuinely good spends the differentiation budget catching up to a mature
  incumbent, with nothing left to be chosen for.
- **The core loop is *play what you own*.** Anything that inverts that ratio is a mistake
  regardless of how well it converts.
- **Installation delegates, never reimplements.** Winnow hands installation to the store's own
  client: `steam://install/`, Galaxy, the Epic launcher.
- Every recommendation states its reason in one sentence.

## 3. Phases

M0 to M2 and M4 shipped as originally specified. Numbering after that reflects the order the
work was taken up, not the order it was planned.

| # | Deliverable | Exit criteria | State |
|---|---|---|---|
| M0 | Host + SQLite + migrations + Steam local ingest + library view | Library visible; playtime and last-played correct from `localconfig.vdf` | shipped |
| M1 | IGDB resolution + merge confirm queue | Hard joins auto-resolve; soft matches queue; no auto-merge on fuzzy title | shipped |
| M2 | Snapshot scheduler + update signal poller + staleness scoring | Buckets query correctly against seeded data | shipped |
| M4 | Epic + GOG local ingest | Installed titles from both appear and dedupe correctly | shipped |
| M4.5 | Epic OAuth ownership source + local fallback | Entitlements resolve when authed; unauthed degrades silently to local files with no loss of install state | shipped |
| M7 | Recommendation core (`Winnow.Recommend`) | Standalone scoring module, explainable output, sensible ranking on a cold library | shipped |
| M3a | Session detection | Process watching records sessions with true start and end; poll for discovery only, events for exit | shipped |
| M4.6 | Store sign-in UI (Epic) | A sign-in button runs an embedded-browser OAuth flow that captures the code automatically; the console flow survives as a documented fallback | shipped |
| M11 | Appearance system | Four themes, a transparency slider with a chosen backdrop, an optional island layout, a drop-in JSON theme format, and an application icon | shipped |
| M3b | Launch + journal prompt | Launching from Winnow records a session; the journal prompt is opt-in | shipped; the `winnow-wrap` launch-option wrapper is specified and deliberately not built |
| M8 | The Feed | The recommender is the app's primary view; every card states its reason in one sentence | shipped with dismiss, snooze, undo and persisted visible-card impressions |
| M5 | Historical playtime backfill | Historical playtime backfills; the feed measurably improves on a cold library | built; backfill tested, feed improvement awaiting live validation against the user's key |
| M6 | Export (JSON + CSV) | JSON is complete and re-readable; CSV covers a defined set of views | acquisition CSV shipped; full JSON/import deferred; exit criterion to be restated |
| M9 | Install / uninstall management | Install and uninstall delegate to the owning store client and reflect state back | shipped; Steam delegates directly, Epic and GOG expose launcher management where direct uninstall is unsupported |
| M10 | Full-screen mode + gamepad navigation | The whole app is navigable on a controller at 10 feet | last |

### Pre-beta hardening

The 2026-09-06 review puts security, data integrity and broken shipped behavior ahead of new
features. PRE-BETA-HARDENING owns the release queue; task priority and ordinal record severity
and execution order. Read Backlog for current completion status.

TASK-148 adds Winnow's own Windows/Linux installer and release workflow, separate from
M9's management of installed games.

| Priority | Tasks in execution order | Reason |
|---|---|---|
| High | TASK-24, TASK-11, TASK-16, TASK-17, TASK-28, TASK-31, TASK-26, TASK-141, TASK-3, TASK-52, TASK-25 | Credential protection, atomic writes, ingest and cover safety, migration and CI gates, working install/uninstall, recoverable backfill, redacted diagnostics |
| Medium | TASK-33, TASK-6, TASK-7, TASK-10, TASK-12, TASK-18, TASK-19, TASK-20, TASK-107, TASK-139, TASK-39, TASK-57, TASK-48, TASK-47, TASK-29, TASK-35, TASK-32, TASK-130, TASK-63, TASK-69 | Timestamp and feed correctness, responsiveness, cache behavior, readable journal notes, acquisition protection, usable auth, accessibility, platform support, contract evidence and accurate explanations |
| Low | TASK-36, TASK-45 | Startup overhead and remaining account-page verification |

TASK-3 moves from M9 into this queue: export was a sequencing dependency, not a technical
prerequisite for store handoff. TASK-26 depends on TASK-31's migration verification.
TASK-141 needs observed Epic launcher success before it can close; passing dispatch tests
alone do not establish that installation works. TASK-45, TASK-47 and TASK-48 require live
verification. TASK-32 now has passing Ubuntu smoke coverage for native discovery and
Proton-environment attribution. Actual Wine/Proton game compatibility remains unmeasured.

Unassigned tasks left outside beta are new scoring signals and evaluation research
(TASK-135–138), achievement ingestion (TASK-15), per-edition years (TASK-13), broader
cross-store automation (TASK-37), notification and navigation features (TASK-108–110,
TASK-114), optional presentation work (TASK-27, TASK-42, TASK-43, TASK-80–82), and deferred
import/research or test maintenance (TASK-40, TASK-41, TASK-44, TASK-46, TASK-49, TASK-65).
These remain useful work, but do not repair the beta's existing core loop. Exact history
aggregates (TASK-139) are included because they fix tier decisions and avoid repeated sampling
reads; new ranking weights can wait for evidence from beta use.

TASK-14 implements the Derelict library bucket and feed shelf using dated IGDB and Steam
lifecycle evidence. Classification is local and recomputed; missing data remains unknown.
PCGamingWiki and Wikidata enrichment remain optional follow-up work.

## 4. Excluded, and deferred

**Excluded outright.** PlayStation and Xbox. Any hosted service, user accounts or multi-user
features. Co-op and friend library matching. A 3D "games on a shelf" view. Mobile. The
grounds for each are in `game-library-design.md` §1 and §4.6.

Full-screen gamepad mode (M10) is a 10-foot UI, not the 3D shelf, and is not covered by that
exclusion.

**GOG sign-in: held, not scheduled.** The local Galaxy reader already carries everything the
authenticated endpoint returns, and more. One thing reopens it: `GET
gameplay.gog.com/.../sessions` exists and accepts GET, but no known client reads it and its
payload is unverified. **If it carries session history, this gets rescheduled.** Tracked as
TASK-49.

**Recommending games the user does not own: deferred to a later phase.** The version that
survives Winnow's own premise is wishlist intelligence, acting on titles the user has already
flagged, rather than a purchase feed.

## 5. Carried debt

Tracked so none of it silently becomes permanent. Each item is a Backlog task; read the task
for its current state.

| Debt | Task |
|---|---|
| Merge execution is not built; the queue records intent and nothing applies it | TASK-5, TASK-64 |
| Cross-store identity should be a link relation, not a destructive merge | TASK-70 and its subtasks |

| The account stats screen is a first pass; presentation cleanup is shelved | TASK-43 |
| The fact tables cannot distinguish two identical same-day transactions | TASK-44 |

| $0.00 purchase rows are skipped rather than recorded as zero, undecided either way | TASK-40 |
| The saved-file licenses route captures one page per file | TASK-41 |
| ACCOUNT and REVIEW each spend a rail section heading on a single row | TASK-42 |

The account-scope filter deliberately errs visible (`game-library-design.md` §6.3). Linux session discovery and Proton-environment attribution passed real-process Ubuntu smoke tests under TASK-32; this does not establish compatibility across actual Wine/Proton games.

## 6. The risk

This scope roughly triples Winnow's surface area, and the realistic failure mode is not
technical. It is becoming a worse Playnite with an unfinished recommender attached. The
mitigation is the ordering above, and the standing constraint in §2 that the feed stays ahead
of the launcher.
