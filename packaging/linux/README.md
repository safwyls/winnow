# Winnow on Ubuntu 24.04 x64

The primary Electron package retains `Winnow-<version>-linux-x64.deb` and
`Winnow-<version>-linux-x64.tar.gz`. Both include Chromium, the independent backend and
its .NET/ASP.NET runtimes, the portable update helper, the bundled artwork provider and
license notices. This directory contains packaging and disposable-runner verification;
the release matrix and publication instructions live in `docs/releases.md`.

## Installation and sandbox permission

Install the Debian package with `sudo apt install ./Winnow-<version>-linux-x64.deb`.
Its launcher remains `winnow`, with an application-menu entry and the `winnow://` handler.
The package installs an AppArmor profile for the exact executable `/opt/winnow/Winnow`.
Updates use the package manager. Removing the package leaves user libraries alone.

For a portable archive, extract it into a stable directory owned by your ordinary user.
Install the following Ubuntu dependencies, then grant sandbox permission once for that
exact path:

```bash
sudo apt install apparmor ca-certificates tzdata libc6 libgcc-s1 libstdc++6 libgssapi-krb5-2 zlib1g libssl3t64 libicu74 libfontconfig1 libgtk-3-0t64 libnss3 libnspr4 libasound2t64 libgbm1 libdrm2 libx11-6 libxcb1 libxcomposite1 libxdamage1 libxext6 libxfixes3 libxrandr2 libxkbcommon0 libatk1.0-0t64 libatk-bridge2.0-0t64 libatspi2.0-0t64 libdbus-1-3 libcups2t64 libpango-1.0-0 libcairo2 libsecret-1-0 xdg-utils
sudo bash ./setup-sandbox.sh --executable "$(pwd)/Winnow"
./winnow
```

Launch without sudo. Ordinary launch and portable updates do not request administrative
privileges. An update in the same directory retains the profile. Moving the application
requires explicit setup for the new path; remove the previous permission with the old
absolute path and `--remove`. The helper refuses pattern/control characters in paths and
refuses to overwrite a different administrator-authored policy.

Ubuntu 24.04 restricts unprivileged user namespaces. The profile grants `userns` to one
executable so Chromium can create its sandbox. It does not disable that sandbox, change a
global sysctl, or install a setuid helper. A functioning graphical session is required.
See [Ubuntu's namespace restriction guidance](https://documentation.ubuntu.com/release-notes/24.04/#unprivileged-user-namespace-restrictions)
and [Chromium's AppArmor guidance](https://chromium.googlesource.com/chromium/src/+/main/docs/security/apparmor-userns-restrictions.md).

## Package verification

`build.sh <publish-dir> <output-dir> <version>` accepts the primary Electron publish layout
and the legacy Avalonia layout. Electron input must pass its complete SHA-256 inventory
before repackaging. The Debian marker and portable integration files receive a fresh
complete inventory. No package may contain a local database, secret configuration, legacy
UI assembly, symlink, world-writable file or privileged executable mode.

```bash
python3 packaging/linux/test-package.py
bash packaging/linux/build.sh artifacts/electron-publish artifacts/electron-packages "$VERSION"
bash packaging/linux/smoke-test.sh artifacts/electron-packages "$VERSION" artifacts/previous/Winnow.deb
```

The smoke script installs only on disposable GitHub Actions runners. Electron validation
requires an earlier real release package supplied by the digest-verified baseline fetcher.
It launches that release with isolated data, upgrades it, checks the database and a sentinel,
then uses the real Electron probe in desktop and fullscreen. It checks native dependencies,
the AppArmor restriction and exact profile, window association, and data after removal.
`with-desktop.sh <command...>` owns an Xvfb server and Openbox window manager for each run.
These checks exercise an X11 virtual desktop; they do not establish physical compositor,
Wayland, graphics-driver or controller-device coverage.
