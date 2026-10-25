# Activity recovery and journal checkpoint — 2026-10-01

TASK-381.17 covers eighteen frozen source contracts. It is the seventh task in the
authorized sequential batch through TASK-381.20.

## Source boundaries

The paging sources use controlled repository readers with one record per response.
Their first and second pages retain session identities 1 and 2 and the exact cursor.
Two week changes during a held, cancellation-ignoring first read must publish only the
final requested week, fourteen local calendar days earlier. Loading another page is
explicit. A disposed page cancels its pending read and never publishes a late response.
Retry preserves the failed cursor; saving the older session preserves selection and
both loaded pages without rereading page one.

The account sources hold a synchronous repository read away from the UI thread,
exercise cancellation and late completion, and preserve usable focus through loading,
failure and retry. Electron's separate backend process and request cancellation are
the corresponding boundaries; a mock Promise alone does not establish process isolation.

The journal view-model fixture uses session 7, ownership 3, August 22, 2026 at 21:00 UTC,
`First route.` and rating 2, then saves `Found the shortcut.` and rating 4 before
confirming deletion. The editor matrix separately uses session 7 at September 10,
2026 at noon, with and without `Original note` and rating 4. Desktop Details,
fullscreen Details and fullscreen Activity must reject a whitespace-only unrated note,
trim successful text, preserve a failed draft, and block conflicting actions during a
held retry. The source fullscreen hierarchy uses its long title and note at 1280px
and 2560px, checking grouped content, directional focus and the reachable Save action.

Session recovery begins with one open monitored sitting and its existing note/rating.
Finding and completing that same sitting must retain its identity and produce one
ten-minute activity record; an unfinished session must not become a fabricated
completed timeline bar.

## Implementation

The native notification adapter now requires a live, unfocused window with a nonzero
native handle before attempting OS delivery. Missing, destroyed or headless targets
return unavailable without invoking the notifier. A window hidden in the tray remains
eligible when its native handle is valid. Lifecycle races during handle lookup also
return unavailable. The existing renderer fallback and explicit journal save remain
responsible for user interaction and persistence.

Account reads now show `Reading your account statistics…` and the actionable
`Couldn't read Steam spending. Try again.` failure sentence. Export failures remain
separate from read failures. The existing refresh control retains its DOM identity
through loading, retry and success. Request cancellation reaches the named preload
bridge, and a cancelled or disposed query rejects a late response even when its
reader ignores the cancellation. Native testing exposed intermittent Chromium focus
loss when disabling the retry button. Completion now restores that lost focus, while
leaving focus alone if the user chose another control during the read.

Activity serializes physical reads across week and section changes. Obsolete query
signals prevent publication and skip queued intermediate scopes; the current bounded
transport read finishes before the latest scope starts. Leaving the last observer
cancels the physical request; an overlapping observer keeps a shared read alive.
This follows the original refresh loop's ordering without a timer
or fabricated request count. It intentionally differs from immediately aborting the
old HTTP fetch on every scope change, which can launch intermediate requests even
while a cancellation-ignoring backend reader is still running.

Desktop Details edits journal entries inline, keeping the section tabs usable and the
draft available when returning to Journal. Fullscreen keeps its separate editor.
Pending saves reject duplicate submissions, rating changes and cancel actions, including
Escape, outside clicks and dismissal of the enclosing Details view. The
field's accessible name is `Journal note`; failed saves keep their draft and show the
original recovery sentence. Fullscreen prompts group the finished session, journal
and rating controls, and state `No rating` until one is chosen.
The prompt retains the source roles: 18px regular group labels, 48px bold title,
22px regular duration/rating metadata and 24px regular error text, all using BodyFont.
Its keyboard hint retains its separate scaled 20px size. Explicit focus rows move from Edit note to
the first rating and then Save, with horizontal movement within the rating/action rows.
This avoids generic spatial navigation choosing the fifth rating beneath a wide editor.

Fullscreen Activity's triggers cycle Sessions, Updates and Journal. The unrelated
History/Library summary row no longer advertises trigger actions. Main-menu LB/RB
hints remain visible, as does the prompt's `Y Keyboard` hint.

## Framework adaptations

The source hierarchy explicitly calls `FocusInitial`; it does not require arrival to
steal focus. Native tests wait for the asynchronous journal model to be ready, then
enter the passive fullscreen prompt with actual Tab input,
assert its initial Edit note target, then follow Down to rating and Save. The source
long title, note, 7800-second duration and both viewport widths are retained.

The recovery source supplies a clock eleven minutes after its session starts. Native
tests use the real clock and begin that sitting eleven minutes earlier, completing
the same identity after ten minutes. Its process linkage, note, rating, 600-second
duration and single activity record are unchanged. Electron's compact history duration
is `10 min`; source/component fixtures retain the explicit source clock.

The reload fixture retains its original fractional-second session timestamps. Native
tests wait until that seed second ends before querying, because the production upper
bound is rounded down to a whole second. Disposal assertions end with exactly the two
source page reads; a supplemental re-entry check uses a new time bound and explicitly
expects a new first-page read, with no late page-two publication.

## Verification

All **30 original source cases** pass unchanged: two main-project cases and 28 UI
cases. All nine source files match the frozen revision. Logs are
`.tmp/task38117-source-tests.log` and `.tmp/task38117-source-ui-tests.log`, with TRX
results under `.tmp/task38117-dotnet-results/`.

The focused HTTP gate passes **17 cases**: eleven new activity/journal cases and six
existing journal-prompt cases. It preserves exact note identities, ratings, timestamps,
revision-safe deletion, held/rejected writes, one-row paging and cursors, cancelled
reads, the source account fixture without monetary facts, and real monitored-session
recovery. Existing cases also verify opt-in defaults and accepted saves through backend
shutdown and frontend disconnection. No production backend change was required.

The complete component/live API gate passes **3,789 cases across 187 files** in
99.33 seconds (`.tmp/task38117-components-final.log`). This includes 37 new activity
contracts, eight account recovery cases, five notification delivery cases and 39
notification presentation cases. The shared-reader, enclosing-Details dismissal,
exact one-sixth-hour projector and focus-restoration checks all pass. TypeScript and
the production build pass (`.tmp/task38117-build-final6.log`); formatting and the
migration audit pass as well.

Native verification passes **53 distinct cases**: forty new source-equivalent cases
and thirteen affected earlier journal/Details regressions. Four additional account
focus repetitions pass. Five final typography reruns pass after the fullscreen role
correction, including 140% text and 20/28px keyboard hints. Results come from focused
runs; the final per-case provenance is in `.tmp/task38117-native-evidence.json`.
Earlier failures were diagnosed and superseded by passing evidence, including the
consumer harness startup failure corrected by supplying the built backend explicitly.

Eighteen screenshots were inspected. Desktop evidence includes the selected older
note, recovered-session plot, held write and a fully visible deletion confirmation
at 1200×640. Fullscreen evidence includes the empty Journal's main-menu bumper hints,
selected older note, recovered sitting, retry focus and both source prompt sizes.
The prompt's computed sizes, weights and body-font family match the source roles;
Save satisfies the original full-viewport bounds with its one-pixel tolerance.
The final prompt captures are under `.tmp/task38117-native-typography-results/`;
the ledger lists each inspected file and the corresponding assertions.

Native checks use real Electron windows, preload/main IPC and authenticated isolated
backends. Controlled repository readers preserve the original injected source cases.
Controller input uses standard Gamepad API frames; it does not certify physical
controller hardware. OS notification delivery is intercepted. All runs use disposable
data directories and all native processes were closed after testing.

All eighteen assigned methods are ported. The generated inventory contains 1,476
ported, 650 retained-backend and 32 framework-specific methods; 238 pending and 39
partial methods remain. Complete migration and release readiness are not established
by this checkpoint.
