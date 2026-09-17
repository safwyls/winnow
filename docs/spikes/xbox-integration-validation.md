# Xbox plugin validation

Measured on Windows on 2026-09-16 (Pacific time) for TASK-318. The implementation is an
optional SDK package; these checks do not establish live Microsoft account access or full
purchase inventory coverage. Product behavior and current limitations belong in the
[plugin guide](../../plugins/Winnow.Plugin.Xbox/README.md).

## Local and public-service smoke checks

The read-only local scanner enumerated 33 current-user Store registrations and identified one
Xbox game, Solitaire & Casual Games. The scan reported complete, with a valid registered
application identity and Xbox TitleId. Manifests and Xbox configuration were copied before
parsing; no application was launched and no launcher files were changed.

Native display-name resolution also returned Notepad and Calculator. Notepad's copied
manifest contained `ms-resource:Resources/AppDisplayName_Notepad`, establishing that the
smoke exercised Windows resource resolution. All three names were non-provisional. The
ordinary applications remained excluded from the game list.

A public Microsoft display-catalog lookup for
`Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe` returned exact package-family identity,
Store product `9WZDNCRFHWD2`, game kind, Xbox TitleId `85494077`, category, release date and
supported image purposes. The provider validates these identifiers before applying metadata
or opening a Store page. The response shape is represented by a small sanitized fixture.

## Automated and presentation coverage

Provider tests cover local package parsing and classification, copied-file failures, path and
identity validation, resource-name fallback, device-code/refresh/Xbox token exchanges,
cancellation, protected credential failures, account cache isolation, PC and optional console
history, exact SCID statistics joins, missing data, legacy installation reconciliation, catalog
correlation and bounded failures. All 98 provider tests passed, including 59 local tests.

Host tests exercise the actual Xbox assembly in a private plugin load context with the shared
SDK, ZIP import followed by opt-in/restart, hidden managed credentials, provisional-name
promotion, offline import, stale-action rejection and launch-intent attribution. Unrelated
releases return an empty artwork result so grouped copies retain Xbox artwork. Application
assemblies have no Xbox implementation reference.

Avalonia headless tests exercise desktop and fullscreen sign-in, displayed verification code,
explicit browser handoff, cancel/sign-out, boolean settings and source labels, including
controller activation. Captures of both account surfaces were visually inspected. These use
fake accounts and temporary storage and never start the production host. The plugin
initialization timeout test allows assembly loading and thread-pool scheduling while still
checking a provider that never completes initialization. Two existing UI test classes now
await startup/activity/feed reads before interaction or database disposal and settle rail
layout before keyboard input; their 18 tests passed in three fresh processes.

The Release build completed without warnings or errors. The application suite passed 4,805
tests, generic plugin hosting passed 89, and the existing covers, SteamGridDB, recommendations
and updater suites passed. All 43 migration hashes verified. The package script produced a
ZIP containing the Xbox assembly, dependency manifest, plugin manifest and README, with no
second copy of the host SDK.

The final focused UI run passed all 37 plugin and corrected lifetime/input checks. The full UI
suite did not pass consistently. An isolated source archive of unchanged commit
`78ed65bd035704aac1b0dd825d0a5b64960393d9` passed 788 of 791 UI tests and reproduced fullscreen
preparation failures, including
`Fullscreen_hosts_a_separate_interface_and_search_keyboard_at_minimum_window_size` and
`Controller_quick_menu_and_browse_navigation_leave_desktop_state_untouched`, plus the
context-menu test's database disposal lock. These are separate from the passing Xbox
settings/source-label tests. Additional intermittent journal/updater
input failures appeared during full runs; this feature does not claim a clean full UI gate.

Verification used Release builds with `--artifacts-path C:\Temp\winnow-xbox-final` to avoid
the running installed app. Full-suite TRX results and focused reruns are in that directory's
`verified-*results` folders. UI captures used `WINNOW_UI_CAPTURE_DIR` with temporary fake
accounts. `plugins/Winnow.Plugin.Xbox/Package.ps1` rebuilds the distributable ZIP.

## Live validation still required

- A suitable Microsoft public-client registration and a consenting test account are needed to
  validate current device authorization, service acceptance, history and cumulative minutes.
- No retail GDK game was launched. Registered activation and process monitoring inside
  protected WindowsApps directories still need device-level validation.
- Console history is fixture-tested; it has no local PC launch or session recording.
- Linux native process tests are intentionally skipped on this Windows host.

Never-played uninstalled purchases cannot be discovered by this played-history integration.
Fixtures and successful public catalog access do not establish entitlement parity or live
authenticated-service availability.
