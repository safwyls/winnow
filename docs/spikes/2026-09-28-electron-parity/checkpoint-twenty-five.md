# Epic HTTP contracts and manual continuation — 2026-09-29

The original interactive sign-in contracts now have evidence at both boundaries. Native
capture passes one code kind and main-owned state; the real authenticated backend endpoint
exchanges the code through the existing token pipeline. Tests inspect the provider's exact
form fields, preserve distinct rejected-code/client outcomes and verify that codes do not
appear in URLs, trace logs or results. Challenge tests assert the canonical consent,
registered redirect, cold login URL, harvest URL and fresh state for every attempt.
Cancellation, expiry, foreign ownership and missing/mismatched state refuse code exchange.
Encrypted sessions survive backend restart; unavailable encryption keeps them in memory.

An unavailable/throwing embedded host, unusable capture or missing session selects the
manual form with the same attempt. This form supplies Electron's equivalent of the original
console peer. The user explicitly opens their browser and submits the masked final address.
Tests cover that complete continuation, cancellation without another prompt, fresh state on
the next attempt, one submission and truthful persistence feedback. Provider rejection
does not advance to another prompt.

Visual review found desktop-sized text in the fullscreen Epic form. It now uses the source
64px title, 28px body/action/input text and 64/72px control targets, preserving consent
paragraphs. The page scrolls long consent text and keeps the focused actions reachable.

## Verification

- All 13 new production HTTP tests pass in six seconds: `.tmp/epic-parity-second.log`.
  The initial compile identified a nullable redirect assertion and private credential
  constructor in the fixture; both were corrected before execution.
- The complete backend assembly passes 105 cases without skips in 16 seconds:
  `.tmp/epic-backend-full.log`. No backend production code changed.
- All 24 cases for twelve retained Epic methods pass: `.tmp/epic-retained-first.log`.
  Source review confirmed unchanged production credential registration/selection,
  completeness authority, re-reading under the sync gate, and local/API candidate merging.
- The focused Electron controller and renderer group passes 81 cases:
  `.tmp/epic-fallback-components.log`. Typecheck passes.
- Build/typecheck passes in 6.33 seconds: `.tmp/epic-readability-build.log`.
- All three native full-app workflows pass in 20.4 seconds:
  `.tmp/epic-workflow-final.log`. Desktop and fullscreen at 100%/140% text use production
  main/preload/renderer, real backend challenges and isolated provider responses. They
  exercise unusable embedded capture, explicit browser continuation, cancellation and
  successful manual submission. Token exchange responses are intercepted here; the new
  HTTP tests above exercise that separate production boundary.
- A controlled check against the previous build failed at 32px versus the source 64px
  title: `.tmp/epic-typography-red.log`. The enlarged-text fixture initially compared a
  JavaScript floating-point string with Chromium's rounded CSS value. Decimal normalization
  fixed the assertion; the expected 39.2px text size remains unchanged.
- Desktop and enlarged fullscreen captures were inspected. The native test checks that
  Finish connecting lies inside every clipping ancestor before activation.
- The first complete component/API run passed 2,755 of 2,756 cases. An existing Steam
  saved-import retry fixture did not wait for the shared busy lock to release.
  It now waits for the enabled control, retaining the original two imports and no-recapture
  assertions. All 98 affected cases pass three consecutive runs; the complete repeat passes
  2,756 cases across 136 files without skips in 46.47 seconds:
  `.tmp/epic-full-integration-repeat.log`.
- The full native run passes 200 of 201 checks in 9.9 minutes:
  `.tmp/epic-native-full.log`. A feed fixture checked its snapshot before explicitly waiting
  for the backend connection and reduced either a failed request or an empty feed to zero.
  It now awaits connection and asserts the full request result separately. Both viewport
  cases pass three consecutive repeats, six checks in 33.1 seconds:
  `.tmp/epic-feed-repeat.log`. All original exposure assertions remain. The preceding
  failure did not preserve the request error, so its exact cause is not established; a
  combined 201-case pass is not claimed.

Fourteen original methods have new ported evidence, twelve are retained backend contracts
and two WebView2/Avalonia runtime probes are framework-specific. Electron bundles Chromium;
its initialization failure and continuation have separate tests. The inventory contains
1,006 ported, 552 retained backend, 15 framework-specific, 735 pending and 127 partial methods.
Legacy console-host registration and other original workflows remain in the inventory.
Live Epic/MFA/provider changes and physical-controller validation are not established by
these fixtures. Migration and task acceptance criteria remain incomplete.
