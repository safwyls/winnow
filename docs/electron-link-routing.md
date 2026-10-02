# Electron reading links

Settings chooses **In Winnow**, **Default browser**, or **Steam client, when available**.
The Steam choice appears only when the OS reports a registered Steam client. If that
client becomes unavailable, Settings displays the browser choice without rewriting the
saved preference. Only canonical HTTPS Steam app-store pages translate to native store
links. Other pages fall back to the browser with a visible message. A failed browser
handoff reports failure and remains retryable.

The reader accepts HTTP and HTTPS pages. Native viewing links are limited to Steam store
and library-details pages, GOG game views, and Epic's library view. Play, install and
uninstall continue through backend game commands; the renderer URL bridge does not accept
launcher execution URLs. Credentials in URLs and direct loopback destinations are refused.

The reader uses a separate in-memory Chromium session and one reusable window per Winnow
window. Back, Forward, current address, Open in browser and Close belong to a trusted
toolbar document. The website occupies a separate sandboxed WebContentsView without Node,
the application preload or IPC credentials. Page navigation, redirects, frames and popups
can reach only validated web destinations. Popups reuse the reader. Downloads and
permissions are denied. A page cannot navigate to the application origin or invoke
toolbar or launcher commands. The renderer receives a structured open result, allowing
fallback notices to survive the originating screen closing.

Fullscreen keeps larger reader controls and page zoom. D-pad and left stick scroll,
LB/RB move between links, A activates, X sends Space, and LT/RT move a page. Menu switches
between the toolbar and website; B closes the reader. Controller samples come from the
trusted parent document in an isolated world, never from website JavaScript. Focus loss,
navigation and reconnect require neutral input before actions resume. The reader owns
window actions and sends only bounded key events to the focused document.

The native Steam account browser remains separate and has its own masked controller
composer, described in [Steam sign-in](electron-steam-capture.md). Physical-controller
behavior and TV-distance readability still need device validation.

## Verification

`tests/parity-link-policy.test.ts` migrates the routing/fallback matrix, web boundary and
controller mapping contracts. `parity-link-browser.test.ts` checks window reuse, isolated
navigation gates, downloads, permissions, cleanup, concurrent opening and stale controller
samples with a mocked Electron host. `parity-link-notifications.test.tsx` and
`parity-link-settings.test.tsx` cover desktop and fullscreen feedback, retry and saved choices.

The original `steam://run/440` routing case uses the authenticated game-action API in
`tests/electron/link-action-routing.spec.ts`. Both surfaces dispatch that exact URI once
through the real backend with an intercepted OS dispatcher, preserving launch attribution
without opening a reader or reporting a browser fallback. The reading bridge continues
to reject executable launcher URLs.

`tests/electron/link-browser.spec.ts` bundles the production reader into an isolated native
harness. It creates a throwaway data directory and intercepts all HTTP/HTTPS traffic with
fixture responses. It checks HTTP-to-HTTPS navigation, Back, the visible address, OS browser
handoff, closing and rejection of application/toolbar URLs in both presentation modes. The
fullscreen check supplies mocked hardware state and verifies that real Chromium activates
a focused button. It does not contact Steam, load a live website or launch the user's browser.

The native harness validates DOM behavior in the website and toolbar separately. A Playwright
screenshot of the toolbar page excludes the separate WebContentsView and is not a composite
window screenshot. Dated run results belong in the migration evidence directory.
