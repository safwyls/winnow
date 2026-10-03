---
id: TASK-385
title: Use the shared themed choice component throughout Electron
status: In Progress
assignee:
  - '@codex'
created_date: '2026-10-03 19:20'
updated_date: '2026-10-03 19:50'
labels: []
dependencies: []
type: bug
ordinal: 463000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Several Electron screens still open native browser select and font suggestion popups, unlike the themed store and sort dropdowns. Audit bundled compositions and shared feature screens so desktop and fullscreen use consistent choice controls while preserving editing, selection and controller behavior.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All production single-choice dropdowns and font suggestions use the shared themed component; no native select or datalist popups remain in bundled renderer views.
- [x] #2 Existing option values, accessible labels, disabled states, persistence and typed custom font names are preserved.
- [x] #3 Keyboard and controller opening, option navigation, selection, dismissal and focus return work on desktop and fullscreen, including nested dialogs and long lists.
- [x] #4 Affected component and rendered checks pass, a source audit guards against native dropdown regressions, and current documentation records the shared control.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Extend ChoiceMenu only where the audited controls need shared field behavior. Migrate feature screens and theme/setup controls in separate bounded scopes; adapt affected tests to actual menu interactions. Verify core accessibility/controller behavior and native rendered dropdown placement across compositions and dialogs, then build, document and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced all 42 native select callsites and the font datalist with ChoiceMenu across features, bundled compositions and Theme Studio. Added field naming/description/form-value support, disabled options, empty-list focus, trigger-return callbacks, wrapping/spacing and controller fixes. Font inputs still accept validated custom names. Main-game relationship submission now explicitly rejects a missing parent. Migrated component, native and packaged test interaction helpers; updated migration references and current visual/frontend documentation.
<!-- SECTION:NOTES:END -->
