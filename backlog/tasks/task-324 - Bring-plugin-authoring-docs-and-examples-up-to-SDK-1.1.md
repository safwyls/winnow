---
id: TASK-324
title: Bring plugin authoring docs and examples up to SDK 1.1
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-17 05:18'
updated_date: '2026-09-17 05:26'
labels: []
dependencies: []
ordinal: 366000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Website SDK docs and examples still describe SDK 1.0, omit account and game-action contracts, and call secrets read-only. The old package reference cannot restore against a freshly packed current SDK.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Website SDK reference and walkthrough match current contracts, settings, secrets, installation and refresh behavior on both UI surfaces.
- [x] #2 Downloadable and documented examples reference SDK 1.1 and build against a freshly packed local package.
- [ ] #3 Website and example validation passes and fixes are committed and pushed to PR 22.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify SDK and host behavior. 2. Update guide, walkthrough and sample together. 3. Build both documented examples and the website. 4. Record evidence, commit and push PR 22.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Updated SDK reference and walkthrough against PluginContracts, PluginInteractionContracts, PluginManifestReader, PluginStorage, PluginSettingsBackend, PluginSettingsViewModel, PluginGameActionService and startup/disposal code. Both desktop/fullscreen shared controls are documented. Corrected sample startup so seeding is followed by normal discovery-enabled startup. Packed SDK 1.1.0 into an isolated local feed; restored both examples with an empty private NuGet cache and built Release with warnings as errors (zero warnings/errors). Extracted walkthrough project/source/manifest verbatim; verified website snippets equal downloadable samples, and both outputs include DLL/deps/manifest without a private SDK copy. TypeScript, normal framework build, and Pages build with six release tests plus route/link checks passed. Sites wrapper hit its known local npm launcher issue; direct npm build passed. No application code changed or real library/credentials used.
<!-- SECTION:NOTES:END -->
