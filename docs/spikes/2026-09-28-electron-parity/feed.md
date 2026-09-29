# Electron feed receipts and history

Avalon's desktop feed and fullscreen hero share a receipt model while keeping their
separate layouts. A saved response keeps its card in place and exposes Undo. Snoozes show
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

## Verification

- `parity-feed-model.test.ts`: 19 cases cover durable kinds and backend expiry, failed writes
  and Undo, duplicate input, slot identity, countdowns, held receipts, distinct reasons,
  stale generations, cross-shelf deduplication and coalesced backfills.
- `parity-feed-ui.test.tsx`: 21 cases exercise both surfaces, including focus-preserving
  Undo, pointer holds, history, retry, reduced motion, inactive-window holds, and launch
  dispatch/retry. These use component fixtures and do not start an installed game.
- `parity-feed-query.test.tsx`: five cases cover independent optional loading, superseded
  passes, optional failures, and reserve-payload validation.
- `tests/electron/feed.spec.ts`: two tests passed in the complete rendered run against a
  disposable seeded backend. Both surfaces save the backend expiry, focus Undo, undo from
  history, keep the restored card, and retain history after navigating away and back.
  The dialog is measured inside the viewport. These tests do not dispatch game launches.
- `migration-feed.json` records individual source-method evidence. Twelve service methods
  remain covered by the original shared-backend .NET tests.

## Remaining source coverage

This package does not complete the feed migration inventory. Exact viewport impression
deduplication across reloads and dates, cover-resource release, invalidation replay,
fullscreen movement/animation and detailed layout contracts still need equivalent tests.
History uses an accessible modal; its relation to the original alternate-body focus and
visibility assertions remains explicitly partial. Fullscreen recommendation counts and
recent-history capacity also need the complete source parameter matrix.
