# Plugin settings and account interaction — 2026-09-29

Electron now presents one tab per runtime-loaded plugin and a final Manage plugins tab.
Pending activation changes keep the loaded catalogue intact until restart. The catalogue
loads on entry and refreshes on return, retains a surviving selection, and selects Manage
plugins when the selected provider disappears. The manager contains unloaded and invalid
packages, the original installation guidance and the loaded name/version summary.

Desktop and fullscreen retain separate navigation and ordinary drafts. Secret replacements
never hydrate from snapshot values and clear on departure or successful save. Advanced
fields retain values while hidden; validation reveals and focuses a missing required field
before any backend write. Writes and device sign-in share a per-plugin busy lock. The
manager also blocks restart during writes and active backend installation operations,
including after leaving and reopening Settings.

Device sign-in starts only on input, displays a validated code and declared HTTPS address,
and opens that address only on request. Canceling during preparation cancels the late
challenge when its identity arrives. Departure aborts polling; provider intervals, SlowDown
and expiry follow the source rules. A connected attempt refreshes settings without canceling
the completed connection. Failures use safe messages rather than provider exception text.

Plugin forms restore the source desktop width and the separate fullscreen reading column,
activation row, title and input sizes. Overflow arrows never select a different plugin.
Desktop Left/Right/Home/End selects; fullscreen moves focus and A selects. Down enters the
form. LT/RT stays on the outer Settings sections. Save returns focus after disabled controls
become available, unless the user moved focus elsewhere.

## Verification

- All 2,734 component/live-backend cases in 135 files pass without skips in 46.99 seconds:
  `.tmp/plugin-full-integration-final.log`. Build and typecheck pass:
  `.tmp/plugin-build-final.log`.
- The ten focused native cases pass in one minute: `.tmp/plugin-final-focused.log`.
  They exercise production main/preload/renderer with intercepted provider responses and
  a real isolated backend. Restart and reconnection use the production backend lifecycle.
  Form writes, activation errors, secret removal, explicit device sign-in, cancellation,
  connection and sign-out run in both modes. The twelve-provider matrices include narrow
  desktop, 1280×720 fullscreen and 1920×1080 fullscreen at 140% text size. Resize-only cases
  check arrow removal/reappearance and selected-tab visibility.
- Native checks found lost Save focus and fullscreen Down skipping activation. Both are
  fixed and covered without weakening focus assertions. Test corrections also account for
  asynchronous query publication, conditional seeded setup and distinct field/action names.
- The initial complete component run passed 2,733/2,734. Its invalid-package fixture
  incorrectly expected a disabled fullscreen activation control; the source hides that
  control on fullscreen and disables it on desktop. The corrected assertions are in the
  complete passing run above.
- The first full native run passed 187/188 in 8.6 minutes:
  `.tmp/plugin-verified-native.log`. The existing merge fixture sent Space while a radio
  was disabled by the preceding save. It now waits for that control to become enabled,
  retaining the checked-state assertion.
- The second full run also passed 187/188 in 8.8 minutes:
  `.tmp/plugin-final-native.log`. The radio case passed. A different relationship-form
  case received pointerdown and pointerup while disabled, with no click event. The fixture
  now retries only when its native trace proves that Chromium suppressed the click on a
  disabled target; a received click that fails to open the form still fails the test.
  Backend logs confirm normal operation until teardown. The disconnected/setup-error
  snapshot was captured during cleanup and did not explain the original missed click.
- The corrected eight native Library/Details/merge workflows pass three consecutive runs,
  24/24 in 1.2 minutes: `.tmp/plugin-merge-repeat.log`. All 188 native cases have passing
  coverage across the full runs and this focused repeat. A combined 188-case pass has not
  yet been recorded. Typecheck also passes after the added assertions:
  `.tmp/plugin-checkpoint-typecheck.log`.
- All four tab-label containment matrices pass on the unchanged build:
  `.tmp/plugin-label-native.log`. Desktop/fullscreen form and enlarged overflow captures
  were inspected; labels remain within their clickable bounds and the selected tab stays
  reachable without reducing the source typography.

Forty original methods from PluginSettingsViewModelTests, PluginAccountSettingsTests,
PluginSettingsInteractionTests and PluginTabsTests now have equivalent evidence. The
inventory contains 989 ported, 540 retained backend, 13 framework-specific, 761 pending
and 132 partial methods. Full migration remains in progress. Browser installation flow,
other original interactions and release transition still require work; task acceptance
criteria remain unchecked.
