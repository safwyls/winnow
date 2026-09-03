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
| M8 | The Feed | The recommender is the app's primary view; every card states its reason in one sentence | shipped; no dismiss or snooze, and nothing remembers yesterday |
| M5 | Historical playtime backfill | Historical playtime backfills; the feed measurably improves on a cold library | built; backfill tested, feed improvement awaiting live validation against the user's key |
| M6 | Export (JSON + CSV) | JSON is complete and re-readable; CSV covers a defined set of views | deferred 2026-08-31; exit criterion to be restated |
| M9 | Install / uninstall management | Install and uninstall delegate to the owning store client and reflect state back | after M6 |
| M10 | Full-screen mode + gamepad navigation | The whole app is navigable on a controller at 10 feet | last |

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
| The IGDB metadata cache has no `payload_version` | TASK-18 |
| The account stats screen is a first pass; presentation cleanup is shelved | TASK-43 |
| The fact tables cannot distinguish two identical same-day transactions | TASK-44 |
| `OwnershipRepository.UpsertAsync` could overwrite an imported `acquired_at` | TASK-39 |
| $0.00 purchase rows are skipped rather than recorded as zero, undecided either way | TASK-40 |
| The saved-file licenses route captures one page per file | TASK-41 |
| ACCOUNT and REVIEW each spend a rail section heading on a single row | TASK-42 |
| The Steam Web API key and the IGDB client secret are still plaintext at rest | TASK-78 |

Two limits are stated as rules in the build spec rather than carried here, because they are
not going to be fixed: session detection is Windows-only in practice
(`game-library-design.md` §5.2), and the account-scope filter errs visible
(`game-library-design.md` §6.3).

## 6. The risk

This scope roughly triples Winnow's surface area, and the realistic failure mode is not
technical. It is becoming a worse Playnite with an unfinished recommender attached. The
mitigation is the ordering above, and the standing constraint in §2 that the feed stays ahead
of the launcher.
