---
id: TASK-119
title: 'Edit a game metadata by hand, including cover and background art'
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 03:47'
updated_date: '2026-09-05 16:42'
labels:
  - ui
  - data
dependencies:
  - TASK-89
priority: medium
type: feature
ordinal: 146000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User request: "we should allow a user to manually change the metadata with an editor if desired allowing them to set all the fields we normally pull from igdb including the cover art and background art".

DESIGN DIRECTION FROM THE USER, 2026-09-05, which supersedes the layered-override approach this task was first written with: "we should really have one source of truth, i think the real issue is granularity. each field we currently populate via IGDB or Steam should stand alone, that way if a user wants to override they can do the existing metadata fetch and override them all in one pass or manually override each field as desired".

So this is not an override layer stacked on top of an automatic value, and not a precedence tower composing three sources. Each field carries its own source, and that source IS the truth for that field. There is exactly one value per field and one answer to where it came from.

That gives the user two gestures over the same model. A metadata fetch — the existing IGDB assignment, or a refetch — rewrites every field in one pass, because the user is saying "take it all from this record". A manual edit sets one field and makes the user its source, leaving every other field alone and still tracking its own.

Enrichment then has a simple rule rather than a guard tree: it writes a field whose source is a service it can speak for, and leaves a field the user owns. Read how the TASK-89 pin works before designing — it blocks a whole work at GetEnrichmentTargetsAsync and again at ApplyEnrichmentAsync — and decide what remains of it once fields carry their own source. The pin answers WHICH GAME this is; field sources answer WHERE EACH VALUE CAME FROM. Those are different questions and should not be conflated, but their interaction must be stated.

Cover art and background art are fields like any other under this model, settable from a local file or a URL, and go through the existing cover cache honouring --data-dir. TASK-111 will pull screenshots from IGDB; user-supplied background art should share that surface rather than growing a second image path.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every field populated from IGDB or Steam carries its own source, and that source is the single answer to where the value came from
- [x] #2 A metadata fetch or IGDB assignment rewrites every field in one pass
- [x] #3 A manual edit sets one field and makes the user its source, leaving other fields tracking their own
- [x] #4 An enrichment pass writes fields whose source is a service and leaves fields the user owns, with no separate override layer to consult
- [x] #5 The user can see the source of each field and hand any field back to automatic
- [x] #6 Cover art and background art are settable from a local file or a URL, through the existing cover cache, honouring --data-dir
- [x] #7 How field sources interact with the TASK-89 pin is decided and documented, since the pin answers which game and sources answer where each value came from
- [x] #8 Tests cover a user-owned field surviving an enrichment pass, a full fetch reclaiming every field, and a single field handed back to automatic
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Migration 0027_work_field_sources.sql: ALTER TABLE works ADD COLUMN background_url; CREATE TABLE work_field_sources(work_id, field, source, set_at, PK(work_id,field)) + partial index on source='user'. No backfill: absence of a row means no writer has claimed the field, which is the honest reading of every value written before sources existed. Append checksums.txt.
2. Core: WorkFields (the six user-visible field names + their works columns), FieldSources (user/igdb/steam/epic/gog vocabulary, IsUserOwned), WorkFieldSource + WorkFieldValue records, IWorkFieldSourceRepository. Work gains BackgroundUrl. WorkEnrichment gains Source/NameSource so a write can stamp what supplied it.
3. Data: WorkFieldSourceRepository (read the map for one work, set one field + stamp 'user', reset one field to automatic by clearing the stamp and emptying the column). WorkRepository: background_url in Columns/Insert; GetEnrichmentTargetsAsync joins a user_owned CTE so a user-owned field is never missing and never a reason to make a request; ApplyEnrichmentAsync drops incoming values for user-owned fields and stamps every field it actually wrote.
4. The pin: keep both NOT EXISTS guards. Restate why — the automatic pass resolves identity from the store id, so on a pinned work everything it would write is another game's facts. Pinning is also the take-it-all-from-this-record gesture: WorkIgdbPinRepository.PinAsync stamps every field it rewrites as 'igdb', clearing any prior user ownership.
5. Art: UserArtStore in Winnow.Covers imports a local file or a URL into <coverCache>/user/<token>, works.cover_url / background_url hold winnow://user-art/<token>, CoverKey.User + UserArtCoverSource render it, ArtKeys.Resolve in Winnow.Covers.Igdb is the one place a stored art URL becomes a CoverKey — the surface TASK-111's IGDB screenshots will share.
6. App seam: IWorkMetadataEditService over the repository + UserArtStore, so a view model never names Winnow.Data.
7. Delegate the editor view and view model to the avalonia-ui agent as its own view (GameDetailsView.axaml and GameIgdbMatch* are held by TASK-122); report the entry point rather than wiring it.
8. Delegate every comment, XML doc and UI string to docs-writer. Update game-library-design.md §6/§6.4 and append the superseded sentences to docs/decisions.md.
9. Tests: user-owned field survives an enrichment pass; a pin rewrites and re-stamps every field; a field handed back to automatic is refilled; art import honours the cache directory.
10. Verify with dotnet build -p:BaseOutputPath=C:\Temp\winnow-u2\ -m:1 then dotnet test per project --no-build.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
WIRING PASS (avalonia-ui). The editor built earlier in this task existed but nothing opened it; this pass wired the entry point and closed the last enforcement gap.

Inventory (tests/Winnow.Tests/IdentityReadInventoryTests.cs). Four readers added, all DO NOT RESOLVE, reasons authored by docs-writer: WorkFieldSourceRepository.GetStateAsync, .SetFieldAsync, .ResetFieldAsync and WorkMetadataEditService.GetAsync. The shared reason is the point of the task - a per-field edit must read and write the row the user is looking at, and resolving through the same-game map would show one work's values and stamp another work's row as user-owned. Every_reader_of_works_or_ownerships_is_on_the_resolve_or_the_do_not_resolve_list now passes.

Entry point. GameDetailsViewModel takes a trailing metadataEditor parameter and exposes MetadataEditor / ShowMetadataEditor, mirroring IgdbMatch. GameDetailsView.axaml gains an 'Edit details' Button.link in the action band (Band 3) beside WrongGameButton, and hosts views:GameMetadataEditorView in the right column's rest band after the IGDB block. LibraryViewModel gains optional IWorkMetadataEditService and IImageFilePicker fields and a BuildMetadataEditor factory beside BuildIgdbMatchAsync, using the same GameWorkIdFor resolved work id. Both services were already registered in Program.cs; UserArtStore comes from AddCoverCache, which is pointed at data.Root, so art honours --data-dir.

Three things the brief did not enumerate, each with a reason.
1. The IsVisible gate sits on a wrapper StackPanel, not on the view element. A DataContext on an element retargets every compiled binding on that same element, so IsVisible resolved against GameMetadataEditorViewModel and the build failed AVLN2000. The IGDB block is wrapped for the same reason.
2. OnEditDetailsPressed does a BringIntoView on the host, the same arrangement OnWrongGamePressed uses. The rest band is a bounded scroll region and the editor opens below the fold, so without it the disclosure appears to do nothing.
3. GameMetadataEditorViewModel gained a note constructor parameter and a Note/HasNote pair, drawn outside the IsOpen gate. An art save reloads the library and reopens the modal (only a reload draws a new user-art cover key on the wall), which leaves the editor closed, so the confirmation had nowhere to land. LibraryViewModel carries it in _metadataNote through AfterMetadataArtChangeAsync, mirroring _igdbNote. A text save does not reload - that would discard the drafts in the other five rows.

linkSameGame: NOT wired, and it should not be. WorkFields deliberately excludes igdb_id ('identity, which is the pin's question, not a field's'), so the editor cannot set an IGDB id and the UNIQUE-constraint collision TASK-122's offer answers cannot arise on this surface. Recorded in design-system.md 10.10.

ACTION BAND IS OVER CAPACITY - stated, not fixed. It now carries seven controls at once: primary action, Store page, All patch notes, Open folder, Wrong game?, Edit details, Hide. It is a horizontal StackPanel with Spacing=10 and does not wrap; it clips. The right column runs 422px (card MinWidth 700) to 582px (MaxWidth 860). Estimated from Button.link chrome (24px) and Button.launch chrome (40px) at 12px Jakarta: the installed-game set is 652-690px and the not-installed set 574-605px, so the full band overruns at every card width and was already at the edge before this link was added. Recorded as a stated cost at the end of design-system.md 10.10 and in docs/decisions.md. Needs a design decision - wrap, second row, or move a control.

New tests: tests/Winnow.Tests/MetadataEditorModalTests.cs, four facts over a real migrated database through the shipped WorkMetadataEditService and WorkFieldSourceRepository - the editor writes the work the tile resolves to and stamps only that field; a field is handed back to automatic from the modal; no service means no editor; no picker means the URL route only.

Documents. design-system.md gains 10.10 'Editing a field by hand' and 10.9's action-band enumeration was corrected; the sentence it replaced is quoted verbatim in a new docs/decisions.md entry, per AGENTS.md. No banned word (supersede/superseded/supersedes/amended/the original text/as first written) appears in either governing document.

All prose - the four inventory reasons, every XML doc comment, every XAML comment, the design-system.md sections and the decisions.md entry - was authored by the docs-writer subagent. NO PLACEHOLDERS REMAIN: swept src/ and tests/ for TODO(docs-writer) and PLACEHOLDER_, both zero.

Verification (PowerShell, per the shared-output-path rule):
  dotnet build Winnow.slnx -p:BaseOutputPath=C:\Temp\winnow-x1\ -m:1
    Build succeeded. 0 Warning(s) 0 Error(s)
  dotnet test tests\Winnow.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-x1    Passed! - Failed: 0, Passed: 3249, Skipped: 0, Total: 3249
  dotnet test tests\Winnow.Recommend.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-x1    Passed! - Failed: 0, Passed: 152, Skipped: 0, Total: 152
  dotnet test tests\Winnow.Covers.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-x1    Passed! - Failed: 0, Passed: 78, Skipped: 0, Total: 78

NEEDS A RUNNING APP, not verified here (the app was not run, per the brief): that the action band actually clips and by how much, which is an estimate rather than a measurement; the OS image file dialog; the art previews decoding at render scaling; the source badge, Auto control and per-row messages as drawn; and the BringIntoView scroll landing where intended.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Each user-visible metadata field on a work now carries its own source, and that source is the truth for that field: one value per field, one answer to where it came from. A metadata fetch or IGDB assignment rewrites every field in one pass; a manual edit sets one field, makes the user its source, and leaves the rest tracking their own. Enrichment writes a field whose source is a service it can speak for and leaves a field the user owns, with no override layer to consult. Cover and background art are fields like any other, settable from a local file or a URL through the existing cover cache, which honours --data-dir.

This final pass wired the entry point. The editor is disclosed from an 'Edit details' link in the details modal's action band beside 'Wrong game?', and draws in the right column's rest band under the IGDB reassignment control, built by LibraryViewModel for the same resolved work id the IGDB and LISTS surfaces use. The four remaining readers of works were classified DO NOT RESOLVE in the identity read inventory, on the reason the whole task rests on: a field edit must read and write the row the user is looking at.

Two things are stated rather than hidden. The editor cannot set an IGDB id - identity is the pin's question, not a field's - so TASK-122's same-game offer cannot arise on this surface and no linkSameGame delegate is wired. And the action band now carries seven controls in a strip that does not wrap and clips at every card width; the cost is recorded in design-system.md 10.10 and needs its own design decision.

Verified by dotnet build (0 warnings, 0 errors) and dotnet test per project: Winnow.Tests 3249 passed, Winnow.Recommend.Tests 152 passed, Winnow.Covers.Tests 78 passed, 0 failed. The app was not run, so the drawn surface - band clipping, file dialog, art previews, source badges - is unverified.
<!-- SECTION:FINAL_SUMMARY:END -->
