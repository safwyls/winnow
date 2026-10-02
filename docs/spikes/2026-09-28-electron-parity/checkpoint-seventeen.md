# Desktop rail creation footer — 2026-09-29

The desktop rail keeps New list and the Settings gear below its scrolling content.
New list opens a keyboard-accessible Static list / Live list menu with explanatory
tooltips. Both naming prompts restore focus to New list after cancellation. Static
creation starts empty; live creation saves the complete current cut, suggests its
first two visible rules and opens the committed list. Failed writes retain the name,
and a pending write locks cancellation until its result is known.

Fullscreen retains its separate creation path. Shared list naming and persistence
remain covered on both surfaces.

## Verification

- Build and typecheck pass: `.tmp/footer-build-final.log`.
- Focused workflow and real HTTP cleanup contracts: 124 passed;
  `.tmp/footer-corrected-components.log`.
- Full component/live-backend suite: 2,621 passed in 129 files, no skips, 44.54 seconds;
  `.tmp/footer-integration-verified.log`.
- Corrected native modal, Library lifecycle and list fixtures: 23 passed, clean worker
  completion, 51.2 seconds; `.tmp/footer-native-corrected.log`.
- Complete native suite: 148 passed with clean worker completion in 5.7 minutes;
  `.tmp/footer-native-verified.log`.
- Native short-window tests measure footer alignment and reachability at 800 and 1200
  pixels, exercise menu wrapping and prompt focus, and verify static/live persistence
  through the real API and renderer reload. The 800×600 footer capture was inspected.

The first complete native attempt passed 140, failed three and did not run five after
a serial-fixture startup failure. A modal test read the API before the backend was ready;
two Library lifecycle selectors also matched the Settings Library section. Readiness
polling and scoped navigation selectors correct those test assumptions. Cleanup now waits
up to five seconds for late backend discovery before requesting authenticated shutdown.
The separate five-second Electron close deadline is unchanged. The orphaned scratch
backend from the failed run was identified by its exact fixture command line and stopped
through that fixture's authenticated endpoint. The exact transport delay behind the
accompanying worker teardown error was not established. The failed run remains in
`.tmp/footer-native-full.log`.

A subsequent component run exposed a palette test checking normalized profile state
after only waiting for fallback colors. Those colors can already be present before
React publishes normalization. The assertion now waits for the profile value itself;
the unchanged expected value passes in the full run above. The failed attempt remains
in `.tmp/footer-integration-final.log`.

The complete original rail section/footer method now has replacement evidence. The
frozen inventory contains 914 ported, 540 retained backend, 13 framework-specific,
830 pending and 138 partial methods. Other fullscreen, feature and source-test work
remains incomplete; task acceptance criteria remain unchecked.
