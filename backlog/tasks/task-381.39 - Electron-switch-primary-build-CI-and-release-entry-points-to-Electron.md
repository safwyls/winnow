---
id: TASK-381.39
title: 'Electron: switch primary build, CI and release entry points to Electron'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:50'
updated_date: '2026-10-02 07:37'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies:
  - TASK-381.2
  - TASK-381.3
  - TASK-381.4
  - TASK-381.5
  - TASK-381.6
  - TASK-381.7
  - TASK-381.8
  - TASK-381.9
  - TASK-381.10
  - TASK-381.11
  - TASK-381.12
  - TASK-381.13
  - TASK-381.14
  - TASK-381.15
  - TASK-381.16
  - TASK-381.17
  - TASK-381.18
  - TASK-381.19
  - TASK-381.20
  - TASK-381.21
  - TASK-381.22
  - TASK-381.23
  - TASK-381.24
  - TASK-381.25
  - TASK-381.26
  - TASK-381.27
  - TASK-381.28
  - TASK-381.29
  - TASK-381.30
  - TASK-381.31
  - TASK-381.32
  - TASK-381.33
  - TASK-381.34
  - TASK-381.35
  - TASK-381.36
  - TASK-381.37
  - TASK-381.38
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
documentation:
  - src/Winnow.Electron/README.md
  - design-system.md
parent_task_id: TASK-381
priority: high
type: task
ordinal: 457000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Even after feature parity, the default run/build/release commands and required CI jobs must select the Electron frontend rather than silently shipping Avalonia.

This task owns a remaining delivery/validation gate beyond the source-method inventory. It does not reassign contracts from the feature tasks.

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Default documented run/build/package/release entry points produce Electron with the independent backend; legacy Avalonia reference code/tests cannot be mistaken for the delivered frontend.
- [ ] #2 Required Windows/Linux CI executes the appropriate Electron, backend, migration-integrity and packaging gates with accurate reusable-evidence provenance; missing evidence runs real checks.
- [x] #3 Product, architecture, roadmap, agent/build and release documentation agree with the verified final composition and platform scope; no release is published as part of this task.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
After Linux package qualification completes, make Publish.ps1 and npm package/dist select the primary Electron directory and existing Inno/ZIP/deb/tar outputs, preserving the Avalonia publisher under an explicit legacy name. Align frontend source version with Version.props. Update documented development/build commands to Electron with its independent backend. Make frontend and package workflows reusable from CI, retaining the protected Windows and Linux check names, fresh migration/audit checks and conservative existing .NET evidence reuse. Execute fresh Electron type/build, unit/live API, complete migration audit and file-preserving native shards on separate Windows runners; build Debug fixture/backend/helper companions once per runner and replace15 stale fixture references without changing assertions. Retain authoritative test inventories and reject missing, skipped or failed shard results. Route release tag/manual builds through the same qualified artifacts and retain plugin validation; no tag, merge or publication. Update product/architecture/roadmap/build/release documents together and verify workflow contracts, entry-point routing and actual package outputs. Known pre-existing full-suite regressions remain assigned41;39 must prove the gates run and fail accurately.

Preserve the documented Epic terminal sign-in command before switching primary commands. Read-only audit establishes Electron has both-surface system-browser/manual-callback UI, but lacks --epic-login and both --code forms, stdin/EOF and no-window exit behavior. Implement a minimal console/API client path in the primary composition without shipping Avalonia, retaining consent-before-browser, printed URL on shell failure, selected data root, 0/1 exit and attempt cleanup. Existing renderer callback-state validation remains strict. Add executable-level fixture tests with fake provider responses, never live authentication.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Read-only CI prerequisite audit: native suite currently1007tests/110files. Canonical existing variables are WINNOW_BACKEND_PATH, WINNOW_ACTIVATION_HELPER_PATH, WINNOW_ELECTRON_FIXTURE_PATH and WINNOW_UPDATE_HELPER_PATH, all absolute apphost paths. Fifteen older specs still hardcode task-specific fixture DLL paths; twelve rebuild in beforeAll, three assume outputs already exist. Migrate to shared prebuilt helper, build unified Fixtures plus Update.Helper once per clean runner, retain probe bundling/assertions. Native shards must use separate VMs/checkouts, one worker and file-preserving scheduling, distinct evidence artifacts and complete accounting. Existing workflow only builds normal backend and cannot establish complete native gate. No implementation started in39.

Read-only display audit: large native layout tests use nonfullscreen setContentSize and assert actual renderer sizes up to7680x1080; pinned Electron allows larger-than-screen Windows windows and Playwright uses CDP coordinates/capture. Current1024x768 CI monitor does not itself imply layout clamping. Retain one worker per interactive VM; record monitor/scale and verify requested content sizes/nativefullscreen restoration early before considering resolution mutation. Physical/compositor evidence remains separate in40.

Additional read-only release wiring audit: legacy packaging/Test-BundledPlugin.ps1 negative fixture copies root Winnow.Core.dll; primary Electron has backend/Winnow.Core.dll. Preserve this bundled-provider negative gate with the primary location when routing workflows. Existing NuGet evidence remains .NET-only; required Windows aggregate can retain its protected name while depending on fresh Electron component/native/package jobs and the original full .NET job. Use same qualified package artifacts in tag/manual release, retaining all3firstparty plugin ZIP/catalogue validation without duplicate legacy PR packaging jobs.

Read-only native output audit found no disposable DB/profile/discovery directory under Playwright outputDir; never upload .tmp wholesale. Three parity.spec.ts screenshots hardcode electron-rendered-results and should use testInfo.outputPath before shard output overrides. Current single-project/repeatEach1 built-in JSON specs.id is stable across shard/worker/root; keep project identity identical across shards. JSON also embeds stdout/stderr and attachment bodies, so retained CI reporting needs a deliberate evidence allowlist/sanitization rather than path-only exclusions.

Activated after38 milestoneb10d4d66. Root owns CI/release workflow integration, native accounting and release documentation; bounded agents own default entry scripts, Epic terminal compatibility and product/architecture documentation. Preserve all original tests. Known full-suite failures remain41;39 must demonstrate gates accurately execute/fail. No merge, tag or release publication.

Implemented primary Publish/npm package+dist, explicit reference publisher, root Build/Run and version alignment. Actual Build.ps1 succeeds; clean CI fixture/backend/helper build zero warnings/errors. Entry16Node+17PowerShell, affected components33, bundledprovider5, retainedCIpolicy53 and native/workflowaccounting37pass.15native fixtures now use common prebuilt apphosts; migrated link-action desktop/fullscreen2/2pass using fresh .39 companions. Actual Playwright collection1011tests/111files across8file-preserving shards [129,125,139,127,118,131,148,94], unioncomplete without duplicates; collection is not test execution. Epic terminal21.NET+7unit+4native pass, startup-race/cold-start checks still being completed. Required Windows aggregate now depends .NET, freshElectron+exactnativeaccounting, bothpackages andplugins; release tag/manual consumes sameartifacts. Full regression gate remains41.

Final local39 checks: actual npm primary package verified697 files; fresh package desktop/fullscreen2/2 and bundled-provider5/5 passed. Terminal27backend+29data-root+7component+4native passed without skips, including simultaneous cold start, selected-root sharing retry and caller pipe closure. Native accounting/sanitization/workflow48/48 passed; current full inventory1011/111 unchanged. Checkpoint94 records measured scope and pending final-tree CI. User batch authorization supersedes earlier per-task stop language. Commit and push next to execute integrated CI; full-suite repairs remain41.
<!-- SECTION:NOTES:END -->
