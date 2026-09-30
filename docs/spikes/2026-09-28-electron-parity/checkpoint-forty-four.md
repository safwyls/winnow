# Merge answers, row feedback and source guards

Merge answers now announce their included members and chosen header. Different games
names every member, including an excluded one; Separate again names the saved header.
Desktop and fullscreen share this copy and the original action and Undo tooltips.
The aggregate unread dot follows included members, while each row retains its own dot.

Desktop row details brighten over 120ms and restore their original reason after pointer
exit. Row fill restores over 140ms. These are the queue's only transitions; card geometry
stays fixed. Reduced motion removes both transitions without removing state feedback.

## Verification

- Final build and TypeScript check pass: `.tmp/merge-surface-final-build.log`.
- All 20 final native cases pass in 1.4 minutes with the verified backend configured:
  `.tmp/merge-surface-native-configured.log` and
  `.tmp/merge-surface-native-verified/results.json`. Six new surface checks run alongside
  the real backend merge and Library/Details suites, including both presentation modes.
- Chromium checks the actual transition durations and an intermediate text color, exact
  unchanged bounds, every descendant's transition properties, reduced motion, independent
  pointer/keyboard row actions, Flare usage, named answers and one Undo write.
- The first six-case native run passed five; its unread test kept the old article name
  after promotion correctly renamed the card. The corrected locator follows the new header.
  A broader attempt then lacked an explicit backend path: both production fixtures stopped
  at startup, 12 dependent cases did not run, and all six new probes passed. The final
  configured run above passes all cases, including strict fixture cleanup.
- The first complete component/API run passed 3,176 of 3,177 cases. Its new AST copy guard
  failed to normalize JSX whitespace; the corrected guard and complete merge component
  file then passed all 109 cases. Evidence: `.tmp/merge-surface-integration.log` and
  `.tmp/merge-surface-final-focused.log`. The final complete run passes all **3,177 cases
  across 157 files**, without skips, in 49.76s: `.tmp/merge-surface-integration-final.log`.
- Desktop and fullscreen captures from both the probe and real application were inspected.
  Long member titles remain inside their rows and controller sheets retain separate actions.

Nine original `MergesSurfaceTests` methods gain behavioral evidence. Three source guards
are classified as framework-specific: Avalonia parse-time resource lookup, ownership of
five named XAML selectors, and reflection over previously deleted CLR types/members.
These classifications do not waive rendered styling, current group/history behavior or
the remaining navigation work.

Five methods from that class remain pending: the original sort menu, centralized screen
copy, dedicated rail destination, and the two seven-key keyboard guards. Electron still
reaches review through Library tools and currently adds Home/End to its desktop queue
navigation. Those differences remain explicit follow-on work.

The inventory is 1,175 ported, 625 retained backend, 20 framework-specific, 514 pending and
101 partial methods, out of 2,435. The migration gate remains incomplete. The full .NET
Release evidence from checkpoint 39 is unchanged; this package changes no .NET source.
