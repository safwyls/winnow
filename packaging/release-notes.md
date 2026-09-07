Winnow beta builds for Windows and Linux, x64. The .NET runtime is included.

- Windows: use the `-setup.exe` installer, or extract the `.zip` and run `Winnow.exe`.
- Linux: install the `.deb` on Ubuntu 24.04 x64 with `sudo apt install ./Winnow-*-linux-x64.deb`, or extract the `.tar.gz` and run `./winnow`. A desktop session and native graphics/runtime libraries are required.
- `SHA256SUMS` contains hashes of the release assets. Each application includes `release-info.json` with its version, platform, and source commit.

Windows packages are currently unsigned. Embedded Steam/Epic sign-in uses Microsoft's Evergreen WebView2 Runtime on Windows; Linux does not provide that embedded sign-in path. Linux launcher discovery and credential persistence have platform limitations described in the README.

Installer removal preserves Winnow's library data. Close Winnow before upgrading. Review these draft assets and notes before publishing the release.
