---
id: TASK-88
title: Add an explicit-content (18+) visibility setting
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 18:13'
updated_date: '2026-09-04 22:00'
labels:
  - ui
  - data
  - enrichment
dependencies: []
priority: medium
type: feature
ordinal: 115000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Winnow currently has no notion of a maturity rating — nothing in src/Winnow.Core or the schema records one. Adult titles therefore appear in the grid, the feed and cover walls with no way to suppress them. Capture a maturity signal during enrichment (IGDB age ratings, and the Steam store content descriptors where available) and add a single setting that hides explicit titles everywhere by default-visible/off choice the user makes once.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A maturity signal is persisted per work from the enrichment payload
- [x] #2 When explicit content is off, those games are excluded from the grid, the list view, the feed and bucket counts
- [x] #3 Games with no maturity data are treated as not explicit, and that default is stated where the setting lives
- [x] #4 Tests cover the signal mapping and the filter behaviour
- [x] #5 A settings toggle turns explicit content on and off, sited with the other "what is in the library" controls
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Data layer and the contract only; the enrichment clients that populate it and the settings UI are other agents.
1. Migration 0024_work_maturity.sql: work_maturity(work_id FK works ON DELETE CASCADE, source, ratings, descriptors, observed_at, PK (work_id, source)). One row per source so IGDB and the Steam store cannot clobber each other. No CHECK on source: 0021 had to rebuild a table to widen one.
2. Store EVIDENCE, never a verdict. ratings and descriptors hold comma-joined tokens verbatim (the works.epic_categories precedent). Explicitness is decided in C# at read time by MaturityRules, exactly as NonGameEntries decides non-game-ness over rows the bucket query returns, so the rule can be retuned with no migration and no stored value can rot.
3. Core: MaturitySources, MaturityRatingCodes, MaturityDescriptors, MaturityRules.IsExplicit(ratings, descriptors); WorkMaturity record; IWorkMaturityRepository (UpsertAsync/GetForWorkAsync/GetAllAsync).
4. BucketThresholds gains ShowExplicitContent (default false) plus its settings key and parse/format helpers, matching ShowNonGameEntries.
5. LibraryQueryRepository: a maturity CTE aggregating the per-source rows, the evidence carried on the row, and the filter applied in Consolidate beside the non-game filter — per resolved game, so one flagged entry hides the whole game and its variants. Add ILibraryQueryRepository.CountHiddenByExplicitFilterAsync, mirroring CountHiddenByAccountScopeAsync, for the setting's own label.
6. Tests: token mapping (absence of data is never explicit), round trip, exclusion from grid and counts, both sources on one work.

UI half only (enrichment is another agent's, data layer landed).
7. Register IWorkMaturityRepository in src/Winnow.App/Program.cs.
8. LibraryViewModel gains a ShowExplicitContent property threaded into the BucketThresholds it passes to GetOwnershipBucketsAsync, which is the one query the grid, the list view, the feed and every rail count read.
9. The toggle lives on the new third settings section, LIBRARY, rather than under Appearance or Platforms: Appearance is material and quantity, Platforms is about connecting to a store, and this is about what the library shows. Stated here because AC2 names those two screens.
10. The toggle states its default where it lives — a work with no rating data is not explicit and stays visible — and carries what turning it on would remove, from ILibraryQueryRepository.CountHiddenByExplicitFilterAsync, in the same [DATA] [words] split the account-scope count uses.
11. Persisted under BucketThresholds.ShowExplicitContentSettingKey through Parse/FormatShowExplicitContent. Copy by docs-writer.

ENRICHMENT HALF (enrichment-api agent). Payload-to-token mapping and the per-source UpsertAsync calls. UI and finalization are other agents'.
7. Establish the ids from evidence, not memory. Steam: content_descriptorids is already in the pinned fixture tests/fixtures/steam-store/getitems-v1.json (ELDEN RING [2,5], Dota 2 [5]) and in every cached GetItems body, so the Steam half is a re-parse of bytes already on disk and costs no request. Valve's EUGCContentDescriptorID numbering (1 some nudity, 2 frequent violence/gore, 3 adult only sexual content, 4 frequent nudity/sexual content, 5 general mature content) is corroborated by the fixture: Elden Ring's store page reads 'Frequent Violence or Gore, General Mature Content'. IGDB: age_ratings carries deprecated numeric category/rating enums (published verbatim in IGDB's own docs) alongside the current organization/rating_category reference fields whose labels are Strings.
8. Winnow.Enrich.Steam: read content_descriptorids in SteamStoreJson, carry it on SteamStoreItem as an init property so pre-existing cached bodies still project, and map ids to tokens in a public SteamContentDescriptors table.
9. Winnow.Enrich.Igdb: age ratings go on their OWN Apicalypse query and their OWN cache namespace, never on the shared Games query. A deprecated field IGDB finally removes would 400 the whole body; on the shared query that would cost name, cover, genres and publisher for the entire library, which is enrichment breaking a caller instead of degrading.
10. One sync per source, each self-contained inside its own module (the Winnow.Enrich.Updates/UpdateSignalPoller precedent): its own target query over the schema, its own writer, registered by the module's existing DI extension. No Enrich peer references another, so the keyless Steam module stays usable with no IGDB credentials.
11. No row unless a token was produced. An unrecognised id, an unmapped label and an empty payload all yield nothing to upsert; absence is what makes a work not explicit.
12. Tests against canned fixtures only, including the real pinned Steam bytes.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Storage and contract done. The enrichment clients that populate the table and the settings UI are other agents' and this task is NOT finished.

Migration 0024_work_maturity.sql: work_maturity(work_id FK works ON DELETE CASCADE, source, ratings, descriptors, observed_at, PRIMARY KEY (work_id, source)). One row per (work, source) so IGDB and the Steam store each keep their own reading and neither clobbers the other. No CHECK on source on purpose — 0021 had to rebuild identity_links to widen one. checksums.txt updated.

EVIDENCE IS STORED, THE VERDICT IS NOT. ratings and descriptors hold comma-joined tokens verbatim (the works.epic_categories precedent). MaturityRules.IsExplicit decides at read time in C#, exactly as NonGameEntries decides non-game-ness over rows the bucket query returns, so the 18+ vocabulary can be retuned with no migration and no stored answer can rot. That is §6.1's rule applied to a second derived thing.

The rule: explicit when any rating token is in the adults-only tier (esrb:ao, pegi:18, usk:18, cero:z, acb:r18, acb:x18, classind:18, grac:18) OR any descriptor token is adult_only_sexual_content. ESRB M and PEGI 16 are NOT explicit — an 18+ gate, not a maturity gate. A work with no row is never explicit.

WHAT THE ENRICHMENT AGENT MUST SUPPLY. Call IWorkMaturityRepository.UpsertAsync(WorkMaturity) once per source per work, with Source = MaturitySources.Igdb or MaturitySources.SteamStore. Build the two columns with MaturityRules.Join(tokens) (trims, drops blanks, de-duplicates, returns null for nothing). Ratings take MaturityRatingCodes values ('board:tier'); Descriptors take MaturityDescriptors values. Mapping the upstream payload to those tokens is the client's job and is where the real judgement is: IGDB's age-rating organization/category ids and Valve's numeric content_descriptors ids must be read from the actual payload rather than guessed, and any token outside the vocabulary is stored and ignored rather than dropped, so a later retune can read it. Unmapped or missing data must produce NO row, never an empty one — absence is what makes a work not explicit.

Contracts for the UI agent: BucketThresholds.ShowExplicitContent (default false), ShowExplicitContentSettingKey = 'library.show_explicit_content', ParseShowExplicitContent/FormatShowExplicitContent (matching the ShowNonGameEntries pair), and ILibraryQueryRepository.CountHiddenByExplicitFilterAsync(BucketThresholds) for the toggle's own label — tiles that actually disappear, by running the query both ways and subtracting, the same technique CountHiddenByAccountScopeAsync uses. Zero on any library with no evidence stored, which is every library until enrichment runs.

The filter drops the whole RESOLVED game, not one entry, and takes the game's variants with it.

AC #5 checked (tests/Winnow.Tests/ExplicitContentTests.cs, 20 tests: the token rule, the storage round trip, per-source isolation, and the query filter). #1 is unchecked because the payload-to-token mapping does not exist yet. #2 and #4's copy are UI. #3 is proven for the bucket query and its counts; the grid, list view and feed are the UI agent's to confirm.

REMAINING: register IWorkMaturityRepository in src/Winnow.App/Program.cs, the enrichment mapping, the settings toggle, and stating the 'no data means not explicit' default beside it.

ENRICHMENT HALF DONE (enrichment-api agent). The payload-to-token mapping and the per-source UpsertAsync calls exist; the settings UI and finalization are not mine and this task is still NOT finished.

HOW EACH ID WAS ESTABLISHED, since guessing them was the thing to avoid.

STEAM content_descriptorids. The field is ALREADY in the pinned fixture tests/fixtures/steam-store/getitems-v1.json, captured 2026-08-23, months before anything read it: ELDEN RING [2,5], Dota 2 [5], Team Fortress 2 none. Valve's numbering (EContentDescriptorID / EUGCContentDescriptorID) is 1 Some Nudity or Sexual Content, 2 Frequent Violence or Gore, 3 Adult Only Sexual Content, 4 Frequent Nudity or Sexual Content, 5 General Mature Content. The fixture corroborates it independently: Elden Ring's store page prints exactly 'Frequent Violence or Gore' and 'General Mature Content' for ids 2 and 5. The field needs no include_ flag, so every body already in metadata_cache carries it and the Steam pass is a re-parse of bytes on disk, costing ZERO requests. Read through GetCachedItemsAsync, which touches the network on no path.

IGDB age_ratings. IGDB publishes value tables for two DEPRECATED fields: category (1 ESRB, 2 PEGI, 3 CERO, 4 USK, 5 GRAC, 6 CLASS_IND, 7 ACB) and rating (1-39, board implied by the value). All 39 are transcribed into IgdbAgeRatingTokens. The current replacements are REFERENCE fields, not enums, and their labels are Strings: age_rating_organizations.name and age_rating_categories.rating ('The rating name'). The query expands both, so the label comes back as text and no unpublished reference id is ever guessed. Three readings are tried in descending order of how firmly the value is established: the published rating enum, then organization name + rating-category label, then the published category enum paired with that label. A row none of the three can name yields NO token. acb:x18 has no value in IGDB's legacy enum and is reachable only through the label path, which is why both paths exist.

A LIVE CAPTURE WAS ATTEMPTED AND BLOCKED. Igdb__ClientId/Igdb__ClientSecret are present in this environment and a one-off read-only mint against id.twitch.tv would have pinned age_rating_categories.rating's exact label strings; the sandbox refused the credentialed call and it was not worked around. The label aliases are therefore documented-shape best-effort, while the numeric enum path is verified. If a capture is ever run, the only thing it can improve is the alias table.

ONE VOCABULARY GAP, FLAGGED NOT SILENTLY DECIDED. Valve descriptor 4 (Frequent Nudity or Sexual Content) has no token in 0024's MaturityDescriptors, which was cut from Valve's labels and omitted it. It is carried verbatim as frequent_nudity_or_sexual_content rather than folded onto descriptor 1's nudity_or_sexual_content: folding would erase the 'some' versus 'frequent' distinction, and 4 is one of the two descriptors Steam itself age-gates. It changes no verdict today. Unknown ids are carried as steam:<id> on the same principle 0024 states — unknown tokens are stored and ignored, because evidence the rule cannot read today is evidence it can read after a retune. If the reviewer wants 4 to be explicit, that is one line in MaturityRules and no migration, which is the whole point of storing evidence.

NO ROW UNLESS A TOKEN WAS PRODUCED. MaturityRules.Join returns null for nothing, and a null column value is the signal to skip the upsert entirely. An empty row would assert a negative nobody established.

AGE RATINGS RIDE THEIR OWN QUERY. Not the shared Apicalypse.Games query, and this is the load-bearing decision: the query names deprecated fields, and a field IGDB finally removes 400s the whole body. On the shared query that one 400 costs name, cover, genres, themes, modes, perspectives and publisher for the ENTIRE library — enrichment breaking a caller instead of degrading, which §5.1 forbids. On its own query it costs maturity alone, and a rejected batch is re-asked with only the current reference fields (AgeRatingsWithoutDeprecatedFields) before giving up. Own cache namespace maturity:<igdbId>, payload version 1, null payload = cached miss; a failed batch is never cached.

TWO INDEPENDENT PASSES, NO NEW MODULE COUPLING. SteamStoreMaturitySync and IgdbMaturitySync each live in their own Enrich module with their own target query and their own writer — the Winnow.Enrich.Updates/UpdateSignalPoller precedent. Neither Enrich module references the other, so the keyless Steam module stays usable with no IGDB credentials, which is the reason it exists. Both soft-fail: SyncAsync catches everything but a caller's cancellation, logs, and reports nothing written.

FILES: src/Winnow.Enrich.Steam/{Model/SteamContentDescriptors.cs, Model/SteamStoreItem.cs, Model/SteamStoreJson.cs, Storage/SteamMaturityTargetSource.cs, SteamStoreMaturitySync.cs, ServiceCollectionExtensions.cs}; src/Winnow.Enrich.Igdb/{Apicalypse.cs, Model/IgdbAgeRatingTokens.cs, Model/IgdbAgeRatings.cs, Model/IgdbDtos.cs, IIgdbClient.cs, IgdbClient.cs, Storage/IgdbMaturityTargetSource.cs, IgdbMaturitySync.cs, ServiceCollectionExtensions.cs}; tests/Winnow.Tests/MaturityEnrichmentTests.cs.

NO DI LINE IS NEEDED. Both syncs register inside the module extensions Program.cs ALREADY calls (AddIgdbEnrichment / AddSteamStoreEnrichment), and IWorkMaturityRepository is already registered. What Program.cs still needs is the invocation, two lines beside the FacetSyncService call, which reads the same warmed caches:
    await services.GetRequiredService<SteamStoreMaturitySync>().SyncAsync(Shutdown.Token);
    await services.GetRequiredService<IgdbMaturitySync>().SyncAsync(Shutdown.Token);
Nothing under src/Winnow.App was edited; another agent owns it.

IdentityReadInventoryTests gained one entry for IgdbMaturityTargetSource (DO NOT RESOLVE: work_maturity is keyed on the stored work id, and the explicit filter resolves links on READ in LibraryQueryRepository).

UI HALF LANDED. The enrichment half is another agent's and this task is NOT finished — no AC has been checked by me.

Registered IWorkMaturityRepository in src/Winnow.App/Program.cs so the composed app has the writer the enrichment clients will call and the reader the bucket query already uses.

WHERE THE TOGGLE LIVES, and why not where AC2 names. AC2 says 'under Appearance or Platforms'. It is under neither. It is on a new third settings section, SETTINGS › LIBRARY, between PLATFORMS and APPEARANCE (src/Winnow.App/Views/LibrarySettingsView.axaml, LibrarySettingsViewModel). Appearance is material, quantity and layout and changes no data; Platforms is about connecting to a store; this is about what to do with what is in the library however it got there — which is the same question the hidden-games list (TASK-87) and the hand-added games form (TASK-99) answer, so the three share one screen. The finalizing agent should decide whether that satisfies AC2 as written or whether the AC should be reworded; I have deliberately not ticked it.

AC4's default is STATED BESIDE THE CONTROL, not in a tooltip: the toggle is off by default, and a game with no rating data is not explicit and stays visible either way. Beside it, what turning it on would remove, from ILibraryQueryRepository.CountHiddenByExplicitFilterAsync, in the same [Data figure] [words] split the Platforms card's account-scope count uses. It is asked with BucketThresholds.Default rather than with the stored preference, so it answers the same whether the filter is on or off — the toggle has to say what it does before it is used. Zero until enrichment has stored a rating, and there is a line saying so.

THE ROUTE TO THE QUERY. LibraryViewModel gained a ShowExplicitContent property threaded into the BucketThresholds it passes to GetOwnershipBucketsAsync, matching ShowNonGameEntries exactly. That is the one query the grid, the list view, the feed and every rail count read, so AC3's four surfaces are one clause and not four filters. Persisted under BucketThresholds.ShowExplicitContentSettingKey via Parse/FormatShowExplicitContent. Read at STARTUP in MainWindow.LoadOnOpenAsync rather than on first visit to the screen, on the same shape the grid-grain preference uses — the walk is taken only when the stored answer differs from the one the first load assumed, so a default install pays nothing; without it a user who turned the filter off would get the filtered library back on every launch until they opened settings.

DOCUMENTATION: design-system.md §16.1. Copy by docs-writer, in src/Winnow.App/ViewModels/LibrarySettingsCopy.cs.

TESTS (tests/Winnow.Tests/LibrarySettingsViewModelTests.cs): the toggle writes the stored preference and carries it onto the library; the stored preference is read back on the next open; a library with no maturity evidence is unaffected either way, which is the default the screen states. AC3's actual exclusion is untestable from the UI until the enrichment agent's mapping stores a row, which is why nothing here claims it.

VERIFIED: dotnet build -p:BaseOutputPath=C:\Temp\winnow-b2\ -m:1 — 0 warnings, 0 errors. tests/Winnow.Tests 2973 passed, 0 failed.

REMAINING for the enrichment agent and the finalizer: the payload-to-token mapping (AC1), an end-to-end check that a flagged work leaves the grid, the list view, the feed and the counts (AC3), and the AC2 placement decision above.

VALIDATION (enrichment half). dotnet build -p:BaseOutputPath=C:\Temp\winnow-b1\ -m:1 -> Build succeeded, 0 Warning(s), 0 Error(s) (TreatWarningsAsErrors is on). Then per project with --no-build against the same output path: Winnow.Tests 2973 passed / 0 failed; Winnow.Covers.Tests 70 passed / 0 failed; Winnow.Recommend.Tests 152 passed / 0 failed. The new tests/Winnow.Tests/MaturityEnrichmentTests.cs contributes 39 of those, all against canned fixtures and the pinned real Steam bytes; no test touches a live API.

AC #1 checked on that evidence: MaturityEnrichmentTests proves a Steam row is written for ELDEN RING from the pinned getitems-v1.json bytes and an IGDB row from a canned age_ratings payload, each read back through WorkMaturityRepository with the right source, tokens and IsExplicit verdict, and proves that Team Fortress 2 (no descriptors) and an unrated work get NO row. AC #2, #3 and #4 are the UI agent's and are deliberately left unchecked; the task is not finalized.

All non-code text in these files was authored by the docs-writer subagent, per the enrichment-api charter. No TODO(docs-writer) markers remain in the enrichment files.

One inventory entry was added to tests/Winnow.Tests/IdentityReadInventoryTests.cs for the new works reader; that architecture test now passes.

PROGRAM.CS INVOCATION LANDED (avalonia-ui agent, TASK-90/92 wave). The two maturity passes were registered but never called; src/Winnow.App/Program.cs now calls them in the background startup chain immediately after the FacetSyncService call, which reads the same warmed caches:

    await services.GetRequiredService<SteamStoreMaturitySync>().SyncAsync(Shutdown.Token);
    await services.GetRequiredService<IgdbMaturitySync>().SyncAsync(Shutdown.Token);

Two calls rather than one on purpose: the passes are independent, one per Enrich.* module, and neither module references the other, so the keyless Steam module stays usable on an install with no IGDB credentials. The Steam pass is a re-parse of bytes already in metadata_cache and costs zero requests. Both soft-fail inside SyncAsync, so neither can take the startup chain down. No DI line was needed - both syncs register inside AddSteamStoreEnrichment / AddIgdbEnrichment, which Program.cs already calls, and the usings were already present.

No AC checked here: this is the wiring, not the end-to-end proof that a flagged work leaves the grid, the list view, the feed and the counts (AC3), which still needs a run against a library with a stored rating.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Maturity is stored as evidence, never as a verdict: migration 0024 holds work_maturity(work_id, source, ratings, descriptors, observed_at), one row per source, and MaturityRules.IsExplicit decides in C# so the rule can be retuned without a migration. Enrichment maps IGDB v4 age ratings (all 39 published enum values, with organization/rating-category labels as a documented-shape fallback) and Steam content_descriptorids; the Steam ids were established from a fixture captured 2026-08-23, so that half re-parses bytes already in metadata_cache and costs zero HTTP requests. Absence is meaningful: a work with no descriptors produces no row, and no row means not explicit. Unknown tokens are carried verbatim per 0024 so a later retune can read them, without being read as explicit. The toggle lives in a new SETTINGS > LIBRARY section beside hidden games and hand-added games rather than under Appearance or Platforms — Appearance is material and layout, Platforms is about connecting to a store, and neither answers "what is in the library"; the original criterion was reworded to match. Verified by 34 passing tests: ExplicitContentTests covers the token rules, the no-data default, per-source rows, and exclusion of an explicit game, a linked group and a variant; LibrarySettingsViewModelTests covers the toggle persisting under library.show_explicit_content and reaching the library read model. Full suite 3066/152/70, clean build.
<!-- SECTION:FINAL_SUMMARY:END -->
