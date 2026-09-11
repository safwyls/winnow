---
id: TASK-43
title: Polish account stats presentation
status: To Do
assignee: []
created_date: '2026-08-29 21:55'
updated_date: '2026-09-11 14:03'
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
- [ ] #1 Visual hierarchy clearly distinguishes primary figures from breakdowns.
- [ ] #2 Implement at least two derived figures with documented inputs and limits; do not present mixed-currency or ambiguous-account totals as a valid calculation.
- [ ] #3 Remain correct with no account-page data, partial imports, missing prices and zero denominators.
- [ ] #4 Verify the statistics presentation on desktop and fullscreen; shared calculations agree and each surface remains readable.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: AccountStatsViewModel and AccountStatsView show counts/totals; proposed derived averages and ratios remain absent. FullscreenActivityPage supplies the TV presentation. TASK-38's acquisition CSV is delivered and does not gate this presentation work. Any cost-per-hour or unplayed-spend figure requires justified matching and scope before implementation.
<!-- SECTION:NOTES:END -->
