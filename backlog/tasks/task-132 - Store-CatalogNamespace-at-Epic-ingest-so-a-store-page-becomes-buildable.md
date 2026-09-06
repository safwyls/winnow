---
id: TASK-132
title: Store CatalogNamespace at Epic ingest so a store page becomes buildable
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-06 03:46'
updated_date: '2026-09-06 04:40'
labels:
  - enrichment
  - ingest
dependencies:
  - TASK-131
priority: medium
type: feature
ordinal: 159000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Found while establishing the per-store action matrix (TASK-131, docs/spikes/store-actions-per-launcher.md). It is the highest-leverage item that would let Epic games have a store page.

Epic ingest reads CatalogNamespace and then drops it — EpicLibrarySource.cs around line 130. The spike censused the live catcache.bin (297 entries) and found the namespace present on 100% of owned entries, offline, with no network call needed to obtain it.

What it unlocks: a store URL needs a slug, and no slug is stored anywhere — the catcache top-level keys are id, namespace, entitlementName, eulaIds, title, description, longDescription, technicalDetails, developer, lastModifiedDate, keyImages, categories, releaseInfo, customAttributes, dlcItemList, mainGameItem, and Winnow authenticated catalog fixtures return zero hits for "slug" either. But namespace plus one cacheable request to Epic productmapping reaches a slug, and the spike measured that route at 84% coverage of an owned library, with the request weighing about 68KB.

The final URL template https://store.epicgames.com/p/{slug} is recorded in the spike as needs-execution-by-the-user, because store.epicgames.com returns 403 to every programmatic request from this machine. Confirm it in a browser before building on it.

GOG has the parallel gap and is cheaper: api.gog.com/v1/games/{productId} returns _links.store.href and api.gog.com/products/{productId}?expand=changelog returns real patch notes, both anonymous and keyless, both verified by execution. GOG store page and patch notes are TASK-131 AC4, left unchecked there precisely because they need this ingest and enrichment work rather than UI.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Epic CatalogNamespace is persisted at ingest for every owned entry
- [ ] #2 A slug is obtainable from the stored ids through a cached, rate-limited, soft-failing request in the established client style
- [ ] #3 The store.epicgames.com URL template is confirmed working before any button is drawn
- [ ] #4 GOG store page and patch notes are reachable from the stored product id, closing TASK-131 AC4
- [ ] #5 Coverage is measured and recorded rather than assumed, since a partial route means some games still get no store page
- [ ] #6 A game whose slug cannot be resolved draws no button, per §10.3
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce the root cause on the live machine before writing code: copy catcache.bin (ProgramData) and winnow.db (+wal,+shm) to scratch, census owned base games for namespace/appId/id, and count metadata_cache rows by provider. DONE: 67 owned base games, 67/67 carry namespace, appId and id; external_ids(epic)=67 and all 67 match catcache ids; gamesdb keys release:epic:<AppName> match catcache appIds 67/67; metadata_cache holds ZERO epic-catalog rows, so SqliteEpicLaunchKeys.GetAllAsync returns empty and no Epic game can offer any action. 66 uninstalled, 1 installed.
2. Ingest side (Winnow.Ingest.Epic): EpicLibrarySource already reads CatalogNamespace and AppName from both catcache.bin and the .item manifests and drops both. Add an EpicLaunchTriple record and an EpicScanResult, expose ScanLibrary() returning candidates plus one triple per owned base game (manifest first, catalog as fallback), and keep Scan() as a delegating overload so no existing caller changes.
3. Persistence seam (Winnow.App.Services, because Winnow.Ingest.Epic does not reference Winnow.Data — the same reason SqliteEpicCatalogCache lives there): new IEpicLaunchKeyStore / SqliteEpicLaunchKeyStore writing metadata_cache under a NEW provider 'epic-local-launch'. A new provider, not epic-catalog: the web backfill's row under that key holds a full catalog item and a two-field local payload written over it would corrupt that cache.
4. Read side: SqliteEpicLaunchKeys.GetAllAsync unions both providers, local rows first, web rows filling gaps. Nothing else changes for the UI.
5. Wire it: LocalLibraryScan carries the triples, LocalLibrarySyncService persists them in SyncAsync, Program.cs registers the store. Amend LocalLibrarySyncService's 'touches no repository itself' docstring and append the superseded sentence to docs/decisions.md.
6. Delegate to avalonia-ui: StoreActions.EpicPrimary gains Install via ?action=install (verified by execution against the launcher log oracle), gated on installed == false AND a complete triple, plus the NoWayIn.NoInstallRoute reclassification and design-system.md 10.3's matrix.
7. Delegate to docs-writer: the spike correction (action=install works and is the real route; action=installer is registered but routes to SelectiveDownloadUpdate; action=updatecheck is not registered in 20.2.9; ://store/product/<slug> works via MainRouter and needs a slug Winnow does not store), the new verified-by-vendor-documentation provenance category, the two methodological failures, and every XML doc comment and code comment in the new C# files.
8. Tests: EpicLibrarySource emits a triple per owned base game against the real sanitized fixtures; the store round-trips; the reader unions both providers; the Install action draws only for uninstalled Epic with a complete triple.
9. Wait for every docs-writer child, scan for TODO(docs-writer) and PLACEHOLDER_, check CRLF, then dotnet build -p:BaseOutputPath=C:\Temp\winnow-ns\ -m:1 and dotnet test per project --no-build.
<!-- SECTION:PLAN:END -->
