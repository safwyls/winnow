# Update acknowledgement composition checkpoint — 2026-10-01

TASK-381.31 covers nine frozen source methods and begins the authorized
TASK-381.31–381.35 batch. All fifteen original cases pass unchanged against
`cf45d9f1127243a987d3cf6e664a32fc767ecb67`.

## Source boundaries

The original fixture has one game with Steam and Epic releases and a separate
never-played GOG game. Its twelve event rows include pushes before, exactly at and
after effective last play, with correlated announcements on each release. Grouped
counts use the maximum across releases, not their sum. Additional selected and
unselected games retain their own update pairs.

Selection and Details acknowledge only their displayed push watermarks. A later
push and its announcement remain unread; reopening Details reads that newer state.
Restoring a grouped game's flags affects its displayed releases only. Failed
persistence leaves unread flags intact. The source count matrix covers zero or
600 minutes, with or without a last-played date, producing 0, 3, 1 and 1 unread
patches respectively.

Source results are in `.tmp/task38131-source.log` and
`.tmp/task38131-source-results/task38131-source.trx`.

## Implementation and verification

Thirteen backend cases pass: eleven new cases and two acknowledgement regressions.
The new fixture preserves all twelve original event rows, release IDs and dates;
multiple selection adds the original two games and four rows. The original SQLite
failure trigger refuses all writes. A supplemental release-specific trigger proves
that a confirmed sibling write survives and only the failed release needs retry.
The same authenticated production routes reject cross-release event correlation and
return exact per-release timestamps. No production backend behavior changed.

The API tests retain 600 minutes on each original store copy and the resulting
1,200-minute grouped total. An initial supplemental assertion incorrectly expected
600 for the group; correcting that expectation did not change the seed or source.
API results are in `.tmp/task38131-api-final.log`; the fixture build in
`task38131-fixture-build.log` has zero warnings or errors.

The renderer now limits Library Mark as read to unread selections in Patched,
captures the entire selection before awaiting requests, suppresses repeated
activation synchronously and retries only releases that remain unread. A refusal
on one release does not prevent another release from saving.

All 203 cases in four affected renderer files pass, including sixteen new exact
fixture cases. TypeScript and the production build pass. The desktop right-click
route uses the existing selected-games action strip; fullscreen retains its
Library options dialog. This is a presentation adaptation, not a claim that the
Avalonia context-menu widget was recreated.

The four-row count method creates no controls despite residing in the original
UI test assembly. Its replacement production HTTP test establishes the original
repository outcomes; the renderer projection matrix supplements it. It is a
ported contract and requires no exception to the migration audit.

Thirteen distinct native journeys pass: ten use the exact fixture and actual
production API, while three existing update-flag regressions use controlled
transport responses. The latter supplement the source proof. Initial failures
were harness mismatches in control lookup, error copy and accessibility lookup
under an inert modal; the corrected five journeys pass without weakening their
stored-state assertions. The wrapper records requests and responses without
rewriting the new fixture's API data.

Visual review found that fullscreen Library save errors inherited desktop text
size. They now use the panel's 28px reading scale and wrap inside its scrolling
body. Sixteen focused component cases and the final production build pass after
that correction. The native refusal pair also passes, verifying the rendered
font and bounds in `.tmp/task38131-native-final2-results`.

Native results are retained in `.tmp/task38131-native-initial-results` and
`.tmp/task38131-native-final-results`, with the consolidated run ledger in
`.tmp/task38131-native-evidence.json`. Simulated controller Y/A exercises the
fullscreen command route; these results do not establish physical-device support.
The existing root footer uses keyboard wording independently of input modality.

All nine source methods are ported. The inventory has 1,635 ported, 705
retained-backend and 37 framework-specific methods, with 48 pending and ten
partial. The complete migration gate still fails as expected. This is the first
task in the authorized five-task batch, not completion of the Electron port.
