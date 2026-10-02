# Release builds

`Release builds` packages the Electron desktop/fullscreen frontend with an independent
self-contained .NET 10 backend and update helper for Windows x64 and Ubuntu 24.04 x64.
Managed components keep trimming and single-file publishing disabled because they use
reflection, embedded migrations and native libraries. Windows managed companions use
ReadyToRun; the primary package does not set `System.GC.ConserveMemory=9`. The older
[memory measurements](spikes/memory-footprint.md) describe the retained Avalonia frontend
and do not establish Electron memory use.

## Application version

The default publisher and release workflow select Electron. Source build and run commands
are described in [`src/Winnow.Electron/README.md`](../src/Winnow.Electron/README.md).
`packaging/Publish-Avalonia.ps1` retains the explicit reference frontend publisher;
its output is not a primary release artifact. The Electron package uses the existing
distribution and update contracts. Windows x64 has passed packaged launch,
installation, previous-release upgrade, uninstall and portable recovery checks on a
disposable runner, including the older release's embedded installer updater.
A separate successful case uses the copied portable helper from that release.
Ubuntu 24.04 x64 has also passed Debian install, actual previous-release upgrade,
desktop/fullscreen startup, data-preserving removal, all five portable recovery cases
and native/Proton session checks. The
[Linux package checkpoint](spikes/2026-09-28-electron-parity/checkpoint-ninety-three.md)
records the tested checkout and retained evidence. Xvfb qualification does not establish
physical Linux compositor or controller behavior.

The primary Electron Windows package is built with `packaging/Publish.ps1`, which forwards
to `Publish-Electron.ps1` using the same arguments,
then `packaging/windows/New-WindowsPackage.ps1`. Install locked frontend dependencies
with `npm ci` in `src/Winnow.Electron` first. The directory contains `Winnow.exe`,
`resources/app.asar`, independent `backend/` and `update-helper/` runtimes, and the
bundled provider. `release-info.json` identifies the Electron frontend; ASAR and managed
assemblies contain the same version and commit. `Verify-ElectronPackage.ps1` checks
identity, assets, runtime and notices and writes `PACKAGE-SHA256SUMS`.

CI calls `Electron package validation` for both platforms. It runs packaged frontend,
Inno/ZIP and Debian/tar installation, previous-version upgrade and recovery scenarios
on disposable runners. It retains `electron-windows-evidence`, `electron-linux-evidence`
and `electron-packages-<runtime>` for 14 days without publishing a release.
This pipeline preserves the Inno AppId, protocol
handler and shortcuts; the installer removes the obsolete Avalonia UI assemblies during
an Electron upgrade. Windows packages remain unsigned. The
[Windows package checkpoint](spikes/2026-09-28-electron-parity/checkpoint-ninety-two.md)
records the clean commit, successful run and artifact names.

`Version.props` owns the three-part version base (currently `0.2.0`). Ordinary builds
append `-dev`; CI packages append `-ci.<run number>`. A tag such as `v0.2.0-beta.1`
supplies the release version. Manual builds and tags must use the base in `Version.props`;
update that file when starting a new release series. Package builds embed the source commit
in the assembly informational version and use the numeric base for Windows file versions.

**Settings → Application → About Winnow** shows the running application's version and
selectable source commit from embedded frontend build metadata, without depending on an adjacent file.
Source archives built without Git metadata show `Unavailable` for the commit.

## Artifacts

| Asset | Use |
|---|---|
| `Winnow-<version>-win-x64-setup.exe` | Per-user Windows installer, Start menu shortcut, and uninstaller |
| `Winnow-<version>-win-x64.zip` | Portable Windows directory; run `Winnow.exe` |
| `Winnow-<version>-linux-x64.deb` | Ubuntu 24.04 x64 package, app-menu entry, and `winnow` command |
| `Winnow-<version>-linux-x64.tar.gz` | Portable Linux directory; run `./winnow` after extraction |
| `Winnow.Plugin.SteamGridDb-<plugin version>.zip` | SteamGridDB artwork plugin, also bundled with Winnow |
| `Winnow.Plugin.Xbox-<plugin version>.zip` | Optional Xbox library, metadata and artwork plugin |
| `Winnow.Plugin.Psn-<plugin version>.zip` | Optional PlayStation library and artwork plugin |
| `winnow-plugins.json` | Release-specific plugin catalogue with package identity, size and SHA-256 |
| `SHA256SUMS` | Checksums attached to a tagged draft release |

Each application directory includes `release-info.json` with its version, runtime identifier,
and source commit. The `backend/` directory contains the independent `Winnow.Backend`
companion and its self-contained ASP.NET Core runtime. Keep that directory with the app;
Avalonia starts or attaches to the backend for the selected data directory. A portable build still uses the normal user data location; pass
`--data-dir <path>` to select another location. Installers preserve user data on removal.
For a manual upgrade, close all frontend windows and stop the backend first. Closing the
last window leaves the backend running. Use authenticated `POST /api/v1/lifecycle/shutdown`
as described in the [frontend API guide](frontend-api.md#start-and-discover), then wait
for the backend process to exit. Windows uses a stable Inno Setup AppId and prior install
directory; Debian prereleases use `~` so they sort before the corresponding stable version.

All three plugin ZIPs are built on every packaging run and attached to the same release as
Winnow. Their versions come from each `plugin.json`, independently of the application
version. Packages include the manifest, entry DLL, dependency manifest and README; the host
supplies `Winnow.PluginSdk.dll`. Packaging checks manifest equality, assembly name and version,
the public entry type and constructor, SDK provider interfaces, and framework/SDK-only
dependencies through metadata without executing provider code.

`winnow-plugins.json` uses `schemaVersion: 1`, `releaseTag`, `appVersion`, and a `plugins`
array containing exactly `steamgriddb`, `xbox` and `psn`. Each entry has `id`, `name`,
`description`, `version`, `apiVersion`, `assetName`, `size` and lowercase `sha256`, plus
`minimumSdkVersion` when the source manifest declares it. Sizes and hashes come from the
finished ZIPs. Release aggregation checks the catalogue against those packages again before
creating `SHA256SUMS`, which includes all application assets, plugin ZIPs and the catalogue.
The website reads published GitHub release assets; a draft is not available for browser
installation or public download until it is published.

## In-app updates

Desktop **Settings → Application → Updates** and fullscreen **Settings → Application**
share the same preferences and update state. Automatic background checks and downloads are
on by default. **Include beta releases** is off by default; turn it on to include published
prereleases as well as stable releases. Turning it off discards any staged beta update and
waits for a newer stable release; it never downgrades an installed beta. Development and CI
builds do not offer release upgrades.

Winnow checks the public `safwyls/winnow` GitHub Releases API after startup and every six
hours while running. Publishing a draft makes it eligible for the next check; merely pushing
a tag or creating a draft does not. **Check for updates** runs a manual check. Disabling
automatic updates leaves manual checks and downloads available. Offline or failed checks
show an error in settings and leave the library usable. There are no repeated popup notices.

| Installation | Update route |
|---|---|
| Installed Windows x64, including a custom installation directory | Download in the background, then **Restart to update** |
| Portable Windows x64 with the bundled update helper and writable local directories | Download and stage in the background, then **Update and restart** |
| Linux x64 Debian package | Release check and browser link to the `.deb`; close and install with the package manager |
| Portable Ubuntu 24.04 x64 with the bundled update helper and writable local directories | Download and stage in the background, then **Update and restart** |

The installed Windows helper waits up to five seconds for transient binary sharing locks
after Winnow exits. Cancellation interrupts that wait. Persistent locks and permission
failures stop the update before running the installer.
| Other portable environments, linked paths or read-only media | Release check and browser link; close and replace manually |

When a supported update is detected, the desktop title bar shows **Update and restart**.
Fullscreen offers the same action in its header, quick menu and Application settings.
The action is disabled while an operation is busy. Clicking it downloads and verifies the
update if needed, then requests normal shutdown and restart. Background downloads alone
never close the application. Both surfaces show the same progress, errors and recovery notice.

The Windows updater requires the registered per-user Inno installation to match the running
executable. Downloading never closes Winnow. It accepts only the exact platform asset from
the official repository over HTTPS, with a matching size and GitHub API SHA-256 digest.
Missing or invalid verification data prevents automatic installation. The digest authenticates
the download against GitHub's HTTPS API response; this is not an Authenticode publisher
signature. Windows packages remain unsigned. See GitHub's [release asset API](https://docs.github.com/en/rest/releases/assets).

On explicit restart, a separate helper verifies and locks the installer, waits up to two
minutes for the app process to exit, refuses locked application binaries, and runs Inno
without forced process closure or Windows reboot. The explicit update action first stops
the shared backend, which cancels workers and releases the database and installation lease.
Ordinary frontend closure does not stop those workers. Close other frontend windows before
updating; their binary locks can still prevent replacement. Setup retains the registered installation directory;
the helper relaunches Winnow with the selected data directory and preserves `--no-sync`.
Fullscreen startup follows the saved preference. One-time seeding and sign-in flags are not
replayed. Library data, credentials, covers, themes and preferences remain in the data directory.

Cancellation or checksum failure deletes the partial download and permits retry. Abandoned
download files older than two days are cleaned on startup. A stopped app does not resume a
partial download; the next check downloads again. A helper that fails before Setup leaves
the existing installation unchanged. A new app launched during Setup can still cause a late
file-lock failure; close other Winnow windows before retrying. Installation errors, required Windows restart, or failed
relaunch leave `failure.txt` and, if Setup ran, `installer.log` under
`<data directory>/updates/handoff-*`. Run the latest official installer manually into the same
installation directory to recover, then start Winnow normally. Keep the data directory.
There is no automatic binary or database rollback: an older binary must not be reopened
against a database a newer build may have migrated. Startup refuses applied migration names
that its binary does not recognize before changing the database journal or schema. Install
the same or a newer release, or restore a pre-upgrade backup into a separate data directory.

Release CI fetches a digest-verified earlier published Windows installer and exercises an
upgrade in a disposable custom directory. It also checks bad checksums, cancellation,
shutdown timeout and locked binaries, relaunch arguments, and preservation of user files.
These installation checks run only on disposable GitHub runners; local unit and headless UI
tests do not install software.

### Portable replacement and recovery

The ZIP and tar.gz contain the frontend, `backend/` companion, and `update-helper/` directories. Copies from before the portable helper was
introduced need one manual archive upgrade; subsequent supported releases offer in-app
replacement. Debian installations, including their `package-managed` marker, stay with the
package manager: close the frontends, stop the backend, and use `sudo apt install ./Winnow-<version>-linux-x64.deb`.
The updater never invokes privilege elevation or replaces package-managed files.

Portable downloads validate the release digest, version and runtime, archive paths and
expanded size. Links and special archive entries are refused. Staging sits beside the
installation in `.<installation folder>.winnow-update`; the installation and its parent
must be writable. Leave room for the download, expanded release and library backup.
The selected data directory may be inside the installation, but must not contain it or
overlap the update workspace. Archive entries must not collide with that data directory.
Abandoned staging can be downloaded again. Starting a later update retains the previous
completed workspace under a `.retained-<id>` name; those older journals are evidence,
while their backup files remain available for manual inspection. Recovery commands use
the current workspace's journal. Retained workspaces are not automatically pruned.

After explicit restart, the copied `Winnow.Update.Helper` waits for the exact original
process to exit. Both the frontend and backend hold the installation lease; the explicit
update action stops the backend before handing off. Installation and library guards exclude
other Winnow instances during
replacement. The helper makes and checks a SQLite backup, including committed WAL data,
before moving the old binaries to `previous` and the staged release into the original
location. It moves an internal selected data directory to the same relative location.
The selected library and `--no-sync` survive restart; one-time seed and sign-in commands do not.

The durable `journal.json` records replacement and possible migration before startup.
The new app acknowledges readiness only after migrations, host startup and Avalonia
framework initialization. A missing or failed handshake leaves recovery evidence; the
helper never silently reopens older binaries against a potentially migrated database.
Keep the workspace, selected data directory and `before.db` until recovery is complete.
Other files placed beside the app remain with the retained previous directory; custom
themes, covers, credentials and preferences belong in the selected data directory.

Close all frontend instances and stop their backends before using the copied helper in the workspace's `helper`
directory. Pass the absolute path to its `journal.json`:

```text
Winnow.Update.Helper recover --journal <absolute journal path>
```

This repairs interrupted replacement only when migration could not have started. After a
possible migration, reinstall the same or a newer release into the original directory,
then run `resume --journal <absolute journal path>` and launch Winnow with the original
`--data-dir`. Alternatively, explicitly choose the paired binary and database restore:

```text
Winnow.Update.Helper recover --journal <absolute journal path> --restore-backup
```

On Windows the helper filename ends in `.exe`; on Linux invoke it with its path.
An explicit restore returns library data to the pre-upgrade snapshot. Post-upgrade database
files are retained in the workspace for inspection. Do not point old binaries at a newer
database yourself. A missing backup or conflicting data location stops recovery without
overwriting the conflicting files.

Release CI also fetches digest-verified earlier portable Windows and Linux archives and
performs actual upgrades on disposable Windows and Ubuntu 24.04 runners. It exercises
external and internal data directories, corrupted downloads, interrupted replacement and
failed new-version startup with explicit paired restore, checking library records and user
files. Engine tests exercise individual recovery boundaries without touching a real library.
The workflow retains journals and baseline-selection evidence. Runner results establish
those environments only; hardware power-loss and filesystem durability behavior still
depend on the device and filesystem.

Portable smoke failures print the scenario and journal failure alongside the helper exit
code. Once Winnow has selected an existing data directory, startup faults also write
`logs/startup-failure.log`, independently of host construction, with the exception type,
stack frames, HRESULT and native error code in the normal privacy-filtered, bounded log format. Smoke artifacts
retain these logs; inspect them before rerunning a failed job. Faults before data-directory
selection or with an unwritable directory still rely on the startup alert and exit code.

On Windows, atomic journal replacement retries access-denied and sharing/lock violations
for up to two seconds to tolerate temporary readers. It preserves the previous journal until
replacement succeeds. Persistent locks and other errors still fail the operation; the updater
does not replay an update phase or delete the journal to bypass a lock.

Archive name checks follow platform case sensitivity: Linux portable packages retain
both the `Winnow` apphost and the `winnow` shell launcher. Exact duplicate entries remain
invalid. The smoke script waits for the update helper's process exit with a bounded
timeout; the restarted application stays open until scenario cleanup.

## Build without publishing

Every pull request and push to `main` runs CI, including fresh Electron and package gates.
Feature branches use the pull-request event. Package versions are
`<version base>-ci.<run number>`. Download
`electron-packages-win-x64`, `electron-packages-linux-x64` and `packages-plugins` from CI artifacts,
retained for 14 days. The plugin artifact contains all three ZIPs and the release catalogue.

Publishing verifies the bundled plugin's source manifest, assembly identity and entry type
before either platform package is built. The entry type is inspected from metadata without
loading provider code. Run `packaging/Test-BundledPlugin.ps1 -BuildDirectory <build output>`
to exercise missing/mismatched package failures against a built application.

**Actions → Release builds → Run workflow**
accepts a version such as `0.2.0-beta.1`. This path runs verification and creates artifacts
without creating a tag or GitHub Release. Versions use three numeric components with an
optional prerelease suffix; numeric components must fit 0–65535. Build metadata and a
leading `v` are not accepted in this input.

Local publishing requires Node 24, locked frontend dependencies, PowerShell 7 and the
.NET 10 SDK. From the repository root:

```powershell
npm --prefix src/Winnow.Electron ci
$commit = git rev-parse HEAD
./packaging/Publish.ps1 -Runtime win-x64 -Version 0.2.0-beta.1 -Commit $commit -OutputDirectory artifacts/publish-win
./packaging/windows/New-WindowsPackage.ps1 -PublishDirectory artifacts/publish-win -OutputDirectory artifacts/packages -Version 0.2.0-beta.1
./packaging/New-PluginRelease.ps1 -Version 0.2.0-beta.1 -OutputDirectory artifacts/plugin-packages
./packaging/Test-PluginRelease.ps1 -PackageDirectory artifacts/plugin-packages -Version 0.2.0-beta.1
```

Windows packaging also requires [Inno Setup 6](https://jrsoftware.org/isinfo.php). On Linux,
publish with `-Runtime linux-x64 -OutputDirectory artifacts/publish-linux`, then run:

```bash
bash packaging/linux/build.sh artifacts/publish-linux artifacts/packages 0.2.0-beta.1
```

Use an empty publish output directory. The publisher rejects local secret configuration and
database files. Installer smoke scripts are restricted to GitHub Actions because they install
and uninstall the package. Their application launches use throwaway `--data-dir` paths.
Plugin release output must also be empty. `Test-PluginRelease.ps1` copies its inputs to a
temporary directory and checks missing packages, catalogue identity and digest mismatches,
unsafe archive entries, manifest changes, and missing or incorrect entry assemblies/types.
Each plugin also has a `Package.ps1` for building its ZIP alone without a release catalogue.

## Create a release

`main` requires an up-to-date pull request with the Windows build/test/migration check and
the Linux native/Proton session check passing. Protection also applies to administrators;
no additional approving reviewer is required. Force pushes and deletion of `main` are disabled.
Merging a pull request produces build artifacts; only a version tag creates a draft release.

After reviewing a commit on main, create and push its version tag, for example:

```bash
git tag -a v0.2.0-beta.1 -m "Winnow 0.2.0 beta 1"
git push origin v0.2.0-beta.1
```

The workflow validates the tag before calling the same CI workflow with that release
version. CI builds and smoke-checks both platforms and validates all three plugins and
their catalogue. The release job consumes those verified artifacts without rebuilding.
Missing or invalid plugin assets fail the release. The gate can reuse matching full-test evidence as described below.
Only then does its release job receive `contents: write` and
create a **draft** release. A prerelease suffix also sets GitHub's prerelease flag. Review
the assets and notes in GitHub Releases before publishing the draft. No signing certificate,
external publishing service, or additional repository secret is required.

A retry can replace assets on an existing draft for the same tag. It refuses to replace an
already published release. Fix a published build by issuing a new version; do not move its tag.
Creating or updating the draft release has a seven-minute timeout. If uploads time out,
rerun the failed job to replace any partially uploaded assets on the draft.

## Reusing CI validation

Pull requests always run the full suite. The protected Windows check aggregates the .NET
backend/reference UI suite, Electron unit/live API tests, the complete migration audit,
native desktop/fullscreen tests, both platform package gates and plugin packages.
The protected Linux native/Proton session check retains its existing name.

Electron and package gates always run fresh. Native tests run on eight isolated Windows
runners, each with one worker, no retries and whole test files. Each builds Debug backend,
fixture and update-helper companions once. A full collected inventory and per-shard plan
must match executed results; every test must pass exactly once, with no skips, missing or
duplicate results. The aggregate compares checkout/tree, lockfile, Node, Electron, SDK and
runner image across receipts. Reports and screenshots are retained per shard; temporary
databases and Chromium profiles stay outside that evidence directory.

Only the .NET Windows suite and Linux session suite use the existing 24-hour evidence
policy. After merge, and for tags or manual release builds, they first restore and audit
dependencies. Windows
also verifies migrations against the current event's baseline. These checks run even when
tests can be reused; a fresh vulnerability warning still fails the gate.

Windows runs a focused fullscreen Home layout preflight before the full solution suite so
those regressions fail without waiting for the longer database tests. The preflight retains
its own test results and does not replace any full-suite coverage or produce reusable evidence.

Each successful full-test job uploads an immutable, per-platform evidence record. Reuse
requires a completed successful run from this repository's CI or release workflow, with both
required platform jobs successful in the recorded attempt. The complete Git tree, actual
SDK version, runner image version, and restored package hashes and selected assets must match.
Records expire for reuse 24 hours after their original run started. A reused job does not
upload another record or extend that deadline.

For ordinary runs, the recorded checkout and producer run must identify the exact target
commit. A PR run instead records the actual temporary merge checkout, its tree and parents.
The gate verifies those against GitHub's commit data and the merged PR's head/base and final
merge commit. This permits reuse after a merge whose commit ID changed but source tree did
not. Fork PR evidence is not reused. A tag can consume this original PR evidence directly,
without waiting for a redundant full test run on `main`.

An unavailable API, missing/expired artifact, different input, unresolvable merge commit,
partial rerun without both jobs in the same attempt, or old evidence falls back to full tests.
The search examines up to 30 recent successful runs per workflow. The job summary links the
original run on reuse, or reports that full validation is running. Required check names and
branch protection remain unchanged. To deliberately rerun everything, dispatch **CI** with
**force_full** enabled (the default for a manual CI run).

Final release packages are always rebuilt with the requested version and run the Windows
installer, Linux startup and portable upgrade/recovery smoke checks. Reuse does not rename
old artifacts, skip version-base validation, or exempt version-only commits. Include the
intended `Version.props` base in a tested PR before tagging. Floating SDK or runner updates
and changed package resolution deliberately require new full-test evidence.

Run `pwsh -NoProfile -File scripts/Test-CiEvidence.ps1` to test the provenance, freshness,
fallback and dependency-fingerprint rules. The policy checks run on both CI platforms.

## Platform limits

Windows packages are unsigned. Embedded Steam/Epic sign-in uses bundled Chromium;
the primary Electron package does not require WebView2. Persistent credential protection
uses Windows DPAPI. Linux has no persistent secret-store backend, and live account
authentication there is not established by package smoke tests.

Linux packages are built and smoke-tested on Ubuntu 24.04 x64 under Xvfb. The Debian package
declares its native runtime dependencies. For the portable archive on Ubuntu 24.04, install:

```bash
sudo apt install ca-certificates tzdata libc6 libgcc-s1 libstdc++6 libgssapi-krb5-2 zlib1g libssl3t64 libicu74 libx11-6 libice6 libsm6 libfontconfig1 libgtk-3-0t64 libnss3 libasound2t64 libgbm1 libdrm2 libxss1 libxtst6 libxshmfence1 libxkbcommon0 libatspi2.0-0t64 libcups2t64 libnspr4 libxcb1 libxcomposite1 libxdamage1 libxext6 libxfixes3 libxrandr2 libatk1.0-0t64 libatk-bridge2.0-0t64 libdbus-1-3 libpango-1.0-0 libcairo2 libsecret-1-0 xdg-utils
```

A working graphical desktop is required. Other Linux distributions and ARM builds are not
part of this release matrix. Linux storefront discovery is limited by the existing Windows
launcher readers. Ubuntu's restricted user namespaces require an exact-path AppArmor
permission: the Debian package manages it; for a portable copy run
`sudo bash setup-sandbox.sh --executable "$(pwd)/Winnow"` in its extracted directory, then launch `./winnow` without
sudo. Moving a portable copy requires granting its new path. Do not disable Chromium's
sandbox. See [Linux packaging](../packaging/linux/README.md) for removal and troubleshooting.

The verifier checks every packaged ELF and its declared transitive dependency owners.
The two exact .NET LTTng tracepoint providers may report missing `liblttng-ust.so.0`
on Ubuntu 24.04; their optional tracing is unavailable. Other missing native libraries fail.
Xvfb/Openbox tests establish package behavior, not a physical Linux compositor or Wayland session.

The secondary Electron configuration includes an optional macOS DMG target, but macOS is
outside the supported release matrix. The primary publisher and CI do not produce it;
signing, notarization, installation, URL association and physical-device behavior are
unverified. Shared browser and Unix monitoring paths do not establish macOS support.
There is no Keychain credential store, and the macOS update path only opens a release page.
