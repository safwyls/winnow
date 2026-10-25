# Architecture and activation parity — 2026-09-29

Electron now coalesces adjacent duplicate activations while its renderer is starting,
preserves the original 64-request bound, and rejects malformed structured requests without
restoring the window or dispatching an action. A request received before the window exists
still restores a background launch once that window is created. Ready sessions continue
to receive repeated requests individually. Each library directory owns an independent
single-instance lock, browser profile and backend.

Renderer/shared source checks parse imports, exports and erased TypeScript dependencies.
They include adversarial native/backend imports, escaped module names and ordinary strings
that must not be mistaken for imports. Separate in-memory builds inspect the full renderer,
drawing-worker and sign-in-host dependency graphs. The seven backend architecture rules
continue to run against the same shared .NET modules.

## Verification

- All 2,882 component and live API cases pass across 144 files without skips in 48.48s:
  `.tmp/activation-full-integration.log`. Build and final typecheck pass:
  `.tmp/activation-build.log`, `.tmp/activation-final-typecheck.log`.
- All ten original architecture tests pass after rebuilding the current modules to a
  separate output directory: `.tmp/architecture-dotnet.log` and
  `.tmp/architecture-dotnet/architecture.trx`. The retained seven rules still apply to
  Core, recommendation and launcher ingest. The three frontend rules have Electron guards.
- All four new native activation workflows pass together in 26.8s:
  `.tmp/activation-native-final.log` and its copied `results.json`. They verify real
  second-process FIFO delivery, duplicate coalescing, post-readiness repeats, trailing
  profile separators, rejection without showing a background window, queue capacity,
  two simultaneous independent profiles, shutdown and restart.
- A native lock-owning fixture without a backend accepts show/fullscreen/game/plugin
  launches. Secondary processes exit successfully without creating a database or backend
  discovery. An invalid installation URI exits 2 without activating the owner or creating
  those files. Its Chromium profile is explicitly isolated before argument rejection.
- The first native run passed three cases and timed out in the lock-owning fixture. Its
  top-level wait for Electron readiness prevented module loading from completing. The
  corrected fixture schedules its ready callback and opens a hidden blank window; its
  focused repeat passes in 1.7s and the combined four-case run above also passes.
- The initial architecture bundle check needed a `.woff` loader for the existing fonts.
  Adding that test loader preserves the dependency assertions. The final complete suite
  includes all fourteen architecture and seven activation-queue cases.
- The preceding complete 214-case native run passes in 18.0 minutes at `3125b61a`, as
  recorded in [checkpoint 28](checkpoint-twenty-eight.md). It predates these activation
  changes; a combined 218-case pass is not yet claimed.

Inventory: 1,053 ported, 559 retained backend, 17 framework-specific, 696 pending and
110 partial original methods. Two retired pipe-frame/handle tests are classified by their
obsolete transport mechanism, with structured-message validation tested at the new boundary.
The typed activation contract remains partial for valid Int64 ownership IDs above
JavaScript's safe integer range. Packaged executable/Windows shortcut launches and the
listener-startup contract also remain outstanding. This is still an incomplete migration.
