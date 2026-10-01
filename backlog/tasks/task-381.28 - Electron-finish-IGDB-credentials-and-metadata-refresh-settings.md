---
id: TASK-381.28
title: 'Electron: finish IGDB credentials and metadata refresh settings'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:48'
updated_date: '2026-10-01 21:03'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies: []
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
documentation:
  - src/Winnow.Electron/README.md
  - design-system.md
parent_task_id: TASK-381
priority: high
type: task
ordinal: 446000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Credential changes affect queued enrichment and can expose stale state or overwrite externally configured credentials if the frontend boundary differs.

Owns 8 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/CredentialMetadataRefreshTests.cs
- CredentialMetadataRefreshTests.ChangesWaitForStartupAndCoalesceIntoOnePass [pending at split]
- CredentialMetadataRefreshTests.ChangesDuringPassQueueOneFollowupAndDoNotOverlap [pending at split]
- CredentialMetadataRefreshTests.FailedPassDoesNotPreventLaterRefresh [pending at split]
- CredentialMetadataRefreshTests.ShutdownDiscardsRefreshWaitingForStartup [pending at split]

tests/Winnow.Tests/Igdb/IgdbRuntimeSettingsTests.cs
- IgdbRuntimeSettingsTests.Saving_activates_cached_absence_and_rotating_same_client_replaces_token [pending at split]
- IgdbRuntimeSettingsTests.Removal_immediately_uses_configuration_fallback_or_disables_auth [pending at split]
- IgdbRuntimeSettingsTests.Failed_transaction_keeps_runtime_credentials_token_and_notifications_unchanged [pending at split]
- IgdbRuntimeSettingsTests.Removal_waits_for_inflight_token_persistence_then_clears_it [pending at split]

Batch boundary: the user authorized TASK-381.21 through TASK-381.30 in order. Keep one implementation task active, verify and commit each milestone, then continue. Pause for review after TASK-381.30.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original runtime setting, credential protection/removal, external fallback and refresh-after-change matrices pass at the backend boundary.
- [x] #2 Shared setup/settings forms on both surfaces show accurate masked, unavailable and configured states and preserve retry behavior without exposing secret values.
- [x] #3 All 8 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Continue within the authorized batch through TASK-381.30, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Execute the unchanged eight source methods/nine cases, preserving startup/coalescing/failure/cancellation and token-persistence gates. 2. Verify real HTTP settings through the warm production token-provider/updater singleton: cached absence, same-client rotation and removal with/without configuration fallback; retain original concurrency proofs. 3. Reuse the existing38 settings component cases and3 isolated native desktop/fullscreen cases, with explicit prebuilt hosts and no overlap with .NET. 4. Review shared implementation and exact IGDB audit paths, record eight justified retained-backend dispositions and checkpoint83, commit and continue to TASK-381.29.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after TASK-381.27 implementation83ecbbf5/completion28137610. Read-only preparation .tmp/task38128-prep.md identifies no production defect; existing HTTP storage tests do not warm the runtime token cache. Root reviewed TwitchTokenProvider, ChainedIgdbCredentialProvider, CredentialSources, SqliteSettingsStore and IGDB ServiceCollectionExtensions before any audit allowance. No-sync native hosts establish form/API behavior, not execution of the automatic metadata queue.

Verified nine unchanged original cases, fifteen HTTP cases (three new warm-runtime cases), 38 component cases and three distinct native journeys. Desktop passed initial run; fullscreen normal/140% text passed final4 after correcting the test for documented0.85 density and Chromium pixel quantization. No production change. Checkpoint83 records precise no-sync, protection-fixture and physical-device limits. Audit:1606 ported,696 retained,35 framework,80 pending,18 partial.

Milestone commit ff2396a6. Verified native evidence and reviewed desktop/fullscreen captures; continue with TASK-381.29 under the authorized batch.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Retained all eight runtime credential/refresh contracts with original concurrency gates and added real HTTP singleton rotation/removal coverage. Shared setup/settings forms pass on desktop/fullscreen, including masked drafts, retry and focus. Evidence: docs/spikes/2026-09-28-electron-parity/checkpoint-eighty-three.md. Continue with TASK-381.29 after the milestone commit.
<!-- SECTION:FINAL_SUMMARY:END -->
