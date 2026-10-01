# Startup and first-run setup checkpoint — 2026-10-01

TASK-381.23 covers seventeen frozen source methods and is the third task in the
authorized sequential batch through TASK-381.30.

## Implementation

The Electron host resolves initial backend startup before constructing its library
window. It reuses a healthy or still-starting published backend, starts at most one
companion, and preserves an owned companion's fatal exit 2 or 3. Configuration and
schema failures report bounded, redacted stderr through the existing startup error
boundary. Backend configuration remains rooted at its installation. Missing companions
and the bounded initial timeout retain the recoverable connection screen; disconnection
after a successful attachment retains the existing reconnection behavior.

Activation during that initial wait is queued until window creation is permitted.
Quitting cancels the wait and releases its listener. The diagnostic pipe does not keep
the frontend alive, and the detached backend retains its independent lifetime.
The initial preference read uses the remaining startup deadline before window creation,
preserving the saved initial presentation even when that response is held. Primary
renderer loading is also observed: rejection reaches the same exit-3 boundary, while
window closure or application quit consumes late rejection without an alert. A renderer
failure leaves the independent backend available until explicitly shut down.

Desktop setup uses the original 8px outer frame, 1px border and 7px inner clip.
Navigation stays fixed while the body scrolls. The IGDB form gives its fields a
separate bounded scroller, leaving Save, Remove and feedback outside it.

Fullscreen setup uses the full canvas, saved safe margins and TV typography. Each
configuration step opens its provider or settings content and returns to the same
cursor and entry focus. Local controller hints describe Select, Back and keyboard
entry. Appearance also identifies left/right adjustment and Y reset. Presentation changes
and leaving credential editors clear unsaved secrets. Application, Library and Appearance
reuse the existing fullscreen controls. Desktop appearance exposes the ordinary palette,
layout, transparency and font choices. Profile writes report pending and failed states
from the existing serialized writer; only the latest queued revision settles those states.
The setup owner retains that write state across presentation changes and provider closure,
so leaving the editor cannot bypass a pending or failed appearance save.

## Source and fixture boundaries

All seventeen original methods pass unchanged as twenty-five cases: sixteen in the
main test project and nine in the UI project. The source files match the frozen
revision. Logs are `.tmp/task38123-source-main.log` and
`.tmp/task38123-source-ui.log`; TRX files are in `.tmp/task38123-dotnet-results/`.

The startup read fixture preserves twenty works, releases and Steam ownerships,
and the manual list `Try next` with release order `[20, 1]`. Its first library
snapshot still uses one repository lease. The HTTP application also reads three
preferences before that snapshot; those leases are counted separately rather than
hidden or misreported as part of the source's repository-only assertion.

Merge review, presentation settings and library settings exercise their actual
application endpoints. The lease ledger records backend process/thread identity
and synchronization context. Renderer publication is verified separately in Electron;
an isolated HTTP test alone does not establish renderer-thread behavior.

The original malformed-configuration tests place configuration beside the Avalonia
working directory. Electron starts the independent backend with its documented
installation-root configuration. Its process fixtures therefore use isolated backend
copies with the same invalid JSON and logging values, preserving fatal status,
diagnostic and unchanged-database assertions.

## Verification

Five authenticated HTTP cases pass, including the four combinations of saved
presentation and first-run state, all startup-model reads and unauthenticated ledger
refusal (`.tmp/task38123-api-final.log`). The fixture build has no warnings or errors.
The final focused renderer gate passes 202 cases across ten files, and the main-process
gate passes 54 cases across four files. A later CSS-only setup check passes 70 cases.

Twenty-four distinct native cases pass against the final production bundle. The full
sequence passes 22 cases; three subsequent cases replace its two locator failures and
add the original consent busy-state assertions on both surfaces. The corrected locators
use Chromium's combobox role for datalist-backed font inputs and scope the provider
heading to the setup header. They retain the original behavior assertions. Logs and
machine-readable reports use the `.tmp/task38123-native-final` and
`.tmp/task38123-native-final2` prefixes. The consolidated per-case ledger and extracted
native attachments are recorded by `.tmp/task38123-native-evidence.json`.

Actual Windows processes preserve the startup exit codes, useful diagnostics and exact
database bytes for malformed configuration, invalid logging and an unsupported schema
in both saved modes. Healthy startup verifies native responsiveness, distinct backend
and renderer process identities, twenty visible games, ordered list publication and
all later startup-model reads. The initial preference response can be held without
publishing a window in the wrong presentation.

An earlier refused-renderer run reported exit 3 but exceeded the thirty-second process
exit deadline. A minimal Electron reproduction, an instrumented application reproduction,
the unchanged individual case and the final full sequence subsequently exited correctly;
the latter took 4.2 seconds. No speculative shutdown workaround was added. The reports
retain the initial timeout. The backend stays independently available until fixture
cleanup explicitly shuts it down.

Desktop captures at 1200×604 show the bounded nine-step frame, fixed navigation and
IGDB actions, and ordinary appearance controls in the scrolling body. Pixel comparisons
verify every 7×7 inner corner in Winnow and Rosé Pine Dawn. Fullscreen captures cover
the actual 3440×1440 startup window, 1920×1080 and 1280×720 with 140% text. All seven
provider/settings entries return to the unchanged step. Theme/font pickers, reset
confirmation and the masked keyboard remain above setup and restore focus. Select,
Back, Adjust, Reset page and context-sensitive Keyboard hints remain visible.

The first native pass exposed the generic dialog's inherited width cap, padding and
outer scrolling. Setup now overrides those properties while retaining the inner rounded
clip and each surface's own padding. Final viewport, inner-inset and non-scrolling-action
assertions pass. Representative final desktop, fullscreen, picker, reset and keyboard
captures were visually inspected. Input is simulated; these checks do not establish
physical-controller or live-provider validation.

All 3,958 component/live API cases pass across 198 files in 356.13 seconds
(`.tmp/task38123-components-final2.log`). Subsequent changes are limited to the shared
fullscreen picker's local hints, field-label typography and scrolling layout. Its
46-case focused gate and later 15-case CSS check pass. The two fullscreen native cases
pass again with real Y/A/B keyboard entry, local hints, restored focus and measured
full visibility/hit-testing of the first font choice. At 720p/140%, three complete
choices remain readable before scrolling; focusing the font field scrolls the body
while Back and the hints stay fixed. Final captures were visually inspected.

The final production build passes (`.tmp/task38123-build-final4.log`), with renderer
`index-DJJXOwe4.js`. Final picker results use the `.tmp/task38123-native-final5` prefix;
the consolidated ledger selects their newer evidence for the two fullscreen cases,
retaining twenty-four distinct passing native cases overall. TypeScript, formatting
and diff checks pass.

The seventeen assigned methods resolve as sixteen ported and one framework-specific
C# lifecycle scanner. Its error-handling intent remains covered by actual Electron
Promise and process failure checks. The inventory contains 1,553 ported, 663 retained-backend,
34 framework-specific, 162 pending and 23 partial methods. The overall migration gate
remains incomplete. This milestone continues into TASK-381.24; review remains after
TASK-381.30.
