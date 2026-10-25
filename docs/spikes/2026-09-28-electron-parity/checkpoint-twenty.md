# Compact Library and identity refresh — 2026-09-29

Fullscreen Library now puts its title and result summary above one compact row of
collections, My lists, Filter & sort and More. Options keep the two-row grid attached
and inert. Lists and filters replace the options panel; Back returns directly to the
same game, row and column. An empty Library returns focus to its active collection.
Desktop keeps its rail and inline controls.

The options panel exposes manual ordering/removal, live-filter save/restore, list
creation, rename, cancellable deletion and selected-game actions. Choosing the current
list keeps it open. The original Weekend reorder/remove and Steam-to-GOG live-filter
fixtures now exercise these paths. Controller focus has an explicit visible outline
after mouse input, and panel actions align their text to the left.

Identity review keeps cached proposals visible during a new read but disables decisions
until that read settles. Returning from Details retains its focus target while controls
are disabled, including a refresh that returns an unchanged snapshot. Platform controls
and Details actions follow the same readiness as their handlers. Closing desktop Details
waits for its retained opener to become available; a subsequent keyboard or pointer
action cancels that restoration. Controlled desktop/fullscreen tests cover revision,
open option sheets, disabled openers, detached nodes and subsequent user input. The native revision refusal
that prompted this investigation was intermittent; the controlled race establishes a
real defect without claiming that it proves the cause of that first failure.

## Verification

- Build and typecheck pass: `.tmp/library-ready-build.log`.
- All 2,643 component/live-backend cases pass in 132 files, no skips, 43.55 seconds:
  `.tmp/library-ready-integration.log`.
- The three native preflight cases pass with clean completion in 29 seconds:
  `.tmp/library-final-preflight.log`. They cover the 900-game controller return and
  both long-title identity-review surfaces, including Details return focus.
- All 16 repeated long-title and platform-preference cases pass across both surfaces
  with clean completion in 1.7 minutes: `.tmp/identity-ready-native.log`.
- The final complete native suite passes all 157 cases with clean completion in
  6.1 minutes: `.tmp/library-verified-native.log`. Eight repeated batch-review cases
  also pass in 37.8 seconds: `.tmp/library-keyboard-ready.log`.
- The native Library fixture moves sixty rows from column two at 1920×1080, retains
  the same viewport node and bounds, rejects focus entering the covered grid, and
  returns through options, lists, filters and Quick menu. The 1280×720/140% text
  composition and 1920×1080 captures were inspected.
- Empty-feed history and exact NotInterested history revocation pass on both surfaces;
  `.tmp/feed-history-parity.log` contains all 33 feed UI cases.

The first complete 155-case run passed 149 cases. Five failures used old inline Library
selectors; one was the intermittent identity conflict. A subsequent 157-case run passed
156 cases and exposed a click before the identity refresh settled. The next focused
preflight caught the disabled Details-return target. These failures remain in
`.tmp/library-compact-native-full.log`, `.tmp/library-options-native-full.log` and
`.tmp/library-options-preflight.log`; assertions and timeouts were retained while the
interaction paths and focus handling were corrected.

The next complete run passed 155 of 157 cases (`.tmp/library-final-native.log`). It
exposed still-clickable platform preferences and Details actions during refresh. The
new platform tests failed on both modes before the fix, including an already-open
choice sheet; the desktop Details readiness assertion also failed before its fix.
An intermediate repeated run passed 12 of 16 (`.tmp/identity-platform-native.log`):
one desktop focus failure preceded that Details readiness fix, and three fullscreen
fixtures pressed Enter while their target was disabled. The fixture now waits for
the target's enabled state before sending Enter. No assertion or timeout was relaxed.
The subsequent complete run passed 156 of 157 cases (`.tmp/library-ready-native.log`)
and caught the same readiness issue in the desktop batch fixture's first ArrowDown.
It now waits for both review rows before moving focus; all batch, focus and persistence
assertions remain intact. The repeated batch cases and final complete suite above pass.

Five additional original methods now have replacement evidence. The frozen inventory
contains 930 ported, 540 retained backend, 13 framework-specific, 815 pending and 137
partial methods. The full migration remains in progress, with every task acceptance
criterion unchecked. No backend code changed in this checkpoint.
