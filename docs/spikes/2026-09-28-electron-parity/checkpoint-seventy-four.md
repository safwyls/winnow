# Recommendation state checkpoint — 2026-10-01

TASK-381.19 covers eighteen frozen source contracts. It is the ninth task in the
authorized sequential batch through TASK-381.20.

## Source boundaries

Desktop shows five recommendations per shelf. Extra primary items lead the hidden
reserve before the service's explicit reserve items. A saved verdict leaves a receipt
in the same card for three seconds; pointer or keyboard focus holds that clock.
Replacing the receipt changes only that slot and releases the departing card's artwork
consumer. Generation, backfill and promotion alone do not record impressions.

A reserve refill preserves every existing card and merges fresh candidates into the
queues in service order. An invalidation arriving during a read earns a later pass;
three invalidations during the initial held read coalesce into exactly one replay
using the final library state. Optional shelves cannot publish after their generation
or feedback state has changed. They append without replacing the focused card.

The five-shelf fixture retains its original shelf order, titles, pitches, reasons and
997-candidate count. A failing initial service produces readable recovery copy.
Recommendation history takes the feed body and gives it back. An absent feedback
store must not offer verdict controls that cannot save a response.

Cold composition keeps the original two store copies of one work. With an installed
Epic sibling, the playable entry, impression, verdict and undo use release 2 while the
library header remains release 1. Without that installed sibling, the action subject is
release 1. Recently played remains outside recommendation verdicts and impressions.

Native viewport checks retain the source's scrolled list-picker origin, offscreen
reserve and initially unshown window cases. Desktop replacement and fullscreen's
complete horizontal shelf are distinct presentation paths: fullscreen does not invent
a hidden reserve to mimic the desktop test. Both require actual viewport entry before
recording a recommendation impression.

## Implementation

The backend advertises verdict controls only when the shelf supports feedback and a
feedback store is present. Recommendation scoring remains in the existing backend
engine.

Electron queues a single primary replay behind an unfinished scoring pass. Library
refreshes can finish independently of that pass and optional providers. The current
cards remain visible during a refresh, while old optional results lose their generation.
Disabled feed observers cannot start optional reads. Reserve backfills also compare
the saved-feedback revision before merging their results.

History replaces the feed body while retaining its cards for return. Its own controller
scope prevents hidden feed actions from handling input. Fullscreen Y opens the selected
game's action sheet; Recently played omits verdict actions there as well as in the hero.

## Verification

The final complete Electron component/live API gate passes all **3,851 cases across
189 files** in 98.36 seconds (`.tmp/task38119-components-final.log`). The final
TypeScript/production build passes (`.tmp/task38119-build-final.log`), producing
`index-PxSwDjer.js`. Changed Electron files pass formatting checks.

The focused recommendation renderer gate passes 116 cases across five files
(`.tmp/task38119-renderer-focused-final3.log`). The first full run found two new
multi-reader tests replacing their initial React key, which correctly cancelled the
departing reader. Keeping that reader's identity stable establishes the intended
shared-observer contract; both cases pass without a production workaround.

Native review exposed a real empty-source error precedence bug: the zero-game setup
message hid a failed recommendation read. Both surfaces now show the failure and Retry
action. A held initial replay remains loading until it has a current result, instead
of briefly synthesizing fallback shelves. History uses the source desktop insets and
fullscreen information typography, with visible local controller hints. The fullscreen
list picker adds Y Keyboard while its editable field is focused; all 25 existing list
transaction cases pass (`.tmp/task38119-list-hints.log`).

All 27 original cases pass unchanged: fourteen main cases and thirteen UI cases.
Logs are `.tmp/task38119-source-tests.log` and
`.tmp/task38119-source-ui-tests.log`, with TRX files under
`.tmp/task38119-dotnet-results/`.

The affected feedback/service/reserve regression classes pass all 63 cases
(`.tmp/task38119-feedback-regressions.log`). Two additional component cases exercise
real recommendation cards, their replacement model and the shared artwork cache
(`.tmp/task38119-artwork-disposal.log`). Ready pixels evicted from the cache remain
alive until their card leaves, then release their object URL. An unfinished departing
image request is cancelled, and its late bytes cannot populate the cache or replacement
card. The four surviving card elements retain their identity in both cases.

All thirteen HTTP cases pass (`.tmp/task38119-http-final2.log`). Cold composition uses
the actual recommendation engine and repositories. The other source tests inject a
controlled recommendation service; their HTTP equivalents inject the same fixed shelf
data through the existing engine boundary. Optional-task fixtures replace only the
authenticated test host's supplement endpoint, without claiming production plugin
execution. Feedback, impressions and undo use the production services and SQLite.

The source backfill fixture silently adds future tile lookups before asking for its
next pass. Electron's library snapshot is atomic, so the six future lookup rows are
present before opening the feed. The displayed five cards, initial Held 101 reserve,
next 200–204/300 population and 997-candidate count remain unchanged. Preloading a
lookup row never records an impression. The service transports six primary items;
the desktop presentation splits the original five-plus-one fixture at its five-card cap.

The cold two-copy source fixture deliberately has no external launcher identifiers.
Native checks therefore verify selected release provenance and absent unavailable
launch actions; they do not invent identifiers or claim a successful operating-system
launch. Existing launcher tests cover dispatch with valid identifiers.

The original main window and fullscreen context retain their shared feed while the
user visits another page. Explicit `FeedViewModel.Dispose` therefore corresponds to
the final query owner's disposal, not Library navigation. Component tests remove the
final owner and reject its late response; the native equivalent destroys the original
renderer window while a separate hidden test window keeps the host alive for the
backend cancellation ledger. Normal navigation retains the shared feed.

The native replay fixture starts after the shell has settled its startup-empty
snapshot. Its held scenario read therefore preserves that settled body; it is not a
cold loading screen. The source assertions check exactly two scoring calls and the
final cards after one or three invalidations. The separate component first-query
cases cover the loading state. An extra native cold-loading assertion was removed
because it described a different lifecycle, with no production change.

The source lease test acquires only the departing image. Electron's real card tree
acquires all five images: replacement preserves five live consumers, releases the
specific outgoing resource, and unmounting releases all five. Image decoding is a
browser-platform test double; the production Artwork component and cache run intact.

All **38 distinct new native cases** pass, along with two existing feed/history
consumers. Evidence combines the newest successful execution of each case from
`task38119-native-initial2`, `task38119-native-final`, `task38119-native-final2` and
`task38119-native-final3` under `.tmp/`; the two consumers are recorded in
`task38119-native-consumers`. Repeated executions are not counted as new cases.
The first run used `index-DJGvfaCh.js`; changed presentation and failed scenarios
were rechecked with the final `index-PxSwDjer.js` bundle. The final seven lifecycle
cases pass in 55.5 seconds. Backend ledgers preserve actual requests, cancellation,
impressions and verdict writes.

Reviewed captures include the final desktop history in
`task38119-native-final-results`, and fullscreen history, the scrolled list picker
and its open keyboard in `task38119-native-final2-results`. Main-menu bumper hints
remain visible in history, while the list keyboard shows its own movement, typing,
backspace, enter and close hints. Controller input is simulated through the production
controller path; this does not establish physical-device or Steam Deck verification.

All eighteen assigned methods are ported. The inventory now records 1,505 ported,
650 retained-backend and 32 framework-specific methods, with **217 pending and
31 partial methods** remaining. The whole-port migration gate remains incomplete.
