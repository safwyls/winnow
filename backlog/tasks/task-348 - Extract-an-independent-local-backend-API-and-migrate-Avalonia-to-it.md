---
id: TASK-348
title: Extract an independent local backend API and migrate Avalonia to it
status: Done
assignee:
  - codex
created_date: '2026-09-26 21:39'
updated_date: '2026-09-26 22:59'
labels: []
dependencies: []
ordinal: 384000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Support simultaneous Avalonia and alternative frontends over the same live library. The backend runs independently and exposes a documented language-neutral API sufficient for third-party frontend authors. Electron implementation is a subsequent consumer, outside this extraction.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Standalone backend owns migrations, database access, background workers, providers, credentials and application commands without Avalonia dependencies.
- [x] #2 Versioned documented API covers application capabilities and live updates with local authentication, reconnect and concurrency semantics.
- [x] #3 Avalonia desktop and fullscreen use the external API with no direct repository or backend implementation access.
- [x] #4 Two independent clients observe committed changes without duplicate workers; frontend disconnects do not stop the backend and reconnect restores state.
- [x] #5 Data and plugin compatibility, startup, authentication, artwork, updates and packaging are preserved and verified.
- [x] #6 API integration, dependency and desktop/fullscreen tests verify the boundary; docs explain independent frontend development.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Proposed architecture for review before implementation:
1. Add Winnow.Application for frontend-independent use cases and projections, Winnow.Api.Contracts for wire DTOs, Winnow.Backend for the standalone ASP.NET Core host, and Winnow.Api.Client for the Avalonia HTTP/event client. Existing domain/data/provider modules remain behind Application. No Avalonia dependency in backend or contracts; no Data, ingest, enrichment, monitor or plugin-host dependency in App at completion. No reflected repository RPC or serialized view models.
2. Expose /api/v1 HTTP JSON queries and commands, OpenAPI and a documented server-sent event stream. Use loopback-only binding, per-data-directory single-instance ownership, protected endpoint/token discovery, bearer authentication and explicit origin/host validation. Backend starts independently; Avalonia attaches or starts it safely. Closing a frontend disconnects that client; explicit backend shutdown and coordinated updates own process termination. Local API replaces the old blanket server-framework exclusion, without hosted accounts or network service scope.
3. Inventory and move use cases by capability: library/details/facets; feed/feedback; lists/manual games/hidden games; identity merges and corrections; metadata/artwork; activity/journal/achievements/statistics/export; typed preferences/setup; provider credentials/auth/plugins; launch/install actions; diagnostics and update lifecycle. Extract business operations currently embedded in view models. Keep navigation, selection, focus and scroll state per client/surface. Share persisted domain changes and global preferences. Never expose generic settings storage or secrets in read DTOs.
4. Publish committed domain invalidations plus operation/progress snapshots with backend epoch and increasing sequence. Subscribe before initial snapshot/refetch to avoid lost changes. Bound subscriber queues and event retention; reconnect from a cursor or request full resync on gaps/restart. Use operation IDs, idempotency where commands can cause repeated side effects, and explicit conflicts for stale edits. Slow/disconnected clients cannot hold up commits. Background changes use the same publication path.
5. Make provider sign-in client-scoped challenge/completion operations with validated state, cancellation and protected credential persistence. Expose artwork via stable IDs/URLs; imports use bounded uploads or explicit local selections. Launch accepts game/action IDs, not arbitrary shell commands. Model supplementary feed work as operations/events rather than Task-valued DTO properties. Keep file dialogs, browser presentation, tray and controller focus in frontend adapters.
6. Migrate Avalonia desktop and fullscreen through the same API client, capability by capability. Temporary internal paths may exist during migration but must be removed before completion. Keep C# plugins and database/rename/credential compatibility. Adapt packaging, autostart, deep links, update staging/restart and diagnostics for backend plus frontend lifetimes.
7. Verify on throwaway data: two concurrent clients see each other and background changes; no duplicate workers or sessions; restart/reconnect/gap recovery; conflicts and retried actions; local authorization and secret redaction; API contract and forbidden-dependency tests. Run current desktop/fullscreen interaction tests adapted to the client boundary, dotnet build/test and migration integrity checks. Document independent host launch, client discovery/authentication, errors, version policy, event protocol and a small language-neutral frontend example. Update README, architecture, plugins/releases and roadmap to match delivered behavior.
Delivery phases: contract/host and client discovery; extracted library/feed and multi-client event proof; all remaining commands and settings/auth; complete Avalonia cutover; packaging/documentation and full verification. Split tracked implementation work after architecture review. Electron UI implementation is not included.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Read-only audit completed. LibraryViewModel combines repository projections and mutation commands; MergeQueueViewModel owns identity writes; LibraryChangePublisher and FetchStatusReporter dispatch into Avalonia; Program registers backend and UI together. Existing single-instance guard is keyed by data directory. Architecture proposal awaiting explicit review under Backlog execution guide step 7; no production code changed.

User explicitly approved the proposed HTTP/JSON and live-event architecture and project split. Implementation started; approval gate satisfied.

Implemented standalone authenticated loopback host, explicit application API, replayable event stream, C# client, full production Avalonia API composition, and no-backend-implementation assembly boundary. Backend has no Avalonia dependency; bitmap rendering split into Covers.Avalonia. Real-host headless tests prove desktop/fullscreen live create/hide/rename, independent searches, and restart/resync. Application use-case suite and focused UI/client integration pass. Release solution builds with zero warnings; all 45 migration hashes verified. Full solution tests and final concurrency/update review in progress; no acceptance criteria finalized yet.

Release solution build passes with zero warnings. Windows self-contained packaging/Publish.ps1 completes with backend plus ASP.NET runtime; published native frontend becomes responsive, authenticated health/OpenAPI respond, backend remains healthy after frontend exits, and explicit authenticated shutdown succeeds. Native startup 9/9 passes including desktop/fullscreen and failure paths. Plugin restart controls pass keyboard/controller tests with rendered visual checks. Final review added IGDB stale pin checks and API-only fullscreen activity/journal/summary paths. Full suite rerun pending after three test expectation/inventory updates; focused new fullscreen API test passes.

The full Release run passed all 4993 core, 178 recommendation, 191 cover, 39 updater, 417 provider/plugin, 21 application, 17 backend and 15 client tests. The new fullscreen API test exposed missing post-refresh layout pumping only under full UI ordering; the test now follows the existing activity-test dispatcher pattern. All three live API UI tests pass within the full UI rerun, which is still completing. Two native Linux process tests skip on Windows as designed. Installer installation/uninstallation smoke remains for disposable CI runners; local self-contained package startup, API, independent lifetime and shutdown were exercised.

Final evidence: all 6767 runnable tests pass across 13 assemblies (full solution run plus complete 896-test UI rerun after fixing layout synchronization); the two Linux-only process tests skip on Windows. Final Release solution build has zero warnings and errors, and all 45 migration hashes verify. Full UI rerun includes independent desktop/fullscreen live edits, retained search, backend restart/resync, API-only activity/journal/summary, and plugin restart keyboard/controller coverage. Final client discovery reads allow atomic endpoint replacement during concurrent frontend reconnects; all 15 transport tests pass. Windows self-contained package publish and responsive native/API/independent-lifetime smoke pass. Installer smoke and native Linux execution remain CI-only. Known implementation limit: cold IGDB matching holds the SQLite write transaction while fetching metadata. Electron frontend is intentionally outside this extraction.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Extracted an independent authenticated local HTTP/JSON backend with OpenAPI, replayable live events, concurrency checks and a C# API client. Migrated production Avalonia desktop and fullscreen to the same external boundary; backend owns persistence, providers, credentials and workers. Preserved plugin/backend restart, startup and update lifecycle and packaged the companion runtime. Verified with 6767 passing tests, zero-warning Release build, 45 migration hashes, and Windows package startup/API/lifetime smoke. See docs/frontend-api.md for third-party frontend development.
<!-- SECTION:FINAL_SUMMARY:END -->
