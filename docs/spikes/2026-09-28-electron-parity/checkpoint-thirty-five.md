# Electron parity checkpoint 35 — IGDB settings and cold startup

Settings and setup share the IGDB form on desktop and fullscreen. It distinguishes a
readable saved pair, an unreadable protected secret, external configuration and missing
credentials. Neither API snapshots nor fields expose saved secrets. Explicit Save trims
the client ID, clears the accepted secret and reports queued metadata refresh. Protection
refusal and failed writes retain the masked draft. Removal clears the saved pair, legacy
plaintext and cached tokens, takes effect immediately and explains configuration fallback.
Read failures and refused setup links provide actionable, sanitized feedback.

Fullscreen fields retain the source's 24px input text, 72px input height, 32px value labels
and 22px help/status text, multiplied by the saved text scales. Controller A opens the
masked keyboard and activates Save. Successful saves restore action focus unless the
user has acted elsewhere; feedback scrolls into view only while focus remains in the form.
Departure and presentation changes discard unsaved secrets.

The preceding full native run also exposed a cold-start mismatch. Its browse-spine fixture
created the data directory at 07:14:18 UTC; the backend logged successful startup at
07:14:31 UTC. The transport had already rejected initial snapshot requests after 12 seconds,
despite displaying a 45-second startup policy. Two controlled tests reproduced that early
failure before the fix. Initial requests now wait up to 45 seconds for the first event
handshake; reconnect requests retain their 12-second bound. Canceled queued mutations are
never sent after attachment. Only startup-specific native waits were aligned with that policy.

## Verification

- All 3,051 component/live API cases pass in 149 files without skips, in 52.56s:
  `.tmp/igdb-full-integration.log`. The focused startup/settings group passes 129 cases.
- Build and type checking pass: `.tmp/igdb-build-feedback.log`.
- All 133 backend HTTP cases pass in 21 seconds: `.tmp/igdb-backend-full.log`.
  The 12 added credential cases also pass separately. They use production HTTP and SQLite,
  a controlled opaque test protector and mid-transaction storage faults to verify protection,
  legacy migration, cached-token invalidation, sanitized responses and rollback of every row.
  Native save tests separately exercise the actual Windows protector.
- All eight activation and five browse-spine cases pass in the focused native run.
  Both Epic sign-in workflows also pass unchanged. The earlier fullscreen Epic navigation
  failure has not reproduced; its exact cause remains unconfirmed. Report:
  `.tmp/igdb-slow-start-native/results.json`. That first run also caught three new IGDB
  fixture assumptions and an early synthetic fullscreen event; it is not an all-pass report.
- The corrected five-case native run passes in 1.1 minutes:
  `.tmp/igdb-slow-start-native-final/results.json`. Real desktop and fullscreen each hold
  the initial handshake for 13 seconds, remain in preparation and reveal the library after
  release. IGDB uses isolated databases with background enrichment disabled. Its browser
  destination and refused launcher are controlled so the setup-link check stays local.
- All three final IGDB native workflows pass in 29.4s:
  `.tmp/igdb-feedback-native/results.json`. These include desktop and fullscreen at 100/140%
  text, actual protected saves, refusal/retry, masked controller entry, removal, departure,
  save focus and feedback containment. Desktop and enlarged fullscreen captures were inspected.
  Inspection first found desktop feedback below the scroll viewport and undersized fullscreen
  help; final captures and assertions verify both corrections.
- The credential-refresh component fixture now waits for the notified render, rather than
  assuming a completed React Query invalidation has already painted the new identity.
  Native fixtures now use the actual desktop Settings location, explicitly select their
  controlled browser destination, and enter real fullscreen for the early-startup test.

Eleven IGDB settings methods and two previously partial interaction methods now have named
replacement evidence. Inventory: 1,107 ported, 559 retained backend, 17 framework-specific,
646 pending and 106 partial, out of 2,435 frozen source methods. The complete 236-case native
run is still outstanding. Overall migration and fullscreen Settings structure/controller
coverage remain incomplete; no overall acceptance criterion is marked complete.
