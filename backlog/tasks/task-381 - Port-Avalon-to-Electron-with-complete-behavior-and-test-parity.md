---
id: TASK-381
title: Port Avalon to Electron with complete behavior and test parity
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-29 04:12'
updated_date: '2026-09-29 07:00'
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
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Electron checkpoint: original typography/palettes and separate desktop/fullscreen Avalon surfaces implemented; setup, account capture, library/detail editors, activity and controller support expanded. Latest unit suite 575 passed/8 skipped, typecheck passed; rendered Electron harness 8 passed across desktop/fullscreen layouts at 1280x720 and 1920x1080. Original Avalonia UI suite 896 passed. Full .NET run exposed nine test-output companion packaging failures; fixture fixed and all 33 focused startup/single-instance tests then passed. Migration inventory expanded to include main test-project presentation contracts: 2089 original methods across 265 files; coverage remains substantially incomplete. Native activation review fixes, updater integration, further test migration and final full verification remain. No acceptance criteria marked complete.

Verification checkpoint: full .NET Release suite completed successfully with 6772 passing tests and 2 Linux-only skips on Windows (13 assemblies, isolated artifact paths). Electron unit suite 731 passed, 8 opt-in integration tests skipped in that run; the isolated live API runner then passed all 8 integration tests without skips. Rendered core parity suite passed all 12 tests. Typography suite subsequently passed all 5 tests including installed-font discovery and combined fullscreen/theme text scaling after correcting Chromium custom-origin permission matching. Frozen inventory preserves 2089 original methods from cf45d9f; large pending/partial coverage remains. Continuing native translucency, reading-link/controller behavior, manual executable metadata, and source-exact tests. No completion claim.

Verification checkpoint: complete Electron unit/component/live suite passed 928 tests in 68 files with no skips using a freshly built isolated Debug backend. Additional native checks passed: account statistics 2, appearance material requests 2, isolated reader browser 2 and modal focus/geometry 6 across desktop/fullscreen. Native tests exposed and fixed font permission origin matching, workspace work-name serialization, immediate relationship Undo revision propagation, Escape bubbling out of modal/fullscreen, and reader controller activation. Frozen source inventory now uses Roslyn and includes 2310 original methods from 290 files (including nested files and multiple classes); 324 reviewed shared backend methods retain their original .NET tests. Source migration is still incomplete: authored JSON themes, full merge-review queue and more original contract coverage are being implemented. Full .NET Release evidence remains 6772 passing and 2 Linux-only skips; no acceptance criteria marked complete.

Milestone verification: 1,183 Electron unit/component/live API cases pass in 81 files with no skips; all 40 real Electron rendered tests pass in one run against isolated Debug backends. Typecheck and production build pass. Coverage includes new feed receipts/history/countdown/launch behavior, authored schema-1 JSON palettes with hot reload/export and hydration-safe opening preferences, grouped bulk merge/revision-safe Undo, and Steam health/input/cancel flows across desktop and fullscreen. Full .NET evidence remains 6,772 passed plus 2 Linux-only skips, including all 896 original UI cases. Source evidence remains incomplete and test:migration intentionally fails; embedded Epic sign-in, detailed original interaction/performance contracts and release transition remain. Committing this stable checkpoint before continuing those packages; no acceptance criteria checked.
<!-- SECTION:NOTES:END -->
