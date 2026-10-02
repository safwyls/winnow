#!/usr/bin/env bash
# An explicit administrator action for one portable path; ordinary launch never elevates.
set -euo pipefail
fail() { printf 'error: %s\n' "$*" >&2; exit 1; }
executable=''
remove=false
while [[ $# -gt 0 ]]; do
    case $1 in
        --executable) [[ $# -ge 2 ]] || fail '--executable requires an absolute path'; executable=$2; shift 2 ;;
        --remove) remove=true; shift ;;
        *) fail 'Usage: sudo bash setup-sandbox.sh --executable /absolute/path/Winnow [--remove]' ;;
    esac
done
[[ $executable == /* ]] || fail '--executable must be an absolute path'
[[ $EUID -eq 0 ]] || fail 'Run this explicit setup step with sudo; launch Winnow as your ordinary user.'
[[ $(uname -s) == Linux && $(uname -m) == x86_64 ]] || fail 'Supported platform: Ubuntu 24.04 x64.'
# shellcheck source=/dev/null
. /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 24.04 ]] || fail 'Supported platform: Ubuntu 24.04 x64.'
command -v apparmor_parser >/dev/null || fail 'Install the Ubuntu apparmor package first.'
aa-enabled --quiet || fail 'AppArmor is not enabled; this helper cannot verify the sandbox permission.'

# Removal remains possible after the portable directory has been deleted. No glob or policy syntax
# from a path may enter the profile. Spaces and ordinary Unicode names remain valid.
if $remove; then executable=$(realpath -m -- "$executable"); else executable=$(realpath -e -- "$executable"); fi
[[ ${executable##*/} == Winnow ]] || fail 'The target must be the Winnow apphost.'
case $executable in *[[:cntrl:]]*|*'"'*|*'\'*|*'*'*|*'?'*|*'['*|*']'*|*'{'*|*'}'*|*'^'*) fail 'This path contains AppArmor pattern or control characters; choose another portable directory.' ;; esac
if ! $remove; then
    [[ -f $executable && -x $executable ]] || fail 'The apphost must be an executable regular file.'
    python3 - "$(dirname -- "$executable")/release-info.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding='utf-8-sig') as stream:
    manifest = json.load(stream)
if manifest.get('runtime') != 'linux-x64' or manifest.get('frontend') not in (None, 'electron'):
    raise SystemExit('error: this is not a supported Winnow Linux release directory')
PY
fi
digest=$(printf '%s' "$executable" | sha256sum)
profile_name=winnow-portable-${digest:0:16}
profile=/etc/apparmor.d/$profile_name
temporary=$(mktemp)
trap 'rm -f -- "$temporary"' EXIT
cat > "$temporary" <<EOF
# Winnow portable sandbox setup; remove with setup-sandbox.sh --remove.
abi <abi/4.0>,
include <tunables/global>
profile $profile_name "$executable" flags=(unconfined) {
  userns,
}
EOF
if [[ -e $profile ]]; then
    cmp -s -- "$temporary" "$profile" || fail "Existing administrator policy differs: $profile; leave it for manual review."
fi
if $remove; then
    if [[ -e $profile ]]; then
        if grep -q "^$profile_name " /sys/kernel/security/apparmor/profiles; then
            apparmor_parser --remove "$profile"
        fi
        rm -- "$profile"
    fi
    printf 'Removed exact-path sandbox permission: %s\n' "$executable"
else
    # Validate before persisting. No setuid bit or system-wide userns setting is changed.
    apparmor_parser --skip-kernel-load --skip-cache "$temporary"
    install -o root -g root -m 0644 "$temporary" "$profile"
    apparmor_parser --replace "$profile"
    printf 'Sandbox permission ready for %s (%s). Launch without sudo.\n' "$executable" "$profile_name"
fi
