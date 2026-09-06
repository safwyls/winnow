---
id: TASK-144
title: Stop the screenshot lightbox from crashing Winnow at startup
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 19:15'
updated_date: '2026-09-06 19:22'
labels:
  - ui
dependencies: []
modified_files:
  - src/Winnow.App/Views/ScreenshotLightboxView.axaml
  - src/Winnow.App/Themes/tokens.axaml
  - src/Winnow.App/Themes/WinnowTheme.cs
  - tests/Winnow.Tests/Enforcement/ScreenshotLightboxStructureTests.cs
  - tests/Winnow.Tests/ThemeContrastTests.cs
priority: high
type: bug
ordinal: 171000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Winnow terminates with System.AccessViolationException while Avalonia constructs ScreenshotLightboxView. Windows Event Viewer identifies the failing path as a BindingExpression created for a runtime binding inside the view deferred resource dictionary. The startup window must construct without native failure while the lightbox controls retain their theme-aware translucent fills.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 dotnet run constructs and keeps the Winnow window alive on .NET 10 instead of terminating with AccessViolationException
- [x] #2 Lightbox controls retain Surface at 70% at rest and SurfaceRaised at 85% on hover or keyboard focus in every theme
- [x] #3 The fix removes the runtime bindings from ScreenshotLightboxView deferred resources and regression coverage pins the safe token path
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Promote the two translucent lightbox fills into the global mutable brush-token dictionary and derive their colors per Winnow theme. 2. Replace the view-local bound brushes with static references to those theme-managed tokens and add structural and theme tests. 3. Build, run the focused tests, and launch against an isolated data directory long enough to confirm there is no Windows crash event.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced the two view-local runtime-bound brushes with global mutable theme tokens derived from each theme Surface and SurfaceRaised colors. Added structural coverage that forbids deferred runtime bindings and theme coverage for the 70% and 85% alpha values. Focused Winnow.Tests: 11 passed. Winnow.Ui.Tests: 20 passed; this suite previously terminated in MainWindow construction on the same binding path. Native isolated launch remained alive for 50 seconds and was then stopped manually.

Final verification: full solution build succeeded with 0 warnings and 0 errors. Full solution tests passed: Winnow.Covers.Tests 84, Winnow.Recommend.Tests 152, Winnow.Tests 3501, and Winnow.Ui.Tests 20; 3757 total, 0 failed. Event Viewer contained no Winnow error during the 50-second isolated native launch.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Moved the lightbox translucent control fills from crash-prone view-local runtime bindings into the application mutable theme-token set. The visual treatment remains Surface at 70% and SurfaceRaised at 85% across every theme. Verified by 3757 passing tests, a zero-warning solution build, and a 50-second native Windows launch with no crash event.
<!-- SECTION:FINAL_SUMMARY:END -->
