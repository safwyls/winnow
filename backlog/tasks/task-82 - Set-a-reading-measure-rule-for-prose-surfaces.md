---
id: TASK-82
title: Apply the established prose measure to remaining desktop surfaces
status: Done
assignee:
  - '@codex-ui'
created_date: '2026-09-03 00:58'
updated_date: '2026-09-11 18:51'
labels: []
dependencies: []
documentation:
  - design-system.md
priority: low
ordinal: 125000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The visual specification already defines desktop ProseMeasure at 410px and distinguishes prose from card/grid width. Complete the remaining layout application: the .para style and affected platform/settings/account/consent surfaces can still span their enclosing cards. Preserve multi-column geometry and fullscreen's separate TV typography.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 design-system.md states a maximum measure for prose, with the size and leading it applies to
- [x] #2 Desktop prose follows the established measure and alignment in affected surfaces; assess platform cards, settings, account/consent text and merge explanatory copy without treating the 720px card cap as paragraph width.
- [x] #3 The rule says explicitly that it does not govern the merge card, so section 6's 840px stays undisturbed
- [x] #4 Verify readable wrapping and accessibility on desktop; assess fullscreen separately without applying the desktop pixel measure to TV layouts.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Apply ProseMeasure and left alignment to the shared desktop para style and the wider account introduction. Preserve card/grid widths and fullscreen typography. 2. Render representative platform, settings, account, consent and merge explanatory prose at narrow and wide desktop widths; verify wrap and layout bounds. 3. Verify fullscreen retains its TV measure, document and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: design-system.md section 3 defines ProseMeasure=410; completed specification criteria remain checked. controls.axaml's .para style has no MaxWidth or left alignment, and StoresView contains wider prose hosts. Only the application/layout verification remains open.

Applied the existing 410px ProseMeasure with left alignment to shared para, account introduction and the merge section blurb override. Card widths remain unchanged. Headless tests render Stores, Account stats, Application, Library settings, Appearance and Merges at 700px and 1400px; all visible prose follows the cap and wraps. Consent wraps to multiple lines. Fullscreen retains 28px text and available width beyond 410px. Seven focused layout/consent/merge tests passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed desktop prose measure application without changing card widths or fullscreen typography. Seven headless tests verify narrow/wide layouts, consent and merge actions.
<!-- SECTION:FINAL_SUMMARY:END -->
