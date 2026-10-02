---
id: TASK-40
title: Decide whether zero-price purchases fill ownership acquisition prices
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:54'
updated_date: '2026-09-11 18:43'
labels:
  - data
  - ingest
dependencies: []
documentation:
  - game-library-design.md
priority: low
ordinal: 101000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Steam account imports retain zero-total transaction facts. The ownership-price attribution pass skips totals at or below zero. Decide whether an eligible single-item, non-refunded Purchase with a known zero total should fill an unknown ownership acquisition price, while keeping zero distinct from missing evidence.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 State the chosen ownership-price attribution policy in the current build specification.
- [x] #2 Verify a parsed zero-total transaction is retained as zero regardless of the ownership attribution choice.
- [x] #3 Verify the chosen ownership-price behavior and distinguish a known zero from an unknown price.
- [x] #4 Preserve bundle, gift and refund exclusions and account-specific provenance; verify the resulting desktop/fullscreen display and acquisition CSV values.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Allow known zero totals through the existing single-item non-refunded Purchase gate. Preserve unknown/conflicting evidence and account provenance. Verify parser, importer, export and both detail presentations; document and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: SteamAccountPageImportService.cs records parsed transactions through TransactionFact with row.Total?.Cents, but the ownership price pass skips total.Cents <= 0. SteamAccountPageParserTests verifies zero-price parsing. The remaining product choice concerns attribution, not dropping transaction rows.

Known zero now fills an unknown eligible purchase price. Verified 80 focused parser/importer/provenance/export tests and 3 headless desktop/fullscreen acquisition tests. Account-scoped zero exports with its account reference; other accounts remain unknown. Existing exclusion and idempotency tests pass. Both details surfaces retain date/licence presentation without showing a per-game price.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Preserve known-zero acquisition prices under existing attribution rules. Verified 80 focused integration tests and 3 desktop/fullscreen UI tests; documented zero versus unknown and exclusions.
<!-- SECTION:FINAL_SUMMARY:END -->
