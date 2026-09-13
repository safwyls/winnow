---
id: TASK-159
title: Extend Winnow updating to portable Windows and Linux with recovery
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 04:59'
updated_date: '2026-09-13 05:55'
labels:
  - app-updates
dependencies:
  - TASK-157
  - TASK-158
references:
  - packaging
documentation:
  - docs/releases.md
priority: low
type: feature
ordinal: 191000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Extend the existing release checks and installed-Windows update flow to supported portable Windows and Linux distributions with tested recovery. Current portable Windows, Debian and portable Linux builds offer release links and manual update routes. Preserve those routes where automated updating is unsupported, and preserve the package manager boundary for managed Linux installations. Packaging and recovery design are execution-time decisions.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Portable Windows users can download, verify, apply, and restart into an update while preserving the portable location and selected data directory.
- [x] #2 Supported Linux distribution formats have a documented and tested update route; package-managed installations use their package-management boundary and any required authentication is explicit.
- [x] #3 Existing Windows and Linux distribution users have a documented transition path if packaging formats or installation layout change; unsupported environments retain useful download links.
- [x] #4 Interrupted replacement, insufficient permissions or disk space, verification failure, and failed new-version startup have tested recovery behavior without silently discarding user data.
- [x] #5 Recovery defines database migration compatibility and backup or restore behavior; an older binary is never automatically reopened against an incompatible migrated database.
- [x] #6 Release CI tests actual older-to-newer upgrades and recovery on supported Windows and Linux environments using disposable data; documentation specifies the support matrix and recovery limits.
- [x] #7 Desktop and fullscreen share update preferences, progress, failure/recovery state and explicit restart behavior; verify both presentation paths for the supported installation types.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Retain ZIP, tar.gz and Debian layouts. Bundle a separate self-contained portable helper for Windows x64 and Ubuntu 24.04 x64; registered Windows keeps Inno and managed Linux keeps its package manager. Stage verified archives beside the installation and preserve selected internal/external data. After explicit restart, wait for the exact parent process and installation/library leases, create a checked pre-migration SQLite backup, and durably journal replacement while retaining previous binaries. Record possible migration before database access and require readiness after migrations, host and framework initialization. Recover interrupted replacement before migration; require explicit paired database restore after migration may have started, or same/newer reinstall followed by resume. Share progress, recovery state and an Update and restart action across the title bar, desktop settings and fullscreen. Verify local engine/UI/integration tests and disposable published older-to-newer Windows/Ubuntu release-runner tests; retain measured evidence and update release/architecture documentation. User approval recorded on 2026-09-12; local implementation and checks completed, platform release-runner evidence remains pending.

Release-runner follow-up: fix Linux archive name comparison with packaged-layout regressions, investigate Windows portable smoke completion, then rerun both platform release jobs and record observed evidence.

Installed Windows runner follow-up: tolerate brief sharing violations after exact parent exit with a bounded, cancellation-aware retry; retain refusal for persistent locks and validate under Windows PowerShell before rerunning release smoke.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: ApplicationUpdater gates staging on IUpdateInstaller.IsSupported; WindowsUpdateInstaller supports only a matching registered Windows installation. docs/releases.md documents manual portable/Linux routes and no automatic rollback. TASK-158 supplies the completed installed-Windows baseline; this task's recovery scope remains unimplemented.

Implementation started with user approval. Portable engine/helper and presentation work delegated; coordinator owns startup/installer integration, packaging and final verification.

Implemented portable staging and independently packaged helper; verified digest/metadata/path checks, SQLite WAL backup after process shutdown, durable replacement journal, explicit paired restore and same/newer resume. App startup gates before data access and acknowledges readiness after host/Avalonia initialization. Desktop title-bar and fullscreen header/quick-menu/settings share Update and restart, progress and persistent recovery notice. Release build passed with zero warnings; focused Release tests: 33 engine + 34 updater + 31 UI/installation = 98 passed. Real app/helper transaction handshake and internal-data replacement passed against disposable built copies. 42 migration hashes verified. Detailed measured evidence: docs/spikes/portable-update-recovery.md. Full isolated suite: 5758 passed, 3 failed, 2 Linux-only skipped; activity paging passed rerun, existing PluginSettingsView accessibility-name and fullscreen platform chevron expectations remain failing outside changed behavior. Actual published older-to-newer Windows/Ubuntu CI scripts and evidence retention are implemented but not executed locally. Keep TASK-159 In Progress with AC2/4/6 pending platform runner evidence rather than claiming Ubuntu or release upgrade success.

2026-09-13 release failure repair: Ubuntu staging rejected distinct Winnow/winnow filenames because archive duplicate detection was case-insensitive. Use platform comparison and add ZIP/tar case-distinct and exact-duplicate regressions; 37 engine tests pass on Windows. Windows smoke invocation also waited for persistent child output EOF; reproduced 5.50-second delay with a five-second child. Process-based helper wait now returns in 0.508 seconds while a 15-second child stays alive, with five-minute timeout and unchanged exit-code assertions. PowerShell parse and diff checks passed. Shared engine applies to desktop/fullscreen; presentation unchanged. Cancelled obsolete stalled release runs; rerun platform evidence pending.

First repaired Ubuntu run passed archive staging and reached post-apply journal inspection, then exposed PowerShell ConvertFrom-Json rejecting case-distinct PayloadHashes keys. All smoke journal reads now use AsHashtable; case-distinct payload hashes and phase mutation survive a verified JSON roundtrip.

Ubuntu release job 103680250659 at 382ec6c passed 37 engine tests, package/startup checks and all four portable upgrade/recovery scenarios from beta.6. Windows jobs instead exposed a latent installed-helper timing failure on Avalonia DLL exclusive-open after parent exit; no changes to installed helper or smoke occurred since earlier passing runs. Investigating bounded sharing retry without weakening persistent-lock refusal.

Installed helper now retries only Windows sharing/lock violations (32/33) for a total five-second window after parent exit, checks cancellation each attempt, and immediately propagates other failures. Four standalone regression checks passed under production Windows PowerShell 5.1: writable files, transient release, persistent-lock refusal, cancellation during wait. Smoke invokes these checks before installer work. No installer was run locally.

Release run 34741215637 passed Windows 2025 x64 and Ubuntu 24.04 x64 at 872efa4. Each platform passed 37 engine tests, packaging and installed smoke, and all four actual portable scenarios (external/internal data, failed startup with explicit paired restore, interrupted replacement), beta.6 to ci.65. Windows additionally passed four PS5.1 lock checks and bad-digest/cancel/shutdown-timeout/persistent-lock/successful installed upgrade scenarios. Artifacts retain journals/TRX/baseline evidence. AC2/4/6 now verified; no hardware-power-loss claim. Unrelated full-suite failures remain outside this task.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Portable Windows and Ubuntu updates and recovery verified on published older-to-newer runner fixtures. Both platform release jobs passed at 872efa4; shared desktop/fullscreen verification and local engine/UI results are recorded in docs/spikes/portable-update-recovery.md. Fixed Linux archive and journal casing, smoke process waiting, and transient installed-Windows sharing locks. Existing unrelated full-suite failures remain documented.
<!-- SECTION:FINAL_SUMMARY:END -->
