---
id: TASK-381.41
title: 'Electron: pass the complete migration and regression gates'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:50'
updated_date: '2026-10-02 18:32'
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
  - TASK-381.39
  - TASK-381.40
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
documentation:
  - src/Winnow.Electron/README.md
  - design-system.md
parent_task_id: TASK-381
priority: high
type: task
ordinal: 459000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Passing focused batches does not prove the final combined tree. The migration needs one reviewed set of complete checks after feature and release integration.

This task owns a remaining delivery/validation gate beyond the source-method inventory. It does not reassign contracts from the feature tasks.

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The source inventory has no pending/partial methods and every mapping has reviewed assertion scope; all Electron component/live API/native and required backend/.NET/Linux suites pass against the final tree without unexplained skips.
- [ ] #2 Complete Windows/Linux packaging smoke and required CI gates pass for the final composition; native results include retained reports and failure diagnostics rather than only counts.
- [ ] #3 Cross-surface visual/workflow smoke evidence and agreed device limitations are recorded, the parent acceptance criteria are assessed against this evidence, and a reviewable final commit is reported without publishing a release.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Preserve all integrated CI36979618488 diagnostics and account every native failure. Device implementation is complete; pending exception review is required for final acceptance, not independent regression fixes. 2. Fix bounded production faults and deterministic fixtures while preserving assertions: canonical theme roots, startup scrubber/window readiness, exact Linux parent identity, process/clock/startup fixtures, native input/layout synchronization. 3. Verify focused changes through a serialized local native/.NET lane and review all migration mapping scope. 4. Run every component/live API/native/.NET/Linux and package gate on the final combined commit and resolve every failure without unexplained skips. 5. Record agreed device limits and parent acceptance, update the draft PR and commit milestones without merging or releasing.

Repair user-reported local launcher failures: make the documented PowerShell7 requirement explicit before work begins, ensure the pinned Electron runtime is installed before invoking electron-vite, and verify version/isolated preview launch. Record this focused correction without claiming refreshed full remote CI.

Correct user-reported settings frame spacing: inspect actual Avalon screen wrapper, move consistent desktop inset ownership out of the Plugins-only rule, preserve fullscreen safe margins, then build and visually inspect all settings sections at narrow/wide desktop sizes plus fullscreen. Keep the fix scoped to layout and record both surface results.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
First clean CI frontend regression run 36961277764 at 20bceb58: 4420 passed,4 failed of4424 across231files. Preserve and resolve before final completion: themeFile canonical path equality rejects Windows runner TEMP aliases (shell.test.ts), theme-packages cleanup compares canonical child to uncanonical tmpdir and refuses two tests, parity-startup-boundaries mocked spawn hangs at5s under CI (investigate race; do not blindly raise timeout). Failure log .tmp/task38137-ci-frontend-failure.log. Clean native Windows packaged11 tests passed in38.9s independently. These findings remain open for the final regression task.

Follow-up diagnosis of CI spawn-boundary timeout: scrubStartupDiagnostic unanchored email regex rescans every suffix of a long non-email word, quadratic on bounded32KB stderr. Local isolated3-call benchmark: current pattern1231ms versus equivalent negative-local-part-boundary pattern0.29ms. Test calls diagnostic3times. Fix privacy scrubber complexity with preserved account-redaction cases before considering timeout changes. Theme failures arise from canonical vs lexical Windows TEMP path equality; normalize trusted base and compare secure containment while retaining link refusal, and canonicalize fixture cleanup root.

Second clean CI frontend run36961972740:4418passed/6failed of4424. Same four reproducible failures plus two parity-setup-settings tests finding Back to setup before accessibility/transition completion (full traces .tmp/task38137-ci-frontend-second-failure.log). Preserve assertions and diagnose asynchronous transition/focus timing; final CI should use explicit bounded worker count matching local evidence. This is not a completed full gate.

Completed clean .NET CI36961972732 found11failures: Backend FrontendActivationParityTests.Loss_of_the_real_parent... timeout10s; BackendOwnershipTests.OwnerCanSecureModifyOnlyDataDirectoryWithoutTakeOwnershipPermission unauthorized ACL (Windows runner account); Update HelperProtocolTests.RealCopiedHelperAcknowledgesHandoffAndReleasesCommandPipesWhileItsParentLives timeout30s; UI FullscreenBrowse visiblefeed1280x720 expected4 actual2; all6 DerelictOverrideComposition cases have empty grouped library; core IdentityReadInventory lacks DetailsApplication.GetMetadataAsync reader classification. Detailed log .tmp/task38137-ci-dotnet-failure.log; retained GitHub TRX artifacts must be inspected. Counts by affected assembly: Core4999/5000, UI892/899, Backend323/325, Update69/70. Linux native/Proton CI job passed. Diagnose all before final full gate; do not weaken expectations or substitute migrated UI passes for failed originals.

Read-only root-cause triage: six Derelict failures come from Sep 1 observations expiring after 30 days on Oct 2; inject a consistent fixed fixture clock, including reopen and later evidence. Fullscreen exposure test waits 150ms for a 220ms transition; use controlled frames plus committed rendering, retain exact 2-to-4 exposure assertions and separate real-frame coverage. Setup helper clicks Appearance while disabled by initial profile persistence; await enabled before one click and await provider entry. Theme roots must be canonicalized with strict child containment and link rejection retained. DetailsApplication.GetMetadataAsync fallback reads the exact editor target and belongs in DoNotResolve inventory with the same rationale as WorkMetadataEditService. Backend ACL fixture must establish current-SID ownership before applying Modify-only permissions. Activation and copied-helper failures both occur before first readiness, their PowerShell wrappers started 41ms apart; capture bounded phase/stderr/child cleanup evidence before changing implementation or deadlines. TRX downloaded to .tmp/task38141-ci-dotnet-results. No regression fixes implemented yet; task remains subsequent to package/device/default gates.

Read-only process fixture follow-up: existing tests/Winnow.Electron.Fixtures can host BCL-only parent modes before BackendApplication.Build, avoiding a new project if PowerShell bootstrap diagnostics justify replacement. Use build-only ProjectReference/GetTargetPath wiring (fixture is outside solution) without copying its backend graph into Update.Tests. Handoff parent must wait for both redirected-stream EOFs as well as child exit before acknowledging, retaining the pipe lifetime regression. Preserve current deadlines, parent PID and guard reacquisition assertions.

Linux package triage found .NET Process.StartTime UTC conversion uses a per-runtime cached boot-time estimate, so equality across helper/PowerShell runtimes is unsuitable for exact cleanup identity.38 smoke now being changed to witness exact kernel start ticks while the owned helper remains parent. Review production PortableUpdateEngine.cs parent wait (line299) and UpdateHelperCommands parent start-ticks handoff for the same Linux cross-process assumption; preserve old protocol compatibility and installation exclusion, and prove actual live parent shutdown behavior before final gate. Do not infer the smoke-only witness resolves production handoff.

Actual Ubuntu evidence now confirms the cross-runtime timestamp mismatch for the same witnessed process: helperUtcTicks639265191429937710, PowerShell observed639265191429933442; kernel birth41641 and executable are identical. See fourth Linux package artifact external-data-child-cleanup/witness JSON. Fix production Linux parent handoff by exact kernel identity, not a UTC tolerance.

First integrated CI36979618488 unitjob110750936748: 4423 passed,4 failed,1 crashed worker with5 incomplete cases out4432. Three known theme failures remain. Fourth executable-version fixture lacks electron.exe because primary workflow unintentionally omitted pinned Electron install in frontend job; restore that step (native has it). AvalonThemeStore real watcher crashed libuv fs-event.c72 !_wcsnicmp(filename,dir,dirlen), investigate lexical Windows TEMP alias before watch; preserve real watcher coverage. Source audit still zero pending/partial. Log .tmp/task38141-integrated-unit.log. No full-gate success inferred.

Integrated39 CI final.NET confirms12 failures: prior six Derelict clock cases, reader inventory, BackendACL, frontend parent loss, copied-helper protocol; new README270 rule wording and ArtworkState source-order fixture race. All27new terminal/provider cases pass. README should describe package verification rejecting localconfig rather than state contributor rule. Artwork fixture configures fake SteamGridDB before asynchronous no-sync BackendStartupService DiscoverPlugins can overwrite its sources; await exact startup completion before fixture seed, preserving policy assertions. First native shards1 and8 retain sanitized reports/screenshots under .tmp/task38141-native-first;124+85 passed,13failed,1unrun. Missing real inlinefont esbuildloader, fixed viewport setup, CSSpixel/motion assertions, slowSSE/startup-window contract, fullscreen importlabel and async Steam navigation require diagnosis/fixes. Both real platform package jobs now pass at4e09761a PRmerge125dfb9b. No complete finalgate claimed.

Complete native diagnostics retained from all eight shards of36979618488:923 passed,85 failed,3 unrun because setup failed. Both platform package jobs, plugins and Linux sessions passed. The final regression fixes now begin; no full passing gate is claimed.

Combined frontend build and fresh isolated fixture build pass. Root native regression selection: 86/87 passed in 10.4m; reproduced the sole 4K Install focus race where the test moved focus before Radix restored the closed panel opener. The helper now verifies opener restoration; final seven-case follow-up passes, including both 1280 and 3840 fullscreen and diagnostics return readiness. Earlier three focused focus/menu cases pass. All four held-feed invalidation cases pass with independent feed refresh scheduling and real SSE delivery barriers. Source audit remains2435=1688ported+706retained+41framework, zero pending/partial;25 stale rationale entries corrected without reclassification. These focused results do not replace the outstanding complete combined CI run. Metadata/controller/software-rendering verification continues.

First complete local component/live-API suite:4448passed/2failed of4450 across232files,209.69s, report .tmp/task38141-full-unit-results.json. Activity obsolete no-trigger assertion corrected to verify requested LT/RT glyphs; complete focused file37passed. Metadata failure exposed a delayed fullscreen parent focus callback after a child dialog opens, being corrected with hidden-parent guard. Independent production review found no other code defects; startup preference and updater readiness docs corrected. Available targeted controller/independent-client19passed, metadata/setup/artwork27distinct nativepassed, reference UI8passed and core inventory/docs6passed. No full final CI success claimed. Automatic approval review rejected external draft PR description update for missing explicit export authorization; prepared body retained locally.

Final combined build/typecheck and complete component/live-API suite PASS:4451/4451 across232files, zero failures/skips,200.63s at2workers. JSON .tmp/task38141-final-unit-results.json. All10 originally selected visual failure causes now have passing evidence plus3 supplementary native cases; final8/8 batch includes both Details sizes, cinematic cache/backdrop disposal, Epic text1/1.4, Activity glyph/navigation and720p plugin focus outline. Initial Details focus now respects a parent hidden by a child dialog;114 focused cases passed. Complete CI on committed composition and explicit remaining device exceptions still required.

Local milestone aaef300d records combined frontend/native repairs. Initial full .NET run7174passed/5failed/2Linux-only skips led to solution Release fixture wiring and repository traversal corrections;101 enforcement cases and3 original process checks pass. Terminal contender lifetime now waits for authenticated ownership or natural exit without killing a possible successor;31 focused terminal cases pass. Next complete Release build0warnings/errors; full .NET7182passed/1failed/2Linux-only skips across13assemblies, including Core5002,Update111,UI899 all passing. Command exits normally. Remaining failure is concurrent2 cold-start terminal sign-in, exact underlying stderr being captured; no full-gate success claimed. Automatic approval review rejected git push to safwyls/winnow:codex/electron-frontend-design and draft PR30 update for missing explicit authorization to export source/status to GitHub. Prepared commits/PR body remain local; explicit async approval is pending, with no merge/tag/release requested.

Final local combined gates PASS after diagnosing cold-start failure: direct backend pair5 captured exit2 access denied on shared .winnow-write-probe while sibling healthy. Each override now owns a GUID/CreateNew/DeleteOnClose probe; real ACL refusal, no fallback, and legacy migration remain. Original mapped refusal test uses real Windows ACL and restored Unix modes; two non-original shared-lock tests now assert preserved legacy-file bytes and concurrent alias cleanup. Review found no issues. Focused29 data-location and31 terminal cases pass. Final Release build0warnings/errors, all7183 executed .NET cases pass across13assemblies with2explicit Linux-only skips; Core5002,Backend344,Update111,UI899. Full frontend/live-API refresh against rebuilt corrected backend4451/4451,232files,0skips,201.15s. Source audit2435 complete and45migration hashes verified. Both commands exited0, final owned-backend scan0. Checkpoint96 and .tmp/task38141-complete-dotnet-evidence.json retain scope and reports. Still open: complete remote native/Windows/Linux/package CI pending authorized push, and remaining explicit device exception agreements. No release/merge/tag.

User local launch follow-up: Windows PowerShell5.1 left IsWindows undefined, omitting the executable suffix; Run/Build/test-fixture entry points now explicitly require PowerShell7 before doing work. After npm ci, Electron44.4.5 package existed but native dist and path.txt were absent; installed pinned runtime and added idempotent installer preflight to Run.ps1 and Build-ElectronTestFixtures.ps1. Updated source-run docs and corrected scan flags to omit both SeedSample and NoSync. Verification: Electron --version44.4.5, existing entrypoint16/16 tests, real PowerShell5.1 early refusal with no data created, and actual Run.ps1 -Preview launches for desktop and --fullscreen with isolated sample roots. Both displayed a Winnow window with authenticated matching-epoch backend health and closed normally, shutdown202, wrapper exit0. Logs/evidence .tmp/task38141-launcher-*. This is focused launcher verification; complete remote CI remains pending explicit publication approval.

Settings frame spacing follow-up (2026-10-02): the Avalon rule expected Settings as a direct child of the content frame, but the production screen-view wrapper prevented it from matching. Plugins alone had its own 24px inset. Applied a shared desktop Settings inset with matching left alignment and width, preserving fullscreen safe margins; documented the spacing in design-system.md.
<!-- SECTION:NOTES:END -->
