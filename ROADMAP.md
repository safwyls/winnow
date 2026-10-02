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

- **No hosted service, no Winnow account, no telemetry.** Inference runs locally over the user's
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
| Frontend independence | Electron/TypeScript is the primary desktop/fullscreen frontend. One independent .NET backend owns the library and workers behind an authenticated versioned HTTP API. Electron defaults to Avalon with the original palettes and typography, alongside Afterglow, Rift and Catalogue. It includes setup, account capture, activity and spending, library/detail editors, controller input and native desktop integration, plus Theme Studio and replaceable React screens. Avalonia remains a reference with retained contracts; the complete migration/regression gate and device validation are separate. See the [frontend guide](src/Winnow.Electron/README.md). |
| Library | Local Steam, Epic and GOG discovery, optional Steam/Epic connections, manual entries, search, filters and user lists. Optional Xbox imports installed PC games and opt-in PC/console played history. Optional PlayStation imports the PS4/PS5 account library and opt-in played/legacy trophy history. Steam collections are not imported. |
| Identity | Exact external IDs resolve automatically. Fuzzy matches require confirmation. Same-game, expansion and variant relations apply immediately through reversible links on desktop and fullscreen. |
| History | Playtime snapshots, process-based session recording and restart recovery, optional journal notes, Steam history backfill and account-page imports. Unknown history remains unknown. |
| Statistics | Gameplay and Spending views on desktop and fullscreen. Gameplay shows recorded hours, top games, session lengths and current library composition, with store and date controls. Spending shows captured Steam purchases by currency, yearly bars, purchase composition, licence acquisition and transaction insights. Dollar credits share the dollar total; currencies are never converted or combined. |
| Recommendations | Recently played leads with ten games ordered by recency, outside feedback. Explainable owned-game shelves support cold-start recommendations, dismiss/snooze/undo, visible-card impressions and update signals. Derelict classification remains in the library and is excluded from the feed. Offline replay tooling compares tuning over captured database states. |
| Game actions | Launch, install and uninstall handoffs. Steam supports direct management routes; Epic and GOG open launcher management where direct uninstall is unavailable. |
| Metadata and artwork | Optional IGDB, built-in store metadata, cached artwork, user corrections and artwork source preferences. Desktop and fullscreen browse and save individual hero, cover and icon choices, with Steam, IGDB and plugin sources according to availability. Local provider plugins can add imports, metadata, artwork and shelves. SteamGridDB is bundled separately; collection application awaits a supported API or export. |
| Presentation | Desktop and fullscreen views, controller navigation and text entry, shared themes, separate layout preferences, accessibility and reduced-motion support. |
| Setup | Optional resumable setup for providers, themes and preferences, available again from Application settings on both surfaces. GOG uses local discovery. |
| Export | Acquisition CSV with title, store, acquisition date, licence and price paid. Missing values stay blank; recorded prices are cents without a currency. |
| Distribution | Primary Electron Windows x64 Inno/ZIP and Ubuntu 24.04 x64 Debian/tar packages, with an independent self-contained backend. Disposable-runner checks cover launch, previous-release upgrade, data-preserving removal and portable recovery. Windows updating uses the registered installer; portable Windows/Ubuntu staging retains paired backups and journals. Desktop/fullscreen share update state. Debian uses the package manager. macOS is outside the supported release matrix. |

## 4. Excluded and deferred

**Excluded:** Hosted or multi-user services, co-op and friend
library matching, mobile, and a 3D shelf view. Fullscreen is a separate TV interface.

**Deferred work:**

| Area | Scope and tracking |
|---|---|
| Portable data | Full JSON export/import and broader CSV views (TASK-2). Confirm the export and importer acceptance scope before implementation; acquisition CSV already exists. |
| Recommendation research | Acquisition-evidence evaluation (TASK-136), achievement progress (TASK-137), expected-commitment data (TASK-138), and achievement ingestion (TASK-15). New weights need evidence. |
| Catalogue and identity | Per-edition years (TASK-13), broader GamesDB cross-store automation (TASK-37), and group-header/row actions (TASK-109–110). |
| Steam collections | Static/dynamic collection import, including account ownership, repeat imports and preservation of Winnow list edits (DRAFT-1). |
| GOG sign-in | Local Galaxy discovery supplies owned games and available local play facts. The authorized sessions probe returned only aggregates, without dates; sign-in remains deferred unless additional dated history is demonstrated ([evidence](docs/spikes/gog-session-history.md), TASK-49). |
| Other data research | Steam support-export format and availability (TASK-46). |
| Navigation and notifications | Windows post-session notification (TASK-108), user-selected destinations for links (TASK-114). |

Unowned-game recommendations are outside the current feed. A later wishlist feature would
start from titles the user has explicitly selected, rather than a general purchase feed.

The backend provider plugin contract excludes custom screens, UI replacement, an online marketplace and automatic
plugin updates. Electron's separate [theme contract](docs/electron-themes.md) supports frontend screen replacement.
The website lists first-party providers with release-backed ZIP downloads and
browser installation into desktop or fullscreen settings. CI packages all three providers with
the application release and verifies a versioned catalogue. SDK 1.1 adds shared account connection and provider game actions. Xbox history
does not establish ownership and cannot discover never-played uninstalled purchases. Winnow
bundles its public Microsoft application ID; users sign in without Entra setup. Live PC/console
history import is validated; cumulative minutes and protected WindowsApps launch/session
tracking need further device validation. Readable PC installation roots use the existing session tracker.

PlayStation uses a protected NPSSO credential and community-documented Sony APIs. Console
imports have no local installation or launch support. Trophy sets supply legacy library
evidence, not individual achievement records. Fixture and desktop/fullscreen checks cover
the implementation; live sign-in, account inventory and reported durations remain unvalidated.

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
- **Linux:** Ubuntu 24.04 x64 package checks use sandboxed Electron under Xvfb and Openbox.
  Real-process tests cover native session discovery and synthetic Proton-environment attribution.
  They do not establish physical compositor, Wayland, controller or actual Wine/Proton game
  compatibility. Local Epic/GOG discovery retains Windows-specific paths. The Chromium browser
  is cross-platform; persistent secrets still require Windows DPAPI, and live Linux provider
  sign-in is unverified.
- **Providers and recommendations:** fixtures and local tests do not establish live sign-in
  availability or a measurable feed improvement for a particular user's backfill. Keep those
  observations separate from implemented data ingestion.
- **Packaging:** release smoke scripts run on disposable CI runners. A local publish or
  passing unit tests do not establish installer behavior on a user's device.
- **Migration:** source-contract mappings, full backend/reference tests, Electron component/API
  tests and native presentation checks are distinct gates. Package qualification alone does
  not establish that the complete migration/regression gate has passed.
