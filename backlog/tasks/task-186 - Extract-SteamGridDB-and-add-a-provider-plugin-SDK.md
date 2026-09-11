---
id: TASK-186
title: Extract SteamGridDB and add a provider plugin SDK
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-11 03:17'
updated_date: '2026-09-11 03:53'
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
- [ ] #1 A versioned public SDK and validated local plugin loader support library, metadata, artwork and recommendation providers without exposing App internals.
- [ ] #2 SteamGridDB ships as a separate loadable plugin with no provider-specific App registration, retaining credentials, cache and artwork behavior.
- [ ] #3 Installed plugins can be enabled and configured through generated settings on desktop and fullscreen; secrets remain protected and source preference order accepts plugin artwork.
- [ ] #4 All four capability types execute through validated host adapters, preserve core identity and ownership rules, and feed recommendations remain explained and limited to owned eligible games.
- [ ] #5 Invalid or incompatible plugins fail visibly without preventing startup; third-party code requires explicit enablement and diagnostics explain the trusted in-process execution model.
- [ ] #6 Tests exercise loading a separate assembly and all capability pipelines; SDK authoring, package installation and compatibility are documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Publish the existing branch as a baseline. Add BCL-only SDK contracts, a manifest-based trusted in-process loader and scoped host services. Generate desktop/fullscreen settings from plugin declarations. Extract SteamGridDB to a separately packaged plugin and preserve existing stored configuration. Integrate capability registries into background sync, artwork selection and recommendation shelves. Verify separate-assembly fixtures, temporary-data pipelines, settings interactions and publish packaging; document trust, API versioning and initial scope.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented API 1 in the BCL-only Winnow.PluginSdk and a validated local loader in Winnow.Plugins. Plugins implement library inventory, metadata, artwork and recommendation capabilities through scoped settings, protected secrets, cache and bounded HTTP services. Third-party packages start disabled; activation requires restart. Manifest/load/provider failures produce safe diagnostics without exposing exception text.
<!-- SECTION:NOTES:END -->
