# IGDB matching and candidate presentation checkpoint — 2026-10-01

TASK-381.13 covers eighteen frozen source contracts. It is the third task in the
authorized sequential batch through TASK-381.20.

## Implementation

Details and manual-game search share the source candidate grid: a 34×51 cover,
remaining-width text and a trailing action. The year and platform list occupy separate
columns. Long platforms trim on one line and retain the full tooltip. The list keeps
the source 238px height limit. Settings now displays candidate covers through the same
authenticated artwork path as Details.

The IGDB state response exposes whether the optional assignment service exists.
Both Details presentations hide matching when it is explicitly unavailable and close
an open matching tool if the service disappears. Missing credentials keep the control;
older responses without the additive property remain supported.

Assigning a match preserves the current Details section and confirmation. Closing and
reopening starts at Overview. Structural link refusal uses the source sentence while
preserving the conflict response and recovery action. Revision conflicts and uncertain
requests retain their existing recovery behavior.

An unfinished shared-Details journal editor also retains its originating Journal or
History section through navigation. Explicitly closing Details clears the active editor
pointer while preserving its draft.

## Source equivalence

The Prey fixture retains IGDB 1234, year 2006, 2K Games, the Cherokee summary, co1r76,
120 minutes and the original dated play facts. Choosing 5678 writes year 2017, Bethesda
Softworks, the Morgan Yu summary and co2abc. Component and native matrices cover all
five source sections on both surfaces; fullscreen Activity maps to its History reading
page. Reopened HTTP clients verify the persisted pin and unchanged owned identity.
Native checks compare decoded cached JPEGs, including Steam3900 capsule → pinned cover
→ restored capsule, and a successful coverless pin that retains the capsule.

Claim fixtures keep the named Prey 2017 holder and wrong Steam game. Before a decision,
every fixture table is unchanged. Accepting writes one user same-game link and no pin;
declining and refusal leave both mappings intact. An additional component case retains
the original bare-refusal branch when optional holder lookup is absent. Structural
refusal is a real HTTP 409, with a separately controlled native repository refusal.

The mapping-refresh fixture preserves Old game111, Old genre, esrb:ao, oldshot,
rating95/count20 and the attributed old background. Real assignment222 and visibility
filtering update the same open Details element to Corrected game/summary/year2020,
retire old projections and retain a visible tile. Fullscreen reception is inspected in
its About reading page.

The GamesDB fixture retains the two Shared game works, Epic catalog and Steam2 IDs,
cached CMS offer/page edition evidence, Bluebird alias and Steam2 graph. The actual
LibraryRefreshPipeline and OwnershipRefreshCoordinator run with offline external HTTP.
Both starting surfaces receive two tiles → one, preserve two ownerships/external IDs,
record two edition observations, clear the pending suggestion and leave legacy version
IDs empty. Switching surfaces retains the publication.

Three observation-isolation methods retain backend coverage. Their seventeen cases
exercise seven delayed provider paths, including mapping away and back to the original
ID; explicit empty versus unavailable maturity responses; and retirement of IGDB
projections while independent store, plugin and user evidence survives. Their production
implementations are unchanged.

## Verification

All **3,667 component/live API cases across 181 files** pass without skips in **94.06
seconds**, recorded in `.tmp/task38113-components-final2.log`. The first full run caught
two regressions where restoring a journal editor returned to Overview after saving.
The correction preserves its originating section; the original assertions remain and
two additional cases cover History. The focused regression gate passes 218 cases.

The final production build, typecheck, changed-file formatting and migration audit pass.
Build output is `.tmp/task38113-build-final2.log`; focused regression output is
`.tmp/task38113-details-draft-regression.log`.

All **42 original source cases** and **10 new HTTP cases** pass. Logs are
`.tmp/task38113-source-tests.log`, `.tmp/task38113-source-ui-tests.log`,
`.tmp/task38113-source-layout-tests.log` and `.tmp/task38113-api-tests.log`; TRX files
are in `.tmp/task38113-dotnet-results/`.

All **30 native cases** pass in
`.tmp/task38113-native-run2-results/results.json`, fifteen per surface. After the final
refusal-copy correction, both affected cases pass again in
`.tmp/task38113-native-final-results/results.json`, asserting the exact sentence.
The initial native run used the wrong tile attribute; its two failures were corrected
to the production data-avalon-game selector without changing behavior assertions.

Screenshots from the successful native matrix were inspected for Details and Settings
candidate geometry, matching controls and corrected live Details on desktop/fullscreen.
The cases also inspect Chromium accessibility nodes, directional reachability, computed
grid bounds, truncation and visible focused actions. Solid test JPEGs isolate artwork
selection; they do not establish provider-network availability.

## Inventory and limits

Fifteen assigned methods are ported and three retain backend tests. The inventory has
**1,427 ported, 647 retained-backend and 32 framework-specific methods**, with **269
pending and 60 partial** remaining. Evidence lives in `migration-igdb-assignment.json`.
The overall migration gate remains incomplete.

Native checks use isolated temporary libraries and simulated standard Gamepad API frames.
They do not establish physical-controller or television validation. No live provider
account, user library, installed game or release was modified. The next task is
TASK-381.14, metadata editing and refresh composition.
