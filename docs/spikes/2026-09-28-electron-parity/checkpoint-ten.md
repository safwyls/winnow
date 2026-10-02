# Library and metadata checkpoint — 2026-09-29

The metadata dialog now visibly disables Back while saving and publishing the accepted
metadata. Pointer and Escape handling already blocked departure during that interval;
the button now reflects the same state on desktop and fullscreen. Controlled pending
requests verify the lock and its release after publication.

Library evidence now includes the original 37-hour Empyrion detail fixture, independent
Closed world and Still waiting collections, stored expansion-grouping defaults and
single writes for each grouping toggle. Native metadata tests follow the independent
editor and fullscreen field menu, retaining an unsaved title while a year edit changes
the open live list. Manual-game fixture writes wait for the temporary database lock.

## Verification

| Check | Result |
|---|---|
| Electron build and TypeScript | Passed; `.tmp/checkpoint-ten-build.log`. |
| Full component/live-backend suite | 2,458 cases in 126 files passed without skips; `.tmp/checkpoint-ten-integration.log`. |
| Full native Electron suite | 127 passed, one fullscreen merge-header case failed; `.tmp/checkpoint-ten-all-native.log`. |
| Subsequent native merge suite | All six cases passed on the unchanged build; `.tmp/checkpoint-ten-merge-repeat.log`. |
| Full Windows Release .NET repeat | 6,839 passed, no failures, two Linux-only skips across 13 assemblies; `.tmp/parity-nine-dotnet-repeat.log`. |
| Source inventory | 842 ported, 540 retained backend, 13 framework-specific, 878 pending, 162 partial, from 2,435 original methods. |

The first full .NET run failed while disposing a temporary database in one Avalonia UI
test. No C# changes were made; the complete repeat passed, including all 896 Avalonia UI
cases. The precise cause of that first disposal failure has not been established.

The full native run lost a manually selected fullscreen merge header. Its unchanged
focused rerun passed, so the intermittent failure remains an investigation item; the
combined run is not described as a complete pass. Metadata and manual-game native
fixtures exited cleanly after awaiting their Electron child processes. The earlier
libuv teardown assertion did not recur in this full run.

The migration remains in progress. The original tests and release entry points remain
in place, and the complete migration gate still fails for pending and partial contracts.
