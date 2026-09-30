# Electron updates and library-service restart

The Electron frontend has a separate updater from the Avalonia release helper. It uses
`electron-updater` 6.8.9 with automatic installation on quit disabled. The main process owns
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

The Windows configuration builds both a per-user NSIS installer and a portable executable.
The Linux configuration builds an AppImage. Artifact names start with `Winnow-Electron-`:

- `Winnow-Electron-<version>-win-<arch>-Setup.exe`
- `Winnow-Electron-<version>-win-<arch>-Portable.exe`
- `Winnow-Electron-<version>-linux-<arch>.AppImage`
- `Winnow-Electron-<version>-mac-<arch>.dmg`

Only a Windows install with its NSIS uninstaller present, or an AppImage launch with its
original AppImage path available, offers in-app installation. Portable, unpacked Windows
and macOS builds offer the official release page in the default browser. Manual discovery
only considers releases containing the exact Electron platform/architecture artifact. It
scans up to ten pages of 100 entries, with one 30-second deadline and a 4 MiB response budget.
A failed, oversized or incomplete scan reports failure. Manual links use the canonical
release page, ignoring asset-supplied download URLs.

The native provider uses the official `safwyls/winnow` GitHub repository and the standard
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
The old Avalonia release layout and release workflow are unchanged.

See the upstream [auto-update documentation](https://www.electron.build/v26/docs/features/auto-update/)
for generated channel metadata and supported installers. The installed dependency source is
the authority for the pinned API: newer documentation may describe features absent in 6.8.9.

## Restart and failure behavior

Before installation, Winnow authenticates a shutdown request to its current library service
and waits for that process to exit. Provider restart uses the same exclusive lifecycle lock,
so the two actions cannot start competing backends. An uncertain shutdown waits for process
exit before recovery. A failed preparation or installer launch clears the restart context
and reconnects or starts the backend. Failure guidance remains visible during later checks.

The installer receives only fixed installer arguments. A bounded, one-use JSON context in
the user's application-data directory preserves the explicit `--data-dir` and `--no-sync`
choice because the native installers do not forward arbitrary application arguments. It
accepts only a matching target version, an absolute data path and a timestamp no older than
two hours. It never replays `--seed-sample`, URI activations, secrets or arbitrary arguments.
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

The NSIS and AppImage installation/update paths still require disposable-machine smoke
tests, including failed replacement, update from a previous version, data-directory
preservation, and recovery after process exit. No live Electron release feed has been
published or verified by this work. AppImage replacement rollback and Windows failure after
a successful installer spawn remain outside the tested recovery boundary. The existing
Avalonia CI installer smoke does not establish Electron installer coverage. Keep its release
pipeline until those separate checks pass; compiling the renderer or producing an unpacked
directory does not meet that requirement.
