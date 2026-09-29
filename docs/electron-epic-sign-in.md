# Epic sign-in in Electron

Settings and setup offer an embedded Epic sign-in window on desktop and fullscreen.
Winnow first obtains a challenge from the backend and displays that challenge's consent
notice. Neither the embedded window nor the system browser opens until the user agrees.
The application offers Cancel during preparation and while the browser is open.

Main owns the backend client ID, complete challenge and captured code. Five named preload
methods prepare, open, complete and cancel the flow; they do not accept arbitrary URLs or
provider origins. The renderer receives consent, expiry and a safe account result. A manual
fallback opens the challenge's start URL in the system browser, independently of reading-link
preferences. Its masked input accepts the complete final address and sends it to main for
validation. A raw authorization code is accepted only for the backend's state-free endpoint
flow. Codes are never stored in application preferences or diagnostics.

The embedded browser supports the original four capture routes: the launcher's exchange-code
callback, exact redirect interception, a JSON page body and same-origin session harvesting.
The backend still exchanges either code type and saves the resulting credentials. A result
that could not be persisted explicitly says that it lasts for this run only. Cancelled,
unavailable, missing-session, rejected-code, rejected-client and network outcomes have
different messages. Cancellation does not automatically open another prompt.

## Provider boundary

The provider uses a sandboxed document with context isolation and no application preload.
A separate capture preload defines only `window.ue.signinprompt` on a trusted top-level
HTTPS origin before the page's own scripts run. Main independently checks the sender,
current main frame, trusted origin, attempt and a fresh document token. A queued callback
from an old document cannot complete a newer page or attempt. Social-login origins may
render, but receive no capture bridge and are never read for codes. Frames cannot use the
capture channel; HTTPS challenge frames may render without gaining application access.

Trust binds scheme, host and port. Redirect capture additionally requires the registered
path and matching OAuth state. The registered loopback address is intercepted before a
network request; other loopback and application destinations remain blocked. Missing state
discards the redirect's code and may request a new code from the provider session. Mismatched
state ends embedded capture so another concurrently pending route cannot complete it.

Main reads JSON only on trusted documents. Harvesting runs on the harvest endpoint's own
origin, at five-second intervals, with at most 150 attempts per document. It follows at most
two deliberate harvest navigations and two returns to the login form. Null code fields mean
no authenticated session; unrelated JSON is not guessed to be a credential. Body reads are
limited to 1 MiB and codes to 4,096 characters. Navigation, cancellation and replacement
invalidate pending reads. The backend challenge expires after its configured timeout, capped
at fifteen minutes by the frontend validator.

Approved social popups fold into the same guarded window. Other validated web popups open
in the system browser; launcher commands, downloads, permission requests and webviews are
refused. Social providers that require an opener relationship may still require the manual
browser route, as with the original embedded host.

Epic has its own persistent Chromium profile under Electron's selected user-data directory
in `account-profiles/epic`. An explicit `--data-dir` keeps that profile inside the throwaway
directory. The profile preserves a partially completed website login between attempts.
Signing out of Winnow removes the backend credential; it does not erase Epic's website cookies.

Fullscreen reuses the account window's trusted controller hint bar and blank masked composer.
The provider view and composer are separate documents. The composer inserts newly entered
text without reading existing provider fields, and the account input lock prevents further
typing after a captured code is submitted. Cancellation after submission cannot undo a code
that the backend has already spent; the flow reports its actual completion result.

## Verification

The Epic policy, executed preload, mocked native lifecycle and renderer tests cover these
boundaries and the shared settings transitions. The intercepted native test runs all four
capture routes and a social-provider return in desktop and fullscreen. It verifies the bridge
exists before the page's first script, is absent from frames and social pages, and does not
expose the application bridge or Node. The redirect never reaches the loopback fixture handler.
See [dated evidence](spikes/2026-09-28-electron-parity/epic-sign-in.md).

These fixture runs do not establish compatibility with Epic's current live login page,
multi-factor authentication or every social provider. Physical controller and OS-keyboard
validation remain separate from the automated hardware adapter checks.
