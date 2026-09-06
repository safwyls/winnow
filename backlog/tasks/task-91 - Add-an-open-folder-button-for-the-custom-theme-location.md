---
id: TASK-91
title: Add an open-folder button for the custom theme location
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 18:13'
updated_date: '2026-09-04 18:44'
labels:
  - ui
dependencies: []
priority: low
type: enhancement
ordinal: 118000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
AppearanceViewModel already exposes ThemeFolder (the user theme directory) and a reload command, but the path is only printed. A user who wants to add or edit a theme file has to copy the path and open it themselves. Put a button next to it that opens the folder in the shell, creating the directory first if it does not exist yet.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The Appearance screen has a button that opens the user theme directory in the OS file manager
- [x] #2 The directory is created if it is missing so the button never fails on a fresh install
- [x] #3 The button sits next to the existing path and reload control
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add an Open the folder button to the YOUR THEMES card in src/Winnow.App/Views/AppearanceView.axaml, in the same Horizontal StackPanel as Export this theme as a template and Re-read the folder, sitting beside the printed path.
2. Follow the launcher pattern already used by GameDetailsView.axaml.cs: TopLevel.GetTopLevel(this).Launcher.LaunchDirectoryInfoAsync, never Process.Start.
3. Create the directory first when it is missing (fresh install), via a small view-model helper that returns the prepared DirectoryInfo or null and reports failure through the existing ThemeActionStatus line.
4. dotnet build and dotnet test -p:BaseOutputPath=C:\Temp\winnow-a1\.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The YOUR THEMES card now carries a third button, 'Open the folder', in the same horizontal row as 'Export this theme as a template' and 'Re-read the folder', directly under the printed path (src/Winnow.App/Views/AppearanceView.axaml).

It goes through Avalonia's platform launcher — TopLevel.GetTopLevel(this).Launcher.LaunchDirectoryInfoAsync — which is the same entry point GameDetailsView.axaml.cs already ships for a game's install directory. Nothing shells out to explorer.exe.

The handler lives in src/Winnow.App/Views/AppearanceView.axaml.cs because a view model cannot reach the window's launcher. It asks the view model for the directory first: AppearanceViewModel.PrepareThemeFolder() returns null when no UserThemeStore is registered (nothing to open, and HasThemeFolder is false so the card is hidden anyway), otherwise calls Directory.CreateDirectory on the theme folder and hands back the DirectoryInfo. Creating rather than testing for existence is what makes the button work on a fresh install, where the path exists but the folder has not been seeded yet. An IO or permission failure is reported on the existing ThemeActionStatus line rather than thrown, and a launcher that refuses is swallowed — the path is printed right beside the button.

All prose (button label, doc comments, status string, test summaries) was authored by the docs-writer agent.

Verification. Two tests added to tests/Winnow.Tests/UserThemeStoreTests.cs: Preparing_the_theme_folder_creates_it_when_it_is_missing (asserts the folder is absent, then present after the call, that the returned DirectoryInfo names it, and that nothing was written to the status line) and Preparing_the_theme_folder_reports_nothing_when_there_is_no_folder. UserThemeStoreTests: 16 passed, 0 failed.

dotnet build -p:BaseOutputPath=C:\Temp\winnow-a1\ : 'Build succeeded. 0 Warning(s) 0 Error(s)'. The Click handler name is resolved by the XAML compiler, so the wiring is build-verified rather than asserted.

Not exercised: the launcher call itself, because running the app was excluded for this change. It is one line, and it is the same ILauncher.LaunchDirectoryInfoAsync call already shipping on the game detail modal's open-folder button.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added an 'Open the folder' button beside the printed theme path and the reload control on Settings > Appearance. It creates the user theme directory when missing, then opens it through Avalonia's platform launcher, the same entry point the game detail modal already uses for an install directory; failures land on the existing status line rather than throwing. Verified by two new tests in UserThemeStoreTests (16 passed, 0 failed) covering directory creation and the no-store case, and by dotnet build with 0 warnings and 0 errors, which also resolves the Click handler wiring. The shell launch itself was not clicked, since running the app was out of scope for this change.
<!-- SECTION:FINAL_SUMMARY:END -->
