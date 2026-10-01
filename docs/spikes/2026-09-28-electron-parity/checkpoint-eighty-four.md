# Plugin source and sign-in checkpoint — 2026-10-01

TASK-381.29 covers fourteen frozen source methods. All eighteen original cases pass
without skips: ten main-project cases and eight desktop/fullscreen presentation cases.
The source files retain their fixtures from `cf45d9f1127243a987d3cf6e664a32fc767ecb67`.

## Provider attribution and settings

Both Details compositions now collect every distinct provider source explanation in
entry order, with its store prefix. A Steam-primary group retains the exact Xbox
played-history explanation. PlayStation uses the original name in filters and source
summaries. History-only plugin records retain their source without invented Play or
Install actions. Saving an enabled, loaded provider's settings acknowledges the queued
refresh; inactive providers receive activation guidance instead.

The original PSN fixture constructs a presentation entry without an ownership row,
giving it an unknown install state. The renderer type and validation now accept that
nullable presentation value. The backend's stored ownership and API contract remain
boolean. Component tests preserve the exact unknown-state fixture; real API and native
tests use the persisted false value. They do not claim the API emitted null.

The NPSSO matrix preserves one empty masked input despite a stored token, the exact
Sony setup URL, two history switches and no account sign-in controls. The first save
sends exactly three keys, clears the local secret and retains fullscreen Save focus.
A subsequent save omits the secret. Removal disables its action; leaving clears an
unsaved replacement. The PlayStation filter retains its one matching title and selects
only Game 2 from the original two-title population.

All 272 focused renderer cases pass across ten suites. They include shared Details,
nullable library parsing, settings, source filters, action policy, cached GOG notes,
install-folder controls and controller dispatch. TypeScript, formatting and the
production build pass. Logs are `.tmp/task38129-ui-components.log`,
`task38129-ui-typecheck.log` and `task38129-build.log`; the renderer bundle is
`index-CNBUuq-a.js`.

## Shared backend and HTTP evidence

The plugin integration and validation cases retain the actual catalog, SDK loading,
storage and synchronization services. Their original fixtures keep source-scoped
metadata and artwork, user overrides, built-in facets, release ordering, unavailable
observations, malformed provider results and separately packaged opt-in behavior.

Twenty backend cases pass: eighteen HTTP cases and two direct registered-service
cases. Five new HTTP cases use real SQLite, cached source observations and an isolated
SDK-only fixture plugin with the source's PSN settings schema. The actual catalog,
DPAPI storage and settings endpoints handle save, omission and removal. Public settings
omit the secret; the isolated fixture control endpoint checks the fake stored value.
Both fixture controls and secret routes require the real bearer token. Backend assembly
reference checks supplement the original Avalonia assembly isolation assertion.

Thirteen existing Epic HTTP cases also pass, alongside 121 Electron settings, main,
preload and policy cases. Empty or unavailable embedded capture offers explicit manual
continuation; cancellation does not force another prompt. The two new direct-service
cases invoke the still-registered legacy Epic prompt policy with no prompts or two
unavailable prompts and require no provider request. Normal Electron HTTP uses request
creation and explicit completion, not that legacy prompt loop. Its continued callable
policy is recorded separately from the actual native/manual frontend flow.

The source logs are `.tmp/task38129-source-main.log` and `task38129-source-ui.log`.
The backend log is `task38129-api-final2.log`; its fixture build has zero warnings or
errors. An initial fixture teardown encountered Windows' mapped collectible DLL; the
test cleanup now handles that condition as the original package tests do. No production
C# change was required. The PSN fixture is a test package, not a live Sony integration.

## Native and visual evidence

All eleven distinct native journeys pass on the same renderer: eight new source/settings
cases across both surfaces and three existing Epic fallback/cancellation regressions.
Six passes come from `task38129-native-results`, the fullscreen grouped-source pass
from `task38129-native-final-results`, and the four remaining passes from
`task38129-native-final2-results`. Each fixture starts with a fresh disposable library
and explicit prebuilt backend and activation helper. Library and settings responses
are not rewritten; only the Sony external destination is intercepted.

The tests preserve exact source viewports, source text, filter counts, three-key save,
secret omission/removal and fullscreen keyboard/focus. Global Library/Settings navigation
retains LB/RB; Details uses its local B Back control. Fullscreen portrait tiles intentionally
hide captions, so their badge text is checked separately from the visible Details
PlayStation label. This does not claim a visible portrait badge.

Initial failures were harness assumptions: a collapsed filter group, a non-exact region
name that also matched active filter chips, root menu hints expected on a Details page,
an action-name match that included Play history, a preference seeded after startup,
and reading the stored secret while removal was still pending. Corrections use the
actual preference PUT and completion acknowledgement, exact regions and the proper
action scope. They did not require another production build. Screenshot review and
report provenance are consolidated in `.tmp/task38129-native-evidence.json`.

Synthetic controllers and intercepted Sony/Epic provider documents do not establish
physical-device or live-service behavior. The source console-availability assertion is
narrowly framework-specific; native-first/manual-fallback order has executed Electron
replacement evidence. Five assigned methods are ported and eight retained-backend.
The inventory has 1,611 ported, 704 retained-backend, 36 framework-specific, 66 pending
and eighteen partial methods. The complete migration gate remains incomplete; the
authorized batch continues with TASK-381.30.
