# Cover ownership and presentation checkpoint — 2026-10-01

TASK-381.21 covers twenty frozen source methods. It begins the authorized
sequential batch through TASK-381.30.

## Source boundaries

The source preference defaults to Fit when no valid mode exists. Loading it does
not write back; selecting either mode persists and reloads it. Existing and newly
created portrait images inherit the choice without changing their bounds.
Background artwork retains its own crop.

User art takes precedence over pinned and provider art. An available pin keeps
the same work image across the library and merge review; unavailable providers
fall back to available sources. Plugin previews require a typed provider reference
and an available provider. These decisions remain shared backend policy.

The original lifetime fixture deliberately never completes its image requests.
Closing Details releases its Steam 620 cover and all three screenshot consumers
(`aa11`, `bb22`, `cc33`). The lightbox holds only its current screenshot, including
after moving from `aa11` to `bb22`, and closing releases it. A merge row owns both
Avalonia cover layers through one lease and releases them together.

The conversion fixture uses 160×240 SlateBlue images, Steam keys 42 and 43, and one
decode permit. A cached image must finish while an unrelated network request is
held. The permit covers transient conversion as well as decoding. Failure while
converting the second Avalonia layer must dispose the first converted bitmap.
Electron instead decodes one image and applies its dormancy transform when painting;
it has no second Avalonia bitmap conversion.

Two source cancellation cases register a throwing .NET cancellation callback.
They require the last waiter's cancellation to remain a cancellation, and shutdown
to release completed and pending images and finish idempotently. DOM abort events,
IPC cancellation and backend cancellation have different failure mechanisms, so
replacement evidence must identify which boundary it executes.

Desktop pointer exit retains vivid art only while keyboard focus remains. Changing
the selected library game updates its artwork without stealing keyboard focus.
The dormancy endpoint joins the token resources, procedural artwork and disk-rendered
floor; checking a CSS declaration alone does not establish painted equivalence.

## Original execution

All twenty methods expand to forty cases and pass unchanged: seventeen main-project
cases and twenty-three UI cases. The nine source files match the frozen revision.
The logs are `.tmp/task38121-source-main.log` and
`.tmp/task38121-source-ui.log`, with TRX files under
`.tmp/task38121-dotnet-results/`.
The capture-enabled UI run also produces four focus images in
`.tmp/task38121-source-captures/`. The library Tab and feed directional captures
were inspected; they are isolated source component hosts, not full application windows.

## Implementation

Screenshot thumbnails and lightbox images now use the same decoded-image ownership
cache as cover art. Previously they used a standalone query with no request ID,
abort propagation or decoded consumer lease. The new selected-asset component
keeps the existing screenshot DOM, caption and crop while releasing its image source
and lease on replacement or removal. Pending work is cancelled after its last
consumer leaves; completed images may remain in the shared scrollback cache.
Same-image refresh retains the visible pixels while pending. A confirmed null clears
them; a rejected transport preserves them for every joined consumer through the
lease's shared failure outcome.

Merge portraits inherit the shared Fit/Fill setting. Desktop library, feed and merge
portraits now also use the existing edge-color padding in Fit mode, as the visual
specification and original controls require. The previous fullscreen-only call-site
guard left desktop letterboxes at the theme surface color. The desktop Library selector
normalizes invalid stored values consistently with desktop Display and fullscreen
Appearance. The shared preference writer ignores invalid crop selections.
Fullscreen review retains its text proposals and controller member actions, as specified
in the visual system. It has no merge portrait consumer. The original composition test
compares the library and merge model's selected key, then renders only the library tile;
it does not require fullscreen review to render a cover. Opening a fullscreen member
uses the normal Details presentation and its cinematic artwork ownership.
Backdrop images clear both current and outgoing DOM sources before their controller
releases pixels. Layout-effect reattachment restores the source for StrictMode;
the crop, layout and transition policy are unchanged.

Backend image conversion now uses `CoverPipeline.GetPngAsync`. Its permit spans
decoding and actual PNG encoding, including disposal on a failed encoder. Network
fetches retain their separate pipeline bound and no longer occupy the endpoint's
four conversion permits. The previous endpoint held those permits during network
waits, which could prevent cached artwork from being served.

The renderer likewise separates admitted image requests from its six browser decode
permits. Queued encoded strings have a 32 MiB budget, charged conservatively at two
bytes per character. An over-budget request drops those bytes and rereads the backend
after a decode permit opens, preserving eventual completion for a still-visible
consumer. It does not return an empty image merely because that queue is full.
This overflow reread holds a conversion permit. If its already-fetched disk bytes
were removed in the meantime, that bounded retry can wait for a new download.
Successful decoded images populate the existing encoded-byte cache; its five-minute
expiry remains time-based, without a separate aggregate byte budget. That existing
cache is distinct from the bounded transient decode queue and decoded scrollback LRU.
Rejected overflow rereads preserve the shared failed-lease outcome so a refresh can
retain warm pixels. Confirmed absence still clears them; cancellation and shutdown
settle without publishing late pixels.

## Verification

Fifteen exact preference cases pass across desktop Display, desktop Library and
fullscreen Appearance (`.tmp/task38121-preferences.log`). They retain the source's
null/invalid/Fit/Fill input matrix, no-write loading, Fill save and independent reload,
invalid-selection rejection, then Fit save and independent reload. The fullscreen
control has no invalid numeric index; an unrelated direction is ignored instead.
These component cases use a bridge fixture with fresh query clients on reopen.

Three PNG conversion cases pass (`.tmp/task38121-png-boundary.log`). With one permit,
cached Steam 43 completes while Steam 42 is held at the source. A held encoder keeps
the next conversion out for 300ms, then both actual PNGs complete and their transient
Skia bitmap handles are released. A throwing encoder releases its bitmap and permit
before a successful retry. The internal encoder delegate supplies the failure/hold;
the public production path always uses the real PNG encoder.

All 194 cover-library tests pass (`.tmp/task38121-covers-full.log`), including those
three new conversion cases. The authenticated HTTP gate passes 21 cases: twelve new
cover cases and nine prior recommendation-preview regressions
(`.tmp/task38121-http-final.log`). The new cases preserve raw preference storage,
independent-client reads, five selected-artwork source kinds, disk access while all
four unrelated network requests are held, and a throwing source cancellation callback.
The callback fixture registers the throwing callback after creating its cancellable
wait so cleanup cannot unregister it before it runs. That corrects the test's
registration-order race without changing production cancellation behavior.

The focused renderer gate passes 171 cases across five files
(`.tmp/task38121-renderer-ready.log`). A subsequent 95-case gate verifies the
screenshot request-ID assertions and a cancellation-observing shutdown case
(`.tmp/task38121-renderer-cancellation-final.log`). That case finishes within the
source's three-second bound without manually releasing the held request, despite
the cancellation acknowledgement rejecting; decoded/pending/live counts reach zero,
the completed image is released once, and repeated shutdown succeeds. Separate cases
retain the cancellation-ignoring late-completion boundary.

The first complete component/live API run passed 3,893 of 3,896 cases. Two existing
screenshot assertions needed to accept and validate the newly required 32-character
cancellation ID. The existing 60-card desktop merge stress test exceeded its
30-second timeout, including in isolation. That fixture returns no selected artwork,
so it does not execute the changed fetch/decode queue. Profiling recorded repeated
whole-page saved-card lookup waits of roughly 0.9–1.4 seconds, including publication.
The test now queries the captured card and waits on its explicit accessible label,
then verifies its connected state. It retains all 60 slots, 20 actual answers,
per-answer reads, final full accessibility/node-identity query and write/history
assertions. Both surfaces pass under the unchanged 30-second timeout: desktop 20.629
seconds, fullscreen 5.410 seconds (`.tmp/task38121-merge-scoped.log`). These are
jsdom test timings, not native application performance measurements.

The backdrop cleanup gate passes 42 cases across five files
(`.tmp/task38121-backdrop-cleanup.log`). It checks current and outgoing DOM sources
before pixel disposal and StrictMode reattachment. The subsequent overflow gate passes
44 cases across four files (`.tmp/task38121-overflow-reread-final.log`). A real
`ArtworkAsset`/loader/cache combination uses one decode permit and a zero-byte waiting
budget to exercise the same overflow path without a 32 MiB fixture string. Rejected
rereads retain the old pixels, confirmed absence clears them, and cancellation/shutdown
settle without late publication. A separate source review found both this failure
classification gap and the desktop padding gap; both are corrected and reviewed.

All 26 distinct new native cases pass across the recorded runs. They cover the ten
source-selection combinations, six focus/selection pixel cases, eight resource-lifetime
cases and two live/persisted Fit/Fill cases. Desktop measures library, feed and actual
merge portraits. Fullscreen measures library/feed portraits, text-only review membership
and the member-to-Details backdrop. Widths and existing DOM identities stay stable;
new portraits inherit the setting, while background artwork keeps its own crop.
The final affected pixel recheck passes eight cases in
`.tmp/task38121-native-final4.log`; the fullscreen Fit case then passes in
`.tmp/task38121-native-final5.log` after waiting for its closing member sheet before
navigating to Settings.

Native capture uses a test-only Chromium sRGB flag. Without it, the machine's monitor
ICC profile changed pure red in the captured PNG, and `nativeImage` conversion did not
recover the exact source channels. Source canvas and painted pixels now use the same
color space, with the original tolerance of two per channel retained. The source SlateBlue
vivid endpoint is `[106, 90, 205, 255]`; its dormant endpoint is `[68, 68, 85, 255]`.
Letterbox samples verify averaged edges, dormancy composition and transparent padding
after Fill. No production color-management setting changed.

The original-style focus probe, desktop library/merge Fit captures, fullscreen feed Fit
capture and fullscreen member/Details/lightbox captures were inspected. Main-menu
bumper hints remain visible. Gamepad input is simulated through Chromium; these checks
do not establish physical-controller or HDR-display validation.

All eighteen directly affected existing native artwork cases pass
(`.tmp/task38121-consumers.log`): eleven cache/lifetime/dormancy cases and seven
padding/hero-transition cases. The existing real-cover case previously asserted that
desktop omitted padding. It now verifies sampled edges, dormancy and Fit/Fill on both
surfaces, matching the unchanged visual specification and original controls.
The consolidated `.tmp/task38121-native-evidence.json` records all 44 distinct latest
successes, bundle provenance, screenshot paths and extracted request/pixel attachments.
The companion source/backend and renderer ledgers are
`.tmp/task38121-backend-evidence.json` and `.tmp/task38121-renderer-evidence.json`.

The production build passes (`.tmp/task38121-build-final3.log`), producing renderer
`index-Cc4rqVE5.js` and stylesheet `index-Brn1ekvD.css`.

The final complete component/live API gate passes all 3,904 cases across 194 files in
354.07 seconds with one worker (`.tmp/task38121-components-final.log`). It uses the
prebuilt isolated Debug backend and temporary seeded data. Changed-file formatting and
`git diff --check` pass.

The migration audit resolves all twenty assigned methods: sixteen ported, three retained
backend policies and one framework-specific second Avalonia bitmap conversion. The
inventory contains 1,531 ported, 653 retained-backend, 33 framework-specific, 189 pending
and 29 partial methods. It still totals 2,435 methods across 299 files. This completes
TASK-381.21; the authorized batch continues with TASK-381.22. It does not establish
complete Electron migration parity.
