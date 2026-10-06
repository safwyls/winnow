---
id: TASK-382
title: Sync Winnow Deck phones over the local network
status: Done
assignee:
  - '@claude'
created_date: '2026-10-06 20:45'
updated_date: '2026-10-06 21:18'
labels:
  - backend
  - sync
  - companion
dependencies: []
ordinal: 385000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Winnow Deck, the phone companion (safwyls/placeholder), can only see what phone-reachable stores report: no Epic or GOG playtime, no session history, no Winnow lists or confirmed merges. The owner chose LAN sync with the Windows app over a file export first. The loopback API on the independent backend (TASK-348) must stay loopback-only, so phones need a separate, opt-in, read-only surface. Builds on branch codex/independent-backend-api. Related: TASK-2 defines a portable JSON export; the companion snapshot is narrower and does not settle TASK-2.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Phone sync is off by default; turning it on opens a separate HTTPS listener and turning it off closes it, without changing the loopback API
- [x] #2 A phone pairs only with a short-lived single-use code shown as a QR code, and the QR code carries the certificate fingerprint the phone pins
- [x] #3 Paired phones read one versioned, read-only library snapshot and nothing else; each phone can be revoked
- [x] #4 Desktop and fullscreen both expose phone sync, pairing and revocation
- [x] #5 The build spec documents the listener, pairing, snapshot contract and their security limits
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Evidence: CompanionTests (8) cover off by default, listener start/stop, pinning, pairing and lockout, revocation, refused routes and the snapshot contract, plus the frontend client round trip. PhoneSyncSettingsTests and PhoneSyncViewModelTests cover both surfaces. Build spec §7.1 covers the listener, certificate, pairing, snapshot, limits and settings. Windows DPAPI, TLS on Windows, the firewall prompt and a real phone are not verified here.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Winnow now serves an opt-in, read-only library snapshot to paired phones over HTTPS on the local network. It is a separate listener; the loopback API stays loopback-only. Pairing uses a single-use QR code that carries the pinned certificate fingerprint. Desktop and fullscreen settings control it. Verified by backend, view model and headless UI tests on Linux; the Windows-only parts are left to Windows CI.
<!-- SECTION:FINAL_SUMMARY:END -->
