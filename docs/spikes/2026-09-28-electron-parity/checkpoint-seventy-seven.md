# Artwork state and backdrop checkpoint — 2026-10-01

TASK-381.22 covers sixteen frozen source methods and is the second task in the
authorized sequential batch through TASK-381.30.

## Implementation

The artwork browser now reads the same selected-cover query as the displayed
header. When the canonical cover is automatic, Current follows that projection.
It waits for the canonical read before doing so: a pending read cannot temporarily
replace a saved choice. Saved artwork retains priority until reset. Selecting a
provider candidate preserves its preview and focus through a projection change;
selecting Current resumes following live changes.

The preferred header work ID travels through both Details compositions and their
metadata/artwork entry points. Only the displayed automatic candidate changes.
Offers, optimistic revisions, save and reset still belong to the canonical work.
The shared query retains its existing key, freshness and cancellation behavior.

The authenticated source-order route previously wrote the setting and published
an event without updating the ArtworkPreferences singleton used by the backend's
candidate policy. Both original desktop/fullscreen order arrays reproduced the
failure: Steam 42 remained first after putting IGDB first. The route now delegates
to the existing preference writer, which normalizes and publishes its new snapshot
only after persistence, before the API change event is sent.

Native screenshot review also found the fullscreen browser's viewport dimensions
were being multiplied by the interface zoom, leaving an inset page. It now uses the
same inverse-zoom viewport and saved safe-margin calculation as other fullscreen
reading pages. Fullscreen controls use the original 28px action, 32px title and
24/22px reading hierarchy, responsive to viewport and saved text scales, with 64px
minimum controls. Persistent trigger, A and B hints sit below the fixed actions;
the URL field adds Y for the keyboard while focused. Desktop keeps its separate
bounded overlay. The source's 24px crop/input roles and 100/150px candidate images
are retained. Short fullscreen windows share rows for slot/source controls and
URL/save actions, preserving the preview at 1280×720 with 140% text.

An existing ultrawide backdrop regression exposed integer layout measurements at
the 21:9 threshold. With 0.85 interface zoom, rounded client dimensions classified
the exact viewport as too narrow. Backdrops now read fractional layout dimensions,
preserving the aspect ratio and fitted Steam hero while retaining physical decode
sizing and the existing ownership/transition policy.

## Source and fixture boundaries

All sixteen source methods pass unchanged as seventeen cases: fourteen main-project
cases and three UI cases. Logs are `.tmp/task38122-source-main.log` and
`.tmp/task38122-source-ui.log`; TRX files are under `.tmp/task38122-dotnet-results/`.

Ten methods exercise policy retained in the shared backend: plugin browsing and
candidate filtering, source normalization and failed-write behavior, shared saved
selections/imports/reset, landscape ranking and stable fallback ordering. Five need
Electron or API replacement evidence: displayed Current, grouped Steam hero identity,
the unowned-root fallback and the two live replacement/detach contracts. The mixed
Steam-order/decode-width method is also ported: ordering remains shared policy, but
Electron's own decode-width calculation must preserve its 1920×1080 → 3840 assertion.

The exact Current component fixture starts with canonical Steam 620 and projected
IGDB `preferred`, saves `manual`, changes the projection to `newprojection`, resets,
then projects Steam 440. Projection changes leave canonical state and revision intact.
The native/API fixture expresses preferred artwork using a real confirmed group and
preferred Epic header. Its final Steam 440 stage changes the group's actual Steam ID,
so that final repository mutation correctly changes the canonical revision. This
adapter is separate from the unchanged callback sequence in the component fixture.

The source grouped fixture contains installed GOG ownership 1 and duplicate Steam
ownerships 2/3 for release 2. SQLite forbids duplicate ownership of one release/store.
The API fixture uses the legal GOG/Steam population. A separate HTTP case repeats
Steam 42 observations at the repository boundary and executes the real candidate
service's deduplication. Both Details component fixtures retain the source's three
ownerships, installed GOG primary, canonical Steam hero and GOG Play dispatch. Their
mocked candidate response proves consumption, not backend deduplication.

Live backdrop fixtures retain the original 32×10 Steam 42 and 32×18 IGDB `art`
images. Desktop uses `igdb,steam,steamgriddb`; fullscreen uses
`igdb,steamgriddb,steam`. The old image remains attached and undisposed while the new
image is held, replacement releases it, and detach clears the image source and
subscription. The unowned root fixture retains `rootsaved`, owned-child Steam 42,
SGDB asset 50584 and its original URL. Root and Steam images fail before SGDB succeeds;
the owned child's `childsaved` background is never requested.

## Verification

The first native run exposed two fixture assumptions. React reuses its image element
when replacing pixels, so replacement checks must assert the old URL is no longer
referenced and is released; element removal belongs to the later detach check.
Native startup also inherited IGDB credentials and tried to authenticate against the
fixture's offline HTTP handler. The isolated fixture now supplies no credentials and
keeps the original local/cached artwork candidates. Neither correction changes
production behavior or relaxes the source image, ordering or ownership assertions.

The final HTTP gate passes seven new cases; eight prior artwork/backdrop cases also
pass, for fifteen distinct HTTP cases. Two application preference regressions pass.
Logs are `.tmp/task38122-api-final2.log`, `.tmp/task38122-api.log` and
`.tmp/task38122-application.log`. The fixture and production backend build with no
warnings or errors (`.tmp/task38122-fixture-build-final.log`).

The renderer owner records 96 distinct focused passes. The later five-file gate
passes 71 cases, including the new fractional-zoom regression and both source
decode-width cases (`.tmp/task38122-layout-focused.log`).

Seventeen distinct native cases pass: five API-backed artwork cases and twelve
browser/backdrop cases, including the added 720p/140% text variant. The final
affected-browser run verifies source typography, candidate image sizes, viewport
and safe margins, the visible preview and crop controls, and keyboard/controller
hints. Desktop retains its 1440×1000 overlay bounds and fixed preview/actions.
The existing ultrawide Home case keeps its exact 2520×813.75 geometry assertion,
crossfade, pending-image retention and URL-release checks. Details and Library
retain their independent image ownership and detach behavior.

Results are consolidated in `.tmp/task38122-native-evidence.json`; the final passes
are recorded by the `task38122-native-final3`, `final4` and `final5` logs/results.
Desktop's last helper correction waits for the original library button to regain
focus after Details closes, before navigating to Settings. It keeps the strict
Settings focus assertion and makes no production focus change.

Desktop and fullscreen saved Current captures, the fullscreen replacement and
root fallback, and the final 1280×720/140% artwork page were visually inspected.
The latter retains a 136px-high preview with both crop controls and all local
controller hints visible. These tests use generated local image bytes, isolated
databases and simulated controller input; they do not establish physical-controller
or live-provider validation.

The production build passes (`.tmp/task38122-build-final3.log`). All 3,918
component/live API cases pass across 196 files in 366.26 seconds
(`.tmp/task38122-components-final.log`). Formatting and diff checks pass.

The sixteen assigned methods resolve as six ported and ten retained-backend. The
inventory contains 1,537 ported, 663 retained-backend, 33 framework-specific,
173 pending and 29 partial methods. The overall migration gate remains incomplete.
This milestone continues into TASK-381.23; review remains after TASK-381.30.
