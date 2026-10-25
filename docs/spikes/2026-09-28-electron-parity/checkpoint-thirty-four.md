# Electron parity checkpoint 34 — rating cap and Display preferences

The rating cap preserves the original six-tier scale, named value, rounding, default top
step, persistence and cap-only exclusion count. Unrated games stay visible. At the top
step, the original explanatory note identifies the separate explicit-content toggle when
it still hides adults-only titles. A failed count is reported as unavailable rather than
zero, and a refused save preserves the stored value and permits retry.

Desktop keeps Density on the command bar beside Display. The popover restores Fit/Fill,
dormancy, non-game, cap, expansion and journal controls in source order, with wrapping
explanations and a content-sized panel bounded by the viewport. The cap is absent from
Avalon's desktop Library settings. Fullscreen exposes Content age limit in Library settings
with scaled labels and controller adjustment. Setup and alternative layouts retain access.
Preference writes read the latest complete object and serialize within the renderer so
independent controls preserve one another's fields.

A stronger native assertion exposed Chromium dropping focus when the slider was disabled
during an asynchronous save. The control now restores its own focus after the save unless
the user has acted or focused another control. Both-mode controlled tests and actual
fullscreen controller input cover this behavior.

## Verification

- All 3,009 component/live API cases pass in 147 files without skips, in 52.38s:
  `.tmp/rating-cap-full-integration-final.log`. The focused settings/setup/library group
  passed 241 cases before the focus follow-up; the final rating/cleanup group passes all
  46 cases, including four focus cases.
- Build and type checking pass: `.tmp/rating-cap-build-final.log`.
- All 121 backend HTTP cases pass in 18.36s: `.tmp/rating-cap-backend-full.log`.
  The six new persistence cases also pass separately: `.tmp/rating-cap-backend.log`.
  These use isolated databases, round-trip every tier, restart the backend, preserve missing
  or malformed stored values, and independently verify adult-gate and cap counts.
- All 33 native rating/layout/library cases pass in 2.8 minutes:
  `.tmp/rating-cap-native-green`. After the stronger focus assertion failed, all three final
  rating workflows pass in 38.6s: `.tmp/rating-cap-native-final`. Captures at desktop,
  720p fullscreen and 140% fullscreen text were inspected. The source-sized Display panel
  keeps its complete content within the available desktop viewport; journal help now wraps
  below its label. Fullscreen rating text and the retained focus ring remain readable.
- The earlier native checkbox helper assumed an immediate local toggle; the final test
  clicks once and waits for the server-confirmed checked state before checking persistence.
- The preceding 228-case journal run passed 227, with an initial profile connection timeout
  and a worker teardown timeout. Cleanup now closes a launch whose readiness check fails,
  records its connection state and waits for its own authenticated backend process to exit.
  All 24 activation repeats pass against that frozen journal build. The original startup
  timeout has not reproduced and its cause remains unconfirmed; a combined 231-case pass
  is not yet claimed. Details are in checkpoint 33.
- The subsequent full 231-case run against `9b0d17d3` passed 225, failed two and blocked
  four after a shared setup failure, in 20.8 minutes. Report and log:
  `.tmp/rating-cap-complete-native/results.json` and `.tmp/rating-cap-complete-native.log`.
  The browse-spine setup captured failed library preparation; its backend started about
  13 seconds after launch, exceeding the transport's 12-second initial request wait.
  Controlled tests subsequently reproduced that mismatch with the advertised 45-second
  startup policy. The separate Epic fullscreen sign-in failure waited for its fixture
  document during navigation; this report does not establish its cause. Follow-up fixes
  and verification belong to checkpoint 35.

Eleven RatingCapPreference methods and the desktop shared Fit/Fill selector contract now
have named replacement evidence. Inventory: 1,094 ported, 559 retained backend,
17 framework-specific, 657 pending and 108 partial, out of 2,435 frozen source methods.
The overall migration remains incomplete, including broader fullscreen Settings contracts
and physical-controller/TV validation. No overall acceptance criterion is marked complete.
