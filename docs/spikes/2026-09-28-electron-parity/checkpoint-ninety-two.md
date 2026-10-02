# Primary Windows package checkpoint — 2026-10-01

TASK-381.37 preserves the existing Inno installer identity and ZIP layout while
replacing the frontend with Electron. The primary publisher bundles the renderer,
preloads and fonts in ASAR, independent backend and helper runtimes, the artwork
provider, and Electron, Chromium, JavaScript and .NET dependency notices. The
package verifier rejects missing components, mismatched identity, stale Avalonia
UI assemblies, local secret configuration and database files.

`release-info.json`, embedded frontend metadata and managed assembly identity
agree on version and commit. Windows PE numeric versions use the base from
`Version.props`; the full prerelease version remains in ASAR and managed metadata.
`PACKAGE-SHA256SUMS` records every payload file. The Inno installer retains its
original AppId, protocol registration and shortcuts, and removes only the named
obsolete frontend assemblies during an Electron upgrade.

## Local evidence

- Primary publisher, typecheck and package verification passed for
  `0.2.0-ci.38137`, embedding base commit
  `bdde1c582ca1dc5249ed41e0ec2215710525b1f0`. This was a local build with the
  checkpoint's uncommitted source changes; clean CI provenance remains separate.
- The directory contains 695 payload files. The ASAR SHA-256 is
  `49cf34b4e28c68c245657b3d01ae2d9e05c55cb7a469a74457b4ea02c8aaeea6`.
  The self-contained runtime notices include both .NET and ASP.NET Core 10.0.11.
- **20** build-identity tests, **25** startup/activation tests, **16** package
  verifier mutation checks, **61** Windows packaging contract checks (including
  the actual published layout) and **4** startup-window smoke contracts passed.
- **11 native packaged checks passed**, with no skips, in 34.5 seconds.
  Desktop and fullscreen load their own bundled backend and rendered ASAR,
  perform a named library read, activate the same owner and backend, and release
  their real Windows portable replacement lock on shutdown. Executable and
  shortcut activation cover show, fullscreen, game and plugin routes. The
  explicit test library preserves the global protocol association.

Build and contract evidence is in `.tmp/task38137-publish-verified.log`,
`.tmp/task38137-package-contracts.log` and
`.tmp/task38137-packaging-evidence.json`. Native reports, health ledgers and
screenshots are in `.tmp/task38137-packaged-results`; stdout is
`.tmp/task38137-packaged.log`. The package is
`.tmp/task38137-electron-package-verified`.

## Disposable installation gate

`.github/workflows/electron-packages.yml` builds this package from a clean
checkout, runs packaged activation, creates Inno and ZIP artifacts, then selects
digest-verified previous published releases. Its existing five installer scenarios
and four portable scenarios retain integrity, cancellation, shutdown timeout,
locked binaries, preserved user data, interrupted replacement and paired restore
assertions. Successful upgrades also run the native desktop/fullscreen probe.
The workflow retains package, baseline and recovery evidence without publishing.

The first disposable run exposed a runtime inventory assumption; validation now
checks the graphics libraries actually shipped by the pinned Electron build.
The second run passed the package/native checks and four rejected-update cases,
but exposed an invisible restart: launching Chromium with `WindowStyle Hidden`
left a healthy, responsive window hidden. The shared installer helper now starts
the frontend normally. Background installers and helpers remain hidden.

A native reproduction on Windows kept the hidden restart invisible for the full
60-second readiness deadline. With the corrected launch, the visible window and
its own authenticated backend became ready in 2.71 seconds; graceful shutdown
completed in 2.86 seconds with no remaining processes or held update locks.
Smoke checks now retain exact-process window, backend and cleanup diagnostics.
Reports are in `.tmp/task38137-close-startup-normal-65d98dbfd0a646eb9a99d9e0485ccc09`.

The [disposable run 36963975414](https://github.com/safwyls/winnow/actions/runs/36963975414)
passed the current-helper scenarios at source commit
`aec9a0a3c481c8324bc602960eedb8e18a8d1d7d`:

- Clean primary publish, identity and integrity checks, and all 11 native
  packaged checks passed.
- All five installer scenarios passed, including the actual upgrade from
  `v0.2.0-beta.3`, visible restart, both presentation probes, preserved data,
  protocol and shortcut registration, and uninstall.
- All four portable scenarios passed: external and internal data upgrades,
  failed startup with explicit paired restore, and interrupted replacement.
  The previous ZIP also came from digest-verified `v0.2.0-beta.3`.
- `electron-windows-evidence` retains the baseline size/digest records,
  process/window/backend reports, screenshots, journals and startup diagnostics;
  `electron-packages-win-x64` contains the validated Inno installer and ZIP.
  Both artifacts have a 14-day retention period. The local evidence copy is
  `.tmp/task38137-ci-passed`.

Review found that this run used the corrected checkout helper against the old
installed files. The published beta embeds its own helper, which still launches
with `SW_HIDE`. The smoke now extracts that exact embedded resource without
executing or loading its assembly and records both hashes. Its successful upgrade
must use that old helper. Installed database identities and all portable recovery
file sentinels are also compared, beyond merely checking database existence.

Electron now retries an explicit show request once when the window remains
hidden. A native probe confirmed that Windows suppresses the first show under
`SW_HIDE` and accepts the second. The corrected production frontend became
visible with its own backend in 2.24 seconds and closed normally in 2.31 seconds;
no forced cleanup or held locks remained. This was the development bundle with
the frozen package's backend; reports are in
`.tmp/task38137-legacy-production-112fad0c6eac4568a5463d4ecd52a4c9`.
All 21 tray tests and typecheck pass. The actual embedded-baseline-helper CI gate
is pending, so the Windows task remains open.

The local tests did not install software or change installer registration.
Packages are unsigned. Linux, default delivery entry points, device validation
and the combined final regression gates remain the subsequent tasks.
