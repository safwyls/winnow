---
id: TASK-74
title: Sanitize turns version numbers in update titles into spaced digits
status: Done
assignee:
  - '@claude'
created_date: '2026-09-02 18:04'
updated_date: '2026-09-02 20:05'
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
- [x] #1 A version number inside a quoted update title survives with its periods: 1.4.10.5, 7.9.1b and 2.03.a all render unchanged
- [x] #2 A sentence terminator is still removed, so a title carrying a second sentence cannot break the one-sentence contract
- [x] #3 Fixtures cover a title that is only a version number, one that ends in a period, and one that mixes both
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Narrow ReasonTokens.Sanitize: a '.', '!', '?' or ';' is a terminator ONLY when it is the last character of the collapsed title or is followed by whitespace. Everything else (a period inside 1.4.10.5, 7.9.1b, 2.03.a) survives untouched.
2. Why this beats a digit-dot-digit test: no version-internal period is ever followed by a space, so keying on WHAT FOLLOWS rather than WHAT FLANKS handles the trailing-letter shapes (7.9.1b, 2.03.a) for free, with no special case.
3. The one-sentence guard still holds: 'Patch 2.0. Read on!' keeps the dot in 2.0 (followed by a digit) and loses the dot after it (followed by a space) and the trailing '!', rendering 'Patch 2.0 Read on'. It matches ReasonContractTests.SentenceCount, which counts [.!?] followed by whitespace or end-of-string, so the narrowed rule and the contract test now define the terminator identically.
4. Tests in tests/Winnow.Recommend.Tests: a version-only title, a title ending in a period, a title mixing a version with a second sentence, plus the three shapes observed in the app verbatim, and a re-run of the one-sentence contract over quoted titles.
5. Delegate the XML doc rewrite on Sanitize to docs-writer.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Narrowed ReasonTokens.Sanitize (src/Winnow.Recommend/ReasonSignals.cs). A '.', '!', '?' or ';' is now a terminator only when the RUN of terminator characters it belongs to ends at whitespace or at the end of the collapsed title; everything else survives. Keying on what follows rather than what flanks is what handles the trailing-letter shapes (7.9.1b, 2.03.a) with no special case, because a period inside a version number is never followed by a space. A digit-dot-digit test gets 2.03.a wrong, since that second period sits between a digit and a letter. The run part handles doubled marks ('Hotfix!!'). Quote stripping now runs before the terminator pass so a removed quote cannot hide the whitespace after a period.

'Patch 2.0. Read on!' renders 'Patch 2.0 Read on': the period inside 2.0 survives, the one after it goes, the trailing '!' goes. That is exactly what ReasonContractTests.SentenceCount counts, so the rule and the one-sentence contract now define a terminator identically.

New tests/Winnow.Recommend.Tests/UpdateTitleSanitizeTests.cs, 20 cases: the three photographed titles verbatim, a version-only title, one ending in a period, ones mixing a version with a second sentence, terminators still removed, and a sweep of twelve release ids through ReasonBuilder asserting one sentence inside the character budget.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Narrowed ReasonTokens.Sanitize so a period inside a version number survives while a sentence terminator still goes. A '.', '!', '?' or ';' now counts as a terminator only when the contiguous run it belongs to ends at whitespace or at the end of the collapsed title. Keying on what FOLLOWS the run rather than on what flanks each character is what handles the trailing-letter shapes (7.9.1b, 2.03.a) with no special case, because a period inside a version number is never followed by a space; a digit-dot-digit test gets 2.03.a wrong, since that period sits between a digit and a letter. The run grouping handles doubled marks. Quote stripping moved ahead of the terminator pass so a removed quote cannot hide the whitespace after a period.

The narrowed rule and ReasonContractTests.SentenceCount now define a terminator identically, so what Sanitize removes is exactly what the one-sentence contract counts. 'Patch 2.0. Read on!' renders 'Patch 2.0 Read on'.

Files: src/Winnow.Recommend/ReasonSignals.cs (Sanitize, new private IsTerminator), tests/Winnow.Recommend.Tests/UpdateTitleSanitizeTests.cs (new).

Verified: dotnet test from repo root, all projects green (Winnow.Tests 2712, Winnow.Recommend.Tests 145, Winnow.Covers.Tests 70), zero warnings under TreatWarningsAsErrors. AC1 by A_version_number_keeps_its_periods, which asserts the three titles photographed on 2026-09-02 survive unchanged. AC2 by A_sentence_terminator_is_still_removed and by A_quoted_title_never_breaks_the_one_sentence_contract, which renders each title through ReasonBuilder across twelve release ids and asserts exactly one sentence inside the character budget. AC3 by Version_only_trailing_period_and_mixed_titles, covering a version-only title, a title ending in a period, and titles mixing both. Prose authored by docs-writer.
<!-- SECTION:FINAL_SUMMARY:END -->
