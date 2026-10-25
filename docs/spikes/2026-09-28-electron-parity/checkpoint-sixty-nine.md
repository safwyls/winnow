# Metadata editing and refresh checkpoint — 2026-10-01

TASK-381.14 covers fifteen frozen source contracts. It is the fourth task in the
authorized sequential batch through TASK-381.20.

## Implementation

The metadata response reports whether the optional editing service is available.
Removing that service leaves ordinary Details readable, hides its editor on both
surfaces and refuses all four metadata mutations without writes. Older responses
without the additive property retain the editor. Both surfaces keep the source
Edit details label and Edit each field by hand tooltip before and after editing.

When the native artwork picker is absent, both artwork fields retain their URL route
and hide file selection. Existing picker-enabled fixtures still exercise uploads.
Compact reception exposes a named accessibility group containing its score and count;
the count remains in its tooltip without appearing in the compact visual text.

Existing per-field save and refresh composition already preserve unrelated drafts.
The new evidence exercises those paths through actual production HTTP services and
native Electron, including simultaneous background updates.

## Source equivalence

The Prey fixture preserves its 2006 year, 2K Games publisher, Cherokee summary,
GOG ownership, 120 minutes and original dated play facts. Saving Prey (2006) changes
only the name and its user source, updates the tile and headline, and retains the
same Details/editor elements and unfinished summary/publisher drafts. Saving and
resetting Human Head Studios clears only the publisher override. Renaming Alpha
Protocol to Zzz Protocol immediately moves it after Zeno Clash under Name ascending.

All six original save/refresh cases retain Game 1 (2006), Game 2 (1990), their original
summary/publisher, static list and play facts. Saving summary, publisher or year keeps
the unfinished name and editor on both surfaces. The active 2006 filter and saved
live-list rules remain unchanged; their visible membership changes from one to zero
only when the year becomes 2017. Live lists derive membership in the renderer, so the
API assertions inspect saved rules rather than inventing stored member IDs.

Both background-refresh cases preserve the focused unfinished journal textbox, old
entry and Details identity while real repository changes publish through SSE. Playtime
becomes 180 minutes, last play advances, a second session/note appears, a patch arrives,
IGDB reception becomes 90 from 20 ratings and the new screenshot decodes. The selected
tracked-history view survives. A later patch raises unread updates from one to two
while the original Read button keeps identity and focus. No draft is saved implicitly.

Component tests retain the source menu command semantics, optional-picker branch and
reduced-motion propagation. Calling an already-open editor's production handler again
preserves its DOM and draft; this is command-composition evidence, not an assertion
that the modal's inert background is clickable. Fullscreen tests stage simultaneous
drafts in the desktop editor before entering its one-field presentation.

Three methods retain unchanged backend implementations: the seven-cache timestamp
boundary matrix, legacy IGDB image-payload refresh followed by a warm no-op, and image
metadata changes with unchanged-run suppression. Their original tests run unchanged.

## Verification

The final full gate passes **3,699 component/live API cases across 182 files** without
skips in **92.29 seconds**, recorded in `.tmp/task38114-components-final2.log`.
The final build/typecheck passes in `.tmp/task38114-build-final.log`; the native fixture
build passes without warnings in `.tmp/task38114-fixture-build-final2.log`.

The first full component/live API run passed 3,698 of 3,699 cases. Its remaining
assertion selected the old generic Reception label to locate the compact header.
It now selects the named accessibility group and keeps the original assertion that
header reception precedes About. The failure log is `.tmp/task38114-components-final.log`.

All 27 original source cases and nine new HTTP cases pass. Logs are
`.tmp/task38114-source-tests.log`, `.tmp/task38114-source-ui-tests.log` and
`.tmp/task38114-api-tests.log`; TRX files are in `.tmp/task38114-dotnet-results/`.
The first HTTP run exposed a test assumption about stored live-list members; the
corrected assertions follow the API contract and native cases verify derived counts.

All sixteen distinct native cases pass: seven desktop cases in
`.tmp/task38114-native-results/results.json`, followed by desktop background refresh
and eight fullscreen cases in `.tmp/task38114-native-final2-results/results.json`.
Initial failures concerned fixture navigation between independent surface pages and
expected tracker copy; corrections retain the original behavior assertions.
After the reception accessibility correction, both background cases pass again in
18.8 seconds in `.tmp/task38114-native-final3-results/results.json`, asserting the
actual browser group role and complete rating/count name.

Screenshots were inspected for the bounded desktop editor, fullscreen field menu,
automatic publisher state, focused journal drafts and focused update actions after
insertion. The tests check database state, DOM identity and decoded cached artwork.

## Limits

Twelve assigned methods are ported and three retain backend coverage. The inventory has
**1,439 ported, 650 retained-backend and 32 framework-specific methods**, with **255
pending and 59 partial** remaining. Per-method evidence lives in
`tests/migration-metadata-composition.json`.

Native checks use temporary libraries, offline external providers and simulated
standard Gamepad API frames. They do not establish physical-controller, television
or live-provider validation. No user library, installed game or release was changed.
The overall migration gate remains incomplete. The next task is TASK-381.15.
