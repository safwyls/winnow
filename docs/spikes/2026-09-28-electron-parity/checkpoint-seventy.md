# Details reading and lightbox checkpoint — 2026-10-01

TASK-381.15 covers eleven frozen source contracts. It is the fifth task in the
authorized sequential batch through TASK-381.20.

## Implementation

Desktop tab focus uses the specified 2px Volt underline. The existing viewport width
cap is named in CSS and still drives the actual card width. Its 860px floor stays above
the original 700px minimum; available window space can still constrain the card.

Reading explanations share a left-aligned 410px desktop measure. Fullscreen prose uses
the available width and a 28px base size, multiplied by saved text scales. Controls and
data rows keep their own sizing. Screenshot thumbnails now include position tooltips.
The lightbox remains a same-document overlay with theme-token control fills.

Achievement rows have a visible heading and an accessible group. Their existing wording
states the release, store, unlocked count, total, percentage and last-known qualification.
The five availability states retain their database meaning without inventing progress.

Fullscreen matching waits until the closing action panel releases its focus trap before
focusing the query. Initial Details focus no longer overrides a control already focused
inside the page or attempts to focus an inert page.

## Source equivalence

All five achievement states use the original Achievement fixture, release 1 and account
12345 through the real repository and API: no fetch, empty schema, known locked A/First,
unavailable fetch, and known unlocked A followed by failed refresh. Both surfaces retain
Not fetched, No achievements, Unavailable, zero-of-one known progress and one-of-one last
known progress. Electron's existing “0 of 1 unlocked” wording replaces the source compact
fraction under an explicit Achievements heading; the values and availability are unchanged.

Keyboard tests retain the desktop Right, Right, Left, End and Home sequence, selected
tab ownership, focus and its palette token. Fullscreen retains its four-section navigation.
The forty-paragraph source description expands and restores its actual scroll offset after
Activity; fullscreen uses its separate About and Play history reading pages.

The matching fixture preserves An unplayed game, the Astral cartographers query and five
numbered candidates with years 2020–2024 and the original two platforms. Production More
contains additional available actions, so the native check navigates to Wrong game before
activation. Back and Escape retain Library, query and all results; reopening focuses the
query without a second service search. React can remount the input while keeping that state.

Lightbox checks exercise five cached images, the second selected shot, real dialog role,
caption and control copy, same-window layering, focus containment and return to the
originating thumbnail. Conditional mounting replaces the original null-safe visibility
binding and cannot leave a ghost overlay when its owner unmounts. CSS variables replace
the Avalonia static-resource mechanism without its deferred-binding initialization hazard.

Desktop prose measurements retain the original 700px and 1400px widths across Platforms,
account statistics, Application, Library, appearance and Merges, plus wrapping Steam
consent. Appearance includes actual theme descriptions and typography notes. The original
preview merge repository is empty and the account-statistics view model is not loaded by
the source test; empty Merges and Spending are the corresponding fixture states. The
fullscreen case retains the original eight repeated sentences at 1200px, 28px base text
and a reading width above 410px without a maximum-width cap.

## Verification

The full component/live API gate passes **3,718 cases across 183 files** in **93.97
seconds** (`.tmp/task38115-components-final.log`). The migration audit records all eleven
assigned methods as ported: 1,450 ported, 650 retained backend, 32 framework-specific,
247 pending and 56 partial methods. There are 303 unresolved methods. The audit and
changed-file formatting pass.

All **21 original source cases** and **five new HTTP cases** pass. Logs are
`.tmp/task38115-source-tests.log`, `.tmp/task38115-source-ui-tests.log` and
`.tmp/task38115-api-tests.log`; TRX files are in `.tmp/task38115-dotnet-results/`.
All six original test files are unchanged from the frozen source revision.

All **21 distinct native cases** have passing final results. The complete initial matrix
is in `.tmp/task38115-native-results/results.json`. The final ten achievement cases are
in `.tmp/task38115-native-final2-results/results.json`; lightbox, keyboard token and
fullscreen matching checks pass in `.tmp/task38115-native-final3-results/results.json`.
Both expanded prose matrices pass in **15.8 seconds** in
`.tmp/task38115-native-final4-results/results.json`. Those reruns cover every prior failure
or unrun case; `.tmp/task38115-native-evidence.json` records the latest result per case.

The lightbox harness originally measured before image loading and sampled an active fill
before its existing 180ms transition settled. It now waits for image decode, uses actual
Tab input, asserts focus-visible and polls exact theme-token equality. The transition
remains enabled. Native measurements also found Theme Studio's older character-width
rule overriding the shared prose cap; that rule now applies only to unmarked paragraphs.

Focused component gates pass **293 Details cases** and **133 prose-consumer cases**.
The final production build/typecheck passes in `.tmp/task38115-build-final3.log`, and
changed-file formatting passes. Native screenshots were inspected for both lightboxes,
achievement headings, tab focus, restored matching, long reading, fullscreen prose and
the desktop explanations/consent at both widths.

## Limits

Native fixtures use isolated databases, cached test images and offline external providers.
Controller input uses simulated standard Gamepad API frames; physical devices and television
validation remain open. No live account, user library or installed game was changed.
The overall migration gate remains incomplete. The next task is TASK-381.16.
