# Steam capture and reported activity checkpoint — 2026-10-01

TASK-381.30 covers seventeen frozen source methods. All twenty-four original cases
pass unchanged: seventeen main-project cases and seven presentation cases. Source
fixtures remain at `cf45d9f1127243a987d3cf6e664a32fc767ecb67`. This is the final task
in the user-authorized TASK-381.21–381.30 batch.

## Host boundaries

The acquisition export bridge now returns the saved outcome and the backend's
ownership count from the same export response. The renderer does not infer record
counts from CSV lines, which can contain quoted newlines. The existing main-process
writer retains its UTF-8 signature, exact content and cancellation-without-writing
behavior. Preload tests execute the actual named bridge and preserve counts zero
and three for successful and cancelled destinations.

Capture tests execute the actual reader and page script against sanitized original
HTML. The combined complete case reports twelve history expansions and nine further
licence pages; the bounded case reports one hundred history expansions and fifty
further licence pages. Both final
pagination markers remain in capped documents. A continuing walk stays pending
instead of emitting a completed result. Existing stalled and missing-session cases
preserve incomplete or absent pages and independent counters.

All four original expected/before/after identity combinations terminate a rejected
capture, discard its pages, remove its navigation listener and perform no later reads
after the document returns to an acceptable identity. Rejection ends that capture
attempt; it does not forbid a separately authorized new attempt. A null capture request
rejects before creating a profile or window. Declined consent and unavailable browser
creation retain readable outcomes, no pages and zero counters.

The legacy three-argument C# result factory's optional-parameter compatibility is
narrowly framework-specific. It passes unchanged, but that factory is not in the
Electron composition. A single observed final page correctly reports exhausted in
Electron; it is not claimed to reproduce an omitted null stop reason.

Focused host logs are `.tmp/task38130-host.log` (83 cases across four files) and
`task38130-capture-policy.log` (82 cases across two files). These runs overlap and
must not be added together. The second includes the final pending-continuation
assertion. Source logs are `task38130-source-main.log` and `task38130-source-ui.log`.

## Presentation and integration

Library settings and account statistics share one export command with pending
protection, exact record counts, cancellation feedback and a safe destination-error
message. Missing capability disables the command. Successful retries clear earlier
errors; disabled-button focus is restored only when focus was lost to the document.
The command does not appear among first-run library preferences.

Desktop Steam observations remain inline. Fullscreen per-game Play history offers a
separate Steam projection and an observation reader; global Activity keeps its own
entry. Both retain exact minute, interval and uncertainty copy. Back closes one reader
at a time and restores its originating control. A, B, paging and reading glyphs remain
local to their actions. Shared journal-note reading retains its original defaults.

Review prompted two fixes: immediate Back while the global Steam tab still has focus
now returns to History, and the portalled projection directly
owns its fullscreen typography instead of inheriting desktop timeline dates. Body,
observation bounds and action roles use 24, 22 and 28 pixels at the 1920-wide reference
viewport before text/interface scaling. Fullscreen saved-page results also retain the
source's `filename · LOADED` label. Desktop keeps its descriptive ready labels.

Screenshot review additionally found that the global Steam projection inherited a
later theme-layer paragraph rule. Its scoped stylesheet now participates in the theme
layer so the same reading sizes apply inside the global host and the per-game portal.
The Library export capture also exposed a 20-pixel horizontal overflow from existing
reading paragraphs: their 100% width did not allow for the settings' side margins.
Those margin-bearing paragraphs now use automatic width. The exporter and its status
already fit; the native check measures the complete settings scroller.

Nineteen HTTP cases pass: twelve new source-shaped cases, five existing page-contract
cases and two account/acquisition regressions. They verify the entire quoted multiline
CSV, three versus zero records, zero versus missing price, and unchanged ownerships.
Exact Alpha/Beta pages import twice, retaining two licence facts with zero new facts
on the repeat. The full original unknown-account HTML remains unassigned despite a
confirmed account; own scope withholds projected date/price, all scope restores the
date, and an explicit different account cannot fill the existing ownership.

Activity fixtures retain the original controlled repository for exact estimate and
comparison-unavailable rows. API and renderer jointly preserve visible ownership,
normalized account, failure and missing-confirmation boundaries. A separate real SQLite
observation case verifies hidden-game, other-account and covered-increase exclusion.
The source's separate GOG preview tracker becomes actual Steam Details with the same
600-second recorded sitting, retaining the unchanged-total assertion. Persisted native
sessions use the valid `process_watch` value; component fixtures retain the source's
`process` literal. Controlled rows are not claimed to be reconciled raw observations.

All 4,130 component/live API tests pass across 216 files with four workers. This
includes the earlier 468-case focused renderer run and all host checks; these counts
overlap. The first broad run exposed setup tests that skipped Epic's restored
confirmation, plus two five-second load timeouts. Setup now tests the real confirmation
and pending navigation. Bounded concurrency resolves the architecture timeout; the
exact 50-page/100-click DOM case has its own twenty-second execution budget, preserving
all source counts and assertions. The final full log is
`.tmp/task38130-full-integration-reviewed.log`.

After the final CSS corrections, all 93 focused settings, activity, export and
architecture cases pass across five files (`task38130-final-css-regressions.log`).
The final type check passes as well. These cases overlap the broad suite.

The fixture build has zero warnings/errors. TypeScript and the production build pass;
the final renderer bundle is `index-Vknj1PPX.js`. Backend evidence is in
`.tmp/task38130-backend-evidence.json`, HTTP results in `task38130-api-final.log`, and
the final build in `task38130-build-final-reviewed.log`. No production backend code changed.

## Native verification

Thirteen distinct native journeys pass: ten new desktop/fullscreen cases and three
existing file-picker/platform regressions. All use disposable data, the prebuilt
backend and fixture host, actual preload/main/export handling and real saved-page
loading/import. No production API responses are rewritten. Export checks compare
actual UTF-8 BOM and CSV bytes, hold a destination pending, cancel without writing,
recover from an injected disk-full write failure and require explicit replacement.

The initial run passed seven cases. Corrected route/role locators and awaiting actual
import completion resolved the saved-page and desktop activity harness failures.
The final affected runs additionally verify the production typography and paragraph
width corrections. `task38130-native-final3.log` supplies passing fullscreen export
and repeated saved-page cases; `task38130-native-final4.log` passes fullscreen activity
and all three existing regressions on `index-Vknj1PPX.js`. Earlier unchanged cases are
recorded in `task38130-native-initial.log` and `task38130-native-final2.log`; these runs
overlap and their totals must not be added.

Native measurements verify 32-pixel headings, 24-pixel body text, 22-pixel observation
bounds/time and 28-pixel actions in both global and per-game fullscreen hosts. The
viewport calculation yields 27.9936 pixels for the action role; assertions allow
0.05 pixels of rounding and also measure painted text. The Library scroller has no
horizontal overflow after the paragraph correction. Reviewed screenshots show the
export count, both `LOADED` files and two already-recorded facts on repeat, separate
global/per-game reading, root bumper hints and local reading/Back hints.

Controller input is simulated through the Gamepad API. This does not establish
physical-device testing, live provider sign-in or release-package validation.

## Migration and batch boundary

All seventeen assigned methods now have reviewed, executed evidence: fifteen ported,
one retained backend exporter and one narrowly justified C# compatibility overload.
The generated inventory validates 1,626 ported, 705 retained-backend and 37
framework-specific methods, leaving 54 pending and thirteen partial. The complete
migration gate still fails on those 67 methods.

This milestone completes TASK-381.30 and the authorized TASK-381.21–381.30 batch.
Work stops for user review before TASK-381.31; it does not establish completion of
the whole Electron port.
