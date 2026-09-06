---
id: TASK-132
title: Store CatalogNamespace at Epic ingest so a store page becomes buildable
status: To Do
assignee: []
created_date: '2026-09-06 03:46'
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
