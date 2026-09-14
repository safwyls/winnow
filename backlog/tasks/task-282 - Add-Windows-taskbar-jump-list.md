---
id: TASK-282
title: Add Windows taskbar jump list
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 02:57'
updated_date: '2026-09-14 03:10'
labels: []
dependencies: []
ordinal: 324000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Offer recently played games and fullscreen entry from the Windows taskbar like the supplied reference.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Windows taskbar shows up to ten launchable recently played games and a Switch to Fullscreen Mode task.
- [x] #2 Actions work with an existing session and cold startup without opening duplicate windows; normal activation remains compatible.
- [x] #3 Jump list refreshes from library data, respects removed destinations and isolated data directories, and non-Windows startup remains unaffected.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Implement Windows shell jump-list adapter, extend bounded single-instance activation messages for fullscreen/game launch, wire UI readiness and library refresh, test routing and native publishing with isolated data.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented native Windows Recently Played (up to ten launchable titles) and Switch to Fullscreen Mode tasks. Both desktop and fullscreen use shared activation and launch commands; cold game activation waits for library/settings readiness. Windows-only adapter soft-fails and uses isolated data-directory identities. Removed destinations persist across refreshes. Entries currently use the Winnow app icon. Verification: 19 targeted tests passed, including real Windows shell publish/refresh/delete under an isolated identity and subprocess normal/fullscreen/game forwarding. Full UI suite: 691 passed, two stale six-card feed assertions failed; corrected those to the desktop five-card projection and all eight FeedImpressionTests then passed. No production library or native visual inspection was used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added Windows taskbar recent-game launching and fullscreen entry, routed through the existing session or cold startup. Verified native shell publishing and activation with 19 targeted tests; UI regression coverage passed after correcting two older feed expectations. Documented behavior and current app-icon limitation.
<!-- SECTION:FINAL_SUMMARY:END -->
