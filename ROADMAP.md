# Winnow — Roadmap

Product scope, delivered capabilities and remaining work. Backlog holds task status,
priority and acceptance criteria; this page describes what the app supports and what is
outside the current scope. Architecture is in [game-library-design.md](game-library-design.md),
and interaction and appearance are in [design-system.md](design-system.md).

## 1. What Winnow is

Winnow helps people play games they already own. It combines Steam, Epic and GOG libraries
and recommends games using local play history, sessions and updates. Every recommendation
explains why that game is worth returning to or starting.

Launching games through Winnow supplies session history that improves later recommendations.
That loop is the product: the launcher supports the recommender, and the recommender gives
people a reason to keep using the launcher.

## 2. Standing constraints

- **No server, no Winnow account, no telemetry.** Inference runs locally over the user's
  database. Optional storefront sign-in connects an account at that store.
- **Prioritize the feed and the play-history loop.** Launcher features support finding and
  playing owned games; matching every feature of another launcher is not the objective.
- **Installation delegates to the owning store client.** Winnow does not implement downloads
  or manage a store's game files itself.
- **Every recommendation gives its reason in one sentence.**
- **Desktop and fullscreen share application behavior.** They have separate layouts,
  navigation and browsing state, with coverage for both surfaces.

## 3. Delivered capabilities

“Implemented” describes repository behavior, not a claim of a published release or completed
hardware validation. The remaining validation is listed in §6.

| Area | Implemented behavior |
|---|---|
| Library | Local Steam, Epic and GOG discovery, optional Steam/Epic connections, manual entries, search, filters and user lists. Steam collections are not imported. |
| Identity | Exact external IDs resolve automatically. Fuzzy matches require confirmation. Same-game, expansion and variant relations apply immediately through reversible links on desktop and fullscreen. |
| History | Playtime snapshots, process-based session recording and restart recovery, optional journal notes, Steam history backfill and account-page imports. Unknown history remains unknown. |
| Recommendations | Explainable owned-game shelves, cold-start recommendations, dismiss/snooze/undo, visible-card impressions, update signals and evidence-based Derelict classification. Offline replay tooling compares tuning over captured database states. |
| Game actions | Launch, install and uninstall handoffs. Steam supports direct management routes; Epic and GOG open launcher management where direct uninstall is unavailable. |
| Metadata and artwork | Optional IGDB, built-in store metadata, cached artwork, user corrections and artwork source preferences. Local provider plugins can add imports, metadata, artwork and shelves. SteamGridDB is bundled separately. |
| Presentation | Desktop and fullscreen views, controller navigation and text entry, shared themes, separate layout preferences, accessibility and reduced-motion support. |
| Setup | Optional resumable setup for providers, themes and preferences, available again from Application settings on both surfaces. GOG uses local discovery. |
| Export | Acquisition CSV with title, store, acquisition date, licence and price paid. Missing values stay blank; recorded prices are cents without a currency. |
| Distribution | Windows/Linux x64 packages, release checks and draft publication workflow. Update notification and installer-based Windows updating; portable Windows/Linux update recovery is deferred. |

## 4. Excluded and deferred

**Excluded:** PlayStation/Xbox integration, hosted or multi-user services, co-op and friend
library matching, mobile, and a 3D shelf view. Fullscreen is a separate TV interface.

**Deferred work:**

| Area | Scope and tracking |
|---|---|
| Portable data | Full JSON export/import and broader CSV views (TASK-2). Confirm the export and importer acceptance scope before implementation; acquisition CSV already exists. |
| Recommendation research | Acquisition-evidence evaluation (TASK-136), achievement progress (TASK-137), expected-commitment data (TASK-138), and achievement ingestion (TASK-15). New weights need evidence. |
| Catalogue and identity | Per-edition years (TASK-13), broader GamesDB cross-store automation (TASK-37), and group-header/row actions (TASK-109–110). |
| Steam collections | Static/dynamic collection import, including account ownership, repeat imports and preservation of Winnow list edits (DRAFT-1). |
| GOG sign-in | Local Galaxy discovery supplies owned games and available local play facts. Investigate sign-in only if the unverified sessions endpoint adds useful session history (TASK-49). |
| Other data research | Steam support-export format and availability (TASK-46). |
| Navigation and notifications | Windows post-session notification (TASK-108), user-selected destinations for links (TASK-114). |
| App updates | Recovery for portable Windows and Linux installations (TASK-159). |

Unowned-game recommendations are outside the current feed. A later wishlist feature would
start from titles the user has explicitly selected, rather than a general purchase feed.

The initial plugin contract excludes custom screens, UI replacement, new launcher actions,
an online marketplace and automatic plugin updates.

## 5. Carried debt

| Limitation or refinement | Tracking |
|---|---|
| Zero-total transactions are retained; attributing a known zero to an ownership price needs a product choice | TASK-40 |
| Saved-page import accepts only one file of each page kind; combining multiple licence pages is deferred | TASK-41 |
| The single-entry ACCOUNT rail section and account-stat presentation need refinement | TASK-42–43 |
| Optional-connection treatment and connection copy need a shared contract; the established prose measure needs wider application | TASK-80–82 |

Identical captured transaction facts within one source/account deduplicate to keep repeated
imports idempotent. Genuine same-day repeats with identical fields remain indistinguishable;
this is an accepted input limitation. Receipts from different known accounts remain separate.

## 6. Validation and release readiness

Repair correctness, data integrity and broken existing behavior before expanding features.
Backlog contains the active work queue; completed review reports do not impose additional
release gates. [Release instructions](docs/releases.md) give the build, package and
publication checks.

- **Fullscreen:** automated interaction and layout checks cover the implemented TV surface.
  TASK-4 still needs a real controller and intended display at normal seating distance to
  establish that every Winnow-owned operation is reachable, focus is visible and no mouse or
  keyboard is required. Record the controller, OS, display, failing operations and external
  provider or launcher input requirements.
- **Linux:** real-process tests cover native session discovery and synthetic Proton-environment
  attribution. They do not establish compatibility across actual Wine/Proton games. Local
  Epic/GOG discovery and embedded sign-in retain Windows-specific integration limits.
- **Providers and recommendations:** fixtures and local tests do not establish live sign-in
  availability or a measurable feed improvement for a particular user's backfill. Keep those
  observations separate from implemented data ingestion.
- **Packaging:** release smoke scripts run on disposable CI runners. A local publish or
  passing unit tests do not establish installer behavior on a user's device.
