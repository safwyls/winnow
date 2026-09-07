---
id: TASK-149
title: >-
  Avalonia previewer fails: WebView2 WPF wrapper in deps.json drags
  PresentationFramework
status: Done
assignee: []
created_date: '2026-09-07 02:30'
updated_date: '2026-09-07 03:41'
labels:
  - rider
  - previewer
  - webview2
dependencies: []
type: bug
ordinal: 176000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
AvaloniaRider's previewer (Avalonia.Designer.HostApp) reflects over the app dependency graph; Microsoft.Web.WebView2.Wpf.dll (unused, referenced unconditionally by the package's build/Common.targets) ends up in Winnow.deps.json, and the XAML xmlns resolver resolves its PresentationFramework 5.0.0.0 dependency, which is absent in a plain net10.0 Avalonia app. Fix by removing the wrapper Reference items in Winnow.Auth.WebView before ResolveAssemblyReferences; also retires the MSB3277 suppression.
<!-- SECTION:DESCRIPTION:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a target in Winnow.Auth.WebView.csproj that removes the WebView2 Wpf/WinForms wrapper Reference items before ResolveAssemblyReferences. 2. Rebuild and verify the wrappers are gone from output and Winnow.deps.json and MSB3277 is gone. 3. Retire the MSB3277 suppressions and stale comments in Winnow.Auth.WebView.csproj and Winnow.App.csproj; update docs/spikes/embedded-auth.md and append the superseded sentence to docs/decisions.md. 4. Run dotnet test.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verification: dotnet build of Winnow.App succeeds with 0 warnings (MSB3277 gone from both projects after retiring the suppressions). Winnow.deps.json in the scratch output contains 0 references to Microsoft.Web.WebView2.Wpf/.WinForms and 0 to PresentationFramework; output contains Core.dll and the three WebView2Loader RIDs only. dotnet test green: Winnow.Covers.Tests 94, Winnow.Recommend.Tests 155, Winnow.Tests 3644, Winnow.Ui.Tests 39, all passed. Docs updated: docs/spikes/embedded-auth.md §2(b) and the dependency-cost table corrected, superseded sentences appended to docs/decisions.md. Limitation: the Rider previewer itself was not exercised (no interactive Rider session from here); the fix removes the assembly whose missing dependency caused the reported FileNotFoundException.

Follow-up: user still saw MSB3277. Cause: the WebView2 package ships its targets under buildTransitive/, so every project transitively referencing Winnow.Auth.WebView (App, test projects) imports them and reproduced the wrapper references in its own RAR graph. Moved the removal into a new repository-wide Directory.Build.targets DropWebView2UiWrappers target (BeforeTargets=ResolveAssemblyReferences) and removed the csproj-local copy. Also fixed the removal itself: Remove with a ** glob matched nothing against the package's absolute-path ItemSpecs; the target now removes by identity with an EndsWith condition. Full-solution build: zero MSB3277, zero wrapper DLLs in any output, app deps.json clean. Full test suite green (3932). docs/spikes/embedded-auth.md §2(b) updated; superseded entry appended to docs/decisions.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed the unused WebView2 WPF/WinForms wrapper references in every project via a repository-wide DropWebView2UiWrappers target in Directory.Build.targets (BeforeTargets=ResolveAssemblyReferences, identity-based removal because globs do not match the package's absolute-path ItemSpecs). This stops Microsoft.Web.WebView2.Wpf.dll from reaching any project's output or deps.json, where Avalonia's previewer reflected over it and failed resolving PresentationFramework 5.0.0.0, and retires both MSB3277 suppressions. Verified by full-solution build (0 warnings, no wrapper DLLs, clean Winnow.deps.json) and the full test suite (3,932 tests passed). Docs updated in docs/spikes/embedded-auth.md and docs/decisions.md.
<!-- SECTION:FINAL_SUMMARY:END -->
