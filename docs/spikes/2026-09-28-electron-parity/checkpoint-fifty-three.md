# Details history, refresh and confirmation

Fullscreen Play history now initially focuses Lifetime. Its two range controls share a
focus row, so controller Right followed by Accept selects Tracked sessions. If the Details
snapshot is still loading, the initial focus waits for the history controls to arrive;
leaving the reading page cancels that observer. About retains its Back-first scroll behavior.

Expansion separation keeps the safe action first and stacks fullscreen's two choices so
controller Down reaches confirmation. The request carries the child identity and expected
link ID. A slow launch-version response now restores the chooser when its temporarily
disabled opener becomes available; another user action cancels delayed focus restoration.

New native tests also establish existing behavior for single-copy achievements, very long
titles, refreshed Details with an open draft, and a shared journal prompt attached to
fullscreen. These are additional parity evidence rather than new product capabilities.

## Verification

- Build and typecheck pass: `.tmp/details-contracts-build.log`.
- All 3,322 component/live API cases pass across 162 files without skips in 45.00s:
  `.tmp/details-contracts-integration-final.log`.
- All 19 native Details/controller cases pass without retries or skips:
  `.tmp/details-contracts-native-final.log` and
  `.tmp/details-contracts-native-final/results.json`. Nine new cases cover both presentations
  for achievements, draft-preserving refresh and child-end separation; fullscreen covers
  history input, the original ten-repeat title and pending-prompt attachment/dismissal.
- The long-title capture in `.tmp/details-contracts-native-final/long-title.png` was inspected.
  The two-line heading and all hero actions remain above section navigation.
- The slow chooser focus case failed before the fix in `.tmp/launch-focus-reproduction.log`.
  All 133 focused action/Details/launch-component cases pass in
  `.tmp/details-contracts-components.log`, including that held-response case.
- Native fixture corrections use the real DELETE identity route, the actual prompt selectors
  and a single synthetic child game so virtualization cannot hide the target. Child identity
  99 uses the seed game's read/artwork endpoints while the mutation boundary records the
  requested child. This matches the original test's recording delegate; it does not establish
  additional database-persistence coverage.
- Whole-app routing tests allow five seconds for asynchronous queries. The previous one-second
  default failed during initial hydration under concurrent worker load, even with controlled
  presentation frames. Assertions and production timing are unchanged. The focused 51-case
  routing run and the complete component run both pass.

Six original methods gain complete evidence: five pending methods and the child-end
confirmation method previously marked partial. The inventory is 1,253 ported, 625 retained
backend, 23 framework-specific, 434 pending and 100 partial out of 2,435. The remaining two
FullscreenDetails partial methods cover rich/empty section geometry and cinematic artwork
selection, pixels and lifetime. The overall migration and release cutover remain open.
