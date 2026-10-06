---
id: TASK-382.2
title: Add phone sync settings to desktop and fullscreen
status: Done
assignee:
  - '@claude'
created_date: '2026-10-06 20:45'
updated_date: '2026-10-06 21:18'
labels:
  - ui
  - sync
  - companion
dependencies:
  - TASK-382.1
parent_task_id: TASK-382
ordinal: 387000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Frontend half of TASK-382. Both presentation surfaces need the same control over phone sync.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop and fullscreen settings can turn phone sync on and off and show its current state
- [x] #2 Starting pairing shows a QR code and the code expiry; the code refreshes or closes after it expires
- [x] #3 Paired phones are listed with name and last sync, and each can be revoked
- [x] #4 UI tests cover both surfaces
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Contracts: ICompanionSettingsService in Winnow.Api.Contracts/Companion; ApiCompanionSettings client in Winnow.Api.Client over the companion routes; register in FrontendServiceRegistration.
2. Shared PhoneSyncViewModel: on/off toggle, running state and problem, addresses, paired phones with Remove, pairing code with QR payload and expiry, a timer that refreshes status while pairing is open and clears an expired window.
3. QrCodeView Avalonia control drawing Net.Codecrete.QrCodeGenerator modules, dark on white with a quiet zone.
4. Desktop: PHONE SYNC card in ApplicationSettingsView bound to ApplicationSettingsViewModel.PhoneSync.
5. Fullscreen: Phone sync action in the Application section that pushes FullscreenPhoneSyncPage over the same view model.
6. Tests: view model behaviour in Winnow.Tests; headless UI tests for both surfaces in Winnow.Ui.Tests.
7. Build Winnow.slnx with warnings as errors; run the affected suites; update docs.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop: PHONE SYNC card in ApplicationSettingsView. Fullscreen: Application > Phone sync opens FullscreenPhoneSyncPage. Both bind the shared PhoneSyncViewModel over ICompanionSettingsService (ApiCompanionSettings). BackendLiveUpdates refreshes it on companion.changed and resync. The QR code uses Net.Codecrete.QrCodeGenerator 3.2.1 (MIT, no dependencies); QRCoder was rejected because it pulls System.Drawing.Common.
Validation on Linux: Winnow.slnx builds with warnings as errors. PhoneSyncViewModelTests 6/6. PhoneSyncSettingsTests 2/2 (desktop toggle, QR, code, expiry, stop, remove; fullscreen controller path from Settings > Application through toggle, pairing, paired note and remove). CompanionTests 8/8, including the ApiCompanionSettings round trip against the real loopback backend. Backend 25, Api.Client 15 and Application 21 pass. Winnow.Tests: 116 failures, identical to the base commit on this Linux host (Windows-only process, path and Galaxy tests). Winnow.Ui.Tests: 6 DerelictOverrideComposition failures, also failing on the base commit.
Not verified: scanning the QR code with a phone camera, and a run on Windows.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added phone sync controls to desktop and fullscreen Application settings over one shared view model: the on/off switch with status and address, pairing with a QR code, a grouped code and a countdown that closes the code on expiry, a note naming a newly paired phone, and paired phones with last sync and Remove. Verified with view model tests, headless UI tests on both surfaces and a client round trip against the real backend; the specs (design-system.md, game-library-design.md §7.1) and ROADMAP are updated.
<!-- SECTION:FINAL_SUMMARY:END -->
