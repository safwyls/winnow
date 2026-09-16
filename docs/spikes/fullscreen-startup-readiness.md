# Fullscreen startup readiness

## Follow-up investigation — 2026-09-15

After the repeated shelf movement was fixed, the user reported one initial flicker and
several seconds of apparent unresponsiveness. The prior regression sampled background
refreshes of an already displayed page; it did not cover entry before initial data was ready.

`MainWindow.EnsureTelevision` attached the fullscreen interface before awaiting
`FullscreenContext.LoadAsync`. That load reads presentation preferences, loads its independent
library and awaits the primary feed. Consequently, initial preferences and data could change
an already visible page. Merely adding an overlay without yielding would not guarantee a
first paint before synchronously completing repository calls started work.

Library reads already ran on a worker. Work/release mapping, grouping and tile construction
then ran uninterrupted on the UI thread. Cooperative preparation now allows input and
rendering between batches while keeping all new models local. Final publication remains
uninterrupted, and stale, cancelled or disposed preparations cannot publish.

## Verification method

Startup tests control render-frame callbacks to check that loading does not begin before
the first presentation opportunity and the page is not revealed until loading and subsequent
layout opportunities complete. Additional cases exercise failure/retry, exit/re-entry and
disposal. A separate real-frame test checks the production scheduling path. The readiness
boundary deliberately excludes optional artwork downloads and supplemental recommendations.

A 512-title library fixture posts an input callback when UI-thread preparation starts.
That callback must run before any new tiles are published. The same callback cancels,
disposes or starts a replacement load in separate cases; only the winning complete library
may become visible. These are ordering assertions rather than machine-speed thresholds.
They verify opportunities to process input, not an end-to-end latency guarantee on the
user's hardware.

The loading layer was inspected at normal and 140% text size and in its failure state.
Window-level tests also cover content assignment before visual attachment, early exit,
controller navigation after readiness and warm re-entry. Preparation duration and failures
are logged through the normal application diagnostics. Tests and captures use preview or
temporary library data; the running app and production database were not changed.

The final full UI run passed all 738 tests, with no failures or skips. An additional 157
library, filtering, list, identity, architecture and documentation tests passed. The initial
full UI run exposed one controller test that sent navigation before preparation completed;
it now waits for readiness and retains its original navigation assertions. Final UI results:
`C:/Temp/winnow-299/results/fullscreen-startup-ui-final.trx`.
