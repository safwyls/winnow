---
id: TASK-82
title: Set a reading-measure rule for prose surfaces
status: To Do
assignee: []
created_date: '2026-09-03 00:58'
labels: []
dependencies: []
priority: low
ordinal: 109000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
design-system.md section 3 tops out at Body 13/18 and section 4 sets no maximum measure, because until the Stores panel nothing had a paragraph in it. That panel used 12/18 capped at 720px, chosen in the file rather than in the system.

The merge card's 840px ceiling is a separate, measured number for a two-column comparison and does not govern prose; section 6 records why. A prose measure is still unstated.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 design-system.md states a maximum measure for prose, with the size and leading it applies to
- [ ] #2 The Stores panel's 720px cap is either ratified by the rule or changed to match it
- [ ] #3 The rule says explicitly that it does not govern the merge card, so section 6's 840px stays undisturbed
<!-- AC:END -->
