---
id: TASK-125
title: Make the folded actions a menu rather than an inline button list
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 19:47'
updated_date: '2026-09-05 20:32'
labels:
  - ui
dependencies:
  - TASK-123
priority: medium
type: enhancement
ordinal: 152000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User request after seeing the disclosure land: "instead of having more spawn in the additional items as buttons below why not make it a context menu with each item listed?"

TASK-123 folded Open folder, Wrong game?, Edit details and Hide behind a More control that discloses them as a vertical list of buttons inline in the modal tree. That agent chose inline over a popup on the standing §10.7 / §12.3 reason: a popup is its own root with no adorner layer, so Avalonia FocusAdorner does not draw there and every focus ring would need hand-drawing.

That objection does not apply to a menu, which is why this request is sound. MainWindow.axaml already ships ContextMenu.actions styling whose MenuItem:selected state draws its own highlight inside the item template — SurfaceHigh ground with a Volt border — rather than relying on the adorner layer. Keyboard traversal in a menu drives that same :selected state. It is also already the idiom for the library tile actions, so this makes the modal agree with the grid instead of inventing a third pattern.

Note the trigger is a button rather than a right-click, so this wants a flyout opened from the control (or the equivalent), not a bare ContextMenu waiting on right-click.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The folded actions open as a menu listing each item, not as inline buttons
- [x] #2 The menu reuses the shipped ContextMenu.actions treatment rather than a new one, so the modal and the library grid agree
- [x] #3 Keyboard reaches and activates every item, and the focused item is visibly marked without relying on FocusAdorner
- [x] #4 Escape dismisses the menu and returns focus to the trigger
- [x] #5 The band still fits both card widths, and adding a further action still costs no width
- [x] #6 design-system.md is updated and the inline-list description is recorded in docs/decisions.md
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Confirm the premise before relying on it: read MainWindow.axaml ContextMenu.actions and verify that MenuItem in the selected state paints inside the item template (SurfaceHigh ground, Volt edge on a constant-thickness border) rather than through the adorner layer.
2. Choose the mechanism against current Avalonia documentation, not memory. The trigger is a button press, so Button.Flyout + MenuFlyout, the idiom this app already ships for the sort menu (MainWindow.axaml Button.Flyout -> Flyout FlyoutPresenterClasses=sortmenu). A bare ContextMenu waits on right-click; opening one from a left-click handler is off-label.

3. Reuse the treatment rather than author a second one: generalize each ContextMenu.actions selector to match MenuFlyoutPresenter.actions in the SAME declaration, so one set of setters governs both roots. Move the block to Themes/controls.axaml, which is that file's stated rule - a style moves the moment a second surface needs it - converting StaticResource to DynamicResource per its header.
4. MEASURE the behaviour in a headless Avalonia harness outside the repository at the app's own 11.3.20, referencing Winnow.App so the REAL controls.axaml and tokens.axaml load over avares, with compiled bindings on as the app has them. Assert: the presenter takes the treatment; arrow keys mark the item with SurfaceHigh and Volt inside the template; an item that is not visible is skipped; Enter invokes the item's command; Escape closes the menu and focus returns to the trigger; and the Hide item's ancestor-Window binding still resolves from inside a popup root.
5. Replace the inline StackPanel with the MenuFlyout in GameDetailsView.axaml, carrying every command, Click handler, IsVisible and tooltip across unchanged.
6. The trigger stays ONE Button.secondary wearing the shipped More label, so the strip is the 357/347px already measured in docs/spikes/details-action-band-width.md and no re-measurement is owed. The open/close label swap goes: a menu is not a two-state disclosure.
7. View model: the flyout owns open state, so MoreActionsOpen and ToggleMoreActionsCommand go; the label and tooltip stay. Update the two tests that pinned the toggle.
8. docs-writer authors every string, XAML comment, XML doc comment, the design-system.md 10.3 / 10.9 / 10.10 prose, the new spike, and the docs/decisions.md appends.
9. dotnet build Winnow.slnx then dotnet test per project to the scratch output path, from PowerShell.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
── MECHANISM, CHOSEN AGAINST CURRENT AVALONIA DOCS AND THEN MEASURED ──

A MenuFlyout hung off the trigger with Button.Flyout, placement BottomEdgeAlignedLeft, and
FlyoutPresenterClasses set to actions. Not a bare ContextMenu, which waits on a right-click;
driving one from a left-click handler is off-label. Button.Flyout is also the idiom this app
already ships, since MainWindow.axaml opens the sort menu exactly this way.

The shipped ContextMenu.actions treatment moved from MainWindow.axaml into
Themes/controls.axaml, which is that file own stated rule (a style moves the moment a second
surface needs it). Each selector now names BOTH roots in one declaration, the context menu and
the MenuFlyoutPresenter together, so one set of setters covers two surfaces and the grid and
the modal cannot drift apart under one class name.

── WHAT THE PROBE FOUND ──

A throwaway probe outside the repository at Avalonia 11.3.20, referencing the BUILT Winnow.dll
so it loads the real controls.axaml and tokens.axaml rather than a copy, with the band markup
copied verbatim out of GameDetailsView.axaml and compiled bindings on as the app has them. Run
on BOTH backends - headless (Skia, the real Plus Jakarta Sans), where Avalonia hosts the popup
in the overlay layer, and the real Win32 backend, where the popup is its own PopupRoot window
with the probe window parked off-screen. Identical results on both, every check passing, over
both row sets (installed, four rows; not installed, three).

1. THE KEYBOARD LANDS ON focus, NOT selected. A menu opened from a BUTTON focuses its first
   row without selecting it - every row reported IsSelected false while the first drawn row
   carried the focus and focus-within pseudo-classes. The shipped treatment answered the
   selected state only, so it would have left unmarked the one row a keyboard user meets
   first. The same setters now answer both states. That is the only change to the treatment.
2. A MenuItem RAISES ITS Click EVENT ALREADY HANDLED (observed handled, bubbling, sourced at
   the row), so a XAML Click handler never runs. A XAML-wired handler stayed at zero
   invocations while the same event drove the row own command. The three rows that do view
   work beyond their command are wired with AddHandler and handledEventsToo.
3. A NAME INSIDE A FLYOUT DOES NOT REACH A CODE-BEHIND FIELD. The trigger resolved from its
   generated field; the rows inside its flyout were null. They are found through the trigger
   own Flyout.Items.
4. Escape closes the menu and focus returns to the trigger, and so does activating a row. Both
   come free on both backends; neither needed code of ours.
5. The walk skips a row that is not drawn, in both directions, and wraps rather than
   dead-ending.
6. The mark is drawn in the item template - PART_LayoutRoot takes SurfaceHigh with a Volt edge
   on a border held at 2,0,0,0 in every state. No adorner layer anywhere in it.

Evidence: docs/spikes/details-action-band-menu.md.

── HIDE NO LONGER GOES THROUGH THE WINDOW ──

The Hide control used to reach the library through a parent-Window binding for its command,
label and tooltip. A popup root has no Window above it for that binding to find, so the library
hands HideGameCommand to GameDetailsViewModel when the modal is built. The words still come
from LibrarySettingsCopy, the same constants the grid context menu uses, so there is one source
for them. A null command draws no row rather than an inert one.

── WIDTH ──

Nothing on the strip changed - the same four controls in the same classes, and the trigger
keeps the More label that was already measured. The trigger no longer swaps to Close, so the
strip widest form is now the 357px (Install) case from docs/spikes/details-action-band-width.md
rather than that spike 361px, against a 420px column at the card minimum width. No
re-measurement was owed and none was taken. The menu card itself measures 134 x 103px at three
rows and 134 x 134px at four, about 31px a row, and it floats over the modal rather than
sitting in a row of the card grid - so a further action costs one menu row and no width at all,
and opening it reflows nothing.

── THE MODAL DOES NOT CLOSE UNDER THE MENU ──

The detail modal answers Escape from anywhere by closing (MainWindow.OnKeyDown), so an Escape
aimed at an open menu could plausibly have closed both. Measured with MainWindow own handler
shape replicated on the probe window - base call, an early return when the event is already
handled, then the Escape branch. On both backends, over both row sets, the window saw that
Escape ZERO times while the menu was open, and saw it once when Escape was pressed with the
menu closed. On Win32 the menu is its own top-level window, so the key never reaches the main
window at all; in the overlay case the menu marks it handled first and the early return holds.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The four folded actions now open as a MenuFlyout of MenuItems from the More trigger, replacing TASK-123 inline vertical button list, at the user request. FlyoutPresenterClasses="actions" reaches the MenuFlyoutPresenter, so the modal reuses the shipped treatment the library grid context menu already uses rather than a third pattern.

The popup objection that drove the inline form does not apply: a menu draws its mark inside the item template rather than through the adorner layer. design-system.md §10.7 was narrowed accordingly, from a blanket no-flyout rule to exactly one permitted popup with the reason stated, so the rule stays meaningful rather than being one the code visibly breaks.

Measured headlessly rather than assumed (docs/spikes/details-action-band-menu.md). The finding that mattered: the keyboard position in a menu opened from a BUTTON is :focus, not :selected — the shipped ContextMenu.actions styling answered only :selected, so keyboard focus would have gone unmarked. The styling now answers both. Two further Avalonia constraints were measured and worked around: a MenuItem raises Click already marked handled, so rows are wired with AddHandler rather than a Click attribute; and a Name inside a flyout does not reach a code-behind field.

Escape closes and returns focus to the trigger; Enter runs the row and closes; the walk skips undrawn rows and wraps in both directions. The strip was not re-measured because it did not change — the same four controls in the same order — and the menu card grows in height, not width: 134x103px at three rows, 134x134px at four, so a further action still costs no width.

Build clean; 3265 / 152 / 78 pass. Appearance in a running window is unverified.
<!-- SECTION:FINAL_SUMMARY:END -->
