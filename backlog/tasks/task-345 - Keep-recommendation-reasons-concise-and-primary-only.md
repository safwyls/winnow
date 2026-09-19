---
id: TASK-345
title: Keep recommendation reasons concise and primary-only
status: Done
assignee:
  - codex
created_date: '2026-09-19 17:06'
updated_date: '2026-09-19 18:20'
labels: []
dependencies: []
type: enhancement
ordinal: 378000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Secondary phrasebook clauses make recommendation explanations wordy. Keep one succinct primary reason that can stand alone.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Built-in recommendation reasons render only the primary reason; no secondary phrasebook clause is appended.
- [x] #2 Primary wording is concise, evidence-based and self-contained; scoring and ranking signals remain unchanged.
- [x] #3 Desktop and fullscreen use the same primary-only reasons; relevant tests and current documentation are updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Render only the existing primary signal, remove secondary phrasebook and supporting-clause rendering machinery, and retain structured evidence/scoring unchanged. Edit primary variants for concise stand-alone evidence-based statements while preserving deterministic variety. Update engine contracts and tests plus recommendation documentation. Verify desktop and fullscreen consume the shared reason and run relevant rendering/integration tests; update design-system wording.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed secondary templates, supporting-clause selection/rendering and citation-cap ledger machinery. Primary variants are concise standalone evidence statements; corrected claims about reading patch notes, refund eligibility or never returning. Preserved score calculation, signal precedence, structured supporting evidence and deterministic primary variant selection. Legacy FactCitation tuning fields remain accepted but inert. Desktop/fullscreen share the rendered primary string; new headless presentation tests verify both. Updated recommendation spec, visual spec, README example and stale reserve-test comment. Verification: 178 recommendation tests, 59 feed VM/reserve tests, 10 UI presentation/shelf tests passed (247 total); full solution build passed with zero warnings/errors; diff checks clean. Independent review found only stale citation-cap docs, corrected before completion. Full solution test suite not run.

Full-suite documentation enforcement flagged the phrase describing supporting-signal ordering as document hierarchy language. Clarified it to ordered by priority; behavior is unchanged.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Built-in recommendations now show one concise primary reason, without a secondary phrasebook clause, on desktop and fullscreen. Ranking and structured evidence remain unchanged. Verified with 247 tests and a clean full solution build.
<!-- SECTION:FINAL_SUMMARY:END -->
