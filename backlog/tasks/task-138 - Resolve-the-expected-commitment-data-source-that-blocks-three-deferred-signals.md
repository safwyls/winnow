---
id: TASK-138
title: Resolve the expected-commitment data source that blocks three deferred signals
status: To Do
assignee: []
created_date: '2026-09-06 16:20'
labels:
  - recommend
  - spike
dependencies: []
priority: medium
ordinal: 165000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
docs/recommendation-engine.md section 7 defers three items on a single unresolved dependency marked [VERIFY]: per-game expected completion time. Session-length fit, genre-conditional thresholds and the short-enough-for-tonight shelf all wait on it, so one source unblocks three items — the highest-leverage unresolved external dependency the engine has.

It also changes which question the feed answers. Today it answers "what have you forgotten", which is not the question someone has when they open a launcher on a weeknight. "What fits the time I have tonight" is, and the engine cannot answer it at all right now.

Section 7 already rejects the Steam Short tag as too sparse and too voted-on to carry the honesty a shelf requires, so the spike has to find something that can, or record that nothing does. Output is a recorded decision with evidence, not an implementation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Candidate sources for per-game expected completion time are evaluated for library coverage, licensing and terms of use, and update cadence
- [ ] #2 Coverage is measured against the real Steam, Epic and GOG rows rather than estimated, and reported per store
- [ ] #3 The finding is recorded in docs/spikes/ with the evidence behind it
- [ ] #4 The [VERIFY] marker in section 7 is resolved, either to a named source or to a recorded decision not to ship these signals
- [ ] #5 If a source is viable, the follow-up implementation work is created as its own task rather than folded into this one
<!-- AC:END -->
