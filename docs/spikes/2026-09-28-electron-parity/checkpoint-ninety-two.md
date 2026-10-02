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
  verifier mutation checks and **54** Windows packaging contract checks passed.
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

At this commit, the workflow is prepared but its remote execution is pending.
The local tests did not install software or change installer registration.
Packages are unsigned. Linux, default delivery entry points, device validation
and the combined final regression gates remain the subsequent tasks.
