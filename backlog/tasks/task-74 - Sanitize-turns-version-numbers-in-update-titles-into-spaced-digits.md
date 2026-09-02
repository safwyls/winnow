---
id: TASK-74
title: Sanitize turns version numbers in update titles into spaced digits
status: To Do
assignee: []
created_date: '2026-09-02 18:04'
labels: []
dependencies: []
ordinal: 101000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
ReasonSignals.Sanitize (src/Winnow.Recommend/ReasonSignals.cs, the '.', '!', '?', ';' case around line 217) replaces every sentence terminator with a space so a store-authored title cannot break the one-sentence contract a reason makes. The intent is right and the rule is too broad: a period between digits is not a sentence terminator, and version numbers are the most common thing an update title carries.

Observed in the running app against the real library on 2026-09-02, on three of six cards on one shelf at once:

  stored 'Dune: Awakening - 1.4.10.5 Hotfix Patch Notes'  rendered '1 4 10 5 Hotfix Patch Notes'
  stored 'Game Update 7.9.1b Patch Notes'                 rendered 'Game Update 7 9 1b Patch Notes'
  stored 'Patch Notes 2.03.a'                             rendered 'Patch Notes 2 03 a'

The database is innocent, every title is stored with its periods intact. The damage is done on the way to the card, and the result reads as though the app cannot handle punctuation.

The guard that motivated the rule is still needed: 'Patch 2.0. Read on!' must not become two sentences inside a quoted clause. So the fix is to narrow the rule rather than drop it. A period flanked by digits on both sides is part of a number and must survive; a period followed by whitespace or end-of-string is a terminator and should still go. Trailing letters after a numeric run are ordinary in version strings ('7.9.1b', '2.03.a') and must not defeat the test.

Found by screenshotting the running app during the TASK-71 verification pass. No test caught it because every fixture update title is prose without a version number.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A version number inside a quoted update title survives with its periods: 1.4.10.5, 7.9.1b and 2.03.a all render unchanged
- [ ] #2 A sentence terminator is still removed, so a title carrying a second sentence cannot break the one-sentence contract
- [ ] #3 Fixtures cover a title that is only a version number, one that ends in a period, and one that mixes both
<!-- AC:END -->
