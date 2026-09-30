# Website plugin installation — 2026-09-29

Validated website activation starts installation immediately. One shared controller owns
the request, progress, cancellation and retry across navigation and presentation modes.
Requests serialize behind an active manual retry. An uncertain start retains its operation
identity; a known terminal failure creates a new one. Publication retry does not reinstall
or repeat a successful provider refresh. Only a newly installed package requests refresh.

Desktop shows progress inside Plugins. Fullscreen uses the original dedicated progress
page and opens a dedicated installed-provider page on success, with the provider fields
and Back action visible. Returning to the installation result remains possible after
navigation, but completion does not take the user back from another screen. Setup pauses
for the handoff and resumes at its saved step only on Resume setup.

## Verification

- Build and typecheck pass: `.tmp/plugin-install-build-second.log`, 8.05 seconds.
- All 2,742 component/live API cases in 136 files pass without skips in 55.26 seconds:
  `.tmp/plugin-install-integration-final.log`.
- All 22 focused native installation, plugin settings and setup cases pass in 2.4 minutes:
  `.tmp/plugin-install-native-second.log`. The ten installation cases run production main,
  preload and renderer with an isolated backend and intercepted package responses. They
  include desktop/fullscreen and Installed/AlreadyInstalled combinations, native keyboard
  and controller retry, serial requests, departure, result return and cancellation.
  Package download/verification remains covered by the retained backend tests.
- The first 34-case native attempt passed 32 cases. Resume setup left the fullscreen
  installation page active behind the wizard; clearing the suspended presentation fixes
  that regression. A separate plugin settings case was blocked by an unidentified startup
  dialog before its assertions. Its controlled gamepad fixture now installs before changing
  mode, and diagnostic capture records any recurrence. The corrected focused run passes;
  the original dialog cause is not established.
- Visual inspection found that the general Settings header displaced installed-provider
  fields. The dedicated fullscreen page fixes that source-layout difference. Desktop and
  fullscreen progress and the corrected 1920×1080 installed-provider capture were inspected.
- All 198 native cases pass in one complete run in 9.2 minutes, with clean worker
  completion: `.tmp/plugin-install-native-full.log`. This includes the previous merge
  input corrections and the expanded installation/settings/setup cases. JSON results are
  preserved at `.tmp/plugin-install-native-full/results.json`.

Three original installation methods now have equivalent evidence. The inventory contains
992 ported, 540 retained backend, 13 framework-specific, 758 pending and 132 partial methods.
Full migration remains in progress and all task acceptance criteria remain unchecked.
