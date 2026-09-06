---
id: TASK-132
title: Store CatalogNamespace at Epic ingest so a store page becomes buildable
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 03:46'
updated_date: '2026-09-06 18:11'
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
- [x] #1 Epic CatalogNamespace is persisted at ingest for every owned entry
- [x] #2 A slug is obtainable from the stored ids through a cached, rate-limited, soft-failing request in the established client style
- [x] #3 The store.epicgames.com URL template is confirmed working before any button is drawn
- [x] #4 GOG store page and patch notes are reachable from the stored product id, closing TASK-131 AC4
- [x] #5 Coverage is measured and recorded rather than assumed, since a partial route means some games still get no store page
- [x] #6 A game whose slug cannot be resolved draws no button, per §10.3
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Verify existing Epic triples; implement cached background Epic productmapping and GOG details clients with HTTP-level Polly; project stored URLs and readable GOG changelog into details; verify fixtures, public URLs and coverage; finalize both tasks with evidence.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
2026-09-06: Built Winnow.Enrich.Stores with background typed HttpClient, shared Polly1request/sec budget and two retries,24hour cache, stale fallback, missing-slug omission and Core read-only repository seam. Browser confirmed https://store.epicgames.com/p/soma title/heading/content; age gate left untouched. Mapping returned1283entries. Historical measured library coverage56/67(84%) remains explicitly a sample; GOG sample1207658871 returns store URL and real changelog.19 canned-response tests plus3architecture enforcement tests passed; added final sync-to-library projection assertions awaiting combined run. No real library or launcher files modified.

Final integration verification: main test suite passed 3,469 of 3,469 tests, including the sync-to-library projection that resolves stored Epic namespaces and GOG product IDs without adding HTTP on library load. Covers passed 84 and Recommend passed 152. The final targeted action check is running before closure.

Final build passed with zero warnings or errors. Final targeted storefront, action and accessibility tests passed 82 of 82 against the finished code.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Verified existing Epic namespace persistence and completed cached storefront enrichment. Typed HTTP clients use a shared Polly budget/retries, 24-hour cache, stale fallback and bounded responses; the UI reads a Core repository seam without HTTP. Epic store links use the browser-confirmed URL; GOG gains the service-returned store URL and readable changelog. Coverage remains explicitly partial (historical 56/67 Epic sample); missing slugs hide links. Verified by live endpoint/browser observations, canned fixture and SQLite tests, compiled-view keyboard/scroll checks, clean build, 3,469 main tests, 84 Covers tests, 152 Recommend tests and 82 final targeted checks.
<!-- SECTION:FINAL_SUMMARY:END -->
