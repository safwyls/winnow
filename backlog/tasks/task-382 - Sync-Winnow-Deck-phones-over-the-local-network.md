---
id: TASK-382
title: Sync Winnow Deck phones over the local network
status: In Progress
assignee:
  - '@claude'
created_date: '2026-10-06 20:45'
updated_date: '2026-10-06 20:46'
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
- [ ] #1 Phone sync is off by default; turning it on opens a separate HTTPS listener and turning it off closes it, without changing the loopback API
- [ ] #2 A phone pairs only with a short-lived single-use code shown as a QR code, and the QR code carries the certificate fingerprint the phone pins
- [ ] #3 Paired phones read one versioned, read-only library snapshot and nothing else; each phone can be revoked
- [ ] #4 Desktop and fullscreen both expose phone sync, pairing and revocation
- [ ] #5 The build spec documents the listener, pairing, snapshot contract and their security limits
<!-- AC:END -->
