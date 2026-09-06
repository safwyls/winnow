---
id: TASK-146
title: Stabilize grid card actions and refine their layout
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 19:54'
updated_date: '2026-09-06 20:14'
labels:
  - ui
dependencies: []
references:
  - design-system.md
modified_files:
  - design-system.md
  - docs/decisions.md
  - src/Winnow.App/Views/GameTileView.axaml
  - src/Winnow.App/Views/GameTileView.axaml.cs
  - tests/Winnow.Ui.Tests/CardDetailsInteractionTests.cs
priority: high
type: bug
ordinal: 173000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Grid-card hover actions intermittently stop responding or remain visually stuck after pointer interaction. Diagnose and fix the reveal/hit-testing lifecycle. Move the Play/Install action to the bottom-right aligned with the resting store mark, and replace the Details button with a top-right folded-corner dog-ear affordance while preserving double-click Details.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Repeated pointer entry, exit, presses, focus changes, scrolling, and recycled tiles do not leave either action stuck or unresponsive
- [x] #2 The Play/Install icon sits at the bottom-right in line with the resting store mark and continues to invoke only the honest primary action
- [x] #3 Details is a recognizable, accessible top-right corner fold/dog-ear with a stable hit target and opens the modal; non-control double-click still opens Details
- [x] #4 Unread, store and grouped-expansion marks remain legible and non-overlapping at the 108px density floor
- [x] #5 Headless interaction tests reproduce the prior intermittent failure and cover the corrected behavior; the visual specification and decision record match the new layout
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce the stuck/unresponsive sequence in the headless tile fixture, including pointer-click focus retention, rapid leave/re-entry, and container rebind/detach. 2. Replace the reveal feedback loop between :pointerover/:focus-within and IsHitTestVisible with explicit GameTileView interaction state that distinguishes keyboard focus from pointer focus and resets outgoing tile hover state during recycling. 3. Recompose the primary action as a fixed bottom-right icon aligned with resting marks and Details as an accessible top-right dog-ear, reserving separate space for the unread badge. 4. Extend hit-target, focus, command isolation, minimum-density, and recycling coverage; visually inspect captures. 5. Align design-system.md and docs/decisions.md, then run focused and full build/test verification.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Root cause: the reveal selector conflated pointer hover with focus-within. Clicking an action gave it pointer focus, which pinned the action hosts after pointer exit; recycling could transfer the focused/revealed descendant to a new tile. The view now tracks pointer presence separately from Tab/directional focus, clears outgoing state on rebind/detach, and resolves captured pointer moves from actual tile geometry. Play/Install now occupies the bottom-right store row; Details is a 40px top-right dog-ear; unread sits below it; the grouped-expansion pip yields the bottom-right corner while actions are revealed.

Regression evidence: Pointer_click_then_exit_does_not_pin_the_reveal failed against 79ca1b1 with expected opacity 0 / actual 1. Focused interaction suite passed 22/22, including 12 immediate first presses, capture-drag-exit, rebind/detach, keyboard-focused rebind, 108px unread/fold separation, expansion-mark yielding, repeated command hits, and double-click Details. Settled 108px captures were visually inspected. Full build passed with 0 warnings/errors; full solution tests passed 3757/3757.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Separated pointer hover from keyboard action focus to eliminate stuck and intermittent grid controls, added lifecycle cleanup for recycled/detached tiles and pointer capture, moved Play/Install to the bottom-right store row, and made Details an accessible top-right dog-ear with the unread badge below. Verified with a reproducing regression, 22 focused headless interaction tests, settled minimum-density captures, a warning-free full build, and 3757 passing tests.
<!-- SECTION:FINAL_SUMMARY:END -->
