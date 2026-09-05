---
id: TASK-102
title: Move the wrong-game control under the cover art and install location
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-04 22:50'
updated_date: '2026-09-04 23:30'
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
- [ ] #1 Wrong Game is a button in the action band (Band 3) alongside Store page, All patch notes, Open folder and Hide
- [ ] #2 The search field and its results populate the full width below the action band and above the LISTS/ABOUT content, so results are not squeezed into a narrow column
- [ ] #3 The results keep the bounded scrolling behaviour from TASK-105 and do not push the modal past the window
- [ ] #4 The left column returns to cover art, appid and install location, with no wrong-game control
- [ ] #5 The ABOUT section reads correctly with the control absent from its footer
- [ ] #6 Search and assignment still disclose inline in the modal tree, never a flyout
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
REVISED 2026-09-04 after the user used the left-column build. Supersedes the ABOUT-footer siting (TASK-89) and the left-column siting (this task first pass).

1. Band 3 gains the toggle. Wrong game? becomes a Classes=link button in the action band, after Open folder and before Hide, gated on ShowIgdbMatch, bound through IgdbMatch.ToggleCommand / ToggleLabel / OpenTooltip. It matches Store page, All patch notes, Open folder and Hide, which is what the user asked for; Hide is already the precedent for a link-styled button that is not outbound.
2. The rest of the control moves to the full width of the right column, as the first thing inside the Band 4 ScrollViewer (Grid.Row=2), above ALSO COVERS / LISTS / ABOUT. Below the seam, not above it. Above the seam Band 3 lives in an Auto row, and an open search would add about 250px of Auto height the card cannot refuse; on a window shorter than about 780px the Auto rows alone exceed the card and the search draws past its edge, which is exactly the defect TASK-105 fixed. Row 2 is the star row that scrolls, so putting the block there makes AC 3 structural rather than arithmetic.
3. No spurious gap when the control is dormant. The Band 4 ScrollViewer content becomes an unspaced StackPanel holding the IGDB block and then the existing Spacing=22 rest band. A StackPanel adds spacing between visible children whatever their size, so the block carries no spacing of its own and each of its parts carries its own top margin; when nothing is disclosed the block measures zero and costs nothing.
4. The block parts, each gated independently: the standing note (HasNote), Clear (ShowPinned), the disclosure (IsOpen: field plus Search, status, no-matches sentence, candidate list) and the refusal sentence (HasProblem). Clear and the refusal stay outside the disclosure for the reason they always did - a refusal nobody can see is a refusal that did not happen.
5. The result row unstacks. Full width restores the shape the LIBRARY settings surface uses: three columns 34, star, Auto - cover, then name over year and platforms, then Use this on the row. The assign control leaves the text column, so the name gets the whole remaining width instead of 85px.
6. The candidate region is retuned, not removed. The row is now 68px (51px cover plus 17px of padding and rule) where the stacked row was 83px, so MaxHeight 208 would show three rows and a 4px sliver. 238 shows three rows and half of a fourth, keeping the cut row and the scrollbar as the signal that there is more.
7. Opening from a scrolled position. The toggle is in a band that never scrolls and the surface it opens is in one that does, so a Click handler brings the block into view and focuses the query field after the command has run.
8. The left column keeps its scroll region. With the control gone it holds the cover, STEAM APPID and ON DISK, but the cover is a fixed 300px and the card is min(720, window minus 80); on a short window ON DISK still overflows without it, so it stays and its comment changes rather than the structure.
9. Prose is delegated. XAML comments, design-system.md 10.9 and the 10.1 diagram, the XML doc comments on GameDetailsViewModel.IgdbMatch and LibraryViewModel, and the docs/decisions.md entry for the superseded 10.9 sentence, all authored by docs-writer. GameIgdbMatchViewModel.cs and GameIgdbMatchCopy.cs are owned by TASK-104 right now and are not touched, so their stale left-column doc comments are reported rather than edited.
10. Verify with dotnet build then dotnet test per project against a scratch output path. Placement, fit and the action band width at the card minimum need a run.
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
<!-- SECTION:NOTES:END -->
