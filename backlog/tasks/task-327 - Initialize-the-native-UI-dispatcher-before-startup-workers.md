---
id: TASK-327
title: Initialize the native UI dispatcher before startup workers
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 15:24'
updated_date: '2026-09-17 15:28'
labels: []
dependencies: []
type: bug
ordinal: 369000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User reports rc.4 failing on Windows with PlatformNotSupportedException in Dispatcher.MainLoop after local sync completes. Startup workers can reach Dispatcher.UIThread before Avalonia platform initialization.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Startup workers cannot initialize a fallback dispatcher before the native platform is ready.
- [x] #2 Desktop and fullscreen startup pass regression coverage using isolated data.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Confirm the Avalonia initialization failure and shared startup boundary. 2. Start hosted services and background work only after native platform services are initialized. 3. Verify isolated startup and existing error boundaries; document the ordering.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Confirmed Avalonia 11.3.20 caches NullDispatcherImpl when UIThread is accessed before platform setup; MainLoop requires a controlled native dispatcher. Moved hosted services, local startup sync, and plugin startup inside AfterPlatformServicesSetup while preserving migration and terminal authentication ordering. Desktop and fullscreen were each launched with isolated data and no sync and checked for a responsive native message loop. Verification: app build passed with zero warnings/errors; 47 startup-related unit/process/enforcement tests passed; 23 desktop/fullscreen/startup-thread UI tests passed; git diff --check passed. Native smoke tests cover no-sync startup; the enforcement test protects ordering of real sync workers. No release package or installed application was changed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed the startup dispatcher race for both presentation modes. Native platform initialization now precedes workers that can publish UI changes. Added startup ordering and isolated native process regression coverage; all 70 focused tests passed.
<!-- SECTION:FINAL_SUMMARY:END -->
