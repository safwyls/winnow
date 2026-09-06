---
id: TASK-25
title: Add persistent rolling diagnostics with redaction
status: Done
assignee:
  - '@steam-ingest'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 21:42'
labels:
  - infra
milestone: m-4
dependencies: []
priority: high
ordinal: 1100
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
There is no persistent diagnostic log. Post-hoc diagnosis of soft-failed paths (enrichment timeouts, cover fetch failures, malformed VDF) requires reproducing the conditions. Finding F41. Source: stabilization-2026-08-28.md Group 2. Trigger: next work that needs post-hoc diagnosis of a soft-failed path.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A rolling diagnostic log persists under the data directory
- [x] #2 The log redacts user-identifying information (steam ids, account names, file paths)
- [x] #3 Redaction is covered by tests
- [x] #4 Log rotation bounds total size
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add Serilog rolling file sink after data-dir resolution and single-instance guard, replacing host default providers. Persist through a privacy formatter: redact structured string/identity properties and scopes, scrub paths/Steam identifiers/secrets in message templates, record exception types and method frames without messages or source paths. Cap event bytes and retain five 1 MiB rolling files (bounded one-event overshoot). Test persistence, restart/rotation size bounds, structured/unstructured redaction and exception privacy. Parent owns documentation additions.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented Serilog host logging under selected data-dir/logs with privacy formatter, five 1 MiB rolling files and 8 KiB event ceiling. All 16 diagnostics tests passed (persistence, restart, rotation, scoped/structured values, raw paths/Steam identifiers/credentials and exception messages); all 8 RepositoryHygieneTests passed. App build passed with zero warnings/errors. Proposed build-spec paragraph sent to coordinator, who owns shared docs and integration review. No storefront files read or written.

Coordinator reviewed privacy formatter and documented logging policy in game-library-design.md section 8. Validation: 16 diagnostic tests, 8 fixture hygiene tests, and app build with zero warnings/errors.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added redacted Serilog diagnostics under the selected data directory. Five rolling files and an 8 KiB event cap bound disk usage; structured identities, strings, scopes, exception messages and paths are suppressed. Persistence, redaction, rotation and restart retention are covered by passing tests.
<!-- SECTION:FINAL_SUMMARY:END -->
