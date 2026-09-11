---
id: TASK-187
title: Explain the plugin system through a beginner walkthrough
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 04:00'
updated_date: '2026-09-11 04:08'
labels: []
dependencies: []
type: docs
ordinal: 218000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Explain how Winnow implements plugins and teach the reusable design principles through the SteamGridDB example, with source references and a small verifiable example.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The walkthrough explains contracts, discovery, loading, host services, settings and all four capabilities in plain language grounded in current code.
- [x] #2 A SteamGridDB trace and a small compiling plugin example show how the pieces connect and how to build a first plugin.
- [x] #3 Failure behavior, trust, compatibility and current limits are explicit; source links and example build are verified.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect the implementation and governing architecture, write a teaching companion to docs/plugins.md, compile its example against SDK 1 in a temporary directory, validate links and facts, and link the walkthrough from the existing guide.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Created docs/plugin-system-walkthrough.md as a beginner teaching companion, with two Mermaid diagrams, a SteamGridDB request trace, a complete metadata plugin example, source links and explanations of contracts, loading, versioning, storage, settings, adapters and failure boundaries. Read-only domain review found no concrete factual errors. Extracted the exact C#/XML/JSON fences, packed SDK 1 locally, restored and built the sample with zero warnings/errors, and ran it through the real PluginCatalog in a temporary console harness. Verified disabled discovery, restart-based activation, host SDK sharing, default/changed settings and unavailable input. Checked all 23 local documentation links, balanced fences and git diff --check. No production app or personal library was opened. Corrected the existing guide to distinguish transient feed results and retained covers from persisted background/screenshot observations; recorded the prior wording in docs/decisions.md. No application behavior changed; desktop and fullscreen are explained from their existing implementations and interaction tests.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a source-grounded plugin-system walkthrough with diagrams, a SteamGridDB trace and a complete metadata provider exercise. Verified the exact example by compiling and loading its separate DLL with the real runtime, checked 23 documentation links, and completed a factual review against the code.
<!-- SECTION:FINAL_SUMMARY:END -->
