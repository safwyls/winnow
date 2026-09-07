#!/usr/bin/env bash
# Exercises release artifacts on a clean Debian/Ubuntu runner. This script
# installs only the package built for this job and keeps all app data temporary.
set -euo pipefail

usage() {
    printf 'Usage: %s <package-dir> <version>\n' "${0##*/}" >&2
    exit 64
}

fail() {
    printf 'error: %s\n' "$*" >&2
    exit 1
}

[[ $# -eq 2 ]] || usage
[[ ${GITHUB_ACTIONS:-} == true ]] \
    || fail "this smoke test installs and removes a package and may run only in GitHub Actions"

package_dir=$1
version=$2
[[ -d $package_dir ]] || fail "package directory does not exist: $package_dir"
semver_pattern='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-([0-9A-Za-z-]+)(\.[0-9A-Za-z-]+)*)?$'
[[ $version =~ $semver_pattern ]] \
    || fail "version must be SemVer X.Y.Z or X.Y.Z-suffix: $version"

package_dir=$(CDPATH= cd -- "$package_dir" && pwd -P)
artifact_name="Winnow-$version-linux-x64"
deb_path=$package_dir/$artifact_name.deb
tar_path=$package_dir/$artifact_name.tar.gz
deb_version=${version/-/\~}
[[ -f $deb_path ]] || fail "missing Debian package: $deb_path"
[[ -f $tar_path ]] || fail "missing portable archive: $tar_path"
command -v dpkg-deb >/dev/null || fail "dpkg-deb is required"
command -v sudo >/dev/null || fail "sudo is required to install the package"
command -v setsid >/dev/null || fail "setsid is required to isolate the smoke-test process group"
[[ ! -e /usr/bin/winnow ]] || fail "/usr/bin/winnow already exists; refusing to replace it"
if dpkg-query -W -f='${db:Status-Status}' winnow 2>/dev/null | grep -Fqx installed; then
    fail "the winnow Debian package is already installed; refusing to replace it"
fi

workspace=$(mktemp -d "${RUNNER_TEMP:-/tmp}/winnow-linux-smoke.XXXXXX")
data_dir=$workspace/data
package_installed=0
stop_app() {
    if [[ -n ${app_pid:-} ]]; then
        # setsid makes this PID the process-group leader, including the
        # xvfb-run wrapper, Xvfb, and the app it starts.
        kill -TERM -- "-$app_pid" 2>/dev/null || true
        wait "$app_pid" 2>/dev/null || true
    fi
    unset app_pid
}
cleanup() {
    local exit_code=$?
    stop_app
    if [[ $package_installed -eq 1 ]]; then
        sudo env DEBIAN_FRONTEND=noninteractive apt-get remove -y winnow >/dev/null || true
    fi
    if [[ $exit_code -eq 0 ]]; then
        rm -rf -- "$workspace"
    else
        printf 'Smoke-test diagnostics retained at %s\n' "$workspace" >&2
    fi
}
trap cleanup EXIT

validate_manifest() {
    python3 - "$version" "$1" <<'PY'
import json
import re
import sys

expected_version, manifest_path = sys.argv[1:]
with open(manifest_path, encoding="utf-8") as stream:
    manifest = json.load(stream)

if manifest.get("version") != expected_version:
    raise SystemExit("error: release manifest has an unexpected version")
if manifest.get("runtime") != "linux-x64":
    raise SystemExit("error: release manifest has an unexpected runtime")
if not re.fullmatch(r"[0-9a-f]{40}", str(manifest.get("commit", ""))):
    raise SystemExit("error: release manifest has an invalid commit")
PY
}

[[ $(dpkg-deb -f "$deb_path" Package) == winnow ]] || fail "Debian package name is not winnow"
[[ $(dpkg-deb -f "$deb_path" Version) == "$deb_version" ]] || fail "Debian package version is wrong"
mkdir -p -- "$workspace/deb-extract" "$workspace/tar-extract"
dpkg-deb --fsys-tarfile "$deb_path" | tar -x -C "$workspace/deb-extract"
tar -xzf "$tar_path" -C "$workspace/tar-extract"
[[ -x $workspace/deb-extract/opt/winnow/Winnow ]] \
    || fail "Debian package does not contain an executable application host"
[[ -x $workspace/deb-extract/usr/bin/winnow ]] \
    || fail "Debian package does not contain an executable launcher"
[[ -f $workspace/deb-extract/usr/share/applications/winnow.desktop ]] \
    || fail "Debian package does not contain the desktop entry"
[[ -f $workspace/deb-extract/usr/share/icons/hicolor/scalable/apps/winnow.svg ]] \
    || fail "Debian package does not contain the desktop icon"
[[ -x $workspace/tar-extract/$artifact_name/Winnow ]] \
    || fail "portable archive does not contain an executable application host"
[[ -x $workspace/tar-extract/$artifact_name/winnow ]] \
    || fail "portable archive does not contain an executable launcher"
[[ -f $workspace/tar-extract/$artifact_name/dragon.svg ]] \
    || fail "portable archive does not contain the icon"
validate_manifest "$workspace/tar-extract/$artifact_name/release-info.json"
validate_manifest "$workspace/deb-extract/opt/winnow/release-info.json"

# `apt-get install` resolves the package's declared runtime dependencies. The
# runner is ephemeral, so this never installs Winnow onto a user's computer.
sudo apt-get update
sudo env DEBIAN_FRONTEND=noninteractive apt-get install -y xvfb "$deb_path"
package_installed=1
command -v xvfb-run >/dev/null || fail "xvfb-run was not installed"
[[ -x /usr/bin/winnow ]] || fail "installed launcher is not executable"
[[ -x /opt/winnow/Winnow ]] || fail "installed app host is not executable"
grep -Fqx 'Exec=winnow %U' /usr/share/applications/winnow.desktop \
    || fail "installed desktop entry has an unexpected command"

# The process owns an isolated path. Seeing its database proves migrations and
# host startup ran under Xvfb without touching any real user library.
setsid xvfb-run --auto-servernum /usr/bin/winnow --data-dir "$data_dir" --no-sync \
    >"$workspace/winnow.log" 2>&1 &
app_pid=$!
for _ in $(seq 1 40); do
    [[ -f $data_dir/winnow.db ]] && break
    if ! kill -0 "$app_pid" 2>/dev/null; then
        cat "$workspace/winnow.log" >&2 || true
        fail "Winnow exited before startup completed"
    fi
    sleep 0.25
done
[[ -f $data_dir/winnow.db ]] || fail "Winnow did not initialize its isolated data directory"
sleep 2
if ! kill -0 "$app_pid" 2>/dev/null; then
    cat "$workspace/winnow.log" >&2 || true
    fail "Winnow exited immediately after initializing its data directory"
fi
stop_app

# Package removal must leave the caller-owned data path alone.
sudo env DEBIAN_FRONTEND=noninteractive apt-get remove -y winnow
package_installed=0
[[ -f $data_dir/winnow.db ]] || fail "package removal deleted user data"

printf 'Linux package smoke test passed for %s\n' "$version"
