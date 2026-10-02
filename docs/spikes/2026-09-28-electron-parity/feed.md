# Electron feed receipts, history and visibility

Avalon's desktop feed and fullscreen hero share a receipt model while keeping their
separate layouts. Desktop shows five valid cards per shelf; fullscreen includes the scored
reserve. Recent-play shelves preserve the backend's order, reasons and capacity without
feedback controls. A saved response keeps its card in place and exposes Undo. Snoozes show
the backend's expiry date. A distinct reserve card can replace the receipt after three
seconds; pointer hover, keyboard focus, an inactive window, or an open dialog holds the
clock. Reduced motion uses one-second steps. A shelf without a distinct replacement keeps
its receipt. Backfills coalesce to one running read and one waiting read, and preserve
visible cards if an optional provider fails.

History includes active, lapsed, and reversed responses, with a fallback title for a game
no longer in the library. Undo from history also restores a visible receipt. Launch buttons
use the existing supported Play/Install facts and backend command path; manual entries keep
their original tracking-only behavior. Loading, failed, unscored, and quiet feeds have
separate states. Numeric words in a reason use the data font without changing the sentence.

One document observer records intersecting cards only while the window is visible and
focused. Hidden or inert ancestors, accessible modals and opaque hit-test coverage suppress
recording. A daily release ledger survives shelf and screen remounts; the next UTC day can
record an exposed card again. Removed cards detach their observers. Pending primary and
optional reads are cancelled on disposal, and a slow optional provider cannot delay a
library refresh. Previously completed shelves remain visible during that refresh.

## Verification

- `parity-feed-model.test.ts`: 24 cases cover durable kinds and backend expiry, failed writes
  and Undo, duplicate input, slot identity, countdowns, held receipts, distinct reasons,
  stale generations, cross-shelf deduplication, optional append identity, disposal and
  coalesced backfills.
- `parity-feed-ui.test.tsx`: 29 cases exercise both surfaces, including focus-preserving
  Undo, pointer holds, history, retry, reduced motion, inactive-window holds, and launch
  dispatch/retry. These use component fixtures and do not start an installed game.
- `parity-feed-query.test.tsx`: nine cases cover independent optional loading, superseded
  passes, cancellation, refresh continuity, optional failures and reserve-payload validation.
- `parity-feed-impressions.test.ts`: 17 cases cover shared observation, clipping, daily
  deduplication, window visibility, ancestor visibility, modal coverage, retries and cleanup.
- `tests/electron/feed.spec.ts`: four tests passed in a focused rendered run against a
  disposable seeded backend. Both surfaces save the backend expiry, focus Undo, undo from
  history, keep the restored card, and retain history after navigating away and back.
  The dialog is measured inside the viewport. Separate exposure checks read the disposable
  database to verify that covered or clipped cards are recorded only after they become
  visible. These tests do not dispatch game launches.
- `migration-feed.json` records individual source-method evidence. Twelve service methods
  remain covered by the original shared-backend .NET tests.

## Remaining source coverage

This package does not complete the feed migration inventory. Offscreen reserve promotion
still needs combined rendered evidence; cover-resource release, exact invalidation replay,
fullscreen movement/animation and detailed layout contracts still need equivalent tests.
History uses an accessible modal; its relation to the original alternate-body focus and
visibility assertions remains explicitly partial. Fullscreen recommendation counts and
recent-history capacity also need the complete source parameter matrix.
