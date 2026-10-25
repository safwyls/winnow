# Lossless activation and listener readiness — 2026-09-29

Shell activations preserve the full positive signed 64-bit ownership range. Values within
JavaScript's exact range remain numbers; larger values remain validated canonical decimal
strings through process handoff, preload, renderer and URL construction. Already-rounded
numbers, noncanonical strings and overflow are rejected.

For extended IDs the renderer asks the backend for the current Primary action instead of
looking up a rounded identity in its numeric library snapshot. This additive action uses
the existing play/install selection, authorization and operation-ID deduplication. Ordinary
theme launches keep their existing path. This change does not claim general lossless IDs
through every numeric JSON library projection.

## Verification

- All 2,907 component and live API cases pass across 145 files without skips in 49.32s:
  `.tmp/activation-int64-full-integration.log`. The focused 92-case run also passes:
  `.tmp/activation-int64-focused.log`. Build and final typecheck pass:
  `.tmp/activation-int64-build.log`, `.tmp/activation-int64-final-typecheck.log`.
- All 109 backend tests pass after a fresh build in 16 seconds:
  `.tmp/activation-int64-backend.log` and its TRX. Four new cases store exact ownership
  IDs above the JavaScript limit, including Int64-max, and issue real HTTP commands.
  They assert exact fake-launcher identity, current Play/Install selection, duplicate
  operation dispatch once, and conflict on reusing the operation for another action.
  Existing invalid-store-identity cases also reject Primary without reaching the launcher.
- All eight native activation workflows pass together in 49.8s:
  `.tmp/activation-int64-native-final.log` and its copied `results.json`. The complete
  original cold-start sequence (game 23, fullscreen, game Int64-max) reaches the native
  pending queue in order. Desktop and fullscreen both send the exact Int64-max HTTP path
  through the production transport and display the backend's not-found response for the
  absent fixture entry, without launching an installed game.
- A real secondary process requests the profile lock while the owner is held before
  listener registration and Electron readiness. A marker is written immediately before
  the child's lock call. After the owner resumes, the child exits successfully and its
  fullscreen request drains normally. No simulated second-instance event substitutes for
  this readiness check.
- The initial native run passed six cases. Two visible-window cases awaited Skip setup,
  but `--seed-sample` intentionally suppresses automatic setup. The fixture now asserts
  the actual `step: null` API result and visible navigation before sending the activation;
  the URL and error assertions are unchanged.

Inventory: 1,055 ported, 559 retained backend, 17 framework-specific, 695 pending and
109 partial. The typed FIFO and listener-startup methods are now ported. Packaged executable
and Windows shortcut launch variants remain within the partial process-handoff method.
The latest complete native suite is still the preceding 214-case pass; the expanded
222-case suite has not yet run together. The overall migration remains incomplete.
