---
id: TASK-186
title: Extract SteamGridDB and add a provider plugin SDK
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 03:17'
updated_date: '2026-09-11 04:08'
labels: []
dependencies: []
ordinal: 217000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Make Winnow extensible through installed local plugins for game library sources, metadata, artwork and recommendation feeds. Ship SteamGridDB as a separate plugin using the same public SDK. Custom screens and UI replacement are outside the initial scope.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A versioned public SDK and validated local plugin loader support library, metadata, artwork and recommendation providers without exposing App internals.
- [x] #2 SteamGridDB ships as a separate loadable plugin with no provider-specific App registration, retaining credentials, cache and artwork behavior.
- [x] #3 Installed plugins can be enabled and configured through generated settings on desktop and fullscreen; secrets remain protected and source preference order accepts plugin artwork.
- [x] #4 All four capability types execute through validated host adapters, preserve core identity and ownership rules, and feed recommendations remain explained and limited to owned eligible games.
- [x] #5 Invalid or incompatible plugins fail visibly without preventing startup; third-party code requires explicit enablement and diagnostics explain the trusted in-process execution model.
- [x] #6 Tests exercise loading a separate assembly and all capability pipelines; SDK authoring, package installation and compatibility are documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Publish the existing branch as a baseline. Add BCL-only SDK contracts, a manifest-based trusted in-process loader and scoped host services. Generate desktop/fullscreen settings from plugin declarations. Extract SteamGridDB to a separately packaged plugin and preserve existing stored configuration. Integrate capability registries into background sync, artwork selection and recommendation shelves. Verify separate-assembly fixtures, temporary-data pipelines, settings interactions and publish packaging; document trust, API versioning and initial scope.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented API 1 in the BCL-only Winnow.PluginSdk and a validated local loader in Winnow.Plugins. Plugins implement library inventory, metadata, artwork and recommendation capabilities through scoped settings, protected secrets, cache and bounded HTTP services. Third-party packages start disabled; activation requires restart. Manifest/load/provider failures produce safe diagnostics without exposing exception text.

Completion evidence from implementation commit 7b10b88: all four capability adapters and separately packaged SteamGridDB passed real-assembly integration checks; generated desktop and fullscreen settings passed interaction tests; credentials, cache, artwork, source order and provenance migration are covered. Root build passed with 0 warnings/errors and root tests passed 4,635 cases, with 2 Linux-only skips on Windows. TRX output: C:\Temp\winnow-plugins-186-results (plugins-verified prefix). All 32 migration hashes passed verification. Release framework-dependent publish included the separate DLL/manifest, its real-loader smoke test passed, and SDK packaging produced Winnow.PluginSdk.1.0.0.nupkg. Publishing with app AssemblyVersion 9.8.7.6 retained SDK AssemblyVersion 1.0.0.0. No authenticated live request or installer smoke was performed. The prior multiline CLI finalization retained only its first paragraph; this update restores the completion record and acceptance checks for that already verified implementation.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented SDK 1 and all four provider pipelines, extracted SteamGridDB into the bundled plugin, migrated its saved data, and generated desktop/fullscreen settings and artwork ordering. Verified 4,635 passing tests, 32 migration hashes, Release publishing, published-plugin loading and stable SDK packaging/versioning; 2 Linux-only checks skipped on Windows.
<!-- SECTION:FINAL_SUMMARY:END -->
