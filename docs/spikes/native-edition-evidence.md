# Native store evidence for edition identity

## Source inspection, 2026-09-11

The public, unauthenticated Epic CMS request
`https://store-content.ak.epicgames.com/api/en-US/content/products/fez` returned a page with:

| Field | Value |
| --- | --- |
| Page `_id` | `78e2d1ca-9ff2-4179-95d4-f67c1acf3b76` |
| Page, item and offer namespace | `41f47fd0d3e248bc938a5815d6d64daa` |
| Item catalog ID | `7a70b499513441c792b541d53505e0b2` |
| Item AppName | `Bluebird` |
| Item `hasItem` | `true` |
| Offer ID | `442f123b4d884d8ca85236aa30b99a79` |
| Offer `hasOffer` | `true` |

The response was fetched without credentials or library data. It confirms an exact bridge
between that catalog item/artifact and those Epic offer/page IDs. It does not establish that
Fez has an explicit IGDB edition mapping, or measure coverage of an owned library. The
reduced public fields above appear in `EditionEvidenceFixture.Cms`; the fixture's IGDB
edition 42 and parent 1 are synthetic, deliberately unrelated to a live game assertion.

[IGDB's API documentation](https://api-docs.igdb.com/#game-version) distinguishes the
`game_versions` grouping from its `games` entries. Edition entries carry `version_parent`
and `version_title`. Using the grouping ID as an individual release's version would join
different editions; the implementation therefore records the explicit edition game ID.

## Verification method

The provider suite uses canned HTTP responses through the existing typed IGDB transport.
It tests exact source/UID correlation, ordinary games, conflicting editions, duplicate and
malformed fields, paging failures, current versioned caches and offline/expired behavior.
Epic CMS tests exercise the full native triple, cached hashes, missing responses, future and
expired timestamps, duplicate fields and failed refreshes without live requests.

Temporary SQLite integration fixtures run the production acquirer and reversible linking
service. A matching native Epic offer plus a complete missing legacy page mapping and a
matching native Steam mapping qualifies one pair. Missing native Epic evidence leaves that
pair unresolved even when Steam resolves. Conflicting offer/page editions or multiple store
IDs on one release cannot qualify. Tests also change CMS, IGDB, launch-cache and store-ID
inputs after recording, and confirm the link transaction refuses them. Identical observations
reuse stored evidence, and explicit pins, rejected pairs and separation history remain binding.

Both presentation tests start with production acquisition and sync, then inspect the shared
library, the resolved Merges strip and per-release details. Desktop and fullscreen invoke
Separate again and verify two titles return without changing external IDs or re-linking on
the next automatic pass. These are fixture outcomes, not a live eligible/unresolved ratio.

The production sync service reports these measured constructed cases in
`EditionEvidenceTests.Coverage_report_distinguishes_eligible_unresolved_and_conflicting_native_pairs`:

| Native evidence for one GamesDB pair | Observed | Eligible | Linked | Unresolved | Conflicting | Protected/changed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Matching explicit Epic and Steam editions | 1 | 1 | 1 | 0 | 0 | 0 |
| Epic offer/page both missing, Steam explicit edition | 1 | 0 | 0 | 1 | 0 | 0 |
| Epic and Steam map to different explicit editions | 1 | 0 | 0 | 0 | 1 | 0 |

These deliberately constructed inputs verify each report category; their proportions do
not estimate production coverage. Existing refresh-composition tests additionally read the
strict versioned IGDB cache through the production dependency graph on both surfaces, with
the legacy release-version columns left empty.

No live IGDB credentials or private library were used for this task. Runtime logs report
observed, eligible, linked, conflicting, unresolved and protected/changed pairs; no queue
reduction or minimum coverage is promised.
