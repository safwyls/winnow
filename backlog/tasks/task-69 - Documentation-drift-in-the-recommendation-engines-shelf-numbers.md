---
id: TASK-69
title: >-
  Documentation drift in the recommendation engine: shelf numbers and the
  retired GDPR importer
status: Done
assignee:
  - '@codex'
created_date: '2026-09-01 21:23'
updated_date: '2026-09-06 22:32'
labels:
  - docs
  - recommend
milestone: m-4
dependencies: []
priority: medium
ordinal: 3100
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Two unrelated drifts in docs/recommendation-engine.md, folded into one pass because they touch the same file.

Shelf numbers. Several recorded shelf figures no longer match the code. The design doc section 5 lists ShelfGenreCap as 4 per ten-item shelf while the code uses 3 with MaxPerShelf 6; RecommendationRequest.MaxPerShelf opens its XML comment with "10:" against a value of 6; and section 6a refers to the patched shelf having ten slots. Found while fixing the probe budget, and predating it. The numbers themselves are not in question, only the records of them.

The retired GDPR importer. docs/decisions.md (2026-08-28) records that there is no Steam GDPR export — the premise came from a single unreliable source, the spike measured it, and the decision states all four citations were corrected. The recommendation-engine ones were not. Section 6 still says "The GDPR importer is the cold-start lever" in the present tense, and sections 2, 6a and 7 still cite it as the thing that will resurrect shelf time and make return latency computable. This is the more serious of the two: it points the reader at a mechanism that does not exist as the answer to the cold-start problem, the largest open weakness the engine has, so the drift actively misdirects work away from the levers that do exist. M5 shipped its replacement (ROADMAP.md line 58).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every shelf figure in docs/recommendation-engine.md matches the code it describes
- [x] #2 RecommendationRequest.MaxPerShelf XML comment matches its value
- [x] #3 No section of docs/recommendation-engine.md describes the GDPR export importer as a live or future mechanism
- [x] #4 The cold-start lever is described in terms of what M5 actually shipped
- [x] #5 Every superseded sentence is appended to docs/decisions.md per AGENTS.md
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Verify the current implementation and required live evidence, apply the scoped correction, update governing documentation with superseded text retained in decisions, and close only acceptance criteria supported by objective checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Source audit confirms ShelfGenreCap=3, MaxPerShelf=6, patched shelf six slots, and MaxPerShelf XML comment already says6. Historical ten-deep reserve/probe measurements remain explicitly historical. Replaced all GDPR-import promises with the implemented Replay snapshots, first-played backfill and account-page acquisition imports; these do not invent sessions or exact return latency. Superseded text appended to decisions.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Corrected cold-start documentation to describe shipped Replay snapshots, first-played backfill and account-page acquisition imports. Verified shelf defaults and existing XML comment against code; retained historical reserve measurements and appended superseded text to decisions.
<!-- SECTION:FINAL_SUMMARY:END -->
