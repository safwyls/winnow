#!/usr/bin/env bash
# Destructive package operations are limited to a disposable CI runner, never a developer host.
set -euo pipefail
fail() { printf 'error: %s\n' "$*" >&2; exit 1; }
[[ $# -eq 3 && ${GITHUB_ACTIONS:-} == true ]] || fail 'Use smoke-test.sh on a disposable GitHub Actions runner.'
[[ $EUID -ne 0 ]] || fail 'Launch verification must run as a normal user.'
# shellcheck source=/dev/null
. /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 24.04 && $(uname -m) == x86_64 ]] || fail 'This smoke covers Ubuntu 24.04 x64 only.'
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
repository_root=$(CDPATH= cd -- "$script_dir/../.." && pwd -P)
package_dir=$(realpath -e -- "$1")
version=$2
previous=$(realpath -e -- "$3")
deb=$package_dir/Winnow-$version-linux-x64.deb
archive=$package_dir/Winnow-$version-linux-x64.tar.gz
[[ -f $deb && -f $archive && -f $previous ]] || fail 'Current packages and released baseline are required.'
[[ $(dpkg-deb -f "$previous" Package) == winnow ]] || fail 'Previous package identity is not Winnow.'
[[ $(dpkg-deb -f "$deb" Package) == winnow && $(dpkg-deb -f "$deb" Architecture) == amd64 ]] || fail 'Incorrect current package identity.'
[[ $(dpkg-deb -f "$deb" Version) == "${version/-/\~}" ]] || fail 'Incorrect current Debian version.'
dpkg --compare-versions "$(dpkg-deb -f "$previous" Version)" lt "$(dpkg-deb -f "$deb" Version)" || fail 'Baseline must be an earlier released version.'
[[ ! -e /usr/bin/winnow && ! -e /opt/winnow ]] || fail 'An existing installation must not be replaced by this smoke.'
if dpkg-query -W -f='${db:Status-Status}' winnow 2>/dev/null | grep -Fqx installed; then fail 'Winnow is already installed.'; fi
logs=$repository_root/artifacts/linux-smoke-logs
mkdir -p -- "$logs"
workspace=$(mktemp -d "${RUNNER_TEMP:-/tmp}/winnow-electron-smoke.XXXXXX")
data_dir=$workspace/library
installed=false
cleanup() {
    local code=$?
    if $installed; then sudo env DEBIAN_FRONTEND=noninteractive apt-get purge -y winnow >"$logs/cleanup.log" 2>&1 || true; fi
    printf '%s\n' "$workspace" > "$logs/workspace.txt"
    if [[ $code -ne 0 ]]; then printf 'Diagnostics: %s; isolated data: %s\n' "$logs" "$workspace" >&2; fi
}
trap cleanup EXIT
sudo aa-enabled --quiet || fail 'AppArmor must be enabled for this validation.'
[[ $(cat /proc/sys/kernel/apparmor_restrict_unprivileged_userns) == 1 ]] || fail 'Enable the Ubuntu userns restriction before running this smoke; never disable it.'
mkdir -p -- "$workspace/current" "$workspace/portable"
dpkg-deb -x "$deb" "$workspace/current"
tar -xzf "$archive" -C "$workspace/portable"
python3 "$script_dir/verify-package.py" "$workspace/current/opt/winnow" "$version" --kind managed
python3 "$script_dir/verify-package.py" "$workspace/portable/Winnow-$version-linux-x64" "$version" --kind portable
desktop-file-validate "$workspace/current/usr/share/applications/winnow.desktop"
cmp "$script_dir/winnow.apparmor" "$workspace/current/etc/apparmor.d/winnow-electron"

sudo apt-get update
sudo env DEBIAN_FRONTEND=noninteractive apt-get install -y "$previous" >"$logs/baseline-install.log" 2>&1
installed=true
# The source release starts its real backend and database. The bounded helper owns both
# frontend and discovered backend shutdown; it never follows a PID without checking its executable.
bash "$script_dir/with-desktop.sh" python3 "$script_dir/launch-baseline.py" /opt/winnow/Winnow "$data_dir" "$logs/baseline-start.log"
python3 "$script_dir/library-evidence.py" "$data_dir" --seed >"$logs/library-before.json"
sudo env DEBIAN_FRONTEND=noninteractive apt-get install -y "$deb" >"$logs/upgrade-install.log" 2>&1
[[ $(dpkg-query -W -f='${Version}' winnow) == "${version/-/\~}" ]] || fail 'Installed upgrade version differs.'
[[ -x /usr/bin/winnow ]] || fail 'Installed launcher is missing.'
grep -Fqx 'Exec=winnow %u' /usr/share/applications/winnow.desktop
grep -Fqx 'MimeType=x-scheme-handler/winnow;' /usr/share/applications/winnow.desktop
sudo grep -q '^winnow-electron ' /sys/kernel/security/apparmor/profiles || fail 'Managed sandbox profile was not loaded.'
python3 "$script_dir/verify-package.py" /opt/winnow "$version" --kind managed --dependencies --deb "$deb" >"$logs/package-verification.log"
for mode in desktop fullscreen; do
    bash "$script_dir/with-desktop.sh" node "$repository_root/src/Winnow.Electron/tests/packaged/probe.mjs" \
        --exe /opt/winnow/Winnow --data-dir "$data_dir" --report "$logs/managed-$mode.json" --mode "$mode"
done
python3 "$script_dir/library-evidence.py" "$data_dir" --expect "$logs/library-before.json" >"$logs/library-after-upgrade.json"
# Verify the actual X11 class recorded by the probe against the shipped desktop association.
python3 - "$logs/managed-desktop.json" /usr/share/applications/winnow.desktop <<'PY'
import json, sys
report = json.load(open(sys.argv[1], encoding='utf-8'))
expected = next(line.strip().split('=', 1)[1] for line in open(sys.argv[2]) if line.startswith('StartupWMClass='))
classes = report.get('linux', {}).get('wmClass', [])
assert expected in classes, f'Actual WM_CLASS {classes!r} does not match desktop entry {expected!r}'
PY
sudo env DEBIAN_FRONTEND=noninteractive apt-get remove -y winnow >"$logs/remove.log" 2>&1
[[ ! -e /usr/bin/winnow && ! -e /opt/winnow/Winnow ]] || fail 'Package removal left the launcher or apphost.'
if sudo grep -q '^winnow-electron ' /sys/kernel/security/apparmor/profiles; then fail 'Package removal left its loaded sandbox profile.'; fi
python3 "$script_dir/library-evidence.py" "$data_dir" --expect "$logs/library-before.json" >"$logs/library-after-removal.json"
sudo env DEBIAN_FRONTEND=noninteractive apt-get purge -y winnow >"$logs/purge.log" 2>&1
installed=false
[[ ! -e /etc/apparmor.d/winnow-electron ]] || fail 'Purge left its default profile file.'
printf 'Managed Linux install, real upgrade, both modes and data-preserving removal passed: %s\n' "$version"
