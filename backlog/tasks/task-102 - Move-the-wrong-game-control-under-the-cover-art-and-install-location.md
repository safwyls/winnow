---
id: TASK-102
title: Move the wrong-game control under the cover art and install location
status: Done
assignee:
  - '@codex'
created_date: '2026-09-04 22:50'
updated_date: '2026-09-06 17:43'
labels:
  - ui
dependencies: []
priority: medium
type: enhancement
ordinal: 129000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The manual IGDB assignment control (TASK-89) currently sits in the footer of the ABOUT section of the details modal. The button itself is the right idea, but it belongs in the left column, beneath the cover art and the install location, where the identity facts about the game already live.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Wrong game? is available through More in Band 3, following the current design-system.md 10.3 action-menu rule.
- [x] #2 The search field and results fill the right column rest band below the action band and above the remaining content.
- [x] #3 Results retain bounded scrolling and do not push the modal past the window.
- [x] #4 The left column contains cover and identity facts, with only Clear for an existing IGDB pin, as specified in 10.9.
- [x] #5 ABOUT reads correctly with the wrong-game control absent from its footer.
- [x] #6 Search and assignment disclose inline in the modal tree, never a flyout.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reconcile the older placement criteria with the shipped More-menu design in design-system.md 10.3 and 10.9. 2. Exercise More > Wrong game with keyboard input in the compiled view, verifying inline disclosure, query focus, full-width results, left-column identity facts and ABOUT. Reuse TASK-105 scrolling evidence. 3. Record objective results and close.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The control left the ABOUT section's footer and is now the last thing in the modal's left column, under the cover art and under ON DISK. Nothing about it depends on its old neighbour, so the move is markup: the whole block was lifted out of the ABOUT StackPanel and reparented into the left column's scroll region.

WHY IT FITS THERE, in the design's own terms. Section 10.1 splits the modal by what each column is about - left is the object (its art, the id Steam calls it, where it lives on disk), right is your relationship with it. Which game this IS is an identity fact, so the correction sits with the other identity facts. The previous agent's argument for the ABOUT footer (ABOUT is the IGDB record in prose, so the recourse sits under the answer it corrects) was reasonable and the user overruled it; it is recorded in docs/decisions.md rather than argued with.

THE ROW HAD TO BE REDRAWN, and this is the only thing the move forced. The left column is 200px. The shipped row put the cover, the name, the year and platforms, and the assign button on one line, which at 200px leaves about 85px for the name - the name trims to nothing, and the name is the fact that tells two candidates apart. The row now stacks: the 34x51 cover keeps its own column, and beside it the name, then the year and platforms, then the assign button. Same four facts, same labels, same automation names, same full-saturation covers on the same image path. The pinned note also moved to its own line, because it does not fit beside the two buttons at that width, and the Search button's padding went from 11,6 to 9,6 to leave the field a usable width.

STRUCTURAL CONSEQUENCE. An open search in a fixed column would run past the card's MaxHeight, which is TASK-105's defect in the other column, so the left column became a two-row Grid: the cover fixed, everything under it in a bounded scroll region. That change is recorded against TASK-105, which owns the containment.

ABOUT with the footer gone is its label, the summary and the empty-state sentence - the shape every other section on the modal has, and the shape it had before TASK-89.

AC 1 checked: the block is a child of the left column (Grid.Column=0), declared after the ON DISK block, in src/Winnow.App/Views/GameDetailsView.axaml.
AC 2 checked: the disclosure is still inline in the modal's own tree. grep for flyout, popup and ContextMenu over the whole view returns comments only - there is no Popup or Flyout element anywhere in the file.
AC 3 deliberately unchecked. Whether ABOUT reads correctly with its footer gone is a judgement about the rendered section, and no test in this repository renders this view. It needs the running app, and so does whether the stacked row and the 200px column look right.

All prose - the XAML comments, the XML doc comments on GameIgdbMatchViewModel, GameIgdbMatchCopy, GameDetailsViewModel and LibraryViewModel, design-system.md 10.1 and 10.9, and the docs/decisions.md entry - was authored by the docs-writer subagent. No TODO(docs-writer) markers remain in the tree. design-system.md 10.9's placement paragraph was rewritten and the sentence it used to say is quoted verbatim in docs/decisions.md, per AGENTS.md.

VERIFICATION, PowerShell, scratch output path:
  dotnet build -p:BaseOutputPath=C:\Temp\winnow-h2\ -m:1   Build succeeded. 0 Warning(s) 0 Error(s)
  dotnet test tests\Winnow.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-h2\   Passed! - Failed: 0, Passed: 3098, Skipped: 0, Total: 3098
  dotnet test tests\Winnow.Recommend.Tests -p:BaseOutputPath=C:\Temp\winnow-h2\   Passed! - Failed: 0, Passed: 152, Total: 152
  dotnet test tests\Winnow.Covers.Tests -p:BaseOutputPath=C:\Temp\winnow-h2\   Passed! - Failed: 0, Passed: 70, Total: 70
Compiled bindings are on, so the build is a check that every binding in the reparented markup still resolves.

PLACEMENT REVISED BY THE USER, 2026-09-04, after seeing the left-column version running: "i think the better solution is to make Wrong Game a button next to Store Page, All patch notes, Hide and have the search bar and results populate below that area above the Lists/About section". The left column is 200px, which is too narrow for a result row carrying a cover, name, year and platforms — the previous pass had to stack the row to fit and the name still trimmed. The action band spans the right column, so the results get its full width. This supersedes both the original ABOUT-footer placement (TASK-89) and the left-column placement (the first pass of this task).

Review reconciled AC 1 and 4 with the later shipped action-menu design in design-system.md 10.3/10.9: Wrong game? is a More menu row and Clear alone remains under the left-column identity facts. This supersedes the older direct-button and entirely-empty-left-column wording; no reversal of the current product design is intended.

2026-09-06 verification: the real compiled GameDetailsView was exercised in the isolated Avalonia.Headless 11.3.20/Skia harness used for TASK-105 at 1200x640, 1280x820 and 1920x1080. Two Enter activations from the focused More trigger opened its Wrong game? row and focused MatchQueryField; the query and all twenty candidates remained in the same window visual tree. The list measured 400px wide at the card minimum, with its own 238px scrolling viewport; all twenty Tab stops remained visible. Rendered captures show cover and Steam appid in the left column, the full right-column search below Band 3, and ABOUT containing its heading and readable summary after disclosure closes. Current pin-only Clear placement was reviewed against 10.9. All 192 targeted details, IGDB match and theme contrast tests pass. Captures and executable harness are under C:/Temp/winnow-task105. No application change was needed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Already implemented by the later More-menu/full-width disclosure design. Verified keyboard opening and query focus in the real view, bounded twenty-result scrolling at three window sizes, left identity facts, and rendered ABOUT; 192 relevant tests passed.
<!-- SECTION:FINAL_SUMMARY:END -->
