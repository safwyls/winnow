---
id: TASK-127
title: 'Section headers: clear the scrollbar and set the header in the secondary ink'
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 22:00'
updated_date: '2026-09-05 22:28'
labels:
  - ui
dependencies:
  - TASK-126
priority: medium
type: bug
ordinal: 154000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reported by the user seeing the TASK-126 section headers in the running app.

Two faults on the header row that each disclosed section (IGDB match, Edit details) gained:

1. THE SCROLLBAR OVERLAPS THE CLOSE BUTTON. The header sits inside the right column rest band, which is a bounded scroll region (TASK-105), so the scrollbar occupies the right edge and lands on the close glyph. The modal own close button at GameDetailsView.axaml:333 does not have this problem because it sits outside the scroll region. Move the close control left far enough to clear the scrollbar with visible breathing room, not merely enough to stop touching it.

Note the repo already has a convention here: the scroll regions carry Classes="inner", which per TASK-105 keeps their scrollbars out of the §9.1 resize-border inset. Check whether that class should also carry the gutter, so every current and future section header gets the clearance without each one solving it again.

2. THE HEADER READS AS CONTENT. The user asked for the title and the close button in the secondary colour, to separate the header from the rest of the view. The heading is Classes="label" and the close glyph is already TextDim; the heading needs the same treatment so the pair reads as one chrome element rather than as part of the section body.

Pick the token deliberately against the design system rather than assuming TextDim is meant — TextFaint also exists — and say what "secondary" resolves to and why. Whatever is chosen must still clear AA against the surface it sits on.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The close control clears the scrollbar with evident breathing room at both card widths
- [x] #2 The clearance is solved once for every section header, not per header, if the existing inner scroll-region class is the right home for it
- [ ] #3 The heading and the close glyph share one secondary ink so the header reads as chrome rather than as section content
- [x] #4 The chosen ink clears AA against its surface, verified rather than assumed
- [x] #5 The modal own header at line 333 is left alone, since it sits outside the scroll region and has no such problem
- [x] #6 design-system.md records the header treatment
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify the mechanism instead of assuming it. Avalonia 11.3.20 Fluent ScrollViewer template: with AllowAutoHide on (the default) PART_ContentPresenter is given ColumnSpan/RowSpan 2, so an interior scrollbar OVERLAYS the content - that is why the close glyph sits under it. ScrollContentPresenter never deflates by Padding in measure or arrange, so a Padding setter on ScrollViewer.inner would be inert. Both checked against Avalonia's own source at the 11.3.20 tag.
2. The gutter therefore does NOT go on Classes=inner. The class can only set the ScrollViewer's own properties, Padding is the only candidate and it is inert; a descendant selector onto the content would lose to the local Margin values the content already carries. The gutter goes on the scroll region's CONTENT, which is the repo's own idiom (the cover wall's Margin clears the scrollbar overlay). One margin on the rest band's content StackPanel covers both TASK-126 headers, every future section in the band, and the trailing Assign/Separate/Ungroup/Patch notes controls that share the same edge.
3. New token InnerScrollGutter = 0,0,20,0: ScrollBarSize 12 (the expanded Fluent track) plus 8, section 4's own spacing step, so the clearance reads as deliberate space. Drop GameMetadataEditorView's stale Margin=0,0,4,0 on the rows ItemsControl, which would otherwise double-count and misalign the editor rows from the rest of the band.
4. Secondary ink resolves to TextDim. Section 2 gives TextDim labels and metadata; section 8 says do not dim further and puts TextFaint under AA on the opaque ground. A label-size heading is 11px SemiBold, which is not WCAG large text, so 4.5:1 applies and the 3:1 large-text exception does not.
5. State the ink where the heading is drawn rather than inheriting it: a .section modifier in tokens.axaml's TextStyles, applied to the eight headings that NAME A SECTION (IGDB MATCH, EDIT DETAILS, ALSO COVERS, EXTENDS, EXPANSIONS, LISTS, ABOUT, the updates heading). Inline labels that name a VALUE keep plain .label.
6. Verify with the repo's own machinery: ThemeContrastTests already walks TextDim over the art-backed field exhaustively; add a test pinning the choice - TextDim clears AA over the worst cover in every theme and TextFaint does not - so the decision cannot drift.
7. docs-writer authors every comment and the design-system.md additions; wait for every child before the final build.
8. dotnet build Winnow.slnx to C:\Temp\winnow-ee2, then dotnet test per project --no-build against the same path, from PowerShell.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
── FAULT 1: WHERE THE GUTTER WENT, AND WHY NOT ON Classes=inner ──

The mechanism was verified against Avalonia 11.3.20's own source rather than
assumed. Two findings decide the whole answer:

1. Fluent's ScrollViewer template gives PART_ContentPresenter Grid.ColumnSpan=2
   and Grid.RowSpan=2 while AllowAutoHide is on, which is the default. The
   scrollbar is painted OVER the content and takes no column of its own. That
   is why the bar lands on the close glyph, and why it lands on the per-row
   Separate, Ungroup and Patch notes buttons too - the user reported the one
   they were looking at.
2. ScrollContentPresenter does not deflate by Padding in measure or in arrange.
   A Padding value on a ScrollViewer is inert.

So the gutter CANNOT live on Classes=inner. The class is on the ScrollViewer,
the only property it could set for this is Padding, and Padding does nothing.
The other route from a class - a selector reaching the content - loses to the
local Margin values that content already carries (the left column's 0,18,0,0,
the candidate list's 0,0,4,0), so it would fail silently on exactly the regions
that need it. The class also means something else: per TASK-105 it is section
9.1's opt-out, the claim that this scrollbar's edge is a divider of ours rather
than the window's. That is about pixels the OS owns; this is about our own bar
drawn over our own content.

The gutter is therefore on the scroll region's CONTENT, which is the repo's own
idiom - the cover wall sets Padding=0 and clears its bar with the wall's own
margin. One margin on the rest band's content StackPanel covers both TASK-126
section headers, every section added to the band later, and every trailing
control already in it. Solved once, and not once per header.

New token InnerScrollGutter = 0,0,20,0 in tokens.axaml, beside ScrollBarEdgeInset:
12 (ScrollBarSize, the width Fluent's track swells to under the pointer) + 8
(section 4's own spacing step). The close glyph's border box therefore ends 8px
clear of the swelled track, and the glyph itself about 15px clear once the
quiet button's 7px padding is counted. The margin is a constant, so it holds at
both card widths (MinWidth 700, MaxWidth 860).

GameMetadataEditorView's rows lost their own Margin=0,0,4,0: the editor draws
inside the rest band, so it now takes the band's gutter, and 4px of its own on
top would be counted twice and would step the editor's rows in from every other
section in the band.

── FAULT 2: WHAT SECONDARY RESOLVED TO ──

TextDim, and the finding that matters is that the headings ALREADY CARRY IT.

Section 2 gives TextDim the labels-and-metadata job. Section 8 says do not dim
further, and the ink below it is measured, not assumed: TextFaint is
3.63 / 3.60 / 3.31 / 3.28 across Winnow / Nightshift / Tungsten / Box art on the
flat card and 2.86 / 3.01 / 2.67 / 2.60 over the brightest cover the art-backed
card of section 5.5 can carry - under AA in every theme before any art is
involved. TextDim is 5.88 / 6.82 / 6.44 / 6.10 flat and 4.63 / 5.71 / 5.19 /
4.83 over that same brightest cover. A heading at label size is 11px SemiBold,
which is NOT WCAG large text (that begins at 14pt bold), so the 4.5:1 bar
applies and the 3:1 large-text allowance does not. Azure was considered and
rejected: section 14.1 forbids spending one job's colour on a second one, and
Azure's job is outbound links, which this modal already draws three of.

tokens.axaml's :is(TextBlock).label style has set Foreground to TextDim
app-wide since it was written, and there is no second .label style anywhere in
src. So every section heading in the modal, and the close glyph beside it, were
already the same ink - #8FA5A0 in the default theme - before this task. The ink
the user asked for was already in force; what does not separate the header from
the section is that much of this modal's body prose is TextDim as well (the
IGDB note, the editor intro, the ABOUT summary, every status line).

What changed is that the ink is now STATED where the decision was made rather
than inherited: a .section modifier declared once in tokens.axaml beside .label,
applied to the eight headings that NAME A SECTION. A section heading and its
close glyph can no longer drift apart if .label is ever retuned for the value
labels, and the choice is pinned per theme by a test.

Changed (Classes=label section): IGDB MATCH and EDIT DETAILS (the TASK-126
header rows), ALSO COVERS, EXTENDS, EXPANSIONS, LISTS, ABOUT, and the updates
heading.

Deliberately left on plain .label, because each names a VALUE rather than a
surface: PLAYED and SINCE YOU PLAYED on the gap rail, the coverage TOTAL label,
the per-release achievements label, STEAM APPID, ON DISK, and the six per-field
row labels in the metadata editor. The one genuinely ambiguous case is the gap
rail's SINCE YOU PLAYED, which shares its words with the updates heading that
DID change: the rail's copy labels the rail beside it, the updates one names the
list under it, so they are on opposite sides of the line despite reading the
same.

── VERIFICATION ──

dotnet build Winnow.slnx to C:\Temp\winnow-ee2: 0 warnings, 0 errors, with
TreatWarningsAsErrors on and compiled bindings, so both changed views are
binding-checked. Then per project against the same path: Winnow.Tests 3275
passed, Winnow.Recommend.Tests 152, Winnow.Covers.Tests 78. Baseline was
3271 / 152 / 78; the four new cases are one theory case per theme of
ThemeContrastTests.The_section_heading_takes_the_quietest_ink_that_still_clears_AA,
which pins TextDim clearing AA over the brightest cover and TextFaint failing
on both the flat and the art-backed card.

── NOT VERIFIED ──

Anything that needs the window. Whether 8px past the swelled track reads as
evident breathing room, and whether the header now reads as chrome, are visual
judgements and this ran without launching the app.

── NOTICED, NOT CHANGED ──

Two more regions have the same overlay and were left alone rather than widening
the task: the IGDB candidate list, its own inner region with a 4px content
margin, so the swelled 12px track covers about 8px of each row's Assign button;
and the left column's region, where a wrapped ON DISK path can run under its
bar (no control sits at that edge - Clear is left-aligned). Both are the same
fault as this task's, in regions the user did not report.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The rest band's content now carries a 20px right gutter, and the ink a section heading is set in is stated rather than inherited.

THE GUTTER. Verified against Avalonia 11.3.20 rather than assumed: Fluent's ScrollViewer gives PART_ContentPresenter both spans while AllowAutoHide is on, so the scrollbar is painted OVER the content, which is why it lands on the close glyph - and on the per-row Separate, Ungroup and Patch notes buttons, which have the same edge. It cannot be solved from Classes=inner: the only property that class could set is the ScrollViewer's Padding, and ScrollContentPresenter does not deflate by Padding in measure or arrange, so it is inert; a selector reaching the content instead would lose to the local Margin values that content already carries. That class is also section 9.1's opt-out, which is about pixels the OS owns rather than about our own bar over our own content. So the gutter is one margin on the band's content - the repo's own idiom, the same one the cover wall uses - covering both TASK-126 headers, every section added later, and every trailing control already there. InnerScrollGutter = 0,0,20,0 is 12, the width Fluent's track swells to, plus 8, section 4's spacing step; the margin is a constant, so it holds at both card widths. The editor's rows gave up their own 4px, which would now be counted twice.

THE INK. Secondary resolves to TextDim. Section 2 gives it labels and metadata; section 8 says do not dim further; and measured, TextFaint is 3.63 / 3.60 / 3.31 / 3.28 per theme on the flat card and 2.86 / 3.01 / 2.67 / 2.60 over the brightest cover the art-backed card can carry, against TextDim's 5.88 / 6.82 / 6.44 / 6.10 and 4.63 / 5.71 / 5.19 / 4.83. A heading at label size is 11px SemiBold, which is not WCAG large text, so 4.5:1 applies and the 3:1 allowance does not.

The finding worth carrying forward is that .label has set TextDim app-wide all along and there is no second .label style, so the eight headings and the close glyph were already one ink before this task. What changed is that the ink is stated at the heading through a .section modifier declared once in tokens.axaml, so the pair cannot drift if the value labels are ever retuned. The eight are IGDB MATCH, EDIT DETAILS, ALSO COVERS, EXTENDS, EXPANSIONS, LISTS, ABOUT and the updates heading; every label that names a VALUE was left alone.

design-system.md 10.1 records the gutter and 10.3 the heading's ink. Nothing in either document was wrong, so there is no decisions.md append.

Verified by build and test only: 0 warnings, 0 errors, and 3275 / 152 / 78 against a 3271 / 152 / 78 baseline, the four new cases being the per-theme pins on the ink. AC 1 and AC 3 are left unchecked: whether 8px past the swelled track reads as evident breathing room, and whether the header now reads as chrome rather than as content, are visual judgements that need the running window.
<!-- SECTION:FINAL_SUMMARY:END -->
