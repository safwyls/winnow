---
id: TASK-153
title: Add tray minimization and boot startup settings
status: Done
assignee:
  - '@codex'
created_date: '2026-09-07 22:20'
updated_date: '2026-09-07 22:45'
labels: []
dependencies: []
references:
  - design-system.md
  - game-library-design.md
modified_files:
  - src/Winnow.App/App.axaml
  - src/Winnow.App/App.axaml.cs
  - src/Winnow.App/Design/PreviewData.cs
  - src/Winnow.App/Program.cs
  - src/Winnow.App/Services/StartupRegistration.cs
  - src/Winnow.App/ViewModels/ApplicationSettingsViewModel.cs
  - src/Winnow.App/ViewModels/LibrarySettingsCopy.cs
  - src/Winnow.App/ViewModels/LibrarySettingsViewModel.cs
  - src/Winnow.App/ViewModels/MainWindowViewModel.cs
  - src/Winnow.App/Views/ApplicationSettingsView.axaml
  - src/Winnow.App/Views/ApplicationSettingsView.axaml.cs
  - src/Winnow.App/Views/MainWindow.axaml
  - src/Winnow.App/Views/MainWindow.axaml.cs
  - tests/Winnow.Tests/ApplicationSettingsViewModelTests.cs
  - tests/Winnow.Ui.Tests/TrayWindowInteractionTests.cs
  - tests/Winnow.Ui.Tests/DesignTimePreviewTests.cs
  - design-system.md
  - docs/decisions.md
priority: medium
type: feature
ordinal: 180000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Winnow can remain available from the Windows notification area, optionally hide there when minimized or closed, and optionally start with Windows. Users manage these behaviors from Settings.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A user can keep Winnow running in the notification area and restore or exit it from the tray menu
- [x] #2 Settings expose and persist notification-area behavior without changing the current default window behavior for existing users
- [x] #3 A user can enable or disable starting Winnow with Windows, and the OS startup registration reflects the saved setting
- [x] #4 Automated tests cover settings persistence, window/tray transitions, and startup registration behavior
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add an Application settings model backed by the existing settings table and a platform seam for per-user Windows startup registration. Keep all new behaviors off by default. 2. Add a fourth Settings > Application pane with independent minimize-to-tray, close-to-tray, and start-with-Windows controls plus honest unsupported/error states. 3. Wire Avalonia's application-level TrayIcon to the main window, including restore, explicit exit, hidden autostart, and taskbar visibility transitions. 4. Add unit and headless UI coverage, then run targeted tests, the full test suite, migration verification, and a build.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented an application-level Avalonia TrayIcon with Open Winnow and Exit actions. MainWindow now hides to the tray on minimize or close when the corresponding opt-in setting is enabled, restores to a normal taskbar window, and honors --background for quiet startup. Added Settings > Application with persisted tray preferences and a per-user HKCU Run registration behind IStartupRegistration; unsupported systems disable the startup toggle. Defaults remain off. Updated the visual spec and decision record.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added opt-in minimize-to-tray and close-to-tray behavior, tray restore/exit controls, and per-user Start with Windows support that launches quietly with --background. Added the Settings > Application UI and automated persistence, registration, preview, and window-transition coverage. Verified 3,958 passing tests, 2 expected non-Linux skips, a clean root build, 28 migration hashes, diff checks, and an isolated native background-start smoke.
<!-- SECTION:FINAL_SUMMARY:END -->
