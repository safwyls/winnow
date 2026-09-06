---
id: TASK-130
title: Recapture the Steam store fixture with include_reviews
status: To Do
assignee: []
created_date: '2026-09-06 00:50'
labels:
  - enrichment
  - test
dependencies:
  - TASK-112
priority: medium
type: task
ordinal: 157000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
TASK-112 added a reader for Steam review summaries, but no pinned fixture proves the response shape. tests/fixtures/steam-store/getitems-v1.json predates the include_reviews flag, so the reader was written from Valve webui/common.proto (StoreItem_Reviews_StoreReviewSummary: review_count, percent_positive, review_score, review_score_label) rather than from bytes on disk.

The reader degrades to "no figure" on anything it does not recognise, so the failure mode is safe rather than wrong. But the consequence is stated in that fixture directory README: the contract test is not the early-warning system for reviews that it is for every other field, because there is nothing captured to compare against.

The recapture command in the README already carries the flag, so running it and re-pinning the fixture closes the gap. Sanitize with fake account ids per AGENTS.md.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 getitems-v1.json is recaptured with include_reviews: true and carries at least one item with a reviews block
- [ ] #2 The reviews reader is verified against the captured bytes rather than against the proto alone
- [ ] #3 The contract test covers reviews the way it covers every other field
- [ ] #4 The README section recording the gap is removed or rewritten, since it will no longer be true
- [ ] #5 The fixture is sanitized with fake account ids
<!-- AC:END -->
