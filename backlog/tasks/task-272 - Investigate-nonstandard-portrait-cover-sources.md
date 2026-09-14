---
id: TASK-272
title: Investigate nonstandard portrait cover sources
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 22:08'
updated_date: '2026-09-13 22:11'
labels: []
dependencies: []
ordinal: 314000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Determine why cached game covers differ from the standard portrait ratio and require color padding.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Measure cached portrait dimensions and identify representative affected games.
- [x] #2 Trace source and cache behavior and report verified causes and potential remedies.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Audit source dimensions and read-only library metadata, trace fallback selection, verify representative upstream assets, and record findings.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audited 910 Steam-keyed and 305 IGDB-keyed source image headers without changing cache or database. Found 141 and 297 respective ratio mismatches (>0.025 from 2:3). Eight sampled Steam IDs split into four valid 600x900 portraits at published hashed paths and four without advertised library capsule paths. Successful fallback cache entries never refresh. Documented measured evidence, limits, desktop crop/fullscreen fit differences, and potential remedy in docs/spikes/2026-09-13-cover-aspect-audit.md. Investigation only; no runtime code change or test run needed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Confirmed legacy-only Steam URL lookup overlooks available hashed portrait assets; IGDB fallback preserves differing ratios and successful fallbacks persist indefinitely. Four upstream 600x900 examples verified; findings recorded with reproducible probes.
<!-- SECTION:FINAL_SUMMARY:END -->
