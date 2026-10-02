# Electron updates and library-service restart

The Electron frontend owns update presentation and dispatch. Primary distribution packages
reuse Winnow's Inno installation identity and portable recovery helper; secondary NSIS and
AppImage builds use `electron-updater` 6.8.9 with automatic installation on quit disabled. The main process owns
release checks, verified downloads and installation; the renderer receives status and named
actions through the preload bridge. Settings, the desktop caption and fullscreen notice use
the same state. The desktop caption replaces its update button with progress in the same
slot. Fullscreen exposes the command in Quick menu and dismisses that menu on activation.
Settings retains release notes and browser download links, vertical controller navigation,
available-action focus after transitions, and polite recovery announcements.

Automatic updates default on and beta releases default off, using the existing backend
presentation preferences. Packaged release builds check after 20 seconds, then every six
hours. Checks can download an available update when automatic updates are enabled, but
only **Restart to update** or **Update and restart** starts an installer. Development/CI
versions do not check. Changing the channel invalidates the current staged update and
cancels active metadata requests/downloads and clears installer-owned staging; switching
away from beta never authorizes a downgrade. Quit cancels and drains owned updater work
before the main event loop exits.

## Packages and feeds

Primary distribution identity comes from an adjacent `release-info.json` with
`frontend: "electron"`, the matching runtime and the running version. These packages use
the existing asset names: Windows `Winnow-<version>-win-x64-setup.exe` and `.zip`, and Linux
`Winnow-<version>-linux-x64.deb` and `.tar.gz`. The registered Windows path must match the
running `Winnow.exe` and original Inno uninstall registration. Portable eligibility requires
the matching manifest, apphost and bundled helper. Linux automatic replacement is limited
to Ubuntu 24.04 x64. `/opt`, `/usr`, and directories with a `package-managed` marker stay
with the package manager. Unsupported copies retain a release link.

Primary discovery selects the newest eligible release and requires its exact canonical asset
URL, positive bounded size and GitHub SHA-256 digest. An incomplete newer release reports an
error instead of falling back to an older download. Metadata has a shared 30-second deadline,
4 MiB streaming budget and ten-page limit. The downloader checks each request and redirect,
streams into a unique partial file, checks size and SHA-256, and renames only verified bytes.
Cancellation and failures remove partial bytes before retry. The main process rechecks the
staged file immediately before helper handoff.

The secondary electron-builder configuration builds a per-user NSIS installer, portable
executable and Linux AppImage. Those artifact names start with `Winnow-Electron-`:

- `Winnow-Electron-<version>-win-<arch>-Setup.exe`
- `Winnow-Electron-<version>-win-<arch>-Portable.exe`
- `Winnow-Electron-<version>-linux-<arch>.AppImage`
- `Winnow-Electron-<version>-mac-<arch>.dmg`

On this secondary route, only an exact registered Windows NSIS apphost with its uninstaller,
or an unmanaged Ubuntu 24.04 AppImage launch with its original absolute file path available,
offers in-app installation. Portable executables, unpacked Windows
and macOS builds offer the official release page in the default browser. Manual discovery
only considers releases containing the exact Electron platform/architecture artifact. It
scans up to ten pages of 100 entries, with one 30-second deadline and a 4 MiB response budget.
A failed, oversized or incomplete scan reports failure. Manual links use the canonical
release page, ignoring asset-supplied download URLs.

The secondary native provider uses the official `safwyls/winnow` GitHub repository and the standard
`latest`/`beta` channels. Release publication must retain the generated channel metadata,
including platform/architecture variants, together with the corresponding artifacts.
Before download, Winnow rejects metadata with a legacy Avalonia filename, foreign URL,
unsupported architecture, missing SHA-512, or web-installer package. The native downloader
checks the SHA-512 while staging the artifact. Winnow checks the staged size and SHA-512
again immediately before handing the file to the installer. A changed checksum for an
already staged version revokes that staging. Failed verification, cancellation and failed
installer launch clear pending installer files. A failed download cannot expose restart.
The native transport checks each request and redirect against HTTPS GitHub API/release
storage hosts; it refuses foreign hosts, credentials and non-default ports before opening
a connection.
Primary packaging uses `packaging/Publish.ps1` and the existing Inno/ZIP/Debian/tar packagers.
The secondary electron-builder formats are separate from the supported release matrix;
an unpacked development build does not establish installer coverage.

See the upstream [auto-update documentation](https://www.electron.build/v26/docs/features/auto-update/)
for generated channel metadata and supported installers. The installed dependency source is
the authority for the pinned API: newer documentation may describe features absent in 6.8.9.

## Restart and failure behavior

Before either installation route, Winnow authenticates a shutdown request to its current library service
and waits for that process to exit. Provider restart uses the same exclusive lifecycle lock,
so the two actions cannot start competing backends. An uncertain shutdown waits for process
exit before recovery. A failed preparation or installer launch clears the restart context
and reconnects or starts the backend. Failure guidance remains visible during later checks.

The primary Windows helper verifies the registered installation and actual Electron parent,
checks the installer again, and waits for the exact parent to exit before running Inno.
It preserves the selected library and `--no-sync`, and never replays seed or sign-in commands.
Portable staging uses the existing archive validation and durable journal. The helper holds
replacement and library guards, backs up and checks the SQLite database, replaces the binaries,
and relaunches the selected library. The replacement frontend starts a parent-bound helper
before starting the backend: it refuses unsafe journal phases and records possible migration
before backend startup. Only a mounted renderer with a healthy backend acknowledges readiness.
The frontend helper and backend both hold the installation lease, including the Electron
`resources/backend` layout. Read-only portable media can launch without replacement capability
only when no recovery journal exists. Parent death or input closure releases the frontend lease.
The helper's existing `recover` and `resume` commands and paired database restore remain the
recovery path; see [release recovery instructions](releases.md#portable-replacement-and-recovery).

On the secondary installer route, a bounded, one-use JSON context in
the user's application-data directory preserves the explicit `--data-dir` and `--no-sync`
choice because the native installers do not forward arbitrary application arguments. It
accepts only a matching target version, an absolute data path and a timestamp no older than
two hours. Restoration allowlists the application path, update marker, selected directory and
`--no-sync`. It never replays seed, sign-in, URI activation, secrets or arbitrary arguments.
An invalid context refuses startup instead of opening a different library. A normal launch
does not consume the context. This is local user configuration, not an elevated input.

NSIS uses the upstream silent-update arguments and waits for the running application before
replacing it. The main process quits only after the installer reports a successful spawn.
AppImage replacement uses the upstream installer with its relaunch promise retained so a
failed spawn can be reported before Winnow quits.

## Verification limits

`parity-updates*.test.*` covers both presentation modes, status sharing, explicit activation,
focus recovery, cancellation, invalid metadata, late replies, semantic release ordering,
installer launch failure, backend lifecycle exclusion and the restart context. Byte tests
execute the installed NSIS and AppImage downloaders, stream verification and cache helpers
through an isolated loopback socket adapter. They test valid/corrupt/partial payloads,
redirect refusal, cancellation, changed staged files and cleanup. The socket adapter does
not establish Chromium networking or platform installer behavior. Native full-app checks
exercise caption geometry, settings, Quick menu and controller actions at 100%/140% text;
those checks substitute updater status and installer callbacks. No installer or live account
runs in these tests. Third-party notices include all updater production dependencies.

`update-installation-contracts.test.ts` preserves the six original installation-policy and
restart contracts. The distribution tests cover primary release selection, real isolated
byte streams, cancellation, changed files and handoff failure. Helper tests exercise native
lease ownership, startup journal validation, readiness and recovery with temporary data.
Primary Windows Inno/ZIP and Ubuntu 24.04 Debian/tar smoke checks run on disposable machines.
They exercise the actual shipped executable and backend, previous released installers/archives,
selected-library preservation and removal/recovery. Windows includes the earlier release's
embedded updater and a separate upgrade through its copied portable helper. Ubuntu uses
Xvfb/Openbox with restricted unprivileged user namespaces and exact-executable AppArmor
profiles. A portable profile remains valid across replacement at the same path; moving the
installation requires explicit setup for its new path. See [release verification](releases.md)
and the [Ubuntu setup boundary](../packaging/linux/README.md).

The NSIS and AppImage installation/update paths still require disposable-machine smoke
tests, including failed replacement, update from a previous version, data-directory
preservation, and recovery after process exit. No live Electron release feed has been
published or verified by this work. AppImage replacement rollback and Windows failure after
a successful secondary installer spawn remain outside that route's tested recovery boundary.
The primary package checks do not establish these secondary routes, physical-device
compatibility or a successful public release. Reference Avalonia tests remain useful for
shared contracts but do not substitute for Electron package evidence.
