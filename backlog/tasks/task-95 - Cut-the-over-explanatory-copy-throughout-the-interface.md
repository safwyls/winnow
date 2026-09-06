---
id: TASK-95
title: Cut the over-explanatory copy throughout the interface
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 18:14'
updated_date: '2026-09-04 21:56'
labels:
  - ui
  - docs
dependencies: []
priority: high
type: bug
ordinal: 122000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The UI explains itself at essay length in places where a few words would do. The explanations belong in the design documents, not on screen; a user reading a settings panel wants the control, not the reasoning behind it. Sweep the user-facing surfaces and reduce every explanatory blurb to a short, unambiguous phrase — a few words, not a paragraph. Empty states, tooltips and error copy that carry real information stay; the justifications go. The transparency block is the worst offender and is tracked separately.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 No user-facing explanatory blurb runs longer than a short phrase unless it carries information the user cannot get elsewhere
- [x] #2 Controls keep a label and, where useful, one short tooltip — not a paragraph of rationale
- [x] #3 Removed rationale that is still true is preserved in the governing design document rather than deleted outright
- [x] #4 design-system.md copy guidance reflects the shortened standard
- [x] #5 Build succeeds and no test regresses
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Survey every user-facing string in src/Winnow.App/Views and src/Winnow.App/ViewModels (the *Copy.cs classes, StoresViewModel, AppearanceViewModel, MainWindow.axaml's Display flyout) and the option 'Reason' strings in src/Winnow.App/Themes. Classify each: cut (rationale, restatement of the control, design-doc reasoning), keep (errors, empty states, consent, connection state, defaults, destructive consequences, automation names), or keep-the-fact-cut-the-reasoning.
2. Delegate every replacement string to docs-writer, one agent per non-overlapping file set, passing the fact each shortened string must still carry. Nothing is reworded by hand.
3. Verify the surfaces that landed today (LibrarySettingsCopy, GameIgdbMatchCopy, FetchStatusCopy, ExpansionCopy, GameListsCopy, MergeCopy, the patch-notes panel) against the short standard rather than rewriting them; leave their error and status copy alone.
4. Preserve the removed-but-still-true rationale: the account-stats honesty rules go into game-library-design.md 4.7; design-system.md 7 gains the shortened copy standard and its 10.4 row for the empty summary is corrected to the shipped string. Every sentence a document used to say is appended to docs/decisions.md. All of that prose is docs-writer's too.
5. No behaviour, binding, control-structure or layout change: string literals and their doc comments only.
6. Verify with dotnet build -p:BaseOutputPath=C:\Temp\winnow-g1\ -m:1, then dotnet test per project with --no-build against the same output path, from PowerShell.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Copy pass over src/Winnow.App/Views, src/Winnow.App/ViewModels and the option-card descriptions in src/Winnow.App/Themes. Every replacement string was authored by the docs-writer subagent across six delegations partitioned by file so none overlapped; nothing was reworded by hand. No binding, control, layout or behaviour changed — string literals and their doc comments only.

CUT (rationale, restatement, design-doc reasoning that leaked onto the screen):
- AccountStatsCopy: seventeen strings. Every *Note lost its argument and kept its fact — the bundle per-game argument, the wallet double-counting argument, the discount list-price explanation, the licence vocabulary note, the refund row mechanics, the day-resolution aside, the currency non-conversion explanation.
- SteamConnectionCopy and StoresViewModel: twenty-four strings, including SectionIntro, SignInGives, ApiKeyGives, AccountScopeMessage, AccountScopeCaveatMessage and the Epic/GOG local-file lines.
- SteamAccountImportCopy: eleven strings. The two transparency paragraphs were halved without losing a consent fact; the route-comparison clauses went, which also removed three places where one import route read as a fallback for the other.
- MainWindow.axaml: the four Display-preference paragraphs, each now a few words; the settings gear and appearance tooltips.
- AppearanceView.axaml, AppearanceViewModel: the slider tooltip, the unavailable notice, IntroMessage, the export status, the Mica substitution note.
- WinnowBackdrop, WinnowLayout, WinnowThemes: the backdrop, layout and four theme option-card descriptions, each cut from two sentences to one short phrase.
- MergeCopy: three of the five section blurbs lost their second sentence.
- FeedCardView: the countdown ring's tooltip, and its automation name, which was a 100-character sentence and is now a name.

KEPT (information available nowhere else): every error naming what failed and what to do; every empty state, including the first-run scan promise design-system.md 7 quotes verbatim; the Steam and Epic consent surfaces; every connection-state and session-health string; the stated defaults on explicit content, expansion grouping, non-game entries and the journal prompt; the delete confirmation that names what survives; every automation name; the user-theme contrast report on the Appearance screen, whose figures are measured per theme and exist nowhere else; and the theme-file diagnostics in ThemeJson and ThemeAudit, which are errors addressed to someone editing a JSON file.

OVER-CUT AND RESTORED: three tests assert rules about copy rather than wording, and they were treated as authority. FeedViewModel's two confidence notes got their improvement promise back (both were already short; the cut was unnecessary). SteamConnectionCopy.BothCredentials got back the reason keys carry the scheduled work — that they do not expire. SteamConnectionCopy.SignInCosts got back all three facts its test requires: renewal is automatic, it may not work against live servers, and an API key does not expire. No test assertion was edited.

RATIONALE PRESERVED: game-library-design.md 4.7 gained a new subsection carrying the twelve account-stats honesty rules the screen used to state in paragraphs; 6.4 gained the non-game filter's absence rule, which had only ever been stated in the Display flyout. The backdrop, layout and theme rationale already lived in design-system.md 14.6, 15 and 14.1.1 and was not restated. design-system.md 7 gained the length rule and 10.4's stale 'No summary yet' row was corrected to the shipped string. The superseded row text is in docs/decisions.md.

NO LAYOUT CHANGE WAS NEEDED anywhere: every shortened string sits in a wrapping TextBlock, a tooltip or an option card that reflows.

VERIFICATION: dotnet build -p:BaseOutputPath=C:\Temp\winnow-g1\ -m:1 — Build succeeded, 0 Warning(s), 0 Error(s). Then per project with --no-build against the same output path: Winnow.Tests 3066 passed / 0 failed; Winnow.Recommend.Tests 152 passed / 0 failed; Winnow.Covers.Tests 70 passed / 0 failed. Baseline 3066 / 152 / 70 matched exactly.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Reduced every explanatory blurb in the interface to a short phrase, across the account-stats screen, the Platforms and purchase-import screens, the Display preferences flyout, the Appearance screen, the theme, backdrop and layout option cards, the Merges section blurbs and the feed card's countdown. All replacement text was authored by the docs-writer subagent; no binding, control, layout or behaviour changed. Copy the user cannot get elsewhere was deliberately kept: errors, empty states, consent and connection-state copy, stated defaults, destructive-act consequences, automation names, the per-theme contrast report and the theme-file diagnostics. Rationale that is still true moved into game-library-design.md 4.7 and 6.4; design-system.md 7 now states the length rule and 10.4's stale copy row was corrected, with the superseded text appended to docs/decisions.md. Three tests that assert rules about copy — not wording — were treated as authority and the copy was corrected to satisfy them rather than the assertions being edited. Verified with dotnet build (0 warnings, 0 errors) and dotnet test per project against C:\Temp\winnow-g1\: 3066 / 152 / 70 passing, matching baseline exactly.
<!-- SECTION:FINAL_SUMMARY:END -->
