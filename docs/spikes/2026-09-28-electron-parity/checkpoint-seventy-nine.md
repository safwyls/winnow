# Native activation and installation identity checkpoint — 2026-10-01

TASK-381.24 covers eighteen frozen source methods and is the fourth task in the
authorized sequential batch through TASK-381.30.

## Implementation

Windows Electron now obtains frontend ownership through a direct child in the
backend executable. The new Avalonia-free `Winnow.Activation` assembly shares the
original mutex, pipe, security and bounded binary protocol with Avalonia. Its helper
branch executes before HTTP hosting, database ownership or background workers.
Electron and Avalonia retain separate frontend namespaces; backend ownership remains
independent of both.

The helper verifies its actual OS parent, holds that process handle, and exits when
the parent dies or its input closes. Real kernel objects retain the current SID as
owner, a protected DACL, NETWORK denial and only the current-user allowance. Typed
requests preserve positive Int64 identifiers as decimal strings. Node validates the
handshake, effective root and bounded output. Established ownership survives a pending
quit drain and ends at final shutdown. Unexpected helper loss reports fatal exit 3
without shutting down the independently running backend.

Windows requires a prebuilt helper. A missing backend can still show the recoverable
connection screen when a separate helper is available; no available helper produces
an actionable startup failure. The default data location uses the shared migration
and fallback operation. The resolved backend root stays distinct from an explicit
override, preserving existing Chromium profiles, startup settings and shell arguments.
Linux and macOS retain their native Electron singleton; package execution remains a
later milestone.

Jump Lists retain the existing `Winnow.Electron` profile hash and the independent
`app.winnow.afterglow` installer identity. Isolated destinations carry their quoted
root; normal destinations omit an override. Generic tasks publish immediately while
artwork loads. Center-cropped cover icons contain six PNG frames, with stable
content-addressed paths under the selected library. Cancellation and generation
checks prevent old artwork from publishing over newer tasks.

Application settings show the frontend version and source commit on desktop and
fullscreen. Development uses the frontend package version rather than Electron's
runtime version; source archives without Git report an unavailable commit. The
existing `hoard` theme alias remains subordinate to an authored theme with that ID.

## Source and fixture boundaries

All twenty-four assigned original cases execute unchanged, including opt-in actual
Windows Jump List publication. The supplemental activation run reports twenty-four
passing cases, but four apphost/shell rows return early because their optional executable
variable is unset. The combined source run therefore reports 48 passes with 44 fully
executed bodies. It does not claim those four optional native launch checks.
Evidence is `.tmp/task38124-source-final.log` and its TRX in
`.tmp/task38124-dotnet-results/`.

The source ICO fixture remains 80×160 red with an 80×80 green center; its cache fixture
remains 64×96 blue. NativeImage replaces Skia while retaining exact pixel, PNG-frame,
ICO-header and cache assertions. Taskbar namespace adapters preserve existing Electron
installation identity. The frontend package and backend/Avalonia assembly retain
independent version sources until release entry points migrate.

The original binary pipe implementation now serves Electron's companion. Two earlier
framework-specific mappings—oversized/empty frame refusal and accepting the next
client while the previous handle remains open—become retained shared native policy.
Their original assertions execute against the extracted implementation.

The source queue limit remains 64. The helper acknowledges delivery to Node; renderer
queue admission and draining are verified separately. A pre-readiness native fixture
holds helper output before the frontend can construct its window; Electron's own
`app.isReady()` is already true. This preserves delayed listener/frontend readiness
without claiming that native framework readiness is held.

## Verification

The shared helper and authenticated backend composition gate passes thirteen cases,
including actual ACLs, parent spoof refusal, parent death, EOF, exact Int64 forwarding,
selected-root database/WAL/cache containment and the real folder route. Main-process
focused checks pass sixty cases, identity and theme checks pass fifty-three, and Jump
List checks pass twelve. The fixture build and production frontend build pass.

The full component/live API run executes 3,992 cases across 203 files. It passes
3,989 cases and exposes three stale test expectations in two files. The dependency
walker now permits only the frontend's own package manifest from main; backend,
database and renderer boundaries remain enforced. Settings capture uses the new
labelled About values and exercises the actual fullscreen link control. Those two
files then pass all ten cases. Combined, every case passes with the final source;
the other 201 files and production bundle are unchanged. Logs are
`.tmp/task38124-components-final.log` and
`.tmp/task38124-identity-final-corrections.log`. The built renderer is
`index-GqYybSk9.js`.

The first native sequence is stopped after six passing activation cases expose two
standalone fixture issues. Their initialization must schedule `whenReady()` rather
than block Electron's module startup on it. The raw Node pipe client also needs the
same bounded connection wait as the original named-pipe client while Windows recreates
the listener. The corrected five-case run passes malformed-frame/queue recovery,
secondary no-database startup, both exact icon fixtures and actual isolated Windows
publication (`.tmp/task38124-native-repair-results/results.json`). Assertions and
production code are unchanged.

Four full-application native cases pass across desktop/fullscreen and house/authored
legacy themes. They verify actual backend artwork requests, immediate generic tasks,
held 128px image completion, cached ICO publication, effective paths and native kernel
security. Killing the owned helper reports exit 3 while the backend remains available;
ordinary closure releases the helper. About values are legible and unclipped in the
inspected 1600×1000 desktop and 1920×1080 fullscreen captures. The packaged-branch
fixture loads frontend metadata through a test-owned application directory and enables
that branch explicitly; it is not an installed executable test.

Fourteen affected startup regressions also pass, retaining saved modes, one coherent
first snapshot, malformed configuration/logging/schema exit 3, invalid-directory exit 2,
renderer failure, background restoration and reduced-motion readiness. These eighteen
cases use `.tmp/task38124-native-host-final-results/results.json`. Native suites now
resolve explicit prebuilt overrides or ordinary Debug outputs rather than depending on
the previous task's scratch build.

The final activation and cold-setup sequence passes ten cases, including unpackaged
frontend identity on both surfaces. Actual Windows Jump List publication, empty tasks
and cleanup all return `ok`. Its smoke adapter uses the wrapper file's URL rather than
Playwright's injected debug argument. The final reports under
`.tmp/task38124-native-activation-final-results/` and
`.tmp/task38124-native-publish-final-results/` complete 31 distinct passing native cases.
The earlier interrupted results are superseded by complete JSON reports. Inputs are
simulated; physical controllers and installed release packages remain separate milestones.

All eighteen assigned methods are ported. With the two corrected shared native-policy
dispositions, the inventory contains 1,571 ported, 665 retained-backend, 32 framework-specific,
145 pending and 22 partial methods. TypeScript, formatting and diff checks pass. The
complete migration gate remains incomplete. Work continues to TASK-381.25; the batch
review boundary remains after TASK-381.30.
