# Release builds

`Release builds` packages self-contained .NET 10 applications for Windows x64 and Linux
x64. It leaves trimming and single-file publishing disabled because Winnow uses reflection,
embedded migrations, Avalonia resources, and native libraries. The Windows x64 build is
also ReadyToRun-compiled and ships with `System.GC.ConserveMemory=9`. The Windows
[memory measurements](spikes/memory-footprint.md) support these settings. Linux x64
uses neither setting; its performance was not measured in that study.

## Application version

`Version.props` owns the three-part version base (currently `0.1.0`). Ordinary builds
append `-dev`; CI packages append `-ci.<run number>`. A tag such as `v0.1.0-beta.1`
supplies the release version. Manual builds and tags must use the base in `Version.props`;
update that file when starting a new release series. Package builds embed the source commit
in the assembly informational version and use the numeric base for Windows file versions.

**Settings → Application → About Winnow** shows the running application's version and
selectable source commit directly from its assembly, without depending on an adjacent file.
Source archives built without Git metadata show `Unavailable` for the commit.

## Artifacts

| Asset | Use |
|---|---|
| `Winnow-<version>-win-x64-setup.exe` | Per-user Windows installer, Start menu shortcut, and uninstaller |
| `Winnow-<version>-win-x64.zip` | Portable Windows directory; run `Winnow.exe` |
| `Winnow-<version>-linux-x64.deb` | Ubuntu 24.04 x64 package, app-menu entry, and `winnow` command |
| `Winnow-<version>-linux-x64.tar.gz` | Portable Linux directory; run `./winnow` after extraction |
| `SHA256SUMS` | Checksums attached to a tagged draft release |

Each application directory includes `release-info.json` with its version, runtime identifier,
and source commit. A portable build still uses the normal user data location; pass
`--data-dir <path>` to select another location. Installers preserve user data on removal.
For a manual upgrade, close Winnow first. Windows uses a stable Inno Setup AppId and prior install
directory; Debian prereleases use `~` so they sort before the corresponding stable version.

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
| Portable Windows x64 | Release check and browser link to the portable ZIP; close and replace manually |
| Linux x64 Debian package | Release check and browser link to the `.deb`; close and install with the package manager |
| Portable Linux x64 | Release check and browser link to the archive; close and replace manually |

The Windows updater requires the registered per-user Inno installation to match the running
executable. Downloading never closes Winnow. It accepts only the exact platform asset from
the official repository over HTTPS, with a matching size and GitHub API SHA-256 digest.
Missing or invalid verification data prevents automatic installation. The digest authenticates
the download against GitHub's HTTPS API response; this is not an Authenticode publisher
signature. Windows packages remain unsigned. See GitHub's [release asset API](https://docs.github.com/en/rest/releases/assets).

On explicit restart, a separate helper verifies and locks the installer, waits up to two
minutes for the app process to exit, refuses locked application binaries, and runs Inno
without forced process closure or Windows reboot. Normal shutdown cancels workers and
disposes the host before process exit. Setup retains the registered installation directory;
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
tests do not install software. Portable Windows and Linux updates use the manual routes in the table above.

## Build without publishing

Pushes to `main` and `codex/**`, and pull requests, build packages when application,
bundled plugin, packaging, version, SDK, dependency configuration, or workflow files change. Their version is
`<version base>-ci.<run number>`. Download
`packages-win-x64` and `packages-linux-x64` from the workflow's artifacts, retained for 14 days.

Publishing verifies the bundled plugin's source manifest, assembly identity and entry type
before either platform package is built. The entry type is inspected from metadata without
loading provider code. Run `packaging/Test-BundledPlugin.ps1 -BuildDirectory <build output>`
to exercise missing/mismatched package failures against a built application.

**Actions → Release builds → Run workflow**
accepts a version such as `0.1.0-beta.1`. This path runs verification and creates artifacts
without creating a tag or GitHub Release. Versions use three numeric components with an
optional prerelease suffix; numeric components must fit 0–65535. Build metadata and a
leading `v` are not accepted in this input.

Local publish commands, from the repository root:

```powershell
$commit = git rev-parse HEAD
./packaging/Publish.ps1 -Runtime win-x64 -Version 0.1.0-beta.1 -Commit $commit -OutputDirectory artifacts/publish-win
./packaging/windows/New-WindowsPackage.ps1 -PublishDirectory artifacts/publish-win -OutputDirectory artifacts/packages -Version 0.1.0-beta.1
```

Windows packaging requires [Inno Setup 6](https://jrsoftware.org/isinfo.php). On Linux with
PowerShell and the .NET SDK installed, publish with `-Runtime linux-x64`, then run:

```bash
bash packaging/linux/build.sh artifacts/publish-linux artifacts/packages 0.1.0-beta.1
```

Use an empty publish output directory. The publisher rejects local secret configuration and
database files. Installer smoke scripts are restricted to GitHub Actions because they install
and uninstall the package. Their application launches use throwaway `--data-dir` paths.

## Create a release

`main` requires an up-to-date pull request with the Windows build/test/migration check and
the Linux native/Proton session check passing. Protection also applies to administrators;
no additional approving reviewer is required. Force pushes and deletion of `main` are disabled.
Merging a pull request produces build artifacts; only a version tag creates a draft release.

After reviewing a commit on main, create and push its version tag, for example:

```bash
git tag -a v0.1.0-beta.1 -m "Winnow 0.1.0 beta 1"
git push origin v0.1.0-beta.1
```

The workflow validates the tag, runs the existing Windows/Linux CI gate, and builds and
smoke-checks both platforms. Only then does its release job receive `contents: write` and
create a **draft** release. A prerelease suffix also sets GitHub's prerelease flag. Review
the assets and notes in GitHub Releases before publishing the draft. No signing certificate,
external publishing service, or additional repository secret is required.

A retry can replace assets on an existing draft for the same tag. It refuses to replace an
already published release. Fix a published build by issuing a new version; do not move its tag.

## Platform limits

Windows packages are unsigned. Embedded Steam/Epic sign-in requires Microsoft's Evergreen
WebView2 Runtime; it is not bundled with the installer. The app handles an unavailable runtime.

Linux packages are built and smoke-tested on Ubuntu 24.04 x64 under Xvfb. The Debian package
declares its native runtime dependencies. For the portable archive on Ubuntu 24.04, install:

```bash
sudo apt install ca-certificates tzdata libc6 libgcc-s1 libstdc++6 libgssapi-krb5-2 zlib1g libssl3t64 libicu74 libx11-6 libice6 libsm6 libfontconfig1
```

A working graphical desktop is required. Other Linux distributions and ARM builds are not
part of this release matrix. Linux storefront discovery is limited by the existing Windows
launcher readers. WebView2 sign-in and DPAPI credential storage remain Windows-only; the
package does not introduce a Linux browser or secret-store backend.

Native dependencies follow [Microsoft's .NET requirements](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install)
and [Avalonia's desktop Linux requirements](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).
