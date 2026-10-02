# Recommendation card composition and preview bounds

Desktop recommendations now use the original shared portrait cover with independent
Play/Install and folded Details targets, an inset 48px feedback strip, and fixed title
and reason slots below it. The complete cover lifts once by two pixels. The feedback
receipt dims the cover, keeps the original geometry and presents its date and Undo on
separate rows. Recently played cards retain Add to list without recommendation verdicts.
Fullscreen retains its separate hero and directional cover presentation.

Preview bounds now use their measured size, including long summaries, and stay eight
pixels inside the client area. Rebinding a tile cancels pending detail requests and
closes the old preview. Escape and action presses suppress reopening until pointer exit;
a newly attached card requires fresh pointer movement. The cover retains its accessible
recommendation reason while a preview adds and removes its own description.

## Verification

- Final build and TypeScript check pass: `.tmp/feed-cards-final-build.log`.
- All 3,170 component/live API cases pass across 156 files, without skips, in 51.84s:
  `.tmp/feed-cards-final-integration.log`. The production backend path was supplied.
- All 47 final native cases pass in 37.1s: 23 recommendation-card cases, 20 shared cover
  cases and four production feed cases. Both presentation modes are included. Evidence:
  `.tmp/feed-cards-final-regression.log` and
  `.tmp/feed-cards-final-regression/results.json`.
- The original Avalonia `FeedCardActionTests` passes all 22 executed cases. Evidence:
  `.tmp/feed-source-results/feed-cards.trx` and `.tmp/feed-source-capture.log`.
- An earlier broader native run passed 66 of 69 cases, including all 22 Avalon layout
  cases. Three new probe assertions were corrected: caption text starts after its
  wrapper's eight-pixel padding, and an open modal hides the underlying Add control from
  accessibility queries. The final assertions still measure exact caption spacing and
  the retained control's styling. The 23-card follow-up passed before final corrections.
- Original 180/240px card captures were compared with Electron. Receipt review caught
  an inline date and a bright primary-action remnant; both were fixed before the final
  native and component runs. The final receipt capture was inspected.

The native matrix checks both source widths, aligned cover chrome, independent targets,
pointer and keyboard focus borders, fixed feedback/Undo geometry, edge previews,
stationary reattachment, same-work rebinding, cancellation, source tooltip copy and
restored Add focus after canceling its dialog. Source evidence maps all 12 methods in
`FeedCardActionTests` to their corresponding Electron assertions.

The inventory is now 1,166 ported, 625 retained backend, 17 framework-specific, 526 pending
and 101 partial methods, out of 2,435. The migration gate remains incomplete. The previous
complete 295-case native run belongs to checkpoint 42; this checkpoint records focused
native regression on the changed source, not a new complete run. No .NET implementation
or test changed, so the full Release evidence from checkpoint 39 remains applicable.
