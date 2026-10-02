# Native reader verification, 28 September 2026

The native Playwright reader harness passed on Windows with Electron 44.4.5 in desktop and
fullscreen (2 tests, 3.1 seconds). Each run used a new `winnow-electron-link-reader-*` directory
under the repository's ignored `.tmp` folder. All web responses were intercepted fixtures;
the system-browser adapter recorded the destination without opening an external program.

Both cases verified HTTP and HTTPS loading, history Back, the address toolbar, Open in
browser, Close, absence of `window.require` and `window.winnow` in the website, and blocked
application-origin and toolbar-command navigation. The fullscreen case verified native
fullscreen state and real Chromium button activation from mocked controller hardware.
It did not validate a physical controller or TV-distance layout.

The native run caught behavior the mocked host did not establish: toolbar hash commands
from a data document did not navigate, and an Enter keydown/up pair did not activate a
Chromium button without the corresponding character event. The production toolbar now
uses a trusted blank document with fixed hash commands; controller Enter/Space delivery
includes a character event. Regression tests exercise those actual outcomes.

Run from `src/Winnow.Electron`:

```powershell
node node_modules/playwright/cli.js test tests/electron/link-browser.spec.ts --output=../../.tmp/electron-link-reader-results --reporter=list
```

See [the reader contract](../../electron-link-routing.md) for the implementation boundaries
and [the method inventory](test-inventory.json) for contracts still pending or partial.
