---
id: TASK-49
title: Evaluate GOG session-history evidence before scheduling sign-in
status: To Do
assignee: []
created_date: '2026-08-29 21:55'
updated_date: '2026-09-11 14:07'
labels:
  - auth
  - ingest
dependencies: []
documentation:
  - game-library-design.md
  - docs/spikes/epic-gog-local-files.md
priority: low
type: spike
ordinal: 99000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
GOG integration currently reads Galaxy and registry data without sign-in. Evaluate whether an authorized response from the previously observed gameplay.gog.com sessions route provides additional dated sessions worth importing. The existing probe does not establish a useful authenticated payload or its current availability. Keep sign-in deferred until evidence justifies its added credential and UI surface.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Probe with an authorized account/session or inspect a supplied response; document endpoint, response shape, timestamps, pagination and account identity using sanitized evidence.
- [ ] #2 Distinguish authentication, availability and transport failures from a verified empty or non-useful history response.
- [ ] #3 Compare usable history with Galaxy's local facts and recommend a defined integration scope only if it adds meaningful evidence; otherwise record the observed limitation.
- [ ] #4 Update current GOG scope and dated evidence directly. If useful history justifies sign-in, create a separately scoped implementation task covering desktop and fullscreen; do not implement sign-in in this research task.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: src/Winnow.Ingest.Gog/ServiceCollectionExtensions.cs registers only local discovery with no credentials. GogLibrarySource composes Galaxy and registry facts. The old task's blanket assertions about the remote service were stronger than the dated probe supports; research remains useful and unverified.
<!-- SECTION:NOTES:END -->
