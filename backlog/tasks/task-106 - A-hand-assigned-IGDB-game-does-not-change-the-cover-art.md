---
id: TASK-106
title: A hand-assigned IGDB game does not change the cover art
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 02:49'
updated_date: '2026-09-05 03:11'
labels:
  - ui
  - data
dependencies: []
priority: high
type: bug
ordinal: 133000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reported by the user after using the wrong-game control: the assignment applies, but the tile and modal keep the old art.

Diagnosed. LibraryViewModel picks a cover key by store precedence: if a release carries a Steam external id it takes CoverKey.Steam(appId), and only a release with NO Steam appid falls through to CoverKey.Igdb(imageId) derived from works.cover_url. AssignAsync does write the new cover_url through WorkIgdbPinAssignment, but for any Steam-owned game — the common case — the tile never consults it, so the Steam capsule for the original appid keeps rendering.

TASK-89 recorded the opposite as fact: "No cover-refresh call is needed: the IGDB cover key is derived from the stored cover_url image id, so a reload after AssignAsync picks up the new art." That is true only for Epic and GOG releases. The note should be corrected, not left standing.

The fix has to decide what a pin means for cover precedence: a user who says "this is the wrong game" is also saying the storefront art is wrong, so a live pin should probably win over the Steam capsule for that work.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Assigning a game by hand changes the cover on the tile, the details modal and any other surface showing that art
- [x] #2 The precedence rule is explicit: a live IGDB pin outranks the store capsule for that work, and the reasoning is recorded
- [x] #3 Clearing the pin returns the work to store-capsule art
- [x] #4 A stale cached cover for the old key is not served after a pin changes
- [x] #5 Tests cover a Steam-owned work changing art on pin and reverting on clear
- [x] #6 The incorrect claim in TASK-89 implementation notes is corrected
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Precedence rule: a LIVE IGDB pin that yields an image id outranks the store capsule for that work. Order becomes (a) live pin -> CoverKey.Igdb(image id from works.cover_url), (b) Steam appid -> CoverKey.Steam, (c) cover_url image id -> CoverKey.Igdb (the existing Epic/GOG fallback). A pinned work whose chosen entry has no cover keeps the store capsule, because a placeholder says less than the wrong capsule does. Precedence is evaluated on the release's OWN work row, never the resolved work, because the pin and the cover_url it rewrote live on the same row.
2. Cross-store duplicate case is untouched: the key is still the image id, never works.igdb_id, so both halves of a duplicate pair still key on the same cover_url.
3. Data layer (delegated to data-layer agent): IWorkIgdbPinRepository.GetLivePinnedWorkIdsAsync -> IReadOnlySet<long> over work_igdb_pins WHERE cleared_at IS NULL, one read per library load rather than one per work.
4. Enrichment layer (delegated to enrichment-api agent): IgdbManualAssignment passthrough for the same read.
5. App seam: IIgdbAssignmentService.GetLivePinnedWorkIdsAsync, soft-failing to an empty set like every other call on that seam.
6. LibraryViewModel: read the pinned set once in LoadAsync, apply the precedence above where the cover key is chosen.
7. Clearing a pin now changes the cover key back, so Clear must reload and reopen the modal the way Assign does. GameIgdbMatchViewModel's afterAssign hook becomes afterChange and Clear invokes it.
8. Stale cache: no eviction. The IGDB key is content-addressed by image id, so a pin produces a key that was never cached and a clear returns to the store key whose cached bytes are still correct. Evicting or marking would risk the negative-marker failure CoverDiskCache warns about. Prove it with a test rather than adding a mechanism.
9. Tests: a Steam-owned work changes cover key on pin and reverts on clear, in tests/Winnow.Tests/IgdbAssignmentModalTests.cs.
10. Docs: design-system.md 10.9 loses 'the cover needs no separate refresh mechanism' and 'Clear does not reload'; superseded sentences appended to docs/decisions.md; TASK-89 implementation notes corrected. All prose delegated to docs-writer.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Fixed. The cover-key precedence in LibraryViewModel.LoadAsync is now: (1) a live IGDB pin on the work, when works.cover_url yields an IGDB image id; (2) the Steam portrait capsule for the release's appid; (3) the image id in works.cover_url. A pin outranks the capsule because a user reaching for the wrong-game control is saying the storefront art is wrong too. The pin is read off the release's OWN work row and never the resolved work, because the pin and the cover_url it rewrote are two columns of one row. A pinned entry IGDB gave no cover falls through to the capsule rather than to the placeholder. Rule 3 is untouched, so the cross-store duplicate case still works: the key is the image id and never works.igdb_id, which is UNIQUE and can be held by only one half of a duplicate pair.

Stale cache: no eviction, and deliberately none. A CoverKey.Igdb names the artwork asset itself, so pinning moves a tile to a key that has never been fetched and clearing returns it to the Steam key whose cached bytes are still correct. The old key is simply no longer asked for. Adding eviction or negative-marking here would risk the failure CoverDiskCache warns about — a wrong .none marker is a month of placeholder art for covers we hold.

Clearing a pin now reloads the library and reopens the modal. It still writes no metadata, but dropping the pin changes the cover key back to the store capsule and only a reload draws it. GameIgdbMatchViewModel's afterAssign hook became afterChange and both writes run through it.

New bulk read so the load does not query per work: IWorkIgdbPinRepository.GetLivePinnedWorkIdsAsync (SELECT work_id FROM work_igdb_pins WHERE cleared_at IS NULL), passed through IgdbManualAssignment and IIgdbAssignmentService, which soft-fails to an empty set — the store-capsule precedence the view model had before. Migration 0026's partial index ux_work_igdb_pins_live makes one live pin per work a database fact, so the query needs no DISTINCT. The data-layer and enrichment slices were delegated to the data-layer and enrichment-api agents; all prose was authored by docs-writer.

design-system.md 10.9 corrected in place (the precedence rule replaces 'the cover needs no separate refresh mechanism'; Clear now reloads), superseded sentences appended to docs/decisions.md, and TASK-89's implementation notes corrected.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
A live IGDB pin now outranks the store capsule when the cover key is chosen, so a hand-assigned game changes its art on a Steam-owned work rather than only on Epic and GOG. Clearing the pin reloads and hands the capsule back. No cover-cache eviction was needed or added: an IGDB cover key names the artwork asset, so a pin moves the tile to a key never fetched and a clear returns it to a key whose cached bytes are still right. Verified with dotnet build (0 warnings, 0 errors) and dotnet test: Winnow.Tests 3167 passed (baseline 3162, plus two new cover-precedence tests over a real migrated database and three repository tests for the bulk pin read), Winnow.Recommend.Tests 152 passed, Winnow.Covers.Tests 70 passed. What remains unverified without a running app is the rendered pixel: that the pinned art actually arrives from the IGDB CDN and repaints the tile and the modal.
<!-- SECTION:FINAL_SUMMARY:END -->
