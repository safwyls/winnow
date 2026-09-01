---
id: TASK-9
title: Restore one-sentence explanation contract for feed cards
status: In Progress
assignee:
  - '@claude'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-01 02:14'
labels:
  - recommend
  - ui
dependencies:
  - TASK-8
priority: high
ordinal: 36000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Feed cards must state their recommendation reason in one sentence. The contract is currently broken. Finding F37. Source: stabilization-2026-08-28.md Group 2. Trigger: when explanation copy is next edited.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every feed card carries a one-sentence explanation
- [ ] #2 No explanation exceeds one sentence
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Structured signals: new ReasonSignal enum (NeverOpened, LaunchedUnmeasured, Sampled, Bounced, PatchedSinceYouLeft, Dormant, UndatedDormancy, TriedToLikeIt, TasteMatch, Installed, BoughtTwice, ProbablyDone, OnlineOnlyMismatch, SoloOnlyMismatch, PlayedRecently, ShownRecently, Rotation), a ReasonEvidence record carrying the actual numbers (minutes, last-played year, dormancy age, update count and latest update title, return episodes, store count and store, taste facet, release id), and RecommendationReason { Primary, Secondary, Evidence }.
2. RecommendationScorer.Explain() selects Primary and Secondary from the same contributions that made the score. Honesty rule inside one sentence: probably-done takes Primary when it fired; mode mismatch takes Secondary. Otherwise Primary = patch, else the commitment variant; Secondary = strongest of tried-to-like-it / taste / bought-twice / installed / dormancy not already implied.
3. ReasonBuilder renders exactly ONE sentence: primary clause + secondary clause (the secondary carries its own joiner) + terminator, inside a named character budget (ReasonCharacterBudget, 180). Over budget drops the secondary. Update titles are sanitised (whitespace collapsed, terminal punctuation stripped, truncated) so a quoted title cannot break the one-sentence contract.
4. Variation: ReasonPhrasebook holds several clause variants per signal; the variant is chosen by a deterministic hash of the release id and the signal, so the feed is stable across reloads but two cards in one session do not read as siblings. Variants whose tokens do not resolve for a given game are filtered out before the pick; every signal must carry at least one token-free variant (asserted).
5. The copy itself is delegated to docs-writer with the full signal inventory, what each means in the data, the one-sentence + budget constraint, and the user's cookie-cutter critique.
6. Tests: one-sentence contract and budget over every producible signal combination; anti-sameness (candidates with genuinely different histories produce distinct sentences AND distinct skeletons after digits/titles/facets are masked); honesty (probably-done and mode-mismatch still say so).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
User critique, 2026-08-31: the recommendation reasons all sound alike and read as cookie-cutter, generated filler. This is a distinct problem from the one-sentence contract F37 records, and pulling this task forward to run beside TASK-8 is a direct response to it. Length is not the complaint; sameness is. Two causes are worth separating during the work. The builder concatenates fixed fragments, so every card is assembled from the same small vocabulary in the same order, and the resulting shape is recognisable after a handful of cards. And the fragments are written to be true of any game rather than of this one, so a specific and unusual history renders in the same words as an ordinary one. The fix direction the review already records is to return structured primary and secondary signals and render one bounded sentence from them, which is the seam that makes varied and specific phrasing possible at all; the phrasing itself is prose and belongs to docs-writer. A reason should sound like it was written about the game in front of the user, naming what actually happened to it, and two cards in one session should not read as siblings.

Plan recorded 2026-08-31. Running alongside TASK-8 because both restructure RecommendationScorer/RecommendationEngine output.

Implemented 2026-08-31, not finalized.

Structure (mine). New ReasonSignals.cs: ReasonSignal enum (17 members - six openings, ten supports, None), ReasonEvidence (release id, title, store, minutes, last-played year, dormancy days, updates-since and latest update title, return episodes, store count, taste facet), and RecommendationReason { Primary, Secondary, Evidence }. RecommendationScorer.Explain builds it from the same contributions that made the score, so a sentence cannot cite a figure the ranking never saw. Exposed on Recommendation.Explanation beside the rendered Reason, so a caller can render its own without parsing prose.

Honesty rules moved into SELECTION rather than into extra sentences. Primary precedence: probably-done (the feed must be able to say 'you were right to drop this'), then the patched bucket, then the commitment shape. Secondary precedence: mode mismatch, then fresh play (both demotions whose effect the user can see), then the strongest supporting fact the opening did not tell - tried-to-like-it, taste, bought-twice, installed, dormancy, shown-recently. Dormancy sits last, and when it IS the secondary the builder forbids the opening from spending {year} or {age}, because 'you put 5 hours in back in 2019, untouched for seven years' is one fact told twice.

Rendering (mine). ReasonBuilder now emits exactly one sentence: primary clause + secondary clause (the secondary carries its own joiner) + one terminator, inside ReasonCharacterBudget (180, sized above the longest producible pair so an honesty clause is never truncated away). Update titles are sanitised - whitespace collapsed, quotes and sentence terminators stripped, capped at 48 chars - so a store-authored headline cannot break the contract. Variant selection: filter to variants whose tokens this game can actually fill, prefer token-bearing variants over token-free ones, then pick by SplitMix64 over (release id, signal, clause). Stable across reloads and across shuffle seeds; varies between neighbouring cards. Primary is capitalised AFTER substitution so a template may open on a token.

Copy (docs-writer, two passes). ReasonPhrasebook.cs holds 3-6 variants per signal per clause. Second pass fixed a real clause-attachment bug the render surfaced: two secondaries opened on a bare relative pronoun (', which you own {stores} times over'), which attached to the primary's final verb. Both reworded as participial fragments.

How different histories now read (real render, one feed):
  patched, multi-update:  You have not seen "Reforged Eden", which arrived after you left, and nobody has opened it in 4 years.
  patched, one update:    An update landed here since you last played, and nobody has opened it in 3 years.
  bounced + returns:      4.3 hours of yours went into this before you drifted off, spread over 5 sittings rather than one.
  bounced + two stores:   Something held your attention for 6.7 hours, then stopped, owned 2 times over, probably from a bundle.
  bounced, undated:       Something held your attention for 12 hours, then stopped, old enough that Steam never recorded when.
  bounced, deep dormancy: 15 hours in, well past the refund line, then nothing, quiet for 5 years now.
  sampled:                A brief look, 22 minutes, and nothing after, untouched for 4 years.
  launched, unmeasured:   There is a launch date here and no playtime at all, which is unusual, and nobody has opened it in a year.
  never opened, installed:This has been waiting since you bought it, and nothing needs downloading first.
  probably done:          43 hours was your answer 7 years ago, and nothing since has argued with it.
  played two days ago:    Something held your attention for 10 hours, then stopped, though 2 days is no time at all to have been away.

Tests (tests/Winnow.Recommend.Tests/ReasonContractTests.cs, 6): exhaustive sweep of 6 primaries x 11 secondaries x 5 evidence shapes asserting exactly one terminator (counted outside quoted spans), inside the budget, no unfilled tokens; phrasebook shape rules including a token-free fallback per list; the patched card always names what landed; ANTI-SAMENESS - ten genuinely different histories must produce ten distinct sentences AND at least eight distinct SKELETONS after digits, quoted titles and proper nouns are masked, which is the 'same frame with substituted nouns' check; twelve identical never-opened games must produce at least three distinct phrasings; and the same game reads identically across reloads and across shuffle seeds. Existing reason assertions were retargeted from fixed strings onto Explanation structure, since pinning one phrasing is exactly what forbade variation.

Known residual: never-opened has five phrasings and the hash is uniform over them (measured 68/91/81/80/80 across 400 release ids), but ten cards drawn from five buckets will sometimes show only three distinct lines, as the render above does. Three is a large improvement on one and the test floors it at three; more variants is the lever if it still reads repetitive on the real library.

Winnow.Recommend.Tests 92/92 green. Full suite: Winnow.Covers.Tests 70/70, Winnow.Tests 2369/2370 with the one failure belonging to TASK-5's migration 0016 (see TASK-8 notes).
<!-- SECTION:NOTES:END -->
