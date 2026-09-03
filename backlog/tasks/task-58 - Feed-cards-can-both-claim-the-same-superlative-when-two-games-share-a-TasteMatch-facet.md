---
id: TASK-58
title: >-
  Feed cards can both claim the same superlative when two games share a
  TasteMatch facet
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-01 02:50'
updated_date: '2026-09-01 03:06'
labels:
  - recommend
  - ui
dependencies: []
priority: high
ordinal: 75000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
ReasonPhrasebook selects a phrasing variant by hashing the release id with the signal and clause. Because the hash is per-card with no awareness of what other cards in the same feed said, two games tagged with the same facet can both render a superlative secondary clause such as "your deepest pile" or "more hours in this facet than in anything else". A superlative is true of at most one game in the library, and two adjacent cards asserting it about different games is a visible contradiction that undermines the feed. Either verify a superlative claim against a library-wide fact before selecting that variant, or replace the absolute phrasing with comparative language that is true locally and reserve the absolute claim for a fact the engine has actually computed and can guarantee is unique.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 No two cards visible in a single feed render a phrasing that asserts a unique-in-library superlative
- [ ] #2 A variant that makes an absolute claim is either validated against the library before selection or reworded to a comparative that holds for any qualifying game
- [ ] #3 Existing non-superlative TasteMatch variants still render and the phrasebook deterministic-per-release-id stability is preserved
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit every string in ReasonPhrasebook (and the adjacent scorer explanation strings) for claims of rank, maximum, minimum, uniqueness or implicit comparison against the whole library; catalogue each with its provability.
2. Take fix (a) as the default: reword every unprovable absolute into a comparative that holds for any qualifying game. Offenders: the three TasteMatch secondary variants (deepest pile / more hours than anything else / most of your hours) and the LaunchedUnmeasured 'which is unusual' rarity claim.
3. Reject fix (b) for TasteMatch: the only cheap library-wide proof available (the taste profile's argmax facet) is a property of the FACET, not of the game, so two cards sharing that facet would still both render it — which is exactly what AC #1 forbids. Record the reasoning.
4. Keep the copy specific rather than hedged by licensing the stronger comparative with a computed fact: carry the already-computed normalised TasteAffinity into ReasonEvidence and add a {strongFacet} token that resolves only at or above RecommendationTuning.OnTasteMinAffinity (0.6 - the same bar the On Your Taste shelf uses). The existing CanFill filter then gates the stronger phrasings automatically; nothing absolute ships.
5. Delegate all wording to docs-writer with the failing example, the rule that a card may only claim what the engine can prove about that game, and the requirement that comparatives stay specific.
6. Tests in Winnow.Recommend.Tests: a feed of several candidates sharing one facet renders no unique-in-library claim on any card; the strong-facet phrasings are only selectable when affinity clears the bar; the deterministic-per-release-id stability and the existing reason-contract sweep still hold.
7. docs/recommendation-engine.md gets a 'what a card may claim' subsection, written by docs-writer.
8. Scoped tests, then the full suite across all three projects with --artifacts-path into the scratchpad. No commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit of the whole phrasebook (every variant, both clauses, plus the fallback) found four claims the engine cannot prove, not just the reported clause:

1. TasteMatch secondary, ', and {facet} is where most of your hours already live' - a majority share the profile never measures. Reworded.
2. TasteMatch secondary, ', filed under {facet}, which is your deepest pile' - a bare rank. Reworded.
3. TasteMatch secondary, ', and you have more hours in {facet} than in anything else' - the reported contradiction. Reworded.
4. LaunchedUnmeasured primary, 'There is a launch date here and no playtime at all, which is unusual' - rarity is a count of the rest of the library and nothing counts it. Reworded to attribute the oddity to the record itself.
5. (found after the first pass) SoloOnlyMismatch secondary, ', but your hours are all online and this one is not' - 'all' overstates a measured ModeDominanceShare of 0.85. Reworded to match what 85% supports.

Cleared as provable and left alone: every per-game claim (minutes, refund line, launch dates, update titles, episode counts, store counts, the 2009 sentinel), both probably-done openings (gated on UpdateCoverage.Observed), the mode-mismatch characterisations that are not quantified, and the token-free TasteMatch variant ', and it sits squarely in what you actually play', which AC #3 requires to keep rendering.

Fix (b) was considered and rejected for the taste clause. The only cheap library-wide proof available is the taste profile's argmax facet, which is a property of the FACET, not of the game: two cards carrying that facet would both still render the absolute, which is exactly what AC #1 forbids. Uniqueness of the fact does not give uniqueness of the card.

To keep the comparatives specific rather than hedged, the normalised affinity the scorer already computes now rides in ReasonEvidence.TasteAffinity, and a new {strongFacet} token resolves to the facet name only at or above RecommendationTuning.OnTasteMinAffinity (0.6, the same bar the On Your Taste shelf uses). The existing CanFill token filter does the gating, so no cross-card bookkeeping and no new query were added, and the deterministic per-release-id variant selection is untouched. Nothing absolute ships.

Copy authored by docs-writer per the project convention.

Mechanism landed and scoped tests green (98/98 in Winnow.Recommend.Tests).

New guard: tests/Winnow.Recommend.Tests/ReasonHonestyTests.cs.
- No_variant_anywhere_in_the_phrasebook_claims_a_rank_it_cannot_prove sweeps every variant of every signal in both clauses, plus the fallback, against five documented patterns (comparison against the whole library, a bare rank, a quantified share, an exclusivity claim, a rarity claim). Verified empirically that all four shipped offenders trip it and that 'one of your deepest piles' does not.
- Games_sharing_one_facet_never_render_competing_superlatives seeds a committed Survival game plus five sealed Survival games and a faint Roguelike side, asserts the taste clause really does fire on two or more cards, then asserts no card makes a library-wide claim and that the faint side never borrows the strength copy.
- A_strength_claim_is_unusable_until_the_measured_affinity_earns_it sweeps 60 release ids just under the bar, at the bar and above it; the strength phrasings are read out of the phrasebook rather than hard-coded so rewording cannot quietly retire the test.
- The_gating_does_not_disturb_per_release_stability and The_taste_match_fallback_still_renders_when_the_facet_is_unknown cover AC #3.

ReasonContractTests.EvidenceShapes widened: the base Rich() evidence now carries TasteAffinity 1.0 so the one-sentence budget sweep covers the gated phrasings (they are the longest the taste clause can produce), plus a new faint-affinity shape at 0.1. Both pass inside the 180-character budget.

Full-suite run is blocked at present, not by this change: src/Winnow.App is mid-edit by the concurrent settings-UI work and does not compile (AVLN1001 unterminated XAML in SteamAccountImportView.axaml, then AVLN2000 on MainWindow.axaml bindings). Both tests/Winnow.Tests and tests/Winnow.Covers.Tests reference Winnow.App, so neither can run until that lands. Will re-run.

Copy landed, authored by docs-writer.

Reworded:
- LaunchedUnmeasured: 'There is a launch date here and no playtime at all, which is unusual' -> 'There is a launch date here and not one measured minute to go with it'.
- TasteMatch secondaries now six variants: ', and you have real hours in {facet} games', ', filed under {facet}, a corner of your library you actually play', ', sitting in {facet} alongside games you gave real time to', ', and {strongFacet} is one of your deepest piles', ', landing in {strongFacet}, a kind of game you keep coming back to', and the kept token-free ', and it sits squarely in what you actually play'.
- SoloOnlyMismatch: ', but your hours are all online and this one is not' -> ', but nearly everything you play is online and this one is not', matching the 0.85 dominance share the engine actually measured and the scorer's own hedged explanation.
- Phrasebook class doc gained {strongFacet} and a new contract bullet stating the honesty rule.
- RecommendationTuning.OnTasteMinAffinity doc now records its second consumer.

98/98 green in Winnow.Recommend.Tests with the final copy, TreatWarningsAsErrors clean.

Verification, verbatim.

dotnet build src/Winnow.Recommend -> Build succeeded, 0 Warning(s), 0 Error(s) (TreatWarningsAsErrors clean).

Full suite, dotnet test from repo root with --artifacts-path into the scratchpad:
  Passed!  - Failed:     0, Passed:    70, Skipped:     0, Total:    70 - Winnow.Covers.Tests.dll (net10.0)
  Passed!  - Failed:     0, Passed:    98, Skipped:     0, Total:    98 - Winnow.Recommend.Tests.dll (net10.0)
  tests/Winnow.Tests did not compile.

The Winnow.Tests compile failures are entirely the concurrent settings-UI work, not this change: CS1503 on the AccountStatsViewModel/SteamAccountImportViewModel constructor argument order in AccountStatsViewModelTests, LibraryViewModelTests and ListsViewModelTests, and CS1061 for Title/RailRow/RailTooltip in SteamAccountImportViewModelTests. Not one error names anything in Winnow.Recommend, and this change alters no API that Winnow.App or its tests consume: ReasonBuilder.Build keeps its signature, and ReasonEvidence only gained an optional init member. Winnow.Tests needs a re-run once that work settles.

Not committed, per instruction. Task left In Progress and not finalized.
<!-- SECTION:NOTES:END -->
