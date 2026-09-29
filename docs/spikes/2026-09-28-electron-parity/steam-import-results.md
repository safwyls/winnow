# Steam import and connection verification, 29 September 2026

The affected Electron suites passed 279 tests across eight files. The production build and
TypeScript check passed. The native saved-page suite passed both desktop and fullscreen
cases in 11.8 seconds using the scratch Debug backend and an isolated Winnow data directory.
No real Steam account, launcher files or authentication session was used.

The native test selects the sanitized first licence page, final licence page and purchase
history fixtures through the real file input. The named preload route sends them to the
backend loader, which immediately hands recognized pages to the importer. The retained parser
unions three repeated licence facts, producing 13 licences alongside Steam's advertised
979. Both modes show the detailed report, per-source incomplete-page guidance and applicable
skip reasons. Counts use Avalon's IBM Plex Mono font alias with tabular numbers, and the report
stays inside its horizontal bounds at 1280 × 720.

Controlled DOM tests execute the native capture script against the same sanitized fixtures.
They check account consistency, stripped session markup, contiguous pagination, gaps, click
and page limits, stalled loading, hidden controls, cancellation and unsigned-in timeout.
Capture now returns page-specific stop reasons and counters; the report uses a completed
live walk in preference to a static parser's incomplete-page guess. A gap or safety limit
never claims completion.

Component tests run each report and route contract in desktop and fullscreen. They cover
ordered grouped counts, differing advertised totals, parser failures, nonzero skip reasons,
empty updates, source-specific pagination advice, shared busy state, explicit consent,
automatic completion, filenames and duplicate/account-mismatch results. A failed import
retains its pages for an explicit retry. Starting another route clears the
previous report. Integrated Settings tests cover saving/removing a key, connection refresh,
run-only sessions, missing refresh tokens, sign-out identity and permission cleanup, failed
sign-out, cancellation on unmount and readable API-key links after a refused launch.

The original 39 import-view-model methods have equivalent evidence in
`tests/migration-steam-import.json`. Complete and partial dedicated captures import after the
initial consent, matching the original command. Optional capture during ordinary sign-in
remains reviewable; the original Stores view model did not import those pages itself.
This classification does not claim that every original Steam test is migrated.

The policy audit found that `SteamSignInServiceTests` resolves a global alias to
`LegacySteamSignInService`. Production `StoreConnectionApplication` implements its own
challenge completion and sign-out workflow. Those legacy coordinator assertions cannot be
classified as retained production behavior solely because their source moved into the
Application project. Similarly, the original Core browser policy is used by Avalonia, while
Electron needs native policy tests. The unchanged `SteamSignInRequest` defaults are retained:
production BeginSteam and CompleteSteam use that exact record.

The replacement `SteamSessionParityTests` passed eight real HTTP cases against the production
backend coordinator, settings store, provider and credential selector. They cover encrypted
persistence across restart, token-only expiry and reconnect, refused and cancelled attempts,
sign-out removing memory/disk/identity and pending attempts, background refresh requests, and
run-only sessions when encryption is unavailable. Only platform encryption is replaced by a
test AES-GCM protector; developer key sources are excluded and no provider request is made.
The original 33 claims/session/provider/registration methods passed all 43 cases and exercise
unchanged implementations used by production `AddSteamWebApi`.

Native main-process tests additionally verify identity-mismatch and browser-failure outcomes,
safe explanations, cleanup, and exact nondefault capture-bound forwarding. The optional bound
forwarding was checked after the native import run; its evidence uses mocked browser and
backend transport boundaries, not a live authenticated Steam account.

Live Steam Guard and current authenticated website compatibility remain manual validation.
The native capture tests use controlled documents; they do not establish physical controller
or OS keyboard behavior. The separate account-browser evidence records those test boundaries.
