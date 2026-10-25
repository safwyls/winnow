---
id: TASK-49
title: Evaluate GOG session-history evidence before scheduling sign-in
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:55'
updated_date: '2026-09-13 00:43'
labels:
  - auth
  - ingest
dependencies: []
documentation:
  - game-library-design.md
  - docs/spikes/epic-gog-local-files.md
priority: low
type: spike
ordinal: 114000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
GOG integration currently reads Galaxy and registry data without sign-in. Evaluate whether an authorized response from the previously observed gameplay.gog.com sessions route provides additional dated sessions worth importing. The existing probe does not establish a useful authenticated payload or its current availability. Keep sign-in deferred until evidence justifies its added credential and UI surface.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Probe with an authorized account/session or inspect a supplied response; document endpoint, response shape, timestamps, pagination and account identity using sanitized evidence.
- [x] #2 Distinguish authentication, availability and transport failures from a verified empty or non-useful history response.
- [x] #3 Compare usable history with Galaxy's local facts and recommend a defined integration scope only if it adds meaningful evidence; otherwise record the observed limitation.
- [x] #4 Update current GOG scope and dated evidence directly. If useful history justifies sign-in, create a separately scoped implementation task covering desktop and fullscreen; do not implement sign-in in this research task.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify account-bound API authorization. 2. Probe owned-game session history and classify results. 3. Compare local facts and document evidence and scope.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: src/Winnow.Ingest.Gog/ServiceCollectionExtensions.cs registers only local discovery with no credentials. GogLibrarySource composes Galaxy and registry facts. The old task's blanket assertions about the remote service were stronger than the dated probe supports; research remains useful and unverified.

2026-09-11 execution: requested a local path to a supplied authenticated GOG session-history response. No response or authorized session has been supplied during this run. No live session endpoint was probed and no credentials were extracted from Galaxy. Authentication/availability and incremental dated-session coverage remain unknown, not a verified empty history. Sign-in remains deferred and all acceptance criteria remain open.

2026-09-12 evidence review: verified GalaxyLibraryReader joins GameTimes and LastPlayedDates by release and userId; GogLibrarySource carries the winning account's minutes and last-played together. embedded-auth.md's historical GET 401 and other-method 405 observations do not establish an authenticated GET payload; the cited POST session_date/time fields cannot establish GET schema, timestamp semantics, pagination, retention or present availability. Requested a supplied response path or an identified authorized session. Until supplied, incremental dated-session coverage cannot be evaluated. No live endpoint request or credential access performed. Sign-in remains deferred on desktop and fullscreen; no application behavior changed. TASK-49 remains open pending authenticated evidence.

2026-09-13 UTC authorized probe: user completed GOG OAuth; token exchange and account library control returned 200. All 45 GOG-owned releases returned 200 with only time_sum (43 zero, two nonzero: 50 and 54). Unauthenticated control returned 401; authenticated repeat returned the same nonzero aggregate. No dates, session list or pagination metadata observed; units and other response variants remain unverified. Sanitized JSON validation passed for counts, response fields and absence of credentials/account ID; git diff --check passed. Compared response fields with GalaxyLibraryReader and GogLibrarySource, without reading live launcher files. Desktop and fullscreen retain local discovery; no UI behavior changed or sign-in task warranted. Updated dated evidence, build spec, roadmap and older probe wording. No build tests needed for documentation-only work.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Authorized sessions endpoint is available but yielded only aggregates across 45 GOG-owned releases, including two nonzero responses. No additional dated sessions demonstrated. Keep sign-in deferred on desktop and fullscreen. Evidence: docs/spikes/gog-session-history.md and gog-sessions-2026-09-13.json; live controls and sanitized artifact assertions passed.
<!-- SECTION:FINAL_SUMMARY:END -->
