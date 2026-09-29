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
The window title reports progress; closing the window stops further reads. In the dedicated
purchase-import route, agreeing to capture also authorizes import of the available pages,
including a partial capture. Selecting saved HTML files likewise reads and imports them in
one operation. Optional capture during ordinary account sign-in remains separate: its pages
are offered for explicit import or discard after sign-in succeeds.

The licence walker follows at most fifty further pages by default. Purchase history permits
at most one hundred load-more clicks, waiting up to fifteen seconds for a click to add rows.
The native sign-in request can override these counts; the reader clamps them to two hundred
further licence pages and five hundred history clicks. Malformed counts are rejected before
opening a browser. These limits, stalled pages and rejected pagination are reported as incomplete captures.
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
The optional sign-in review shows which page types were captured and any incompleteness, and
offers explicit import or discard. Dedicated capture and saved-file import show the result
directly; a failed import retains its pages for an explicit retry. Import uses the same backend service and
reports each page type's result and the facts recorded or already present. Both routes show
grouped counts in reading, matching and update order, followed by only the skip reasons that
occurred. Steam's reported licence total appears beside the rows read when they differ; that
difference alone is not evidence of missing pages.

Live capture records a separate stop reason for licences and history. A completed walk
outranks a static parser's truncation guess. A gap, stalled control, safety limit or interrupted
walk remains incomplete. Saved pages instead explain how to gather the remaining pages;
licence files with at least ninety rendered or parser-skipped rows and no advertised total
receive the same pagination hint as the original frontend. Parser failures keep the successful
page's counts visible and report the failed page's reason.

The purchase routes and connection changes share a busy state, so importing, saving a key,
signing in and signing out cannot overlap within the Steam settings card. A new import attempt
clears the previous attempt's report. Selected filenames and their outcomes remain visible
after import, including duplicate and different-account results. Closing the settings host
cancels its pending private browser. Signing out clears the session's status message and
purchase-capture permission while preserving an independent API key.

The backend owns the encrypted session store and credential selector. An unrenewable session
remains usable until its expiry, then stays visibly expired until reconnect or sign-out.
Successful sign-in and sign-out request an ownership refresh. Sign-out clears the session
from memory and disk, reconciles account confirmation and invalidates pending Steam sign-ins.
A host that cannot encrypt keeps the session for the current backend run and reports that
limitation. Native account mismatches and browser failures return distinct safe outcomes;
provider exception text never becomes their displayed explanation.

Native sign-in completion writes a bounded diagnostic log in the active backend data
directory, `logs/electron-steam.log`. Each line contains only the outcome, session expiry,
whether access and refresh tokens were captured, and captured page byte counts. Tokens,
account identifiers, provider messages and page contents are excluded before the log sink
receives a line. One previous file is retained when the current file reaches 512 KiB;
an unavailable or unwritable log never changes the sign-in result.

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
