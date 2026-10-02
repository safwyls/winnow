# Independent cover presentation and size upgrades

A visible cover keeps its ready pixels while a larger image loads. The surface retains its
best decoded size when density decreases, and a late small response cannot downgrade it.
Recycling changes the image identity immediately and releases the outgoing hold; another
surface showing the same game keeps its own image. A failed upgrade retains the ready
image and retries on the next measurement. Decode-error eviction now targets the displayed
image's cache key, including while a different size is pending.

Dormancy uses Chromium's CSS filter over the vivid decode. It therefore needs no separate
floor bitmap. Native tests verify disabled, enabled and mid-decode preference changes on
desktop/fullscreen covers and desktop merge thumbnails. Fullscreen merge review uses text
and member sheets, so that thumbnail contract belongs to desktop; its cover path is checked
separately. Warm pixels and DOM image sources remain unchanged by dimming.

## Verification

- All 49 focused artwork and cache cases pass: `.tmp/cover-presenter-focused.log`.
- Build and TypeScript check pass: `.tmp/cover-presenter-final-build.log`.
- All 64 native cases pass in 1.2 minutes. The group covers artwork lifetime, artwork
  browser, cover controls, recommendation cards and real-backend feed workflows on both
  surfaces. Evidence: `.tmp/cover-presenter-native-regression.log` and
  `.tmp/cover-presenter-native-final/results.json`.
- All 3,195 component/live API cases pass in 158 files without skips, in 49.94s:
  `.tmp/cover-presenter-integration.log`.
- Five added component cases reproduce the source's 148px/108px shared surface and
  300px/108px independent sizes, control response ordering, check cancellation and object
  URL publication, and assert zero live leases after the final consumer leaves.
- Native frame sampling observes eight consecutive ready frames with the original image
  during a held upgrade. Shrinking after completion preserves the larger source; detaching
  the wall leaves the feed visible. The synthetic feed capture was inspected.
- The initial component run passed 30 cases and failed the two new upgrade/downgrade
  assertions. One failed test also left a held request unresolved during cleanup; its
  `finally` now releases that request. The production fixes pass the complete rerun.
- The first native lifetime group passed all 11 cases. After simplifying size retention
  to the best decoded image and correcting displayed-key eviction, the broader final
  native group also passes. Formatting uses `npm exec -- prettier --write`; earlier
  invocations omitted npm's argument separator and did not format the files.

All twelve `CoverPresenterTests` methods have equivalent evidence. Floor-layer assertions
are expressed through Chromium's one-image composition and request counts, with the
mechanism change recorded per method. The inventory is 1,192 ported, 625 retained backend,
20 framework-specific, 497 pending and 101 partial methods out of 2,435. Complete migration
and the final combined native run remain open. No .NET source changed.
