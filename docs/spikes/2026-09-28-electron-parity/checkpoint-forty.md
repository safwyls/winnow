# Desktop cover actions and tile geometry

This checkpoint restores the original desktop cover's 32-pixel Play/Install button,
40-pixel folded Details corner, inside interaction ring, compact statistics and store
words. The source contract list remains frozen at
`cf45d9f1127243a987d3cf6e664a32fc767ecb67`.

## Behavior and presentation

Desktop covers retain single-click Details, selection modifiers, context actions and
hover previews. The primary control uses the existing ownership launch policy, prevents
duplicate pending requests and offers retry after failure. The sibling action buttons
have independent hit areas and keyboard focus; they are not nested in the cover button.

Pointer hover, keyboard focus and selection remain separate. Selection alone draws the
ring. Leaving after a pointer click hides the controls, while keyboard focus keeps them
available. Replacing or detaching a tile releases its outgoing focus and press. Fresh
pointer movement reveals the replacement; a late launch failure cannot affect it.

The title uses the original 15-pixel medium face with an 18-pixel line height. Store
words, statistics, unread dot and expansion mark retain their positions at 108, 148 and
200 pixels. Reduced motion removes transitions. Fullscreen keeps its distinct cover,
directional collection and Details launch path. Desktop feed-frame presentation is a
separate, still-pending source contract group.

## Verification

- All 26 original `CardDetailsInteractionTests` cases pass, including capture of the
  original 108/148/200-pixel layouts: `.tmp/cover-source-capture.log` and
  `.tmp/cover-source-captures`.
- The first 73-case native regression passed 64 and failed nine:
  `.tmp/cover-native/results.json`. Eight failures exposed a two-pixel double hover lift
  from competing CSS rules. One exposed Chromium synthesizing mouse-enter on reattachment
  under a stationary pointer. The corrections preserve the original strict assertions.
- All 20 final native cover cases pass in 20.8s:
  `.tmp/cover-final-native/results.json`. They measure geometry and source typography,
  test twelve immediate move/press sequences, canceled and recycled presses, keyboard
  focus, the entire Details rectangle, repeated real modal opens and both presentation
  modes. Final 108/200-pixel captures and the integrated Library capture were inspected.
- The 73-case run also passed every existing dashboard, artwork lifetime, shelf/grid
  layout, feed, Library lifecycle and rating-cap case. Its dashboard and rating tests
  verify the two fixture corrections from checkpoint 39 without removing their behavior
  assertions. All 13 of those dashboard/rating cases pass.
- All 3,154 component/live API cases pass across 156 files with no skips in 52.73s:
  `.tmp/cover-final-integration.log`. Final build/typecheck and inventory audit pass:
  `.tmp/cover-final-build.log` and `.tmp/cover-migration.log`.

Seventeen source methods now have complete evidence. Inventory: 1,147 ported, 559
retained backend, 17 framework-specific, 611 pending and 101 partial out of 2,435 methods.
The broader migration remains incomplete. These focused native results do not replace
a complete run on one frozen source snapshot.
