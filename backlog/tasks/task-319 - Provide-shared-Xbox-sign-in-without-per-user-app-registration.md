---
id: TASK-319
title: Provide shared Xbox sign-in without per-user app registration
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 02:39'
updated_date: '2026-09-17 02:51'
labels: []
dependencies: []
priority: high
ordinal: 361000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Xbox sign-in currently asks every user to register a Microsoft application. The user approved maintaining one Winnow application registration and bundling its public client ID, while retaining a client-ID override only under advanced settings.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A validated Winnow-owned public application ID supplies the default Xbox sign-in identity; users do not register applications.
- [x] #2 The editable client-ID override is optional and appears only under advanced settings in desktop and fullscreen; clearing it restores the bundled identity.
- [x] #3 Account token and history isolation survives effective client-ID changes; focused tests cover default and override behavior.
- [x] #4 Documentation reflects the maintainer-owned registration, and an updated local client is built for end-to-end testing with an isolated profile.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect Microsoft registration access and verify the supported public-client device flow. 2. Add generic advanced plugin settings to both presentation paths. 3. Resolve the Xbox application ID from a bundled registration with an optional advanced override, preserving identity-scoped token/cache behavior. 4. Validate registration and sign-in where account access permits, run focused provider/host/UI checks, update documentation and publish a fresh isolated local test client.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Created the Winnow public application registration after the user accepted Microsoft Platform Policies. Personal Microsoft accounts only; Live SDK support and public-client flows enabled; no redirect URI or client secret. Microsoft consumers device-code endpoint returned HTTP 200 for XboxLive.SignIn XboxLive.offline_access. Bundled default and optional advanced override implemented; 104 provider tests pass, including clearing overrides and account/history isolation. Live authenticated-service probe pending user completion.

Live validation succeeded using the production Xbox plugin and host HTTP policy with the bundled ID and no override: Microsoft access token, Xbox user token and XSTS all HTTP 200; TitleHub and three UserStats requests HTTP 200. Returned 70 history entries (51 PC, 19 console), all with last-played timestamps; this run produced no playtime values. Probe used in-memory credentials/caches, removed its credential and exited without writing a library. Release solution build passed with 0 warnings/errors. Focused checks passed: 104 provider, 37 host/account/view-model, 19 desktop/fullscreen UI, and real Xbox ZIP/manifest load test.

Published self-contained Windows client 0.2.0-dev from source commit e8caab2647bc2e934e9d90a79bf9598581bcef73 to artifacts/xbox-shared-e2e/client, with the rebuilt Xbox plugin included for local testing. Launched PID 62076 using isolated artifacts/xbox-shared-e2e/profile; nonzero main window verified. Restart launcher and TESTING.txt are beside the client. Standalone Xbox ZIP is artifacts/xbox-shared-plugin/Winnow.Plugin.Xbox-1.0.0.zip. Live probe confirms registration and account history; the desktop client remains available for user testing of persistence, artwork and launch. No full-UI gate or live cumulative-minute/WindowsApps tracking claim.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Bundled the validated Winnow-owned Xbox public client ID and moved developer overrides into collapsed advanced settings on desktop and fullscreen. Clearing the override restores Winnow while preserving account/cache isolation. Live sign-in read 51 PC and 19 console entries, all with last-played dates. Release build and 161 focused tests passed. Built and launched an isolated local client; cumulative minutes and protected WindowsApps session behavior remain documented validation limits.
<!-- SECTION:FINAL_SUMMARY:END -->
