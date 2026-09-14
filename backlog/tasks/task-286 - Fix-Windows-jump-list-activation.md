---
id: TASK-286
title: Fix Windows jump list activation
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 03:47'
updated_date: '2026-09-14 04:52'
labels: []
dependencies: []
ordinal: 328000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Clicking either recent games or Switch to Fullscreen briefly shows a busy cursor but performs no action.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Generated shell launch arguments reach the existing session for games and fullscreen, verified with actual subprocess execution.
- [x] #2 Cold startup and normal activation retain their behavior; regression tests cover shell argument ordering and launch context.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect persisted shell links, reproduce their exact launch command under an isolated instance server, fix the failing boundary, and verify both game and fullscreen requests plus existing activation behavior.

Confirmed user launched from an administrator terminal. Replace elevation-sensitive Windows CurrentUserOnly with explicit current-user SID ownership and ACL plus medium integrity on activation IPC, validate peer identity, and test security descriptors. Preserve Linux CurrentUserOnly and bounded typed commands.

Align malformed-payload test client with Windows explicit SID validation, retain Linux CurrentUserOnly, and rerun activation tests before pushing CI fix.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Persisted Windows shortcut inspected read-only: correct executable, command arguments and working directory. Seven subprocess cases passed against the exact source Debug Winnow.exe, covering dotnet, direct apphost and actual Windows .lnk launches in disposable data directories. A real sample-data Winnow window accepted the fullscreen request and changed to 3440x1440 fullscreen. A retained prior pipe client did not break subsequent activation. Original user failure remains unreproduced; requested whether affected session was launched from IDE/debugger, normally, or was closed. No product fix claimed. Added opt-in native shortcut regression coverage using WINNOW_ACTIVATION_TEST_EXE.

User confirmed dotnet run was launched from an administrator terminal. Windows CurrentUserOnly rejects clients at a different elevation. Replaced Windows pipe and mutex security with explicit user SID ownership, same-user ACL, network-logon deny and medium integrity; client verifies server owner before exchanging requests. Linux retains CurrentUserOnly. Shared activation covers both desktop and fullscreen. Validation: 22 targeted tests passed, including actual apphost and Windows .lnk subprocesses for fullscreen and game requests, plus live pipe/mutex owner and ACL checks. Earlier isolated UI smoke verified fullscreen restoration and sizing. Actual elevated-owner/medium-client end-to-end smoke remains unverified in this medium-integrity tool session; user must restart Winnow to recreate the named objects.

CI run 34806635673 failed only three malformed-payload theory cases: their raw test client retained CurrentUserOnly and ValidateRemotePipeUser rejected explicit user-SID ownership on the Windows runner. Aligned that client with production: Windows VerifyServer, CurrentUserOnly elsewhere. Rejection and no-dispatch assertions remain intact. All 22 activation/security tests pass locally; no application changes. New CI run required to confirm runner result.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed elevation-sensitive Windows activation permissions while retaining same-account isolation. Verified 22 targeted tests including real shell shortcuts, typed game/fullscreen requests and live object ACLs. Elevated-session end-to-end validation remains a manual check after restarting the updated app.
<!-- SECTION:FINAL_SUMMARY:END -->
