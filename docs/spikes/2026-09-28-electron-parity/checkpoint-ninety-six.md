# Checkpoint 96 — combined migration regression gates

TASK-381.41 validates the combined Electron migration. The source audit accounts for
2,435 methods in 299 files: 1,688 ported, 706 retained backend and 41 framework-specific,
with no pending or partial mappings. That classification is separate from test passage.

## Initial integrated result

[CI run 36979618488](https://github.com/safwyls/winnow/actions/runs/36979618488)
tested branch commit `4e09761a` through merge checkout
`125dfb9badea0c8bf738e6d97c3b52bb64d85c70`. Both Windows and Ubuntu package jobs,
Linux sessions and plugins passed. The frontend, reference/backend and native gates
failed, so the required aggregate correctly refused completion.

The eight native reports contain 923 passes, 85 failures and three cases that did not
run after fixture setup failed. All reports and diagnostics were downloaded under
`.tmp/task38141-native-first/shard-1` through `shard-8`; the complete failure ledger is
`.tmp/task38141-native-failures.json`. The unit report contained 4,423 passes, four
failures and five incomplete cases after a worker crash. These results remain failure
evidence, rather than being replaced by the focused runs below.

## Production corrections

- Theme file serving canonicalizes the trusted data-directory anchor, including
  Windows short-name paths, before enforcing package containment. Package roots,
  intermediate directories and assets still reject symbolic links. The theme watcher
  observes the canonical directory and retains its debounce and disposal behavior.
- Initial saved window preferences can be read by the main process before the SSE
  stream connects. This read uses the existing validated, authenticated transport and
  startup deadline; it does not mark the renderer connected or enable disconnected writes.
- The startup diagnostic email scrubber avoids quadratic matching on a long non-address
  prefix. Redaction tests retain their original time limit and privacy assertions.
- A child Metadata or Artwork dialog consumes its own Escape event before unmounting,
  keeping the parent dialog, draft and return focus. A delayed fullscreen Details focus
  callback now checks whether a child dialog has hidden its parent. Activity again
  exposes its local trigger navigation hints. The native window title remains Winnow.
- Feed invalidations reach the scoring coordinator immediately while unrelated library
  reads remain queued. Changes received during one held scoring pass coalesce into the
  required replay instead of producing a late third read.
- Segmented controls use a separate inner clip layer. Software rendering reproduced
  changing outer antialiased corner pixels; full-window repainting did not fix them.
  Isolating the inner clip fixed the exact matrix on both rendering paths. The tests
  retain the negative control that removes clipping and exposes square selected fill.
- Fullscreen Details reserves eight logical pixels when scrolling focused content into
  its reading pane. At 720p, the prior nearest-edge scroll cropped the button slightly
  and left no room for its focus outline. Native checks require both to remain visible.
- Linux portable handoff identifies its parent by kernel process birth ticks, avoiding
  runtime-dependent UTC conversion. Legacy callers conservatively wait for the observed
  live identity. Windows update workers no longer inherit their caller's standard pipes:
  the real copied-helper regression reproduced an exited command with open pipes, then
  passed after the launch correction. Existing parent deadlines and installation/data
  leases remain in force.

## Fixture and evidence corrections

The CI unit job explicitly installs the pinned Electron executable, matching the native
job. Backend startup fixtures wait for their no-sync startup operation before installing
fake artwork sources. Process-parent fixtures use the existing BCL fixture executable
before backend startup. Reference UI fixtures use a controlled clock and animation frames.
The required Ubuntu updater step includes real Linux parent-identity cases.

Native fixtures now wait for actual initial load and dialog focus restoration, use owned
games when opening Details, capture the intended custom-date request, and distinguish
logical CSS dimensions from painted dimensions after zoom. Isolated native bundles embed
the actual font bytes. Existing semantic selectors follow the accessible markup without
dropping workflow assertions. Held-feed tests wait for real SSE delivery before releasing
the held request. Twenty-six mapping rationales were updated without changing any status
or test reference; terminal authentication is distinguished from the original Avalonia type,
and Activity's requested local trigger glyphs replace its former absence-of-hints assertion.

## Focused verification

- The final combined build and complete component/live-API rerun pass: 4,451 tests in
  232 files, no failures or skips, 200.63 seconds with two workers. The JSON report is
  `.tmp/task38141-final-unit-results.json`; build and test logs use the matching
  `task38141-final-build.log` and `task38141-final-unit.log` names. This local result does
  not substitute for the complete native, .NET, Linux and package CI matrix.
- The first complete local component/live-API run finished in 209.69 seconds with 4,448
  passes and two failures across 232 files. The Activity failure was its obsolete
  absence-of-hints assertion; the corrected contract file passes all 37 cases. The
  Metadata failure exposed a delayed parent focus callback while a child dialog was open.
  Its corrected Details, focus restoration and actions tests pass all 114 cases, including
  a positive check that initially exposed fullscreen Details still receives focus.
- Combined Electron build/typecheck and fresh isolated backend/fixture build pass.
- Startup privacy/boundaries: 51 passes. Feed coordination: 20 passes. Metadata, startup,
  theme and nested-dialog units: 216 distinct passes. Fixture cleanup: five passes.
- Updater: 47 passes; backend ownership/activation/artwork: 20 passes; terminal sign-in:
  27 passes. The final copied-helper pipe regression also passed after the handle
  restoration review. The two Linux-only process bodies were not exercised by this
  Windows run.
- Reference UI clock and visible-shelf exposure: eight passes. Identity-read inventory
  and README consistency: six passes. The real artwork-provider catalog and persistence
  regression passed against a freshly built backend.
- Root native batch: 86 of 87 passed in 10.4 minutes. Its sole 4K Install failure reproduced
  a test moving focus before the dialog's deferred opener restoration. The corrected
  helper verifies restoration first. The subsequent seven-case run passed, including
  1280 and 3840 fullscreen, diagnostics and both settings surfaces.
- Segmented software matrix: two passes; default-renderer typography suite: five passes.
  The permanent suite now explicitly covers both renderer modes.
- Controller, accessible keyboard and Details navigation: 17 passes; distribution lease
  and independent API client: two passes. Both clients received committed edits over
  the real event stream without bypassing primary frontend activation.
- Metadata, setup, artwork and startup: 27 distinct native cases passed. Four initial
  settings failures came from a GET-only mock advertising an unavailable plugin; the
  corrected fixture installs the real provider catalog after startup and retains the
  original persisted-order and focus assertions.
- Visual layout and workflows: 13 distinct native cases passed after diagnosing five
  initial failures. This includes both desktop Details sizes, cinematic backdrop disposal
  with the separate screenshot cache retained and then invalidated, Epic sign-in at two
  text sizes, fullscreen settings, source Activity navigation, and 720p plugin actions
  with complete button and focus-outline visibility.

Reports are retained in `.tmp/task38141-root-native`, `task38141-root-focus-final`,
`task38141-host-results`, `task38141-controller-results`,
`task38141-metadata-native-final-results`, `task38141-visual-final`, and the host/visual
evidence ledgers. Focused results establish the corrections' scope; the complete final CI
run is still required. Device observations and explicitly unverified environments are recorded
in [checkpoint 95](checkpoint-ninety-five.md) and TASK-381.40. No release is published.
