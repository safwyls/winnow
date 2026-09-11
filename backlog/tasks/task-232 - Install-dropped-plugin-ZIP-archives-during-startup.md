---
id: TASK-232
title: Install dropped plugin ZIP archives during startup
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 15:32'
updated_date: '2026-09-11 15:38'
labels: []
dependencies: []
type: feature
ordinal: 264000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Users can put a plugin ZIP in the user plugins folder and restart Winnow to unpack and discover it automatically.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Startup imports root-layout and single-folder ZIP packages and discovers them under existing plugin enablement rules.
- [x] #2 Archive imports cannot escape the plugin directory, publish partial packages, or overwrite existing or bundled plugins.
- [x] #3 Successful archives are retained outside the input scan; invalid, oversized or conflicting archives remain intact and report useful diagnostics without blocking other plugins.
- [x] #4 Desktop and fullscreen explain ZIP installation and show shared discovery diagnostics; documentation and automated tests cover the lifecycle.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add a staged ZIP installer with path validation, bounded extraction, manifest validation and collision checks. Integrate it after existing package discovery. Keep successful source ZIPs in .archives and preserve failed inputs. Add installer and catalog regression tests, update shared UI copy and docs, and verify both settings presentations.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented PluginArchiveInstaller with root/single-wrapper layouts, atomic staged extraction, portable path and link rejection, duplicate-ID/destination protection, 2048-entry and 256 MiB compressed/uncompressed limits, and 1024-character/32-component path limits. Successful inputs move to .archives; failures retain source ZIPs; retention failure still exposes the installed plugin. Catalog scans existing directories before imports, preserves existing enablement semantics, and ignores archive/staging folders. Review identified excessive prefix-allocation risk for deeply nested paths; bounded paths before expansion and added regression cases. Verification: all 87 Winnow.Plugins.Tests pass, including 45 installer tests and catalog restart/collision/isolation tests; 22 PluginHostStorageTests/PluginSettingsViewModelTests pass; 8 PluginSettingsInteractionTests pass. Desktop and fullscreen both render ZIP installation help and shared archive diagnostics, with controller access to the error details verified. Updated copy captured and visually inspected on both surfaces. README, plugin guide, design and architecture docs updated; git diff --check passes. Tests used temporary data and scratch build outputs, with no production host or real library modifications.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Automatically unpack plugin ZIPs at startup, preserving source archives and existing installations. Added staged validation and extraction limits, shared desktop/fullscreen help and diagnostics, and lifecycle regression coverage. All 117 relevant tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
