---
id: TASK-44
title: Resolve day-resolution transaction collapse
status: Done
assignee: []
created_date: '2026-08-29 21:55'
updated_date: '2026-09-11 13:59'
labels:
  - data
dependencies: []
priority: low
ordinal: 94000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Identical reported Steam transaction facts within the same source/account deduplicate to make repeated imports idempotent. If two genuine transactions have identical captured fields on the same day, the source data cannot distinguish them. This limitation is explicitly accepted and tested; transactions from different known accounts remain separate.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Either the schema supports distinguishing same-day identical transactions, or the limitation is documented and tested as accepted behavior
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: migration 0014 documents identical same-day collapse as accepted behavior. AccountStatsTests.A_transaction_that_differs_in_one_reported_value_is_a_different_fact verifies exact repeat deduplication and differing amounts. Migration 0035 and SteamAccountPageProvenanceTests preserve identical receipts across distinct accounts. Targeted account/provenance/backup run passed all 40 tests on September 11.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Satisfied the task's documented-and-tested alternative. Same-account identical captured facts deduplicate; distinct accounts remain separate. The source-data limitation remains documented, with no unimplemented schema change required by this task.
<!-- SECTION:FINAL_SUMMARY:END -->
