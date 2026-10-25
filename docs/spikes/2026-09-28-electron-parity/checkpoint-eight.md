# Electron parity checkpoint eight

An empty Library list now removes its column headers as well as its rows, and the empty
search names the query. Choosing a desktop collection closes Library management and
returns to its games; fullscreen retains its explicit Close tools action. Stored cover
dimming now parses whitespace and case consistently in Settings, the App and merge
review. The existing App CSS already connected the shared preference to Avalon covers;
this package corrects parsing and adds direct rendered evidence for that connection.

The complete Electron suite passed 2,438 cases across 125 files without skips against an
isolated Debug backend. Eight native Library lifecycle cases passed together. They cover
actual keyboard/pointer activation of Details, retained selection after both close paths,
cover dimming with decoded test-owned images, expansion grouping, changed default sort,
list cuts, density and column geometry. Build and TypeScript checks pass. The native
fixtures use temporary data directories and disable syncing.

Dimming checks inspect Chromium's computed filter and decoded 600 × 900 image, keep the
same cover elements and image source across toggles, and verify that reduced motion
remains active. A Settings save is read back from the real backend and survives a fresh
renderer. The initial persistence test used Playwright's immediate checkbox assertion;
the corrected test clicks the control and waits for the authoritative asynchronous save.
Screenshots of both surfaces with dimming off were inspected.

The source audit covers the six sort orders, composed bucket/search filters, All games
selection and counts, exclusive view switching, primary selection, Home/Library navigation,
Settings section retention and shared game-object identity between shelves and Library.
The frozen inventory remains 2,435 methods across 299 files: 836 ported, 540 retained
backend, 13 framework-specific, 881 pending and 165 partial. The completion gate still
fails. The combined original 37-hour Steam Details fixture remains partial even though
its opening and retained-selection behavior has native coverage. The no-settings-store
constructor and remaining grouping/lifecycle contracts still need direct evidence.

No backend production code changed. The fifth checkpoint records the latest .NET and
migration-hash checks. The original frontend and release entry points remain in place.
