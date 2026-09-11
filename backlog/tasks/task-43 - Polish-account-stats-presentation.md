---
id: TASK-43
title: Polish account stats presentation
status: Done
assignee:
  - '@codex-ui'
created_date: '2026-08-29 21:55'
updated_date: '2026-09-11 18:57'
labels:
  - ui
  - data
dependencies: []
documentation:
  - design-system.md
  - game-library-design.md
priority: low
ordinal: 169000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Improve the account-statistics hierarchy and add at least two useful derived figures, such as a safely defined transaction/year average or percentage breakdown. Current views already show counts and monetary totals. Define each new figure from attributable evidence, preserving currency, account, wallet and refund distinctions instead of assuming every aggregate can be combined.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Visual hierarchy clearly distinguishes primary figures from breakdowns.
- [x] #2 Implement at least two derived figures with documented inputs and limits; do not present mixed-currency or ambiguous-account totals as a valid calculation.
- [x] #3 Remain correct with no account-page data, partial imports, missing prices and zero denominators.
- [x] #4 Verify the statistics presentation on desktop and fullscreen; shared calculations agree and each surface remains readable.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a primary captured net-spend figure and two shared percentages over product transactions with recorded prices: refunded original purchases / gross product transactions; bundle purchases / non-refunded product transactions. Both populations already share the priced-row filter. Exclude wallet and standalone reversals; withhold percentages for ambiguous account overlap or zero denominators. 2. Present primary figures before breakdowns on desktop/fullscreen with captured-data scope and missing-price limits. 3. Verify empty, partial, missing-price, mixed-currency and ambiguous-account cases plus both rendered surfaces; commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: AccountStatsViewModel and AccountStatsView show counts/totals; proposed derived averages and ratios remain absent. FullscreenActivityPage supplies the TV presentation. TASK-38's acquisition CSV is delivered and does not gate this presentation work. Any cost-per-hour or unplayed-spend figure requires justified matching and scope before implementation.

Added primary net spend and shared refunded/bundle percentages ahead of detailed breakdowns. Repository inspection established both ratio denominators and numerators require recorded product prices; missing-price rows are excluded rather than mixing incompatible populations. Wallet credit/standalone reversal rows excluded. Mixed currencies retain counts only; unknown/identified-account overlap and zero denominators withhold ratios. AccountStatsSummaryTests: five passed using temporary SQLite facts and both rendered surfaces, covering missing prices, wallet and refund rows, partial/empty/license-only shapes, mixed currencies and account ambiguity. Existing regression-suite build was temporarily blocked by concurrent AchievementEvidenceTests raw-string syntax; focused UI suite compiled and passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added readable primary statistics and two bounded transaction percentages on desktop/fullscreen. Five SQLite/headless tests verify calculations, currency/account limits and rendering.
<!-- SECTION:FINAL_SUMMARY:END -->
