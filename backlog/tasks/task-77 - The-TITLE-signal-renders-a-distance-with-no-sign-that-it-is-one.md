---
id: TASK-77
title: The TITLE signal renders a distance with no sign that it is one
status: In Progress
assignee:
  - '@safwyl'
created_date: '2026-09-02 19:39'
updated_date: '2026-09-02 19:47'
labels: []
dependencies: []
ordinal: 104000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
On a review card the signal column reads:

  CONFIDENCE 0.97 STRONG MATCH
  TITLE     0.00        'torchlight' vs 'torchlight' (100 % similar)
  YEAR      delta-0     2009 vs 2009
  PUBLISHER SAME        both 'encore'

TITLE 0.00 sits directly above evidence saying the titles are 100 percent similar, so the number reads as a score of zero contradicting its own explanation.

The value is not wrong. MergeSignalViewModel.cs line 69 renders Number(1.0 - payload.TitleSimilarity), a DISTANCE, so identical titles correctly give 0.00. The defect is that nothing says so. YEAR marks itself a delta with a sign, PUBLISHER and EDITION render words, COVER renders x/64, and TITLE alone renders a bare decimal whose direction the reader has to infer, against an adjacent line pointing the other way.

Pick one reading and make it legible: mark it as a delta the way YEAR does, or show the similarity the evidence line already speaks in. Whichever is chosen, a reader should not have to know which direction the number runs.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The TITLE value cannot be read as contradicting its own evidence line: either it is marked as a delta or it states the similarity directly
- [ ] #2 The choice is consistent with how YEAR, PUBLISHER, COVER and EDITION present themselves in the same column
- [ ] #3 A strong match and a weak match are distinguishable at a glance from the TITLE row alone
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Confirm design-system.md \u00a76 already names TITLE a 'distance' paired with 'year delta' -- the spec intends TITLE and YEAR to be symmetric delta signals.
2. In MergeSignalViewModel.cs, prefix the TITLE row's existing distance value (1 - TitleSimilarity) with the same U+0394 marker YEAR uses (\"\u03941\"), producing \"\u03940.00\"/\"\u03940.27\" etc. Do not switch to restating the similarity percentage -- the Detail sentence already states that, so restating it would be redundant rather than clarifying (per task guidance). Confined to MergeSignalViewModel.cs.
3. Add/extend tests in MergeQueueViewModelTests.cs: assert the TITLE row on the existing strong-match fixture (identical Witcher core title) reads \"\u03940.00\", and add a new fixture pair with a related-but-distinct title (fires the signal, clears the queue floor, similarity ~0.73) whose TITLE row reads \"\u03940.27\" -- proving AC#3 (strong vs weak distinguishable from the TITLE row alone) and AC#1/AC#2 (marked as a delta, consistent with YEAR's own \u0394 marking).
4. Build with TreatWarningsAsErrors and run the full test suite via --artifacts-path into the scratchpad build dir.
5. Delegate any non-code text (comments already added inline) review to docs-writer; confirm phrasing before finalizing.
<!-- SECTION:PLAN:END -->
