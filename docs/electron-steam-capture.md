# Steam sign-in and account-page capture in Electron

Steam sign-in uses a private Electron browser session. The sandboxed application renderer
receives the sign-in outcome, never the access or refresh token. Main passes credentials to
the existing backend connection API and clears the browser's storage and cache when the
window closes. Main denies downloads, popups, webviews and permission requests in this window.

Only exact Steam HTTPS origins can navigate the private window's main document. Embedded
HTTPS challenge frames can use other web origins, with no application bridge or token probe.
Application origins, launcher protocols and direct loopback destinations cannot load as frames.
Login and account-recovery
pages can display but cannot be probed for tokens. A token probe checks the current document
again before reading Steam's account configuration. Closing the window or navigating while
a probe or cookie read is pending prevents that result from being saved. A session expires
after fifteen minutes even when its document probe has not returned.
The application also offers Cancel while sign-in or capture is running. Cancelling a pending
backend start returns immediately; any challenge that arrives afterward is released without
opening a window or saving credentials.

Fullscreen separates the provider document from a trusted hint bar. Y opens a blank, masked
local composer with letter, case and symbol controls. Done inserts only that newly composed
text through Chromium's native insertion API; Winnow does not inspect the provider field's
existing value or type. B, Escape, navigation, closure and capture lock discard the local
draft. The composer never exposes a preload bridge. Controller samples come from the
trusted application document, and focus loss requires neutral input before resuming.
Capture locks provider mouse and keyboard events after recognizing an account table;
B or Escape can still cancel.

Purchase and licence capture is a separate consent, off by default when opening sign-in.
Connections also offers a separate capture window that does not save sign-in credentials.
Both routes read only `/account/licenses/` and `/account/history/` on Steam's store origin.
The window title reports progress; closing the window stops further reads. Pages already
captured can be reviewed or discarded. Capture never starts an import automatically.

The licence walker follows at most fifty further pages by default. Purchase history permits
at most one hundred load-more clicks, waiting up to fifteen seconds for a click to add rows.
These limits, stalled pages and rejected pagination are reported as incomplete captures.
Page content is limited to 64 MiB per document and 128 MiB across the capture. The import API
still applies its own payload and parsing limits. Large accounts can import additional saved
pages separately.

Account identity is checked before and after each page read and across the whole capture.
If a signed-in session requested capture, each page must expose that same identity. A separate
capture can remain unidentified when every page lacks an identity; its facts remain under an
unknown account. A changed or lost identity discards the capture. Current credentials never
provide an account label for unidentified page contents.

Only the account tables and pagination evidence cross into the application renderer. The
capture removes scripts, account configuration, forms, session fields and irrelevant link
parameters. Licence package IDs and purchase app IDs remain available to the existing parser.
The review screen shows which page types were captured and any incompleteness, and offers
explicit import or discard. Import uses the same backend service as saved HTML files and
reports each page type's result and the facts recorded or already present.

Tests execute the capture scripts against the sanitized account-page fixtures in
`tests/fixtures/steam-account-pages/`, alongside mocked browser navigation, cancellation,
identity changes and pending reads. These checks do not establish compatibility with the
current authenticated Steam website; live sign-in and Steam Guard checks remain manual
validation with a throwaway Winnow data directory.

`tests/electron/account-browser.spec.ts` runs the production fullscreen account host against
intercepted fixture pages. It verifies masked insertion, case and symbols, cancellation,
navigation, capture lock and zero provider-script execution by the input host. Native
controller tests supply mocked hardware state; physical controller and OS keyboard checks
remain separate. Playwright's CDP keyboard injection bypasses Electron's native keyboard
event hook, so the lock hook is verified by an event-level test rather than that injection.
