# Merge review and suggestion refresh checkpoint — 2026-10-01

TASK-381.12 covers eighteen frozen source contracts. It is the second task in the
authorized sequential batch through TASK-381.20.

## Implementation

Desktop review keeps Same game and Different games beside the proposal title and
confidence. Refresh is an icon beside Merge selected; fullscreen places Refresh after
Sort, Kind and Preferred platform. Both surfaces announce busy, failure and completion
without changing previous answers. Refresh has one state owner throughout initial-load
recovery: a background snapshot arriving during a manual request cannot abort that request
or leave the queue disabled.

Preferred platform sits before the proposal count in the desktop filter bar, exposes its
current selection to assistive technology and restores focus after saving. Opening
Details preserves the selected member's identity and the
proposal's header. A member without a visible library tile leaves the review open and
reports that the game is unavailable.

Fullscreen review sheets now occupy the right edge at full height. Their title, action,
description and hint type use the original 40/28/22/24 reference sizes, adjusted by the
existing fullscreen and text scales. Choices scroll between the fixed heading and the
A/B hints. This corrects the shared typography gap recorded in checkpoint 66, including
saved Header store choices. Native screenshots cover the short Bastion title at 1920×1080
and the full Metal Gear Solid collector's-edition title at 1280×720.

## Source equivalence

The refresh fixture preserves separate Bastion Steam/PSN works, year 2011, Supergiant
Games and Windows releases. A guarded test backend uses the actual matcher and serialized
signals; its match subjects omit platform as in the original test. Only the asynchronous
refresh boundary is held or failed. Success, review reads, database writes and publication
use the production preload, authenticated HTTP, SQLite and SSE paths. Replacing Bastion
with Hades while the count remains one exercises automatic publication on both surfaces.

The Prey fixture preserves the 2017 and unknown-year identities, Steam/Windows releases
and score 0.8. Both App and native cases prove exact-member Details navigation without
header promotion, and refusal when there is no owned tile. Desktop row cases preserve
the independent body, radio, Details and inclusion actions. The long-title case uses
Steam/GOG and Epic ownerships and measures actual panes at 1670, 950, 1030 and 950 pixels.

Electron's native HTML select is the desktop platform flyout equivalent. Real keyboard
input opens, changes and closes it; its selected option and accessible description expose
Steam. Fullscreen retains its separate choice sheet and standard Gamepad API route.

Backend serialization, cancellation ownership, worker-thread dispatch and plugin import
matrices continue to execute the unchanged production services. Two new HTTP/composition
tests cover an empty review and a missing identity-link repository. The latter explicitly
resolves the required service, matching the source assertion; it does not assert eager
HTTP-host validation of every registered service.

## Verification

All **3,611 component/live API cases across 180 files** pass without skips in **90.12
seconds**, recorded in `.tmp/task38112-components-final2.log`. The prior run's two failures
held a detached loading-state Refresh button; their assertions now locate the currently
rendered control. No behavior assertion changed. Four new lifecycle cases separately
verify that the active request itself survives the loading-to-loaded transition.

All eighteen original registration, refresh and plugin cases pass, as do both new backend
composition cases. Logs are `.tmp/task38112-source-tests.log` and
`.tmp/task38112-api-tests.log`; their TRX files are in `.tmp/task38112-dotnet-results/`.

Fourteen new native cases pass in `.tmp/task38112-native-final-results/results.json`,
seven per surface. They cover real accessibility nodes, controller reachability, platform
preferences, busy/failure/retry, publication, owned and unavailable Details, desktop row
actions and resize geometry, and fullscreen sheet typography. Two Header store regressions
pass in `.tmp/task38112-header-regression-results/results.json`, including refusal with
focused recovery. Screenshots in those directories confirm wrapped sheet titles, complete
glyphs, visible focus and bounded desktop trailing actions.

Six final targeted cases pass in `.tmp/task38112-native-final2-results/results.json`.
These recheck the desktop picker before the count, fullscreen Sort → Down twice →
Preferred platform and Sort → Down three times → Refresh, both refresh flows, long-title
resizing and the dedicated Merges Details return route. The existing regression run
passes sixteen other cases in `.tmp/task38112-native-regression-results/results.json`;
its failed Details fixture passes in the final targeted run. Together these reports
establish **33 distinct passing native cases**, without retries or skipped assertions.

The original native Merges navigation regression used a deliberately unowned sample
release. Its fixture now supplies an ownership before checking the owned Details route;
the separate unavailable-entry cases assert refusal. No product assertion was removed.

The final build/typecheck passes in `.tmp/task38112-build-final2.log`; changed Electron
files pass Prettier and whitespace checks. The migration audit validates **1,412 ported,
644 retained backend, 32 framework-specific, 287 pending and 60 partial** methods in
`.tmp/task38112-migration-report.log`. The eighteen assigned methods are eleven ported
and seven retained backend. **347 methods remain unresolved** in later tasks.

## Remaining validation

Controller checks simulate standard Gamepad API frames in Electron. They do not verify
physical devices, TV-distance readability or native display mode switching. The complete
.NET suite and packaged installers are outside this checkpoint. Interactive runs use
disposable data directories and do not modify real libraries or launcher files.
