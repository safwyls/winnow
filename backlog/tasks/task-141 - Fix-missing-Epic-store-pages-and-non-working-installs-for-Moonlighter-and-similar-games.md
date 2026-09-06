---
id: TASK-141
title: >-
  Fix missing Epic store pages and non-working installs for Moonlighter and
  similar games
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-06 18:23'
updated_date: '2026-09-06 21:44'
labels: []
milestone: m-4
dependencies: []
priority: high
type: bug
ordinal: 800
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User reports Epic titles such as Moonlighter have no Store page and Install does not work after TASK-131/132. Diagnose actual stored identifiers and launcher dispatch evidence from copies, fix the general cause, and preserve honest omission when the storefront cannot resolve a title.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Moonlighter root causes are established from copied library and launcher data, with findings recorded.
- [x] #2 Valid Epic game identifiers reach the launcher install confirmation; invalid or unsupported routes are not offered as Install.
- [x] #3 A valid store page is available when Epic can resolve the stored identifiers, including namespace map misses, without title-specific hardcoding.
- [x] #4 Regression fixtures cover the diagnosed failures, caching and malformed or missing responses; no live API calls in tests.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Copy the relevant Winnow and Epic files to scratch; correlate Moonlighter ownership, launch key, catalog releaseInfo and recent URI logs; verify a supported identifier and store lookup route; implement the general fix and regression fixtures; validate and document observed limits.

Keep the verified install URI and document the launcher-owned failure, without claiming an installation completed. Add per-namespace GraphQL fallback for bulk-map misses, cache null/empty mapping answers, reject GraphQL errors and ambiguous/non-product-home mappings. Verify against Moonlighter catalog/API fixtures and the copied-library census.

Investigate the newly reported Store Page disappearance after Install with an actual-view pending and completed dispatch regression; preserve storefront metadata through install-state refresh.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Copied live Moonlighter entry has a complete matching namespace/catalog/artifact triple. User-action logs show successful URI dispatch, entitlement refresh and catalog resolution, then launcher alert AI-NE at 18:14:01; repeated clicks are rejected while its selector is pending, until the 600-second backstop. The precise AI-NE meaning is not established by official documentation. Existing install URI remains unchanged. Epic support recommends trying an affected game from the Epic Library and checking the owning account and pending launcher updates. The anonymous catalogNs GraphQL lookup resolves Moonlighter despite the bulk mapping miss. A copied-library census finds 8 of 11 misses resolved by this fallback, increasing measured coverage from 56/67 to 64/67; Dauntless, observer and Unreal Tournament remain unresolved.

Store-page fix and fixture verification complete: 108 focused tests passed, including exact persisted-namespace fallback, cached null/empty responses, GraphQL error stale fallback, unsafe and ambiguous mapping rejection, Moonlighter scan/persistence/URI round-trip, tile actions and architecture guards. Diff check passed. AC2 remains unchecked and task remains In Progress because the real launcher still has no verified working install confirmation. AC1 also remains unchecked because the exact launcher AI-NE cause is not established; the missing-link cause and accepted-dispatch failure boundary are documented. No launcher restart, cache deletion or install was performed.

Final integration build passed with zero warnings and errors; all 3483 main tests and 5 UI tests passed. Install confirmation remains unverified and this task stays In Progress.

Follow-up actual-view tests at four window sizes keep Moonlighter Store page visible and hit-testable during pending Install, after handoff, mouse leave and focus changes. A fifth test verifies it survives in-place Install-to-Play refresh and the command receives the new installed tile. The reported disappearance is not reproduced; asked whether closing and reopening Details restores it. Do not claim this symptom fixed from passing tests alone.

Final integration checks pass: 3496 main tests, 20 actual-view UI tests, and 185 tests after selection preservation. Store Page disappearance remains unconfirmed and this task stays In Progress; Install-to-Play completion monitoring was delivered separately as TASK-143.

User confirms Moonlighter install-location confirmation opens from Epic Library. This weakens the earlier inference that URI acceptance ruled out a handoff issue. Investigating artifact-only install addressing versus the composite launch key, using launcher logs copied to scratch.

At 21:40 UTC an artifact-only Eagle install probe resolved the same complete Moonlighter key with ownership Owned, but reported AI-AAI and opened no confirmation (user observed). Retry at 21:41 was rejected as already processing. Fresh copied committed manifest now marks Moonlighter fully installed (bIsIncompleteInstall=false), so this probe cannot compare uninstalled addressing routes and does not establish an artifact-only fix. Awaiting current Play-state confirmation; no URI code change based on this invalid comparison.

Live user verification: Moonlighter is now installed and shows Play. Enter the Gungeon was uninstalled in a fresh copied manifest set; both the existing composite route and artifact-only Garlic route opened the correct install-location selector and were canceled. Copied logs confirm selector completion accepted=false. Existing route retained; earlier transient AI-NE root cause remains unknown, so AC1 stays unchecked. Epic store/library management navigation also confirmed by user.
<!-- SECTION:NOTES:END -->
