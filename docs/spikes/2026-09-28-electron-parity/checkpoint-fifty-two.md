# Shared launch feedback and version selection

Library and Details now use one launch-status owner across desktop and fullscreen.
A successful launcher handoff shows `Starting <title>…`; only `launch.observed` for
the selected ownership changes it to `<title> is running.` The original 90-second
waiting, three-second confirmation and seven-second refusal lifetimes are preserved.
Install and management actions do not produce a gameplay status. Unrelated observations,
duplicate completions, late responses and disposed hosts cannot replace the current status.
An observation arriving before its HTTP response is retained. Uncertain retries preserve
the original action, ownership and operation ID.

Fullscreen Details offers **Choose launch version** when a game has multiple copies.
Opening or cancelling the chooser dispatches nothing. Each row identifies its store,
installation state and action; unavailable rows are disabled. Controller selection sends
the selected ownership rather than the collapsed group's first copy. Desktop retains its
per-copy actions in Library. Both presentations keep the grouped game title in feedback.

Fullscreen summary text now inherits the Details reading size instead of an unrelated
16px panel rule. The unavailable-action explanation sits in fullscreen's scrollable
Overview and beneath desktop's action row. This preserves reading space at 720p with
140% text and 120% interface scale. Inspected 1080p and 720p captures retain separated
identity, actions, section navigation and reading content.

## Verification

- Electron build and typecheck pass: `.tmp/launch-build-final.log`.
- All 3,321 component/live API cases pass across 162 files, without skips, in 58.97s:
  `.tmp/launch-integration-final.log`.
- All 34 native launch, Details and controller cases pass without retries or skips:
  `.tmp/launch-native-final.log` and `.tmp/launch-native-final/results.json`.
  This includes controller copy selection, independent local sections, watcher events through
  preload, ambient refusal feedback, installed-copy preference, surface changes, and the
  previous controller and Details size matrices.
- Two new backend HTTP cases pass in `.tmp/launch-backend.log` and
  `.tmp/launch-backend-results/launch-parity.trx`. They use the production action and launch
  services with a recording URI dispatcher, verify `steam://run/20`, assert that the correct
  intent exists before dispatch, suppress a second start, and read the ownership-specific
  observation through the real event stream. No fixture starts a real game.
- The first native text expectation missed the original viewport scaling. The corrected
  expectation uses the 1920px reference canvas; it is not a product font-size reduction.
- A broader run found only 96px of reading space at the largest small-screen scale. Moving
  the unavailable explanation out of the fullscreen hero restores the existing geometry
  assertion. Earlier results are in `.tmp/launch-native-before-layout-fix/`.
- Routing tests now use controlled presentation frames while retaining real readiness gates.
  Their one-second queries previously raced the real fade under concurrent test load. Real
  frame scheduling remains covered by native startup tests. The owned-copy assertion now
  scopes the Library content so the new header chooser cannot count as an owned-copy row.

Eight original methods gain complete evidence. The inventory is 1,247 ported,
625 retained backend, 23 framework-specific, 439 pending and 101 partial out of 2,435.
The migration completion gate still fails. These counts are test dispositions, not a
product completion percentage. Remaining Details contracts, fullscreen status/battery,
native integration and the primary release/CI cutover remain open. The earlier complete
native run still has its checkpoint-51 caveat; this is a focused native result.
No backend implementation changed; the new backend tests supplement the retained Release
suite evidence from checkpoint 39.
