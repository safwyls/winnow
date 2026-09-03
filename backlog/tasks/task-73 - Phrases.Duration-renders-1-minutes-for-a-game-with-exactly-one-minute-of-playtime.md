---
id: TASK-73
title: >-
  Phrases.Duration renders "1 minutes" for a game with exactly one minute of
  playtime
status: Done
assignee:
  - '@safwyl'
created_date: '2026-09-02 15:21'
updated_date: '2026-09-02 17:35'
labels: []
dependencies: []
ordinal: 100000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
RecommendationScorer.Duration (exposed as Phrases.Duration, src/Winnow.Recommend/RecommendationScorer.cs) formats anything under 120 minutes as "{minutes} minutes" with no singular case, so a release with exactly one recorded minute renders "1 minutes". That string reaches the user: {minutes} is the token behind the Bounced, Sampled and ProbablyDone openings, so a card can read "1 minutes and you were done with it".

One minute of playtime is a real value in a Steam library - a game launched once and closed. Only the minutes branch is affected: the hours branches cannot produce one, because anything under 120 minutes takes the minutes branch, so hours is always at least two. Age() on the same class is already correct at every magnitude, returning "a day", "a month" and "a year" rather than a bare one, which is the pattern to follow.

Found while fixing TASK-71. A new feed variant there read "1 patches behind the current version" for the same reason - a bare-number token beside a hard-coded plural - and that one was fixed by moving the variant to {updates}, which carries its own noun. {episodes} and {stores} are bare numbers too but their resolvers gate at two, so their plural nouns always agree. Duration is the one remaining hole, and it is in the formatter rather than the copy, which is why it was left out of TASK-71 rather than folded into it.

ReasonContractTests.No_variant_puts_a_count_of_one_against_a_plural_noun guards this class of defect and currently sets playtime deliberately away from one, with a comment naming this task. When this is fixed, that exclusion should go and the test should set every count to one.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A release with exactly one minute of playtime renders a singular phrase rather than "1 minutes"
- [x] #2 The wording is authored by docs-writer and matches how Age() already handles the singular
- [x] #3 The deliberate playtime exclusion in ReasonContractTests.No_variant_puts_a_count_of_one_against_a_plural_noun is removed, so every count token is exercised at one
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. In RecommendationScorer.cs, add a minutes==1 branch to Phrases.Duration returning docs-writer-authored singular wording ("a minute"), matching Age()'s a-day/a-month/a-year pattern; update its XML summary example list.
2. In ReasonContractTests.cs, add PlaytimeMinutes = 1 to the 'one' fixture in No_variant_puts_a_count_of_one_against_a_plural_noun and delete the TASK-73 exclusion comment paragraph.
3. Add direct ScorerTests.cs coverage: Duration(1) == "a minute", and Duration(2)/Duration(119) stay plural, proving the fix without relying only on the regex-based contract test.
4. dotnet build and dotnet test (full suite) with --artifacts-path into the scratchpad build dir; confirm zero warnings/errors and all tests green.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Phrases.Duration now special-cases minutes==1 -> "a minute" (docs-writer-authored, matching Age()'s a-day/a-month/a-year pattern) before the existing "{minutes} minutes" branch; only the <120 branch can ever see a count of one, so no other branch needed a case. XML summary updated to '"a minute", "40 minutes", "5.2 hours", "33 hours".'. ReasonContractTests.No_variant_puts_a_count_of_one_against_a_plural_noun now sets PlaytimeMinutes = 1 on its fixture (removed the TASK-73 exclusion comment). Added ScorerTests.Duration_renders_one_minute_as_singular_not_a_bare_count_of_one and Duration_renders_every_other_minute_count_under_the_hours_branch_as_plural as direct unit coverage. Verified: dotnet build (0 warnings/errors) and dotnet test from repo root, both pointed at the scratchpad --artifacts-path -- full suite green (Winnow.Tests 2664, Winnow.Recommend.Tests 115, Winnow.Covers.Tests 70, all passed); targeted filter on the two TASK-73 tests plus the contract test also passed (3/3).
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed the singular-count defect in Phrases.Duration (src/Winnow.Recommend/RecommendationScorer.cs): minutes==1 now renders "a minute" instead of "1 minutes", following the same pattern Age() already uses for a-day/a-month/a-year (only the <120-minute branch can ever hit a count of one). Wording and the updated XML doc comment were authored by docs-writer, matched against every Bounced/Sampled/ProbablyDone sentence template that carries {minutes}. Removed the deliberate playtime-away-from-one exclusion and its TASK-73 comment in ReasonContractTests.No_variant_puts_a_count_of_one_against_a_plural_noun (now sets PlaytimeMinutes = 1), and added direct ScorerTests coverage for Duration(1)/(2)/(119). Verified via dotnet build and the full dotnet test suite from repo root (--artifacts-path into the scratchpad build dir): 0 warnings/errors, 2664+115+70 tests all passing, including the three TASK-73-relevant tests run in isolation.
<!-- SECTION:FINAL_SUMMARY:END -->
