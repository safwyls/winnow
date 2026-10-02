#!/usr/bin/env bash
# One owned X server and window manager per invocation; never changes the caller's desktop.
set -euo pipefail
[[ $# -gt 0 ]] || { printf 'Usage: %s command [arguments...]\n' "${0##*/}" >&2; exit 64; }
if [[ ${WINNOW_SMOKE_DESKTOP:-} != 1 ]]; then
    exec xvfb-run --auto-servernum --server-args='-screen 0 1920x1080x24 -nolisten tcp' \
        env WINNOW_SMOKE_DESKTOP=1 bash "$0" "$@"
fi
openbox --sm-disable >"${RUNNER_TEMP:-/tmp}/winnow-openbox-$$.log" 2>&1 &
wm_pid=$!
cleanup() { kill "$wm_pid" 2>/dev/null || true; wait "$wm_pid" 2>/dev/null || true; }
trap cleanup EXIT
for _ in $(seq 1 80); do
    if xprop -root _NET_SUPPORTING_WM_CHECK 2>/dev/null | grep -q 'window id # 0x'; then
        "$@"
        exit $?
    fi
    kill -0 "$wm_pid" 2>/dev/null || { printf 'Openbox exited before readiness.\n' >&2; exit 1; }
    sleep 0.1
done
printf 'Openbox did not publish its window-manager readiness.\n' >&2
exit 1
