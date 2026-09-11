---
id: TASK-228
title: Simplify repository documentation around current choices
status: Done
assignee:
  - codex
created_date: '2026-09-11 13:33'
updated_date: '2026-09-11 13:46'
labels: []
dependencies: []
type: docs
ordinal: 260000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Remove precedence chains, decision reversals and stale claims from current reading paths while preserving useful evidence.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Current docs state present choices without decision-history traversal
- [x] #2 Architecture UI scope and entry-point claims checked against source and task evidence
- [x] #3 Historical material separated and references and paired instructions checked
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inventory docs; audit specifications and supporting material; rewrite current paths; verify claims links and paired instructions.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reviewed all 74 tracked Markdown documents outside Backlog, six Codex role files and documentary HTML entry points. Backlog execution history and generated build copies are not current specifications. Current choices and rationale now live together; Git preserves earlier decision and plan text. Dated reports, probes and mockups are labeled evidence. Corrected source-verified drift in identity links, GamesDB resolution, Steam connections and collections, installation, metadata fixtures, artwork, achievements and CI triggers. Desktop and fullscreen documentation were reviewed separately; application behavior and visual assets are unchanged. Documentation enforcement covers both agent formats and provider/release guides; repository scans exclude generated artifacts. Verification: 100 enforcement tests passed after building their project dependencies, local links resolve in all 74 documents, six agent pairs and both skill/diagram copies match, and git diff --check passes. Full application tests, new visual captures, live provider checks and hardware validation were not required for these documentation changes. Preserved pre-existing ApplicationSettingsView.axaml changes, local settings, TASK-180 and docs/api.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Simplified current specifications, README, roadmap and agent guidance; removed duplicate decision narratives and competing mockup specs; retained dated evidence and Git history. Verified 100 enforcement tests, all tracked Markdown local links, paired guidance and diff hygiene.
<!-- SECTION:FINAL_SUMMARY:END -->
