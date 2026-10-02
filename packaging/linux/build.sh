#!/usr/bin/env bash
# Creates the Debian and portable Linux archives from a self-contained publish
# directory. Run this on Linux after `dotnet publish -r linux-x64 --self-contained`.
set -euo pipefail

usage() {
    printf 'Usage: %s <publish-dir> <output-dir> <version>\n' "${0##*/}" >&2
    exit 64
}

fail() {
    printf 'error: %s\n' "$*" >&2
    exit 1
}

[[ $# -eq 3 ]] || usage

publish_dir=$1
output_dir=$2
version=$3
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
repository_root=$(CDPATH= cd -- "$script_dir/../.." && pwd -P)
semver_pattern='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-([0-9A-Za-z-]+)(\.[0-9A-Za-z-]+)*)?$'

[[ -d $publish_dir ]] || fail "publish directory does not exist: $publish_dir"
[[ -x $publish_dir/Winnow ]] || fail "expected self-contained app host is not executable: $publish_dir/Winnow"
[[ $version =~ $semver_pattern ]] \
    || fail "version must be SemVer X.Y.Z or X.Y.Z-suffix: $version"

release_info=$publish_dir/release-info.json
[[ -f $release_info ]] || fail "missing release manifest: $release_info"

python3 - "$release_info" "$version" <<'PY'
import json
import re
import sys

manifest_path, expected_version = sys.argv[1:]
with open(manifest_path, encoding="utf-8") as stream:
    manifest = json.load(stream)

if manifest.get("version") != expected_version:
    raise SystemExit("error: release-info.json version does not match the requested package version")
if manifest.get("runtime") != "linux-x64":
    raise SystemExit("error: release-info.json runtime must be linux-x64")
if not re.fullmatch(r"[0-9a-f]{40}", str(manifest.get("commit", ""))):
    raise SystemExit("error: release-info.json commit must be a 40-character lowercase SHA-1")
PY
frontend=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8-sig")).get("frontend", "avalonia"))' "$release_info")
[[ $frontend == avalonia || $frontend == electron ]] || fail 'unsupported frontend in release manifest'
if [[ $frontend == electron ]]; then
    python3 "$script_dir/verify-package.py" "$publish_dir" "$version"
    node "$repository_root/src/Winnow.Electron/scripts/verify-primary-asar.mjs" \
        "$publish_dir/resources/app.asar" "$version" \
        "$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["commit"])' "$release_info")"
fi

icon_source=$repository_root/src/Winnow.App/Assets/Icons/dragon.svg
[[ -f $icon_source ]] || fail "application icon does not exist: $icon_source"

mkdir -p -- "$output_dir"
output_dir=$(CDPATH= cd -- "$output_dir" && pwd -P)

stage_root=$(mktemp -d)
trap 'rm -rf -- "$stage_root"' EXIT

portable_name="Winnow-$version-linux-x64"
deb_name="Winnow-$version-linux-x64.deb"
tar_name="$portable_name.tar.gz"
deb_version=${version/-/\~}

write_launcher() {
    local destination=$1
    cat > "$destination" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
app_root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
exec "$app_root/Winnow" "$@"
EOF
    chmod 0755 "$destination"
}

write_desktop_entry() {
    local destination=$1
    cat > "$destination" <<'EOF'
[Desktop Entry]
Type=Application
Name=Winnow
Comment=Surface forgotten games in your library
Exec=winnow %u
MimeType=x-scheme-handler/winnow;
Icon=winnow
Terminal=false
Categories=Game;Utility;
StartupWMClass=Winnow
EOF
}

# Portable archive -----------------------------------------------------------
portable_root=$stage_root/$portable_name
mkdir -p -- "$portable_root"
cp -a -- "$publish_dir/." "$portable_root/"
# A developer-local config can be copied by dotnet publish. It must never be
# present in a distributable archive.
if [[ $frontend != electron ]]; then rm -f -- "$portable_root/appsettings.local.json"; fi
write_launcher "$portable_root/winnow"
write_desktop_entry "$portable_root/winnow.desktop"
cp -- "$icon_source" "$portable_root/dragon.svg"
if [[ $frontend == electron ]]; then
    install -m 0755 "$script_dir/setup-sandbox.sh" "$portable_root/setup-sandbox.sh"
    install -m 0644 "$script_dir/README.md" "$portable_root/LINUX-README.md"
    python3 "$script_dir/verify-package.py" "$portable_root" "$version" --kind portable --refresh-hashes
fi

tar -C "$stage_root" -czf "$output_dir/$tar_name" "$portable_name"

# Debian package -------------------------------------------------------------
deb_root=$stage_root/deb
install_root=$deb_root/opt/winnow
mkdir -p -- \
    "$install_root" \
    "$deb_root/usr/bin" \
    "$deb_root/usr/share/applications" \
    "$deb_root/usr/share/icons/hicolor/scalable/apps" \
    "$deb_root/DEBIAN"
cp -a -- "$publish_dir/." "$install_root/"
printf 'deb\n' > "$install_root/package-managed"
if [[ $frontend != electron ]]; then rm -f -- "$install_root/appsettings.local.json"; fi
cat > "$deb_root/usr/bin/winnow" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
exec /opt/winnow/Winnow "$@"
EOF
chmod 0755 "$deb_root/usr/bin/winnow"
write_desktop_entry "$deb_root/usr/share/applications/winnow.desktop"
cp -- "$icon_source" "$deb_root/usr/share/icons/hicolor/scalable/apps/winnow.svg"
dependencies='ca-certificates, libc6 (>= 2.27), libgcc-s1 | libgcc1, libgssapi-krb5-2, libicu74, libssl3t64, libstdc++6, tzdata, zlib1g, libx11-6, libice6, libsm6, libfontconfig1'
if [[ $frontend == electron ]]; then
    # Ubuntu 24.04's t64 package names are deliberate. The smoke also checks ldd against
    # every shipped ELF, so runner preinstallation cannot hide a missing binary dependency.
    dependencies='apparmor, ca-certificates, libc6 (>= 2.39), libgcc-s1, libgssapi-krb5-2, libicu74, libssl3t64, libstdc++6, tzdata, zlib1g, libfontconfig1, libgtk-3-0t64, libnss3, libnspr4, libasound2t64, libgbm1, libdrm2, libx11-6, libxcb1, libxcomposite1, libxdamage1, libxext6, libxfixes3, libxrandr2, libxkbcommon0, libatk1.0-0t64, libatk-bridge2.0-0t64, libatspi2.0-0t64, libdbus-1-3, libcups2t64, libpango-1.0-0, libcairo2, libsecret-1-0, xdg-utils'
    mkdir -p -- "$deb_root/etc/apparmor.d"
    install -m 0644 "$script_dir/winnow.apparmor" "$deb_root/etc/apparmor.d/winnow-electron"
    install -m 0755 "$script_dir/postinst" "$deb_root/DEBIAN/postinst"
    install -m 0755 "$script_dir/postrm" "$deb_root/DEBIAN/postrm"
    printf '/etc/apparmor.d/winnow-electron\n' > "$deb_root/DEBIAN/conffiles"
    python3 "$script_dir/verify-package.py" "$install_root" "$version" --kind managed --refresh-hashes
fi

installed_size=$(du -sk "$deb_root/opt/winnow" | awk '{ print $1 }')
cat > "$deb_root/DEBIAN/control" <<EOF
Package: winnow
Version: $deb_version
Section: games
Priority: optional
Architecture: amd64
Installed-Size: $installed_size
Maintainer: Winnow contributors
Homepage: https://github.com/safwyls/winnow
Depends: $dependencies
Description: Surface forgotten games in your library
 Winnow is a local-first desktop app for rediscovering games you already own.
EOF

dpkg-deb --build --root-owner-group "$deb_root" "$output_dir/$deb_name" >/dev/null

printf 'Created %s\nCreated %s\n' "$output_dir/$tar_name" "$output_dir/$deb_name"
