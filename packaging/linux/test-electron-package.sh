#!/usr/bin/env bash
set -euo pipefail
[[ $# -eq 1 ]] || { printf 'Usage: %s <published-electron-directory>\n' "${0##*/}" >&2; exit 64; }
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
for script in "$script_dir"/*.sh "$script_dir/postinst" "$script_dir/postrm"; do bash -n "$script"; done
python3 "$script_dir/test-package.py"
version=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8-sig"))["version"])' "$1/release-info.json")
python3 "$script_dir/verify-package.py" "$1" "$version"
# Parsing only: neither kernel policy nor the administrator-owned cache is touched.
apparmor_parser --skip-kernel-load --skip-cache "$script_dir/winnow.apparmor"
