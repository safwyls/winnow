---
id: TASK-242
title: Show Steam spending by currency and add useful statistics charts
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 05:21'
updated_date: '2026-09-12 05:34'
labels: []
dependencies: []
ordinal: 283000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Treat Steam dollar credit amounts as USD and replace mixed-currency withholding with currency-specific totals. Add readable visualizations of captured purchase patterns on desktop Steam stats and fullscreen Activity library summary.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Dollar credit normalizes to USD alongside dollar purchases, including existing imports; distinct currencies retain separate accurate totals with no conversion or refund double counting.
- [x] #2 Desktop and fullscreen show per-currency spending and useful labeled charts with equivalent facts, accessible text and empty/partial capture handling.
- [x] #3 Regression tests cover currency handling and chart projections; both surfaces are rendered and checked; relevant docs updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Normalize dollar-credit currency labels at parse/read boundaries and compute separate currency aggregates without conversion. 2. Present currency-specific spending with yearly bars, purchase-kind composition and licence-acquisition charts on desktop and fullscreen, retaining accessible text and detailed rows. 3. Test normalization, refunds, unknown currencies, currency switching and charts; render both surfaces with representative mixed-currency data, inspect layout and fix issues. 4. Update the domain docs and run build/regression checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implementation uses query-time normalization of Steam dollar-credit labels, preserving existing fact fingerprints and idempotent imports. Currency groups reuse the stats aggregation shape within one read snapshot. Shared native dashboard planned for both surfaces; money charts select one currency and licence counts remain currency-independent.

Data verification: all 30 AccountStatsTests passed, including existing dollar-credit records, repeat-import fingerprints, currency-specific year/kind/refund/wallet/bundle/discount/biggest totals, missing currency, account overlap and ambient read transaction behavior. Read-only data review found no actionable issues.

Final verification: full solution build succeeded with 0 warnings/errors. Root dotnet test filter AccountStats, FullscreenActivityTests and DesignTimePreviewTests passed 75 tests (42 data/viewmodel and 33 UI). Captures inspected at desktop 600x900 and1200x900, fullscreen1920x1080, and isolated fullscreen1280x1080 with the shell-equivalent140% text adjustment. Overview, currency switching and lower charts inspected; no horizontal overflow. Captures: C:\Temp\winnow-stats-captures. Controller Right/Accept and desktop picker focus checked headlessly. Physical controller/TV distance not tested. Read-only review findings about negative charts, zero years and global count labeling were fixed and re-reviewed without residual findings.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Steam dollar-credit records now group with dollars without reimport or fingerprint changes. Known currencies retain separate totals and detailed amounts; unknown currencies retain counts. Added shared responsive yearly signed bars, spend-composition donut, licence acquisition bars, highest recorded year and average kept transaction on desktop/fullscreen. Preserved refund/wallet and account-overlap safeguards. Clean full build, 75 targeted regression tests and rendered visual QA passed.
<!-- SECTION:FINAL_SUMMARY:END -->
