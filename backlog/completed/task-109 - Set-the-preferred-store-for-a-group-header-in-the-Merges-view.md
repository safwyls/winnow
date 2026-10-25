---
id: TASK-109
title: Persist per-group preferred platform for already linked games
status: Done
assignee:
  - '@codex'
created_date: '2026-09-05 02:49'
updated_date: '2026-09-11 19:41'
labels:
  - ui
dependencies: []
documentation:
  - game-library-design.md
  - design-system.md
priority: medium
type: feature
ordinal: 151000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Let users choose which store entry provides the header for an already linked group. This is a persisted per-group preference. The existing queue-wide preferred platform applies only to pending proposals and does not implement this feature. Keep presentation preference changes compatible with identity-link invariants.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Users can choose the store supplying an already linked group's header on desktop and fullscreen.
- [x] #2 The per-group preference persists across launches, re-ingest and further links.
- [x] #3 The selected preference is visible and reversible.
- [x] #4 If the preferred store is unavailable or removed, the group falls back to a valid automatic header.
- [x] #5 Verify identity and undo invariants, and keep this preference distinct from pending-proposal queue defaults.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Persist a same-game group preferred store independently from identity links; resolve preference anchors through current links and choose the newest explicit setting when groups combine. Automatic is an explicit reversible reset. 2. Apply the preference to shared library header title, cover and primary store entry, and already-linked Merges strips, with deterministic fallback when the store has no available entry. Keep canonical Work metadata/editing authority, pending-proposal defaults and all link/undo operations. 3. Add a desktop resolved-group selector and equivalent fullscreen actions with visible saved state. 4. Verify SQLite persistence, further links, removal, reset and identity/undo invariants; exercise both UI surfaces; document semantics and commit this item separately.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: MergeQueueViewModel skips resolved cards when changing the queue-wide platform preference. FullscreenLibraryToolsPage offers Make header only for unresolved cards. Completed TASK-178 explicitly excludes persistent preferences for already linked groups; it is related work, not duplicate delivery.

Migration 0041 stores a preferred store on the group root independently from identity acts. Current same-game resolution carries anchored choices through further links; the latest explicit revision wins, including an Automatic reset. Library title, cover and primary store entry use the chosen available member. Canonical root metadata, metadata editing, list membership and identity/undo retain their existing authority. Desktop resolved strips have a named Header store selector; fullscreen group actions expose the same choices and current value. Missing ownership falls back automatically while retaining an unavailable saved choice. Validation: clean App build; 289 targeted data/identity/grouping/queue tests passed; 33 desktop/fullscreen UI tests passed including rendered selector/actions, save/reset, details and year-filter parity; Verify-Migrations verified 41 hashes. New tests cover reload, re-ingest, further links, combined-group precedence, store removal/return, automatic reset, unsupported relationships, unchanged link history and independent pending queue defaults.

Full-suite copy enforcement found the new header tooltip outside MergeCopy. Moved the group-header tooltip, accessible-name format, Automatic/unavailable option labels and save-failure text into the shared copy definitions without changing wording or behavior. The tooltip now uses an explicit static binding; a separate TASK110 Details literal exposed by the same check is corrected in its own follow-up.

Full Release verification found a fullscreen fixture cleanup race after all assertions passed: the visible library title changed during SetGroupHeaderAsync, before the action callback completed its additional Context.RefreshAsync and render. Follow-up plan: expose the fullscreen action completion task, await it after selecting and resetting a header store, and retain the persistence/identity assertions. Do not infer completion from an intermediate title change.

Follow-up implemented: FullscreenIdentityPage.PendingAction now represents the whole save, refresh and render callback. The fullscreen fixture awaits that task for the store choice and Automatic reset before checking persistence and disposing its database. Both desktop/fullscreen GroupHeaderPreferenceUiTests passed in Release after the change.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a persisted per-group Header store choice to desktop and fullscreen, with Automatic reset and fallback for unavailable stores. Header presentation changes without reparenting identity links or redirecting Work metadata edits. Verified 289 data/identity tests, 33 UI tests, a clean App build and migration integrity.
<!-- SECTION:FINAL_SUMMARY:END -->
