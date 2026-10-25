# Runtime metadata credentials checkpoint — 2026-10-01

TASK-381.28 covers eight frozen source methods, expanding to nine cases. The unchanged
originals pass without skips. Four queue cases preserve held startup, ten-request
coalescing, one non-overlapping follow-up, failure recovery and cancellation before
startup completes. These remain tests of the actual shared application coordinator.
The backend subscribes credential changes to that coordinator and runs its IGDB-only
refresh pipeline after startup; shutdown removes the subscription.

Five original runtime cases exercise real SQLite and the production credential/token
registrations. They preserve cached absence, rotation with the same client ID, removal
with and without external configuration, transaction rollback and removal blocked on
an in-flight token write. The persistence gate is in the actual store write. It is not
replaced by an HTTP timing assumption. Failed mutation preserves the old token and
emits no change notification.

Three new HTTP cases use the actual settings service and its existing token-provider/
credential-updater singleton. Both settings and credential resolution receive the same
controlled configuration. PUT activates cached absence and replaces a same-client
token; DELETE immediately selects configuration credentials or disables authentication.
Redacted snapshots expose neither secrets nor access tokens. The twelve existing HTTP
settings cases also pass, covering protected storage, legacy migration, rollback,
unreadable values and sanitized errors. The deterministic reversible protector and
canned token transport establish behavior, not cryptographic or live Twitch validation.

The source log is `.tmp/task38128-source.log`, with its TRX under
`task38128-source-results`. All fifteen HTTP cases pass in `task38128-api.log` and
`task38128-api-results`. No production backend change was needed.

## Shared desktop and fullscreen forms

All 38 existing component cases pass: nineteen per surface. They cover empty, saved,
unreadable and externally configured states; masked local drafts; accepted and refused
saves; removal; failed read/mutation retry; setup-link outcomes; dirty-field preservation;
and a held save that admits one mutation. Desktop and fullscreen Setup and Settings
all use the same credential form. Setup supplies its bounded or sectioned layout,
keyed draft lifetime and shared busy boundary. The reviewed composition and executed
assertions are in `.tmp/task38128-ui-evidence.json`.

The native checks use disposable data directories, an explicit prebuilt backend and
activation helper, and the unchanged production renderer from checkpoint 82. Their
first protection refusal and external-link outcomes are simulated; subsequent save,
read and removal use the real HTTP service. A `--no-sync` host verifies forms and
settings mutations, not execution of the automatic metadata queue. Synthetic controller
input does not establish physical-device behavior.

All three native journeys pass: desktop from `task38128-native-results`, fullscreen
at normal and 140% text size from `task38128-native-final4-results`. The fullscreen
control assertion now accounts for the documented 0.85 baseline and Chromium's
1/64-pixel quantization. It retains the exact 72px authored minimum and separately
verifies the painted height (61.1875px), the 44px hit-target floor and the original
24/22px typography scaled by the text preference. Earlier strict unscaled height
assertions failed; this required a test adaptation, not a product layout change.
TypeScript and focused formatting checks pass. Native evidence and visual review
are recorded in `.tmp/task38128-native-evidence.json`.

All eight source methods retain the shared backend implementation, with exact queue,
credential source, cache, token, transaction and registration paths recorded in the
migration mapping. This package does not replace working services with frontend copies.
The inventory retains 2,435 methods: 1,606 ported, 696 retained-backend,
35 framework-specific, 80 pending and eighteen partial. The complete migration gate
remains incomplete; the authorized batch continues with TASK-381.29.
