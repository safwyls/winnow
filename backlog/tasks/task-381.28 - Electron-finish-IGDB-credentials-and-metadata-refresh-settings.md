---
id: TASK-381.28
title: 'Electron: finish IGDB credentials and metadata refresh settings'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:48'
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

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original runtime setting, credential protection/removal, external fallback and refresh-after-change matrices pass at the backend boundary.
- [ ] #2 Shared setup/settings forms on both surfaces show accurate masked, unavailable and configured states and preserve retry behavior without exposing secret values.
- [ ] #3 All 8 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
