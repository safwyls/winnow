# Account input and connection status verification, 28 September 2026

The production account browser passed its isolated Windows/Electron native harness
(`tests/electron/account-browser.spec.ts`, 1 test, 4.6 seconds). A new throwaway data
directory and intercepted web responses kept the run separate from real accounts.

The fullscreen test entered a fresh masked draft with letter, Shift and symbol keys,
inserted it into the previously focused provider field, and verified discard after B,
navigation and capture lock. Native pointer activation and mocked controller activation
were blocked during capture; B still closed the window. The input host made zero calls
to execute JavaScript in the provider document. The initial native run caught reversed
character order when Chromium changed the blurred password field's selection; the local
composer now owns its caret and synchronizes it with physical edits to its own field.

Playwright's CDP keyboard injection does not raise Electron's `before-input-event`. It
cannot establish the physical OS keyboard lock. An event-level regression verifies that
handler, including the Escape exception; physical keyboard/controller validation remains
separate from this fixture run.

The focused unit/component checks cover both presentation modes: six distinct Steam health
states, four credential combinations, key ownership, renewable/expired/unpersisted session
messages, identity explanations, report-supplied account confirmation, failed-key retention,
cancelled/refused outcomes and an expired Epic connection's reconnect action. The composed
sign-in/visibility test found and fixed an initial-read race: a pending pre-sign-in snapshot
could otherwise leave the account filter disabled after successful confirmation.

Cancellation is tested while backend begin is pending, while a token probe is pending, and
after a new independent attempt has started. A late challenge is released using its own
client and attempt identifiers. Locked key and text requests finish without waiting for
browser readiness; locks, navigation and disposal also invalidate already-waiting requests.

The original account suite is not yet fully migrated. Detailed import reporting, embedded
Epic capture, the wider sign-in fallback matrix, and several store-panel contracts remain
pending or partial in the method inventory. Live Steam authentication, Steam Guard and
physical hardware are not covered by these tests.
