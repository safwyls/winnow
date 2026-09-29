# Embedded Epic sign-in verification

Measured on Windows on 2026-09-29, after checkpoint `a8c7ad0`.

The focused suite passed 210 cases across eight files:

```text
tests/parity-epic-policy.test.ts
tests/parity-epic-native.test.ts
tests/parity-epic-preload.test.ts
tests/parity-epic-settings.test.tsx
tests/parity-native-auth-integration.test.ts
tests/parity-account-input.test.ts
tests/parity-steam-connections.test.tsx
tests/parity-setup.test.tsx
```

The first four exercise the new Epic implementation. The others regress the account host,
Steam input/cancellation and shared setup behavior. Renderer cases run in both presentation
modes. Original sanitized `tests/fixtures/epic-oauth/redirect-*.json` bodies are used directly
for the no-session and authorization-code reader tests.

The real Electron harness passed two tests in 35.1 seconds:

```text
node node_modules/playwright/cli.js test tests/electron/epic-auth.spec.ts --output=../../.tmp/electron-epic-auth-results --reporter=list
```

Each test creates a throwaway data directory and intercepts HTTP and HTTPS in its provider
session. Desktop and fullscreen each exercise launcher bridge capture, exact redirect capture,
JSON body capture, a navigable-but-untrusted social provider returning to a JSON body, and
same-origin session harvesting. Every completion uses the expected backend grant and state.
No fixture request reaches the loopback redirect, and no live account credentials or website
are involved.

The native run caught two issues before passing. Sandboxed Electron lacks
`process.isMainFrame`; the provider preload now checks `window.top === window.self`, backed
by a separate main-process sender-frame check. Assigning a synchronous IPC reply twice sends
the first value too early; bootstrap now replies exactly once. Executed-preload and mocked
native tests retain regressions for both cases.

Settings integration additionally caught a result-lifetime issue: replacing the account editor
when the refreshed connection became live hid the run-only warning. The editor now remains
mounted while the sign-in action is withdrawn. Tests confirm that warning survives the query
refresh and is cleared when the account signs out, that failed renewal retains the expired
account identity, and that refresh does not replace a pending attempt.

This evidence covers fixture behavior rather than Epic's current authentication service.
Live MFA/social-login and physical input validation remain outstanding. The full migration
inventory remains incomplete; this package does not establish complete Avalonia parity.
