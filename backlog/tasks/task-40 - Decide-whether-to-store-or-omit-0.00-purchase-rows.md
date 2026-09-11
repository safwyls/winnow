---
id: TASK-40
title: Decide whether zero-price purchases fill ownership acquisition prices
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-09-11 13:59'
labels:
  - data
  - ingest
dependencies: []
documentation:
  - game-library-design.md
priority: low
ordinal: 90000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Steam account imports retain zero-total transaction facts. The ownership-price attribution pass skips totals at or below zero. Decide whether an eligible single-item, non-refunded Purchase with a known zero total should fill an unknown ownership acquisition price, while keeping zero distinct from missing evidence.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 State the chosen ownership-price attribution policy in the current build specification.
- [ ] #2 Verify a parsed zero-total transaction is retained as zero regardless of the ownership attribution choice.
- [ ] #3 Verify the chosen ownership-price behavior and distinguish a known zero from an unknown price.
- [ ] #4 Preserve bundle, gift and refund exclusions and account-specific provenance; verify the resulting desktop/fullscreen display and acquisition CSV values.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: SteamAccountPageImportService.cs records parsed transactions through TransactionFact with row.Total?.Cents, but the ownership price pass skips total.Cents <= 0. SteamAccountPageParserTests verifies zero-price parsing. The remaining product choice concerns attribution, not dropping transaction rows.
<!-- SECTION:NOTES:END -->
