---
id: TASK-93
title: Open patch notes in a contained webview instead of the system browser
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 18:14'
updated_date: '2026-09-04 22:49'
labels:
  - ui
dependencies: []
priority: medium
type: feature
ordinal: 120000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The "patched since you played" signal is the core of the product, but reading what actually changed throws the user out to their browser. Winnow already ships an embedded WebView2 for sign-in (src/Winnow.Auth.WebView). Reuse that surface to show a games patch notes in a contained panel, so reading an update does not leave the app.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A patched game offers a way to read its notes without leaving Winnow
- [x] #2 The notes open in a contained webview panel, not the system browser
- [x] #3 Navigation inside the panel is constrained to the storefront news origin; anything else opens externally
- [x] #4 The panel is dismissable and does not block the rest of the UI
- [x] #5 A game with no notes URL says so rather than opening an empty panel
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Winnow.Core/Reading/PatchNotesPolicy.cs — the security model as pure functions, built ON AuthFlowPolicy (same origin machinery as the Epic sign-in and the Steam page harvest, not a second mechanism). Two gates: an ENTRY gate (an https URL on one of Valve's news origins, under that origin's news path) decides whether a panel opens at all, and a NAVIGATION gate (origin in the allowlist) decides where it may go once open. Off-allowlist http(s) opens externally; a non-web scheme is blocked outright.
2. Winnow.Core/Reading/IPatchNotesReader.cs — the seam, beside IInteractiveAuthPrompt in spirit: Core-only contract, so no view model names a browser type.
3. Winnow.Auth.WebView/WebView2PatchNotesReader.cs — one non-modal owned window hosting WebView2Host. Non-modal so the library stays usable; a separate window rather than an in-window overlay because a hosted HWND paints over Avalonia content regardless of z-order (the same airspace constraint WebView2AuthPrompt records). Hardening: in-private profile under the --data-dir WebView2 root, no injected script, no host objects, no web messages, downloads cancelled, permissions denied, external-scheme launches cancelled, top-level AND frame navigations gated, popups gated.
4. Winnow.App: register the reader on the same profile root as the sign-in prompt; pass it through LibraryViewModel into GameDetailsViewModel; route the update row's Patch notes button and the links row through the reader first and fall back to the system browser when the policy refuses the URL.
5. AC5: a line in the updates block when the game is flagged or carries updates but no row has a readable notes URL. Copy by docs-writer.
6. Tests: PatchNotesPolicyTests (entry gate, navigation gate, popup gate, scheme refusals) plus a details view model test for the no-notes line.
7. docs-writer authors every string, comment and XML doc, plus a design-system.md section for the new surface and any docs/decisions.md append.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implementation landed (code; prose delegated to docs-writer).

New:
- src/Winnow.Core/Reading/PatchNotesPolicy.cs — two gates over AuthFlowPolicy. Entry: https, origin exactly one of store.steampowered.com / steamstore-a.akamaihd.net / steamcommunity.com / www.steamcommunity.com, path containing /news/ or /announcements. Navigation: allowlisted origin renders, about: allowed, any other http(s) address is cancelled and handed to the system browser, anything with no http(s) origin (data:, blob:, file:, javascript:, steam:, custom schemes) is blocked. Popups take the same decision; frames take a stricter one (off-allowlist frames are blocked, never opened externally).
- src/Winnow.Core/Reading/IPatchNotesReader.cs — Core-only seam, so no view model names a browser type.
- src/Winnow.Auth.WebView/WebView2PatchNotesReader.cs — one non-modal owned window hosting WebView2Host, in-private, profile at <data root>/WebView2/patch-notes so --data-dir carries it. Nothing injected, host objects off, web messages off, DevTools off, context menus off, script dialogs off, accelerator keys off, downloads cancelled, permissions denied, external-scheme launches cancelled. Script stays on.
- src/Winnow.Auth.WebView/PatchNotesServiceCollectionExtensions.cs.
- tests/Winnow.Tests/PatchNotesPolicyTests.cs.

Changed: Program.cs registers the reader on the sign-in prompt's profile root; LibraryViewModel passes it into GameDetailsViewModel; GameDetailsViewModel gains TryReadNotes plus the no-notes line; GameDetailsView routes both link handlers through the panel first and falls back to the launcher; controls.axaml gains Window.notes / DockPanel.notes / Border.notes-bar / .notes-problem; GameDetailsViewModelTests gains two tests for the no-notes line.

A window rather than an in-window overlay: a hosted browser HWND paints over Avalonia content regardless of z-order, so no Avalonia chrome could sit above it.

Verification (the app was deliberately not run — this session was instructed not to launch it):
- dotnet build -p:BaseOutputPath=C:\Temp\winnow-e1\ -m:1 -> Build succeeded, 0 Warning(s), 0 Error(s) (TreatWarningsAsErrors is on).
- dotnet test tests/Winnow.Tests --no-build -> Passed! Failed: 0, Passed: 3046, Skipped: 0, Total: 3046.
- dotnet test tests/Winnow.Covers.Tests --no-build -> Passed! 70/70.
- dotnet test tests/Winnow.Recommend.Tests --no-build -> Passed! 152/152.

AC3 checked on PatchNotesPolicyTests: every branch of the decision is exercised — the four allowlisted origins render, an off-allowlist http(s) address returns OpenExternally for both a top-level navigation and a popup, a non-web address (data:, blob:, file:, javascript:, steam:, ms-msdt:) returns Block, an off-allowlist frame returns Block rather than OpenExternally, and the port and host are part of the comparison (store.steampowered.com:8443 and store.steampowered.com.example.com are refused).
AC5 checked on two new GameDetailsViewModelTests: a game whose updates carry no readable page has HasNotesPage false, ShowNoNotesNote true and non-empty copy; a game with one carries the opposite.
AC1, AC2 and AC4 are implemented but left unchecked: proving them means opening the window and clicking, and the app was not run.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Patch notes now open in a contained WebView2 window rather than the system browser, hosted on the same runtime as the Epic consent and Steam sign-in windows. A separate top-level window rather than an overlay because a hosted native browser HWND paints over Avalonia content regardless of z-order. Two allowlist gates in PatchNotesPolicy, built on the existing AuthFlowPolicy so there is one origin mechanism in the app: an entry gate (https, one of four Steam origins compared as scheme+host+port, /news/ or /announcements in the path) and a navigation gate on NavigationStarting, FrameNavigationStarting and NewWindowRequested. Off-allowlist http(s) is handed to the system browser; non-http(s) schemes are refused outright rather than shelled out; off-allowlist subframes are blocked rather than externalised, a deliberate cost. Nothing is injected: no host objects, no web messages, no DevTools, downloads cancelled, permissions denied, in-private profile under the --data-dir root. Verified by PatchNotesPolicyTests over every branch including port and lookalike-host refusals, two GameDetailsViewModelTests for the no-notes line, and by the user exercising the panel in the running app (criteria 1, 2 and 4 — opening, containment and dismissal).
<!-- SECTION:FINAL_SUMMARY:END -->
