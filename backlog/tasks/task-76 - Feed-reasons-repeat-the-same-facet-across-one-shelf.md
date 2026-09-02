---
id: TASK-76
title: Feed reasons repeat the same facet across one shelf
status: Done
assignee:
  - '@claude'
created_date: '2026-09-02 19:39'
updated_date: '2026-09-02 20:07'
labels: []
dependencies: []
ordinal: 103000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
TASK-71 stopped two cards on a shelf rendering the same phrasing VARIANT. The underlying FACT still repeats, and at a glance it reads almost as templated as the thing that was fixed.

Observed in the running app on 2026-09-02, Patched while you were away, six cards, real library:

  Stormworks    '...landing in Sandbox, a kind of game you keep coming back to.'
  Stationeers   '...and you have real hours in Sandbox games.'
  Project Gorgon '...and Sandbox is one of your deepest piles.'

Three of six name the same facet. Every sentence is true and the variants are all different, which is why the TASK-71 ledger did not catch it: it tracks which variant was used, not which fact the variant cites.

Sandbox dominating is not itself wrong, it is the users deepest genre, and suppressing the true fact to manufacture variety would be worse than the repetition. The fix is to vary WHICH supporting fact a card reaches for when the strongest one is already spent on this shelf, and to fall back to saying less rather than repeating.

Same class as TASK-58 and TASK-71: each fixed one layer of sameness and exposed the next one down.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 No shelf cites the same facet in more than a set number of cards, with the threshold stated in code rather than implied
- [x] #2 A card whose strongest supporting fact is already spent on the shelf reaches for its next-strongest, or says less, rather than repeating
- [x] #3 No card asserts something untrue to achieve variety; every claim still traces to the evidence that card actually has
- [x] #4 Determinism per feed build is preserved, as it was for the variant ledger
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Diagnose: the ledger from TASK-71 keys on (signal, clause, variant), so three cards drawing three DIFFERENT TasteMatch variants that all name Sandbox each claim a free variant and the shelf still says Sandbox three times. The repeating unit is the cited fact, not the phrasing.
2. Decide where it lives: extend the TASK-71 ledger rather than add a second one. Both are one shelf's memory with one lifetime and one determinism contract, and the fact check has to run in the same pass that claims a variant or a rejected clause burns a phrasing. Rename ReasonVariantLedger to ShelfReasonLedger, which now answers two questions: which phrasing is spent, and which fact is spent.
3. Give a card somewhere else to go. RecommendationScorer.SecondarySignal is already a strict strongest-first cascade that returns the first supporting fact that fired; turn it into SupportingSignals, the ordered list of every supporting fact that fired for that card, and set Secondary to its head so nothing changes when nothing is spent. Every entry is a fact the scorer proved about THAT card, so reaching past the head is never a new claim.
4. ReasonBuilder walks that list: skip a fact whose citation is already spent on this shelf, render the first one that is admitted and fits the budget, and render the primary alone when none is. Saying less is the designed floor, never a substituted claim.
5. Citation key: TasteMatch cites the descriptor name, so Sandbox and Roguelike are different citations and two Sandbox cards are the same one. Every other supporting fact cites its own signal.
6. Exempt the demotion disclosures (OnlineOnlyMismatch, SoloOnlyMismatch, PlayedRecently, ShownRecently). Those clauses exist to tell the user why a card ranks where it does; suppressing one for variety would hide ranking information, and they name no facet so AC 1 is untouched.
7. Cap: new RecommendationTuning.ShelfFactCitationCap, default 2 against a MaxPerShelf of 6. Two cards making the same claim reads as coincidence, three reads as a template, and three of six is exactly what was photographed.
8. Determinism: the cap is counted in shelf fill order, which is already fixed (score, then release id), the same guarantee the variant ledger gives.
9. Tests: the Sandbox shelf end to end, the next-strongest reach, the say-less floor, an honesty sweep asserting no card claims a fact its own evidence does not carry, and a same-shelf-twice determinism check.
10. Delegate all comments and XML docs to docs-writer.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Extended the TASK-71 ledger rather than adding a second one; renamed ReasonVariantLedger to ShelfReasonLedger (src/Winnow.Recommend/ShelfReasonLedger.cs) because it now answers two questions: which phrasing is spent, and which fact is spent. The fact check has to run in the same pass that claims a variant, or a rejected clause burns a phrasing.

RecommendationScorer.SecondarySignal became SupportingSignals: the same strongest-first cascade, but it now returns every supporting fact that fired instead of only the first. RecommendationReason.Secondary is still its head, so nothing changes when nothing is spent. ReasonBuilder.ChooseSupporting walks that list, skips a fact the surface has already spent, renders the first admitted, and renders the primary alone when none is left. Every entry fired for that card, so reaching past the head is never a new claim.

Citation unit: the taste clause is keyed on the descriptor NAME (Sandbox and Roguelike are two claims); every other supporting fact on its signal. Demotion disclosures (mode mismatch, played recently, shown recently) are exempt and never withheld, since withholding one would hide ranking information.

Cap derived per surface: FactCitationCards=3 (one card in three) with FactCitationFloor=2, both on RecommendationTuning. A shelf of 6 caps at 2; the flat feed of 20 caps at 6. A flat 2 would have silenced eighteen of twenty flat-feed cards.

Measured on the reported shelf (six patched Sandbox games behind a 50-hour Sandbox anchor): before, 6 of 6 named Sandbox; after, 2 name Sandbox, 2 fall to the dormancy clause, 2 say less. A control test runs the same shelf with the cap lifted and asserts it does repeat.

New tests/Winnow.Recommend.Tests/ShelfFactVarietyTests.cs, 10 tests.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The feed's supporting clause is now capped per surface by the FACT it cites, not only by the phrasing it uses. ShelfReasonLedger (the renamed TASK-71 ReasonVariantLedger) answers two questions for one surface: which phrasing is spent, and which claim is spent. RecommendationScorer.SecondarySignal became SupportingSignals, returning every supporting fact that fired in the same strongest-first order rather than only the head, and ReasonBuilder walks that list, skipping a claim the surface has spent, and renders the primary alone when nothing is left. Every entry on the list fired for that card, so reaching past the head is never a new claim and running out yields silence rather than a borrowed fact.

Claim identity: the taste clause is keyed on the descriptor name (Sandbox and Roguelike are two claims), every other supporting fact on its signal. Demotion disclosures (mode mismatch, played recently, shown recently) are exempt and never withheld, because suppressing one hides ranking information rather than repeating it.

Threshold: RecommendationTuning.FactCitationCards (3, one card in three) with FactCitationFloor (2), derived per surface by ShelfReasonLedger.CapFor. A 6-card shelf caps at 2, under the reported three-of-six; the 20-card flat feed caps at 6, where a flat 2 would have silenced eighteen of twenty.

Files: src/Winnow.Recommend/ShelfReasonLedger.cs (renamed from ReasonVariantLedger.cs), ReasonBuilder.cs, ReasonSignals.cs, RecommendationScorer.cs, RecommendationTuning.cs, RecommendationEngine.cs, ShelfBuilder.cs; tests/Winnow.Recommend.Tests/ShelfFactVarietyTests.cs (new) and ShelfVarietyTests.cs; docs/recommendation-engine.md section 6c.

Verified: dotnet test from repo root, all projects green (Winnow.Tests 2712, Winnow.Recommend.Tests 145, Winnow.Covers.Tests 70), zero warnings under TreatWarningsAsErrors. Measured on the reported shelf seeded as data (six patched Sandbox games behind a 50-hour Sandbox anchor): before, 6 of 6 cards named Sandbox and the uncapped run reproduces the three reported sentences verbatim; after, 2 name Sandbox, 2 fall to the dormancy clause, 2 say less.

AC1 by The_citation_cap_is_stated_rather_than_implied (pins 2 for a 6-card shelf, 6 for a 20-card feed, the floor for shorter surfaces) and No_more_than_the_cap_of_one_shelf_names_the_same_facet, with The_same_shelf_repeats_the_facet_when_nothing_counts_it as the control that fails if the cap ever stops binding. AC2 by A_card_whose_facet_is_spent_reaches_for_its_next_strongest_fact and A_card_with_nothing_else_to_add_says_less_rather_than_repeating, the latter matching the bare opening against the phrasebook so it cannot drift from the copy. AC3 by No_card_names_a_facet_it_does_not_carry, A_different_facet_is_a_different_claim_and_is_not_blocked and A_demotion_is_still_disclosed_on_every_card_that_earned_it. AC4 by The_same_shelf_renders_the_same_way_twice and The_same_cards_in_the_same_order_render_the_same_sentences, with the existing The_first_card_on_a_shelf_keeps_the_phrasing_its_own_id_chose still green. Prose authored by docs-writer.
<!-- SECTION:FINAL_SUMMARY:END -->
