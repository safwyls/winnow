# Fetch status and diagnostics checkpoint — 2026-10-01

TASK-381.25 covers thirteen frozen source methods and is the fifth task in the
authorized sequential batch through TASK-381.30.

## Implementation

Avalon's passive desktop caption now shows the remaining metadata count using the
original label, separator, data font and singular/plural copy. Fullscreen hides the
caption while its application-level observer continues tracking progress. Progress
events have a dedicated read path and no longer invalidate the Library and feed.
Superseded reads are aborted and checked against a local generation; disconnection
clears the caption and reconnect requests the current state. Mode changes and dialogs
do not reset that observer or move focus. The named status uses polite, atomic live
updates; physical screen-reader behavior is not claimed by these checks.

Startup logs persist bounded exception metadata independently of the human alert:
type, named call frames, available native code/errno, version, commit and random run
identity. The entire exception message is omitted, including multiline content.
Paths and anonymous/eval frames are omitted. Reporting failure preserves the original
alert and exit status, and cancellation creates no failure log.

Windows login-setting reads and writes share the same executable and background
arguments. The logs action uses the backend's resolved directory, includes its exact
path in local failure feedback, and clears the feedback on successful retry. Settings
retains the action after watcher recovery on both surfaces. Watcher subscriptions stay
active under a modal; presentation resumes accurately after it closes, retaining the
existing modal focus policy.

## Source and fixture boundaries

All fourteen assigned original cases pass unchanged: eleven main-project cases and
three UI cases. Two additional original watcher failure/recovery cases also pass,
covering both surfaces without focus changes. All sixteen bodies execute without
skips. The five source files match the frozen revision. Logs use the
`.tmp/task38125-source-main` and `.tmp/task38125-source-ui` prefixes; TRX files and
source captures are under `.tmp/task38125-dotnet-results/` and
`.tmp/task38125-original-captures/`.

The status fixtures retain zero, 1,247, 80→40, completion, Clear followed by a late
40, and singular/plural copy. The native caption fixture retains the original
1200×688 viewport and 997→fullscreen→1→desktop→0 sequence. The pure status object
ignores Report after Clear; the separate live reporter may begin the next pass,
matching the original distinction between the view model and its reporter.

Startup persistence uses the original arbitrary private message, both normal-sink
states, a file blocking the logs directory, and cancellation without a log directory.
JavaScript records available native code and errno rather than inventing a .NET
HRESULT. The source executable containing spaces and background argument are passed
to Electron's native login API; its read and write options are identical. Tests
capture registration without changing the user's startup entries.

## Verification

The authenticated backend test passes progress snapshots 997→1→0 and independent
progress events. Watcher failure produces a safe diagnostics snapshot and its own
event; recovering a different operation does not clear the failure. Recovering the
failed operation clears it. The response contains neither the private exception
message nor the selected directory. Evidence is `.tmp/task38125-api.log`.

The isolated native fixture builds with zero warnings or errors. It drives the real
backend reporter and watcher health through authenticated fixture-only routes and
counts API reads. External provider access is disabled.

All 202 focused component/host cases pass: 58 in four host files and 144 in six
renderer files. The final caption font correction also passes its three targeted
cases. TypeScript, formatting and the production build pass. Logs use
`.tmp/task38125-host-focused-final.log`, `.tmp/task38125-ui-focused.log`,
`.tmp/task38125-ui-font-correction.log` and `.tmp/task38125-final-ipc-build.log`.

Six distinct native cases pass: the source-size caption round trip, watcher/log retry
on each surface, captured Windows login registration on each surface, and refused
renderer startup with a private marker. The initial refusal fixture used a URL path
that correctly failed development-origin validation; the corrected root URL with a
query marker reaches Chromium's actual connection refusal. Its persisted log omits
the marker and selected directory. Native log feedback initially exposed Electron's
transport prefix; the preload now removes that exact wrapper, and both affected
surface cases were rerun with exact useful-copy assertions.

`.tmp/task38125-native-evidence.json` records the reports, decoded attachments and
captures. The original 1200×688 desktop caption fits its header with 10/10/11-pixel
type and tabular data figures. Desktop and fullscreen log feedback captures were
inspected for readable paths and unchanged focus. Controller state is simulated;
physical controller and screen-reader validation remain separate. Native login APIs
are captured without changing Windows autorun registration.

All thirteen assigned methods are ported. The inventory now contains 1,584 ported,
665 retained-backend and 32 framework-specific methods, with 134 pending and 20
partial. The frozen total remains 2,435 methods. The final migration gate therefore
still fails; this checkpoint completes TASK-381.25, not the entire port.
