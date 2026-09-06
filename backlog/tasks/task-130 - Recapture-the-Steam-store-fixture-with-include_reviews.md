---
id: TASK-130
title: Recapture the Steam store fixture with include_reviews
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 00:50'
updated_date: '2026-09-06 22:41'
labels:
  - enrichment
  - test
milestone: m-4
dependencies:
  - TASK-112
priority: medium
type: task
ordinal: 2900
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
TASK-112 added a reader for Steam review summaries, but no pinned fixture proves the response shape. tests/fixtures/steam-store/getitems-v1.json predates the include_reviews flag, so the reader was written from Valve webui/common.proto (StoreItem_Reviews_StoreReviewSummary: review_count, percent_positive, review_score, review_score_label) rather than from bytes on disk.

The reader degrades to "no figure" on anything it does not recognise, so the failure mode is safe rather than wrong. But the consequence is stated in that fixture directory README: the contract test is not the early-warning system for reviews that it is for every other field, because there is nothing captured to compare against.

The recapture command in the README already carries the flag, so running it and re-pinning the fixture closes the gap. Sanitize with fake account ids per AGENTS.md.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 getitems-v1.json is recaptured with include_reviews: true and carries at least one item with a reviews block
- [x] #2 The reviews reader is verified against the captured bytes rather than against the proto alone
- [x] #3 The contract test covers reviews the way it covers every other field
- [x] #4 The README section recording the gap is removed or rewritten, since it will no longer be true
- [x] #5 The fixture is sanitized with fake account ids
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Verify the current implementation and required live evidence, apply the scoped correction, update governing documentation with superseded text retained in decisions, and close only acceptance criteria supported by objective checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The anonymous GetItems capture on 2026-09-06 requested include_reviews:true. Three successful items carry summary_filtered and summary_language_specific; failed item 760 has neither. Replaced public creator_clan_account_id fields with fake 123456; no user credentials or account data were sent. Contract tests assert the captured four-field shape and production projection. The missing-reviews test now uses an explicit missing-block response. All SteamStore tests pass in Release.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Recaptured and sanitized the real Steam review payload, verified the production reader against it and closed the fixture documentation gap. Release contract tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
