---
id: TASK-56
title: Probe Steam web session sign-in and token minting in WebView2
status: Done
assignee:
  - '@claude'
created_date: '2026-08-30 03:19'
updated_date: '2026-08-30 14:50'
labels:
  - auth
  - ingest
dependencies:
  - TASK-55
priority: high
ordinal: 56000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Gating verification for TASK-55, per the user's decision to test before building. Two questions only a live session can answer: whether Steam's login flow (Steam Guard, possible hCaptcha) completes inside Winnow's existing off-the-record WebView2 profile, and whether a token minted from a store page against the user's real account returns populated data from ClientGetLastPlayedTimes, GetOwnedGames and GetUserYearInReview. The probe is throwaway scaffolding, not the shipped design: it reuses the harvest session's browser, mints the token at the end of a successful sign-in, makes the three read-only calls, and reports shapes and counts. It must never log or persist the token, the refresh token, or any cookie.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A user-run sign-in inside the off-the-record WebView2 completes or fails with a recorded reason (Steam Guard prompt, captcha, embedded-context refusal)
- [x] #2 A store-minted access token is obtained and its expiry read from the JWT, with neither token nor refresh token written to disk or any log
- [x] #3 The three endpoints are called with access_token and their populated-or-empty status recorded, alongside the account id the token resolves to
- [x] #4 Findings are recorded in docs/spikes/steam-web-session-auth.md, resolving spike items 1 and 5
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Read the spike, the harvest session (WebView2SteamPageHarvester, WebView2Host, EphemeralBrowserProfile, SteamAccountPagePolicy) and the two Steam Web clients, so the probe reuses the existing off-the-record context and the existing response parsers rather than reimplementing them.
2. Add src/Winnow.Auth.WebView/SteamSignInProbeSession.cs: a clearly-marked throwaway probe session that opens the SAME WebView2Host(inPrivate: true) over an EphemeralBrowserProfile, gates navigation through SteamAccountPagePolicy.ClassifyNavigation, starts at store.steampowered.com/login/, never scripts a login-journey path, and on landing on a store page mints the token the way Playnite does (application_config -> data-store_user_config.webapi_token, data-userinfo.steamid, window.g_wapit as fallback). Teardown is the harvester's existing finally-block order, unchanged.
3. Add src/Winnow.App/Services/SteamSignInProbe.cs: the pure parts (JWT payload read of exp/sub/aud with NO signature validation, a Redact() for error paths, the three access_token endpoint URI builders) plus the --steam-signin-probe console entry point, which drives the session, calls the three endpoints with access_token=, parses them with the SHIPPED parsers (SteamHistoryJson, SteamWebJson) and prints only derived facts.
4. Wire the switch into Program.cs before DatabaseInitializer.Initialize() so the probe provably writes nothing to the database, and never into any UI.
5. Add tests/Winnow.Tests/SteamWeb/SteamSignInProbeTests.cs over the pure parts: JWT claim reading (exp/sub/aud, malformed, unpadded base64url), redaction (JWT-shaped, access_token=, hex key, nothing left of the token), URI building (paths, parameter names, no key=, SteamWebRedaction.Describe hides the token).
6. dotnet build then full dotnet test; docs prose via docs-writer; no commits, no finalization.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Probe built (not run — the live run is the user's). Command: dotnet run --project src/Winnow.App -- --steam-signin-probe

Files: src/Winnow.Auth.WebView/SteamSignInProbeSession.cs (interactive half: reuses WebView2Host inPrivate + EphemeralBrowserProfile and the harvester's teardown order verbatim; navigation gated by the existing SteamAccountPagePolicy, unchanged; never scripts a sign-in-journey path; mints via application_config/data-store_user_config.webapi_token with window.g_wapit as second route), src/Winnow.App/Services/SteamSignInProbe.cs (pure parts + console report + the three access_token calls), tests/Winnow.Tests/SteamWeb/SteamSignInProbeTests.cs (28 tests), and one guard in Program.cs placed BEFORE DatabaseInitializer.Initialize() and before host.Start() so the probe provably writes no row and starts no hosted service.

Constraints held: the token, refresh token and cookies are never logged, printed or persisted; request URIs are logged through the shipped SteamWebRedaction (allowlist, so access_token is hidden by construction); error paths run through a three-pass redactor; the ephemeral-profile teardown is untouched; nothing is wired to any UI or to the normal startup path.

Responses are parsed with the SHIPPED parsers (SteamHistoryJson, SteamWebJson), so a populated line is also evidence that token-auth response shapes match key-auth ones (spike section 3).

docs/spikes/steam-web-session-auth.md gained section 7.1 recording the instrument, the command, which output block settles which spike item, what it does NOT settle (items 3, 4, 6, 7; refresh-token durability; unattended renewal), and a BLANK findings table to fill in after the run. No findings were invented.

Build clean under TreatWarningsAsErrors (0 warnings). Full suite green: Winnow.Tests 2121, Winnow.Recommend.Tests 75, Winnow.Covers.Tests 70, 0 failed. No commits. Acceptance criteria deliberately left unchecked: every one of them needs the live run.

FIRST RUN 2026-08-29 — stalled on an instrument bug. Diagnosed and fixed; awaiting a second run.

REAL FINDING (the more important half of spike item 5): sign-in WORKS inside the off-the-record WebView2 profile. Steam's login page rendered, password entry worked, Steam Guard completed, and Steam's post-login redirect landed on the store home page. First time the repository has a record of that flow exercised end to end against a live account, which spike section 5 explicitly said it lacked. Still open on item 5: the QR route was not exercised and no hCaptcha was presented, so 'hCaptcha passes in this profile' is untested rather than confirmed. Item 1 learned nothing: no token was minted.

ROOT CAUSE (not the coordinator's leading hypothesis). The probe never called SteamAccountPagePolicy.AllowsHarvest; it gated on IsTrustedOrigin, which the store root passes. The actual fault was SteamSignInProbeSession.IsSignInJourney, copied verbatim from WebView2SteamPageHarvester, whose first clause is path.Length == 0 — it counts the store ROOT as part of signing in. That clause is correct in the harvester (the root is only a waypoint in Steam's post-login redirect, and the harvester wants neither to read it nor bounce off it) and exactly wrong here: the root is where Steam lands the user after Steam Guard AND carries application_config. So the poll refused the only page it was ever shown, SawSignedIn was never set, and the recovery navigation to /explore/ was itself inside the read-succeeded branch — downstream of the same gate it existed to escape, so it could only fire on a page that had already answered. Two faults compounding.

FIX. New public static SteamSignInProbeSession.IsMintScope(Uri?) — the probe's own scope predicate, distinct from the harvest predicate: HTTPS + store.steampowered.com + not a credential-entry page, deliberately admitting the root, and a strict subset of the shipped policy's trusted origin. IsSignInJourney became IsSignInForm with the empty-path clause dropped (the named login/join/password/twofactor/mobilelogin/account-security paths were always the clause keeping the login form unread). The shipped SteamAccountPagePolicy and WebView2SteamPageHarvester are byte-for-byte unchanged, and a test now pins that AllowsHarvest still refuses the store root.

Also: MintPage (one) became MintPages (ordered walk) — /explore/ (Playnite, section 4), /replay/ (section 1 route 3, verified anonymously 2026-08-29 to carry data-store_user_config.webapi_token), /points/shop/ (section 1 route 1). Which page reliably carries a populated token is itself an open question the walk answers. Each page gets 4 polls after sign-in, then the walk advances; exhausting the list concludes SignedInWithoutToken naming every page tried, in roughly fifteen seconds instead of ten minutes. The steer decision moved out of the read-succeeded branch so a blocked or failed read still advances it. A heartbeat line per poll now goes to the console via a new optional progress callback: elapsed time, origin+path (never the query — a post-login URL carries redir and Steam's own parameters), and what the probe did that cycle.

Tests 28 -> 46 (18 new, all on the mint scope). Regression test: The_store_home_page_is_in_mint_scope. Invariant tests: the probe never reads outside what the shipped policy already trusts, and the two predicates are allowed to disagree about the store root so nobody collapses them back into one.

docs/spikes/steam-web-session-auth.md section 7.1 updated: status, the first-run findings, the heartbeat and bounded walk, and a findings table with the sign-in rows filled in and dated and everything else still blank.

Build clean under TreatWarningsAsErrors (0 warnings). Full suite green: Winnow.Tests 2139, Winnow.Recommend.Tests 75, Winnow.Covers.Tests 70, 0 failed. No commits.

SECOND RUN 2026-08-29 — printed nothing at all and the terminal had to be force-closed. Two confirmed instrument defects, both fixed. Awaiting a third run.

REPORT FILE (the important line for the next run): %LOCALAPPDATA%\Winnow\steam-signin-probe.txt — written on every path, flushed line by line, overwritten each run. The console is now only a second copy. Tell the user to read that file.

ROOT CAUSE 1 — there was no console to write to. Winnow.App is WinExe (src/Winnow.App/Winnow.App.csproj:4), so the Windows GUI subsystem gives the process no attached console and every Console.WriteLine went nowhere. Already recorded in the repo as code review finding F41 (docs/code-review-2026-08-28.md:652), which demanded a rolling file log for exactly this reason. Output ordering/buffering was NOT the explanation: there was no console to buffer to. Compounding it, ConsoleAuthPrompt.AttachConsoleIfNeeded (src/Winnow.App/Services/ConsoleAuthPrompt.cs:165) opens with a guard on Console.IsOutputRedirected, and a WinExe with no console has a null stdout handle for which GetFileType answers FILE_TYPE_UNKNOWN, which .NET reports as redirected — so the guard written to protect a piped run also skipped the attach in the one case the method existed for. LATENT PROBLEM FOR SHIPPED CODE: --epic-login and --epic-signin use that same helper and are likely equally silent. NOT fixed here; the probe was given its own opener rather than changing a method three shipped flows depend on. Worth its own task.

ROOT CAUSE 2 — the process could not terminate. Run called ReportAsync(...).GetAwaiter().GetResult() on the main thread AFTER Dispatcher.UIThread.MainLoop(loop.Token) had returned. Avalonia installs its own SynchronizationContext on the UI thread during SetupWithoutStarting() and never uninstalls it, so the await on the first HTTP call captured a context whose dispatcher was no longer pumping while the main thread sat blocked in GetResult(). Deadlock — which is why the terminal appeared frozen: dotnet run was waiting on a child that could never exit.

FIX. New internal SteamProbeLog writes the report to file AND console (StreamWriter, AutoFlush = true, so a run killed halfway still leaves everything on disk; derived facts only, no token/refresh/cookie/query string). Deterministic path via Program.DataLocation.Root + SteamSignInProbeConsole.ReportFileName, printed in the banner and again at the end. TryOpenConsole is the probe's own opener: leaves an already-valid stdout handle alone so redirection still works, otherwise AttachConsole(ATTACH_PARENT_PROCESS) with an AllocConsole fallback, then rebinds Console.Out to CONOUT$ by hand because AttachConsole leaves .NET's cached writer on the old handle. The whole report now runs INSIDE the dispatcher-posted lambda while the loop is still pumping, the loop is cancelled only in that lambda's finally, there is no blocking wait on the main thread at all, and the HTTP awaits use ConfigureAwait(false).

GUARANTEED TERMINATION, three layers: a ReportBudget (2 min) CTS around the report only; a HardBudget watchdog (sign-in + report + 30s) that cancels the loop; and ArmHardExit, a background thread that calls Environment.Exit(3) if the process is still alive past that. Stop() cancels and then posts an empty job to wake a pump that might be blocked waiting for a message. Exit codes: 0 all three endpoints populated, 1 a conclusion short of that, 2 stopped or failed, 3 the hard exit.

WHY NOT A SEPARATE CONSOLE PROJECT (considered, rejected with reasons): the Avalonia + STA + app.manifest host inside Winnow.App is proven working — the browser opened and sign-in completed on both live runs, so the host was never the broken part. Making the file the guaranteed channel removes the only real benefit a console-subsystem project would have brought, and a new project would have had to re-derive the manifest and Avalonia bootstrap that NativeControlHost requires, untested, to fix an output problem the file already fixes.

FINDING FROM RUN 2. Sign-in completed again inside the off-the-record WebView2 profile and the window closed immediately afterwards, which is the mint path reaching its end. Consistent with a token having been minted, but nothing was recorded, so it is NOT evidence that a token was obtained — item 1 is NOT upgraded. Item 5 stays partially VERIFIED for the password-plus-Steam-Guard path, now observed twice; QR still untested, hCaptcha still not presented.

Tests 46 -> 52 (new SteamProbeLogTests: deterministic path, every line on disk before close, directory created, unopenable file survivable, double dispose harmless, invariant composite formatting). Shipped SteamAccountPagePolicy, WebView2SteamPageHarvester, WebView2Host, EphemeralBrowserProfile and ConsoleAuthPrompt are all byte-for-byte unchanged (verified by git diff --stat).

Build clean under TreatWarningsAsErrors (0 warnings). Full suite green: Winnow.Tests 2145, Winnow.Recommend.Tests 75, Winnow.Covers.Tests 70, 0 failed. No commits.

Third run succeeded 2026-08-30 after two instrument bugs (the store root excluded from mint scope by a rule copied from the harvester; then no console at all because Winnow.App is a WinExe, compounded by a deadlock after the dispatcher stopped). Results: sign-in completed in the off-the-record WebView2, token minted from the store root via application_config/data-store_user_config, expiring in 24h 22m with audience web:store, resolving to steam3 account id 49024752 with the page steamid and the token sub in agreement. All three endpoints returned 200 with x-eresult 1 and populated bodies under access_token with no API key present: 613 apps and 592 first-played dates from ClientGetLastPlayedTimes, 841 named games from GetOwnedGames, and 43 games with 52 monthly points across 11 months from GetUserYearInReview for 2025. The 592 first-played dates match exactly the 592 steam_first_played rows the M5 backfill wrote under key auth, so the two auth methods agree on this account's data. Instrument caveat: the probe reported no password field seen despite a real sign-in, so its sign-in-form detection is heuristic and says nothing about which login route Steam used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
A user-run live probe settled the two questions gating TASK-55's design. Steam sign-in completes inside Winnow's existing off-the-record WebView2 profile, and a token minted from the store root returns populated data from all three endpoints Winnow uses (ClientGetLastPlayedTimes, GetOwnedGames, GetUserYearInReview) with no API key in any request, while resolving the signed-in account exactly. Verified by the probe's own report at %LOCALAPPDATA%\Winnow\steam-signin-probe.txt, cross-checked against the live database where the 592 first-played dates match the M5 backfill's 592 steam_first_played rows. Recorded in docs/spikes/steam-web-session-auth.md section 7.1, resolving spike item 1 and partially item 5; refresh-token survival, unattended renewal, and the key-versus-token disclosure A/B remain untested.
<!-- SECTION:FINAL_SUMMARY:END -->
