---
id: TASK-73
title: >-
  Phrases.Duration renders "1 minutes" for a game with exactly one minute of
  playtime
status: To Do
assignee: []
created_date: '2026-09-02 15:21'
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
- [ ] #1 A release with exactly one minute of playtime renders a singular phrase rather than "1 minutes"
- [ ] #2 The wording is authored by docs-writer and matches how Age() already handles the singular
- [ ] #3 The deliberate playtime exclusion in ReasonContractTests.No_variant_puts_a_count_of_one_against_a_plural_noun is removed, so every count token is exercised at one
<!-- AC:END -->
