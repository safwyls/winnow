---
id: TASK-308
title: Use the Winnow dragon for Windows journal notifications
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 05:18'
updated_date: '2026-09-16 05:19'
labels: []
dependencies: []
type: enhancement
ordinal: 350000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Windows journal notifications currently request the stock information symbol and generic application tray icon. The user wants the Winnow dragon instead.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Native Windows journal notifications use the bundled transparent dragon for notification and temporary tray icons, with owned handles released on dismissal.
- [x] #2 Existing desktop/fullscreen activation and fallback behavior passes verification.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Load a native icon from the bundled ICO, set the custom balloon icon flags, preserve quiet delivery behavior, and validate native icon lifetime plus journal notification tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Native adapter now loads the bundled transparent dragon ICO at the Windows large-icon size, supplies it as both hIcon and hBalloonIcon, and sets NIIF_USER plus NIIF_LARGE_ICON while preserving no-sound and quiet-time flags. The owned icon is released after the shell entry is removed on dismissal. Uses the bundled asset rather than the window icon because desktop may replace its icon with selected-game artwork. Validation: 11 journal notification tests passed, including native icon decode/release and both presentation paths. Isolated Windows smoke check submitted successfully, received NIN_BALLOONSHOW, and routed synthetic activation to the callback without any journal write; logs in C:/Temp/winnow-308-smoke. Build passed with zero warnings/errors; git diff --check passed. Native flag contract: https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Windows journal notifications now use the Winnow dragon instead of the information symbol. Confirmed native delivery/activation and 11 notification tests.
<!-- SECTION:FINAL_SUMMARY:END -->
