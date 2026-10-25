# Fullscreen action panels — 2026-09-29

Library options and Details More now share the original fullscreen edge-panel interaction.
The originating page remains attached, visible beneath the scrim and inert. Tab stays in
the panel; Play and Search cannot reach the covered page. Escape, controller B, right-click
and the scrim close one layer and restore its trigger. Right-click on a reading page first
uses that page's Back handler. Back at the fullscreen root opens Quick menu.

The panel uses the source's 620-unit width, 40-unit heading, 28-unit labels, safe margins,
left border, square corners, accent focus underline and 180 ms entrance. Text scale adjusts
its type and width. Reduced motion removes the entrance. The action list scrolls without
moving the retained page. It fills the viewport's height rather than inheriting the generic
dialog's 85vh limit. Desktop keeps its inline More menu and separate modal Details layout.

Opening metadata, matching or artwork removes the action panel. Nested Hide replaces its
actions over the same origin and starts on Cancel. Cancellation writes nothing; Hide sends
one request. A failed request leaves its error on the retained Details page after the panel
closes. Existing shared link, launcher, folder and metadata-refetch controls retain their
pending/result presentation within More; this checkpoint does not establish complete
source parity for those asynchronous control lifecycles.

## Verification

- All 168 focused component cases pass across the four affected suites in 9.49 seconds:
  `.tmp/actions-components-third.log`.
- All 13 new native cases pass with clean completion in 26.2 seconds:
  `.tmp/actions-focused-final.log`. They cover all dismissal and activation inputs,
  nested menus, the original fourteen-action fixture, disabled input, scrolling,
  100%/140% text, reduced/animated motion, 10% safe margins and 100%/120% interface scale.
- The composed native cases use the production renderer and isolated real backend. The
  action probe bundles the production panel, styles and controller hook into a sandboxed
  Electron window; its origin records attachment and detachment counts. It does not add
  a test route or bridge to the production application.
- The full-height 140% Details panel and fourteen-action scrolled captures were inspected.
  The latter retains the source's last action and visible focus underline within the
  scroll viewport. The isolated probe's origin is test content, not a product screenshot.
- The complete component/live-backend suite passes all 2,654 cases in 133 files without
  skips, 43.91 seconds: `.tmp/actions-integration-final.log`. Build and typecheck pass:
  `.tmp/actions-build-final.log`. The corrected fixture build also passes:
  `.tmp/actions-corrected-build.log`.
- The complete native suite passes all 170 cases with clean completion in 6.6 minutes:
  `.tmp/actions-verified-native.log`. This run includes the corrected request-interception
  fixtures and the assertion that the identity trace contains a successful real link.
- All 20 corrected action, artwork-browser and identity native cases pass in 56.1 seconds:
  `.tmp/actions-corrected-focused.log`. The action fixture supplies a viable Play action
  and intercepts dispatch before production constructs its HTTP transport. Play sends
  zero requests beneath the panel and exactly one after dismissal; no launcher opens.

## Failures resolved during verification

The first composed run referenced a detached Read more node after returning from About.
The assertion now locates the rendered return target and still requires exact focus.
The new native fixture initially left Details open between cases, where its navigation is
intentionally replaced by Back to Library. Each case now returns through that control.

The first native run also caught a real retained-grid regression: automatic focus during
the panel's entrance horizontally scrolled its parent. Focus now prevents parent scrolling;
directional movement scrolls only the action body. The original 900-game, sixty-row
geometry and retention assertions then passed unchanged. The failing evidence remains in
`.tmp/actions-integrated-native.log` (18/22 passed).

The isolated probe first blocked Electron readiness with a top-level await and then loaded
CSS layers in a different order from production. It now schedules startup after readiness
and imports the base layer first. The full production composition passed while those probe
issues were corrected: `.tmp/actions-integrated-second.log` and
`.tmp/actions-integrated-third.log`. No test timeout was increased.

The first complete component run passed 2,646 of 2,654 cases. Five old assertions queried
the inline menu or clicked its now-inert trigger; they now use the fullscreen dialog and
Back while retaining their action/order/focus checks. Three new Hide cases had a recursive
mock delegation, which now calls the captured implementation. One focused error assertion
also now includes the API's existing uncertain-response suffix. Failure logs remain in
`.tmp/actions-integration-full.log` and `.tmp/actions-components-second.log`.

The first complete native run passed 167/170 cases. Two fullscreen artwork tests still
queried the retired inline menu; they now query the portalled action dialog. The third
failure clicked Create a relationship without opening its form. It did not recur in 24
repeated identity cases or the corrected focused run. Pointer and HTTP revision traces
remain enabled to investigate recurrence; no production fix is claimed for that failure.
Logs: `.tmp/actions-native-full.log`, `.tmp/actions-identity-repeat.log`.

Review of those diagnostics found that installing a global fetch wrapper after startup
cannot observe the API transport, which captures fetch when constructed. Both fixture
wrappers now install before importing production main. The identity test verifies that
its trace contains a successful real link request. The action test's positive control
verifies the interception as well as the absence of leaked dispatch beneath the overlay.

Seven original FullscreenActionOverlay methods have replacement evidence. The inventory
contains 937 ported, 540 retained backend, 13 framework-specific, 808 pending and 137 partial
methods. The complete migration remains in progress; task acceptance criteria remain
unchecked. No backend code changed in this checkpoint.
