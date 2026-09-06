---
id: TASK-129
title: Game tiles and feed cards are nameless to a screen reader
status: Done
assignee:
  - '@claude'
created_date: '2026-09-06 00:18'
updated_date: '2026-09-06 00:55'
labels:
  - ui
  - accessibility
dependencies: []
priority: high
type: bug
ordinal: 156000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Found during the TASK-119..127 design analysis, and outside the scope of every task that found it.

GameTileView.axaml:247 sets the tile entire accessible name on a Border. Avalonia prunes a Border from the UIA tree, so the name never reaches a screen reader and every game tile in the library is nameless. FeedCardView.axaml:387 has the same fault.

Both are a one-attribute fix — the name belongs on an element UIA does not prune.

Related and worth doing in the same pass: GameTileViewModel.AutomationName does not include the unread-update count, which is TASK-30 premise confirmed at source. A Flare-marked tile tells a sighted user something a screen-reader user cannot get at all.

Verified facts about Avalonia 11.3.20 that constrain the fix, established during that analysis rather than assumed:
- AutomationProperties.Name on a TextBlock is silently ignored: TextBlockAutomationPeer.GetNameCore does not call base.
- PositionInSet, SizeOfSet, IsRequiredForForm, IsColumnHeader and IsRowHeader compile and are wired to nothing, so "3 of 12" is impossible and a count must be spelled into the name string.
- Changing a Name at runtime raises no UIA event; a live region must be a TextBlock whose Text is bound.
- An ItemsControl reports as a List with zero items, because every container is a ContentPresenter yielding NoneAutomationPeer. The fix belongs on the DataTemplate root, not the control.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A game tile reports its accessible name to a screen reader
- [x] #2 A feed card does the same
- [x] #3 The unread-update count is part of the tile accessible name, spelled into the string since PositionInSet is inert
- [x] #4 A test pins name reachability so a future refactor cannot silently prune it again
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
0. VERIFIED against Avalonia 11.3.20 source (raw.githubusercontent.com at tag 11.3.20) before designing:
   - Control.OnCreateAutomationPeer returns NoneAutomationPeer; Border, Panel, Grid, StackPanel,
     ContentPresenter and ContentControl do not override it. NoneAutomationPeer.IsControlElementCore()
     is false, and Win32 AutomationNode maps IsControlElement onto UIA_IsControlElementPropertyId, so
     the name on a Border never reaches the control view. Bug confirmed at source.
   - TextBlockAutomationPeer.GetNameCore() returns Owner.Inlines?.Text ?? Owner.Text and never calls
     base: AutomationProperties.Name on a TextBlock is ignored. HOLDS.
   - PositionInSet, SizeOfSet, IsRequiredForForm, IsColumnHeader, IsRowHeader: GitHub code search over
     AvaloniaUI/Avalonia returns exactly one hit each, their own declaration file. Nothing reads them.
     HOLDS - a count must be spelled into the name string.
   - ControlAutomationPeer.OwnerPropertyChanged raises property-changed events for IsVisible, Bounds,
     RenderTransform, VisualParent and ItemStatus only - not for AutomationProperties.Name. HOLDS.
     ItemStatus is the one attached property that does raise an event, and TextBlock raises a Name
     change when Text changes, so a live region is a bound TextBlock or ItemStatus.
   - ItemsControl gets ItemsControlAutomationPeer (AutomationControlType.List); its containers are
     ContentPresenters yielding NoneAutomationPeer, so the list reports no items in the control view.
     HOLDS - the fix belongs on the DataTemplate root.
   - Additionally verified: ControlAutomationPeer.IsControlElementOverrideCore honours
     AutomationProperties.AccessibilityView, so AccessibilityView=Control un-prunes an element that
     would otherwise be dropped, and AutomationControlType.None maps to UIA Group. This is the escape
     hatch for a host that cannot become a peer-bearing control.
1. A repository-wide scan found four sites, not two: GameTileView.axaml:247 (Border),
   FeedCardView.axaml:387 (Panel), MainWindow.axaml:1131 (Border) and MergeQueueView.axaml:565
   (Border). Neither GameDetailsView.axaml nor GameMetadataEditorView.axaml is affected. All four are
   fixed, because the enforcement test in step 7 is only credible if the tree is clean.
2. GameTileView.axaml: move AutomationProperties.Name from Border#Lift to the UserControl root, whose
   UserControlAutomationPeer is a control element that reads AutomationProperties.Name.
3. Data layer: carry the unread-update count out of the major_update CTE (COUNT(*) beside the existing
   MAX(occurred_at), same GROUP BY, same watermark and announcement-correlation filters) onto BucketRow
   and into GameGrouping. Per game the figure is the MAXIMUM across the game's releases, never the sum:
   two store copies of one game carry the same patches, and adding them would report a number no
   storefront ever pushed.
4. GameTileViewModel.AutomationName gains the unread fact and its count (TASK-30).
5. FeedCardView.axaml: the card's name goes on Button#Card, whose ButtonAutomationPeer is a control
   element and consults AutomationProperties.Name first. FeedCardViewModel gains AutomationName
   (title, stores, unread, the engine's reason sentence, which its own doc comment already says exists
   for the accessible name) and a bound ItemStatus for the receipt/countdown state, which is the one
   channel that raises a UIA event. The inert attribute on Panel#Countdown goes.
6. MainWindow.axaml:1131 keeps Fetch.AutomationName and gains AccessibilityView=Control, which is what
   makes it reachable; MergeQueueView.axaml:565 does the same on the focusable row, plus
   ControlTypeOverride=ListItem so it reports as a list item rather than a group. The rail bucket
   button gains a name so the Flare-marked Patched count is announced instead of the button reporting
   its Grid's type name (TASK-30 AC1).
7. Test: Enforcement/AutomationNameReachabilityTests scans every .axaml under src/Winnow.App, finds the
   element hosting each AutomationProperties.Name, resolves it to a Type, and asserts by REFLECTION
   over Avalonia's own metadata that the type overrides Control.OnCreateAutomationPeer (so its peer is
   not NoneAutomationPeer) or carries AccessibilityView=Control/Content. TextBlock is rejected
   separately because its peer ignores the property. An unresolvable element type fails rather than
   being skipped. No control is instantiated, so no dispatcher or Application is needed. Plus
   view-model tests pinning the tile and feed-card name strings.
8. All prose - name strings, copy classes, code comments, XML doc comments, design-system.md and
   docs/decisions.md - is authored by the docs-writer agent, per AGENTS.md.
9. Verify with dotnet build -p:BaseOutputPath=C:\Temp\winnow-a11y\ -m:1 then dotnet test per project
   with --no-build against the same output path.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
All four constraints in the description HOLD, verified against the Avalonia 11.3.20 source rather
than trusted. TextBlockAutomationPeer.GetNameCore returns Owner.Inlines?.Text ?? Owner.Text and
never calls base. PositionInSet, SizeOfSet, IsRequiredForForm, IsColumnHeader and IsRowHeader each
return exactly one hit in a code search over the whole Avalonia repository - their own declaration -
so nothing reads them. ControlAutomationPeer.OwnerPropertyChanged raises a UIA event for IsVisible,
Bounds, RenderTransform, VisualParent and ItemStatus only, never for a Name change.
ItemsControlAutomationPeer reports AutomationControlType.List while its ContentPresenter containers
yield NoneAutomationPeer.

The bug itself is confirmed at source: Control.OnCreateAutomationPeer returns NoneAutomationPeer,
Border and Panel do not override it, NoneAutomationPeer.IsControlElementCore is false, and
Avalonia.Win32.Automation.AutomationNode maps IsControlElement onto UIA_IsControlElementPropertyId.
The element keeps its children, so nothing throws and nothing looks broken; the name is simply
dropped from the control view a screen reader walks.

Two facts NOT in the description, both load-bearing for the fix:
- ControlAutomationPeer.IsControlElementOverrideCore consults AutomationProperties.AccessibilityView
  before the peer's own answer, so AccessibilityView="Control" un-prunes an element and keeps its
  name. AutomationControlType.None maps to the UIA Group type, so it reports as a named group.
- ContentControlAutomationPeer.GetNameCore falls back to Owner.Content?.ToString(). The rail's
  bucket rows are Buttons whose content is a Grid, so they were announcing the literal string
  "Avalonia.Controls.Grid".

A repository-wide scan found FOUR sites, not two: GameTileView.axaml:247 (Border),
FeedCardView.axaml:387 (Panel), MainWindow.axaml:1131 (Border) and MergeQueueView.axaml:565
(Border). GameDetailsView.axaml and GameMetadataEditorView.axaml are clean and were not touched.
All four are fixed, because the enforcement test is only credible if the tree is clean.

WHAT CHANGED

- src/Winnow.App/Views/GameTileView.axaml - the name moved off Border#Lift onto the UserControl
  root, whose UserControlAutomationPeer is a control element and reads AutomationProperties.Name.
- src/Winnow.App/Views/FeedCardView.axaml - the card's name now sits on Button#Card, which is the
  Tab stop and has a ButtonAutomationPeer; the Button also carries AutomationProperties.ItemStatus
  for the verdict receipt and the replacement countdown, that being the only attached property
  Avalonia raises a UIA event for. The inert Name on Panel#Countdown is gone.
- src/Winnow.App/Views/MainWindow.axaml - the fetch status field keeps Fetch.AutomationName and
  gains AccessibilityView="Control" plus the same sentence as ItemStatus, so the falling count is
  announced rather than frozen at whatever it read when the field was drawn. The rail's bucket rows
  gain a name of their own.
- src/Winnow.App/Views/MergeQueueView.axaml - the focusable candidate row is a DataTemplate root
  with no peer-bearing control to move the name to, so it gains AccessibilityView="Control" and
  ControlTypeOverride="ListItem".
- src/Winnow.App/ViewModels/UnreadCopy.cs (new) - the badge and the rail count in words.
- GameTileViewModel gains UnreadUpdateCount and UnreadText; AutomationName now carries the badge.
- FeedCardViewModel gains AutomationName (tile name plus the engine's reason sentence, which its
  own Reason doc comment already claimed existed for this) and StatusAnnouncement.
- BucketViewModel gains AutomationName.
- The count is carried end to end: COUNT(*) beside the existing MAX(occurred_at) in the
  major_update CTE, under the same acknowledgement watermark and announcement correlation, onto
  BucketRow, folded per game as the MAXIMUM across the game's releases (never the sum - two store
  copies carry the same patches), into GameGrouping.UnreadUpdateCount. GameGrouping.Of forces the
  count to zero when there is no push, so the pair cannot disagree.

HOW THE TEST PINS IT

tests/Winnow.Tests/Enforcement/AutomationNameReachabilityTests.cs scans every .axaml under
src/Winnow.App, finds the start tag hosting each AutomationProperties.Name, resolves it to a Type,
and asks Avalonia's own metadata by reflection whether that type overrides
Control.OnCreateAutomationPeer. Types that do not are pruned, and fail unless the same tag carries
AccessibilityView="Control" or "Content". TextBlock fails unconditionally, because its peer ignores
the property. An element type that does not resolve fails rather than being skipped. No control is
instantiated, so the test needs no dispatcher and no Application - and because the rule is asked of
Avalonia rather than of a list kept in the test, an Avalonia release that gives a type a peer
relaxes it on its own. A second, theory-driven test pins that the tile and the card actually have a
name, on UserControl and Button respectively: a name that is reachable but absent is the same
silence.

Verified by mutation, not by assumption. Restoring the name to Border#Lift makes both halves fail:

  src/Winnow.App/Views/GameTileView.axaml:250 - <Border> does not override OnCreateAutomationPeer,
  so Avalonia gives it a NoneAutomationPeer and UIA drops it from the control view. The name never
  reaches a screen reader. Move the name to a control that has a peer of its own, or add
  AutomationProperties.AccessibilityView="Control" to this element.

and

  The_tile_and_the_card_name_themselves_on_their_outermost_control
  Expected: "UserControl"  Actual: "Border"

The reported line number is the real one: XML comments are blanked in place with their line breaks
kept, so offsets still match the file on disk.

DOCUMENTS

design-system.md §8 gains four rules and the sentence that says why they are enforced by a test:
where a name may sit, that a name on a TextBlock is discarded, that a changing value travels on
ItemStatus or a bound TextBlock's Text, and that a count is spelled into the name string. §8's
first bullet was also corrected - it claimed the unread badge is "backed by the rail count and a
tooltip", and there is no tooltip on the badge in either GameTileView.axaml or FeedCardView.axaml,
and never has been. The superseded sentence is quoted verbatim in docs/decisions.md under
"2026-09-05 - Accessible names sat where UIA drops them (TASK-129, TASK-30)".

All prose - the copy strings, every comment, every XML doc comment, design-system.md and
docs/decisions.md - was authored by two docs-writer agents. No TODO(docs-writer) and no
PLACEHOLDER_ survives anywhere under src/ or tests/; the only TODO markers left in the tree are
three pre-existing ones in Themes/tokens.axaml that predate this task. Every edit went through the
Edit tool, and a scan for the mojibake signature finds nothing.

VERIFICATION

  dotnet build Winnow.slnx -p:BaseOutputPath=C:\Temp\winnow-a11y\ -m:1
    Build succeeded.  0 Warning(s)  0 Error(s)

  Winnow.Tests           Failed: 1, Passed: 3329, Skipped: 0, Total: 3330
  Winnow.Recommend.Tests Failed: 0, Passed:  152, Skipped: 0, Total:  152
  Winnow.Covers.Tests    Failed: 0, Passed:   82, Skipped: 0, Total:   82

The single failure is not this task's. Another agent is working in the same tree on the
details-modal reception work and has added an untracked migration,
src/Winnow.Data/Migrations/0028_reception_and_screenshots.sql, with no line yet in checksums.txt;
SchemaDisciplineTests.No_shipped_migration_has_been_edited fails on that. The reported checksum
changed between two runs half an hour apart, so that file is still being edited. Nothing here adds
a migration or touches the schema. Three further failures present at the start of this session -
IgdbPlatformFieldTests, IdentityReadInventoryTests and SchemaDisciplineTests' stored-score rule -
belonged to the same concurrent work and have since gone green.

WHAT STILL NEEDS A RUNNING APP

That a screen reader speaks these names is not proved here. What is proved is that the name now
sits on an element Avalonia exposes to the UIA control view, established from Avalonia's own
source and held by a test. Confirming the announcement itself needs NVDA or Narrator against a
running app, which was out of bounds for this pass.

NOTICED, NOT FIXED

GameTileView.axaml's badge comment says the back face restates the badge as the "Patched since"
bucket name. The label was renamed to "Patched" (docs/decisions.md, 2026-09-04), so the comment
names a string that no longer exists. It was already stale before this task and is outside these
acceptance criteria, so it was left alone rather than folded in silently.

EVIDENCE UPGRADED FOR AC1 AND AC2

The markup scan alone proves only that the name sits on a type Avalonia gives a peer to, which is
reasoning about Avalonia rather than asking it. A third test now asks. It builds the host control
each view names - a UserControl for the tile, a Button for the card - sets the accessible name the
way the compiled binding does, creates the control's own automation peer through the protected
OnCreateAutomationPeer the framework itself calls, and reads back IsControlElement() and GetName().
Both hosts return true and hand back the tile's name. The Border the name used to sit on, given the
same name by the same call, returns false: its peer still holds the string - which is precisely why
the bug survived - while reporting itself out of the control view UIA gates on
UIA_IsControlElementPropertyId. The name is dropped on the way out, not on the way in.

Control.GetOrCreateAutomationPeer is not used, because it calls VerifyAccess and would need a
dispatcher; the peer returned is the same object the control would have cached. No Avalonia
Application is started.

That is the layer directly beneath a screen reader and as close to one as a test without a running
window can stand. It is not an NVDA or Narrator pass.

FINAL RUN

  dotnet build Winnow.slnx -p:BaseOutputPath=C:\Temp\winnow-a11y\ -m:1
    Build succeeded.  0 Warning(s)  0 Error(s)

  Winnow.Tests           Passed!  Failed: 0, Passed: 3331, Skipped: 0, Total: 3331
  Winnow.Recommend.Tests Passed!  Failed: 0, Passed:  152, Skipped: 0, Total:  152
  Winnow.Covers.Tests    Passed!  Failed: 0, Passed:   82, Skipped: 0, Total:   82

Fully green. The migration-checksum failure noted earlier was the concurrent details-modal work and
has been resolved by that agent. Baseline was 3279 / 152 / 78; the deltas are this task's 13 new
tests and the other agent's.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Every game tile in the library was nameless to a screen reader, and had been. The name sat on a
Border; Border does not override Control.OnCreateAutomationPeer, so Avalonia gives it a
NoneAutomationPeer whose IsControlElementCore is false, and the Win32 provider maps that onto
UIA_IsControlElementPropertyId - what the control view a screen reader walks filters on. The
element keeps its children, so nothing looked broken and nothing threw. Only the name was lost.

All four Avalonia constraints recorded on this task hold, checked against the 11.3.20 source rather
than trusted, and two more were found that the fix depends on: AccessibilityView is consulted before
the peer's own answer and so un-prunes an element, and ContentControlAutomationPeer falls back to
Content.ToString(), which is why the rail's bucket rows were announcing "Avalonia.Controls.Grid".

A repository-wide scan found four faulty sites, not two. The tile's name moved to its UserControl
root and the feed card's to its Button; the fetch-status field and the merge-queue row, which have
no peer-bearing control to move a name onto, state AccessibilityView="Control" instead; the rail's
bucket rows gained names. The feed card also gained a live ItemStatus, that being the one attached
property Avalonia raises a UIA event for, so a verdict landing mid-read is announced. TASK-30's
count is carried end to end out of the same major_update aggregate that gives the badge its
timestamp, under the same acknowledgement watermark, folded per game as the maximum across a game's
releases and never the sum, and spelled into the name string because PositionInSet and SizeOfSet
are inert.

Verified, not assumed. AutomationNameReachabilityTests holds the rule three ways: a scan of every
.axaml under src/Winnow.App that asks Avalonia's metadata by reflection whether each name's host has
a peer of its own; a pin that the tile and the card have a name at all, on the right element; and a
test that builds the real hosts, makes their real automation peers, and reads the name back -
returning true and the tile's name from the UserControl and the Button, and false from the Border
the name came off. Restoring the bug makes the first two fail with the file and line. What is not
proved here is the announcement itself, which needs NVDA or Narrator against a running app.

Build succeeded, 0 warnings, 0 errors. Winnow.Tests 3331 passed, Winnow.Recommend.Tests 152 passed,
Winnow.Covers.Tests 82 passed, none failed or skipped. design-system.md §8 gains the rule and one
corrected sentence; the superseded text is in docs/decisions.md. All prose was authored by
docs-writer.
<!-- SECTION:FINAL_SUMMARY:END -->
