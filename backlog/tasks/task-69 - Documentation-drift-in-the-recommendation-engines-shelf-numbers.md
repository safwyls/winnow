---
id: TASK-69
title: Documentation drift in the recommendation engine's shelf numbers
status: To Do
assignee: []
created_date: '2026-09-01 21:23'
labels:
  - docs
  - recommend
dependencies: []
priority: low
ordinal: 86000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Several recorded shelf figures no longer match the code. The design doc section 5 lists ShelfGenreCap as 4 per ten-item shelf while the code uses 3 with MaxPerShelf 6; RecommendationRequest.MaxPerShelf opens its XML comment with "10:" against a value of 6; and section 6a refers to the patched shelf having ten slots. Found while fixing the probe budget, and predating it. The numbers themselves are not in question, only the records of them.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every shelf figure in docs/recommendation-engine.md matches the code it describes
- [ ] #2 RecommendationRequest.MaxPerShelf XML comment matches its value
<!-- AC:END -->
