---
id: TASK-382.1
title: Serve the companion snapshot to paired phones from the backend
status: Done
assignee:
  - '@claude'
created_date: '2026-10-06 20:45'
updated_date: '2026-10-06 20:53'
labels:
  - backend
  - sync
  - companion
dependencies: []
parent_task_id: TASK-382
ordinal: 386000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Backend half of TASK-382: the LAN listener, pairing, device tokens and the snapshot contract, plus loopback API routes the frontends use to control them.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The LAN listener runs only while phone sync is enabled, on HTTPS with a certificate whose private key is kept encrypted at rest; where it cannot be encrypted, phone sync refuses to start
- [x] #2 Pairing accepts only a current single-use code, locks out after repeated wrong codes, and returns a device token stored only as a hash
- [x] #3 The snapshot lists visible games with IGDB and store IDs, per-store playtime, last played, acquisition dates, editions and manual lists, and excludes credentials, paths and account identifiers
- [x] #4 Requests without a valid device token, and any route other than pairing and the snapshot, are refused on the LAN listener
- [x] #5 Revoking a phone makes its token fail immediately
- [x] #6 Backend tests cover enable and disable, pairing, lockout, revocation, refused routes and the snapshot contract
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Contracts (Winnow.Api.Contracts/Companion): snapshot v1 records (games with IGDB id, year, cover, bucket, playtime, last played; entries with store, store IDs from external_ids, edition note, IGDB version id, per-entry playtime, last played, acquired date; manual lists as ordered release IDs), pairing request/response, and loopback status/pairing/device records.
2. Application (Winnow.Application/Companion): snapshot builder over ILibraryQueryRepository.GetSnapshotAsync with the user's visibility preferences, excluding account refs, install paths and prices; device registry in settings (companion.devices.v1, token SHA-256 only) and companion.enabled / companion.port; in-memory single-use pairing code (5-minute expiry, lockout after 5 wrong codes).
3. Certificate: self-signed ECDSA P-256 server certificate created on first enable, PFX stored DPAPI-protected through an injectable protector; no protector available means phone sync refuses to start with a stated reason.
4. Backend: CompanionLanHost hosted service runs a separate minimal Kestrel app on 0.0.0.0:port with HTTPS only while enabled; routes POST /companion/v1/pair and GET /companion/v1/snapshot (ETag), everything else 404; bearer device tokens compared in constant time. The loopback app and its middleware are untouched.
5. Loopback API: GET /api/v1/companion, PUT /api/v1/companion/enabled, POST and DELETE /api/v1/companion/pairing, DELETE /api/v1/companion/devices/{id}; publish companion.changed. QR payload carries LAN IPv4 addresses, port, certificate SHA-256 and the code.
6. Tests in Winnow.Backend.Tests with a fake protector; docs in game-library-design.md.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented as planned: contracts in Winnow.Api.Contracts/Companion; CompanionSnapshotBuilder, CompanionDevices and CompanionPairingWindow in Winnow.Application/Companion; CompanionCertificate, DpapiCompanionSecretProtector, CompanionLanHost and CompanionEndpoints in Winnow.Backend. The LAN listener is a separate slim Kestrel app with request logging cleared; the loopback app's middleware is unchanged. Documented in game-library-design.md §7.1 and docs/frontend-api.md.

Validation on Linux (.NET SDK 10.0.112): dotnet build of Winnow.slnx succeeded with 0 warnings; Winnow.Backend.Tests 24 passed (6 CompanionTests plus a Windows-only DPAPI round trip that returns early here); Winnow.Application.Tests 21 passed; Winnow.Api.Client.Tests 15 passed; Winnow.Tests Enforcement 101 passed. Not verified here: DPAPI and Kestrel TLS on Windows (Windows CI runs the same tests), Windows Firewall prompting, and a real phone on a real network.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The backend serves a read-only library snapshot to paired phones over a separate HTTPS listener that runs only while phone sync is on. Pairing uses a five-minute single-use code with lockout; device tokens are stored as SHA-256 and revocable; the certificate key is DPAPI-encrypted or phone sync refuses to start. Loopback routes let both frontends control it. Verified with Winnow.Backend.Tests (24 passed), application, client and enforcement suites; Windows DPAPI and TLS run in Windows CI.
<!-- SECTION:FINAL_SUMMARY:END -->
