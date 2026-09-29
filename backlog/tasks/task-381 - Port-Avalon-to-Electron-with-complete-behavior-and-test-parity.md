---
id: TASK-381
title: Port Avalon to Electron with complete behavior and test parity
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-29 04:12'
updated_date: '2026-09-29 09:24'
labels: []
dependencies: []
priority: high
type: feature
ordinal: 417000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user requests an entire Electron rebuild of the Avalonia UI with all existing functionality, preserved aesthetics, and migrated passing tests. The existing Electron frontend intentionally has narrower feature coverage. Preserve the shared local backend and current themes while closing the UI gap.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A source-based parity inventory covers every Avalonia UI test and desktop/fullscreen feature, with equivalent Electron tests or a justified framework-specific classification.
- [ ] #2 Avalon desktop and fullscreen preserve the visual specification, themes, dormancy, navigation, accessibility, and interactions in Electron.
- [ ] #3 All Avalonia library, details, settings, setup, activity, spending, connections, artwork, controller, and native application workflows are available in Electron.
- [ ] #4 Electron automated checks and required backend checks pass; isolated runtime and visual checks are recorded for desktop and fullscreen.
- [ ] #5 Documentation and release/test entry points accurately describe the migration and remaining device-validation limits.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inventory source features and Avalonia tests against Electron and backend contracts. 2. Implement Avalon visual, library/details, and settings/setup packages concurrently; integrate native bridges and remaining workflows. 3. Port behavioral tests and validate source-to-test coverage. 4. Run typecheck, frontend build/tests and appropriate .NET checks; inspect isolated desktop/fullscreen renderings. 5. Update documentation and task evidence, retaining incomplete criteria until objectively verified.

6. After checkpoint a8c7ad0, close original feed viewport/impression lifecycle contracts, Home/Library layout and navigation contracts, merge-review visual/fullscreen/cancellation contracts, and embedded Epic authentication with source-equivalent tests; then repeat focused and integrated verification before the next milestone.

7. Port detailed Steam account import reports, capture stop reasons, connection refresh/sign-out and persistence transitions on both surfaces; continue source-equivalent feed lifecycle and hover/preview interaction coverage. Preserve the shared backend and validate retained-source classifications against production composition.

8. Complete remaining merge-review contracts by auditing source assertions, coordinating refresh/race handling, preserving answered-card positions/history, finishing preferred-platform and keyboard/controller behavior, and verifying expansion refusal and artwork precedence. Keep backend revision checks intact. Root also audits native startup/data-directory/activation contracts and ports missing boundary behavior before release transition.

9. Restore details facts and presentation against GameDetailsViewModel, DetailsModalAdditions and TileActions source contracts: root owns shared facts, safe update links, acquisition, screenshots and exact tests; visual agent follows its current layout checkpoint with desktop five-tab modal and fullscreen cinematic details composition. Preserve metadata/artwork/edit operations behind source-equivalent actions and verify both surfaces.

10. Close remaining merge decision/history/refusal/timer/focus contracts with renderer and production HTTP tests while preserving revision checks; measure refresh behavior rather than adding protocol routes to mimic retired repository call counts. Complete settings diagnostics, setup reconnection/replay failure recovery, preference validation and remaining Stores/LibrarySettings audits. Root continues exact details facts, lifecycle evidence and action availability; visual agent implements original Avalon details composition.

11. Restore Stores platform selection and count/status contracts: exactly one Steam/Epic/GOG card at a time, attention badges on hidden tabs, whole-library per-store title totals and account summary, local-only GOG behavior, and mutually exclusive Steam account/credential/purchase/consent layers. Reuse shared setup cards and test both presentation modes. Continue source-equivalent manual create/edit/remove and validation after coordinating Library ownership.

12. Complete per-field metadata and IGDB matching source contracts in the shared Details leaves: preserve independent sources/drafts and accepted stored values, distinguish loading/refusal/success, refuse blank art URLs before writes, preserve conflict revisions, and verify all matching/refusal/claim/pin transitions through renderer and actual backend boundaries. Keep obsolete view-model-only call-count assertions explicit when the production architecture requires revision-checked HTTP snapshots.

13. Restore dedicated Avalon fullscreen Search through an additive theme Search surface, with Library fallback for other themes. Route fullscreen Ctrl+K/controller View to Search, preserve inline desktop search, and verify Enter/Go to results, title-only scope, paging/range footer, focus, empty states and Details return. Follow with source-equivalent non-Home backdrop lifecycle and contrast matrices after the Search package.

14. Complete Library default-sort lifecycle and source-exact selection behavior on both modes, preserving manual list order, selections through sort/reload/view changes, and pruning filtered selections. Audit filter and list contracts against exact source assertions; keep refresh-publication/cancellation work separately scoped because it crosses App and Details.

15. Extract manual game editing into owned frontend files while keeping exports. Restore field-specific accessible title/year/IGDB/Steam validation, typed conflict handling and Cancel-first removal. Verify create/edit/delete with production HTTP tests and desktop/fullscreen form flows; preserve uncertain-write and revision safeguards.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Electron checkpoint: original typography/palettes and separate desktop/fullscreen Avalon surfaces implemented; setup, account capture, library/detail editors, activity and controller support expanded. Latest unit suite 575 passed/8 skipped, typecheck passed; rendered Electron harness 8 passed across desktop/fullscreen layouts at 1280x720 and 1920x1080. Original Avalonia UI suite 896 passed. Full .NET run exposed nine test-output companion packaging failures; fixture fixed and all 33 focused startup/single-instance tests then passed. Migration inventory expanded to include main test-project presentation contracts: 2089 original methods across 265 files; coverage remains substantially incomplete. Native activation review fixes, updater integration, further test migration and final full verification remain. No acceptance criteria marked complete.

Verification checkpoint: full .NET Release suite completed successfully with 6772 passing tests and 2 Linux-only skips on Windows (13 assemblies, isolated artifact paths). Electron unit suite 731 passed, 8 opt-in integration tests skipped in that run; the isolated live API runner then passed all 8 integration tests without skips. Rendered core parity suite passed all 12 tests. Typography suite subsequently passed all 5 tests including installed-font discovery and combined fullscreen/theme text scaling after correcting Chromium custom-origin permission matching. Frozen inventory preserves 2089 original methods from cf45d9f; large pending/partial coverage remains. Continuing native translucency, reading-link/controller behavior, manual executable metadata, and source-exact tests. No completion claim.

Verification checkpoint: complete Electron unit/component/live suite passed 928 tests in 68 files with no skips using a freshly built isolated Debug backend. Additional native checks passed: account statistics 2, appearance material requests 2, isolated reader browser 2 and modal focus/geometry 6 across desktop/fullscreen. Native tests exposed and fixed font permission origin matching, workspace work-name serialization, immediate relationship Undo revision propagation, Escape bubbling out of modal/fullscreen, and reader controller activation. Frozen source inventory now uses Roslyn and includes 2310 original methods from 290 files (including nested files and multiple classes); 324 reviewed shared backend methods retain their original .NET tests. Source migration is still incomplete: authored JSON themes, full merge-review queue and more original contract coverage are being implemented. Full .NET Release evidence remains 6772 passing and 2 Linux-only skips; no acceptance criteria marked complete.

Milestone verification: 1,183 Electron unit/component/live API cases pass in 81 files with no skips; all 40 real Electron rendered tests pass in one run against isolated Debug backends. Typecheck and production build pass. Coverage includes new feed receipts/history/countdown/launch behavior, authored schema-1 JSON palettes with hot reload/export and hydration-safe opening preferences, grouped bulk merge/revision-safe Undo, and Steam health/input/cancel flows across desktop and fullscreen. Full .NET evidence remains 6,772 passed plus 2 Linux-only skips, including all 896 original UI cases. Source evidence remains incomplete and test:migration intentionally fails; embedded Epic sign-in, detailed original interaction/performance contracts and release transition remain. Committing this stable checkpoint before continuing those packages; no acceptance criteria checked.

Stable migration checkpoint committed as a8c7ad0. Source audit now records 215 ported methods, 477 retained production-backend methods and 2 framework-specific methods; 1,440 remain pending and 176 partial. These counts are evidence classifications, not a completion claim. Subsequent work continues on the same task and all acceptance criteria remain unchecked.

Post-checkpoint work: native Epic authentication passes 2 Electron tests covering 10 capture scenarios across both modes; 210 focused account/auth tests pass. Feed exposure tests now pass all 4 native cases against the disposable backend, including covered and scroll-clipped cards; document-wide daily observation, disposal cancellation, original recent reasons/order and fullscreen reserve capacity were corrected. Frozen inventory expanded from the same cf45d9f baseline to 2435 methods/299 files, retaining every initial method/path and adding 125 missed account/auth methods. The validated report currently has 301 ported, 480 retained-backend and 2 framework-specific methods; 1470 pending and 182 partial remain. Layout and merge-review corrections are still receiving native verification. No acceptance criteria checked.

Integrated Electron unit/component/live API rerun passed all 1395 tests in 91 files without skips. A following component assertion verifies optional unscored shelves never register impressions. Combined native layout12 passed; the full58-case rendered run is in progress and exposed a fullscreen history control overlapping Theme Studio after the backdrop layout correction. The regression is being fixed; no full rendered pass is claimed yet.

Startup checkpoint: Electron now uses a redacting failure boundary in main initialization and argument refusal, with exit codes 3 and 2, clean cancellation, and bounded logs only inside an existing selected directory. All 46 focused startup/shell/activation checks passed. Mapped 13 ported startup/argument contracts and nine retained data-location contracts; original legacy composition assertion remains partial. Full native58 previously yielded55pass/3fail; followup11 yielded9pass with appearance2 andfeed4 now passing. Fullscreen text scaling still changes cover geometry; visual agent correcting it.

Integration checkpoint: all1705 Electron unit/component/live cases passed across99 files without skips. Native Home/Library layout16 plus typography5 passed together after saved viewport and text-size cover fixes. Immediate Steam saved import2 passed through real backend; merge additions4 passed; gallery3 passed with native-size image cap, wheel containment, trapped focus and original-thumbnail return. Shared details fact tests now61pass; source evidence for unintegrated reception/refetch remains partial. New production backend regression files passed Steam session8 and merge artwork3. Avalon desktop/fullscreen Details composition is in progress. No acceptance criteria checked.

Second milestone committed as1611b6b (134files): source inventory2435 methods across299files, with495ported/524retained-backend/2framework-specific;1216pending and198partial remain. In-progress Details composition was deliberately left unstaged. Work continues under the same full migration task; no completion criteria checked.

Added actual production HTTP action-dispatch replacements: all 18 cases pass for measured Steam/GOG/Epic launch, install and management URIs and invalid identity refusals. Renderer controls/links cover both Avalon modes, unknown installation states, grouped Steam references, IGDB base-36 identities and viable-copy selection. Restored missing SteamGridDB/GOG Galaxy destinations and lifecycle evidence. Added ownership-only install-folder IPC with authoritative workspace re-read and directory check; focused folder/action checks 35 pass and TypeScript check passes. Migration audit after 33 reviewed source mappings: 1183 pending, 198 partial, 519 ported, 533 retained backend, 2 framework-specific; no completion criteria checked. Details native composition remains under active corrective verification.

Reviewed the three old MergeQueue repository-call assertions against the external API architecture. Hidden and unchanged queue paths now use deferred/cached HTTP reads; answers retain all card slots but require one authoritative GET after a revision-checked POST. Replacement 60-card/20-answer renderer and real-backend tests cover stable slots,40 remaining proposals,20acts and no candidate status writes. On this Windows Debug/temp-SQLite run, POST mean10.60ms/max14.81ms, GET mean58.09ms/max69.67ms, max combined80.54ms. The literal old COUNT/zero-read assertions are classified as retired in-process/framework contracts, with their replacement tests and measured limits recorded; no claim that zero backend reads were preserved.

Third checkpoint verification: all 1936 Electron component and live-backend cases passed in 105 files with no skips. Native Details9, gallery4 (including both install-folder handoffs), setup2 and merge6 passed in the combined native run. Coordinator inspected desktop/fullscreen Overview captures with actual artwork IPC. Two native fullscreen layout cases exposed synchronization gaps (fixture readiness and intermediate animation sampling); correction and focused rerun remain in progress. New production backend replacements passed game actions18, merge decisions9 and setup5 plus retained setup2. No acceptance criteria checked.

Third checkpoint native run finished 83/85 passing; both failures were test synchronization gaps. The corrected full16-case Avalon layout rerun passed in24.8s on the unchanged production build, retaining opacity/inertness/focus/node-reuse assertions. Feature snapshot has 1936/105 passing integrated cases; all85 native cases now have passing coverage across the combined run and corrected layout run. Source inventory2435:600ported,535retained-backend,6framework-specific;1128pending,166partial. Next packages remain in progress; no criteria checked.
<!-- SECTION:NOTES:END -->
