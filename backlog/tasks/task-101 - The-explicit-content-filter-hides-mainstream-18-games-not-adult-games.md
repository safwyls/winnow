---
id: TASK-101
title: 'The explicit-content filter hides mainstream 18+ games, not adult games'
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 22:50'
updated_date: '2026-09-04 23:10'
labels:
  - data
  - ui
dependencies: []
priority: high
type: bug
ordinal: 128000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The intent of the setting is to filter adult/hentai/sex games. It currently hides most mainstream violent games as well.

MaturityRules.ExplicitRatings (src/Winnow.Core/Queries/Maturity.cs) excludes esrb:m deliberately, but includes pegi:18, usk:18, cero:z, acb:r18, classind:18 and grac:18. Those are the ordinary ratings a mainstream violent game carries outside North America — GTA V, Doom Eternal, The Witcher 3 and Cyberpunk 2077 are all PEGI 18 and USK 18, and ACB R18+ in Australia. Because IGDB returns every board for a work, excluding ESRB M achieves nothing: the same game arrives carrying pegi:18 and is hidden anyway.

The signals that actually mean adults-only sexual content are esrb:ao, acb:x18 (the Australian X18+ category is specifically sexually explicit material) and the Steam adult_only_sexual_content descriptor. The broad 18+ boards are a maturity level, not an adult flag, and belong to the rating-cap filter instead.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The explicit gate is restricted to genuinely adults-only signals; a game rated PEGI 18 or USK 18 for violence alone is not hidden by it
- [x] #2 The broad 18+ board tiers remain stored as evidence and are not discarded — they become the input to the rating-cap filter
- [x] #3 Tests assert that a mainstream violent title carrying pegi:18/usk:18/acb:r18 is shown when the toggle is off, and that an adults-only title is still hidden
- [x] #4 The reasoning is recorded so the tier split cannot be silently re-widened later
- [x] #5 The copy beside the toggle says what it filters, in a few words
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify each board's meaning against primary/secondary sources before relying on the split (ESRB AO definition, ACB R18+ vs X18+, PEGI 18, USK 18, CERO Z, ClassInd 18, GRAC 18, Steam's four content descriptors).
2. Add an ordered tier scale to src/Winnow.Core/Queries/Maturity.cs: a MaturityTier enum (Unrated, Everyone, Preteen, Teen, Mature, Restricted18, AdultsOnly) and a MaturityTiers map covering the whole IGDB board:tier vocabulary, exposing For(token), Highest(ratings, descriptors), Ordered and IsWithinCap(tier, cap). This is the contract TASK-103 consumes.
3. Rebuild MaturityRules.IsExplicit on top of it: explicit is exactly MaturityTier.AdultsOnly — esrb:ao, acb:x18 and the adult_only_sexual_content descriptor. The broad 18+ boards (pegi:18, usk:18, cero:z, acb:r18, classind:18, grac:18) drop to Restricted18 and stay stored as evidence.
4. Derive ExplicitRatingCodes from the tier map so the gate and the scale cannot drift apart.
5. Tests: pin the explicit set exactly so it cannot be widened silently; assert a mainstream violent title carrying esrb:m/pegi:18/usk:18/acb:r18/cero:z survives the bucket query with the toggle off; assert an adults-only title (esrb:ao, and the Steam descriptor) is still dropped; assert every token IgdbAgeRatingTokens can emit has a tier; assert the tier ordering.
6. No migration: work_maturity stores tokens verbatim and the verdict is C# at read time, which is what migration 0024 was designed for.
7. Delegate all prose to docs-writer: Maturity.cs XML docs and comments, game-library-design.md 6.4, design-system.md 16.1, docs/decisions.md superseded text, LibrarySettingsCopy.cs explicit strings, and the comments in LibraryQueryRepository / WorkMaturityRepository / IWorkMaturityRepository / Buckets.cs / Program.cs / IgdbAgeRatingTokens.cs / ExplicitContentTests.cs that enumerate the old eight-code list.
8. Build and test with the scratch output path.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Board verification (how the tier split was established, not assumed):
- ESRB Adults Only 18+ — esrb.org ratings guide, verbatim: 'Content suitable only for adults ages 18 and up. May include prolonged scenes of intense violence, graphic sexual content and/or gambling with real currency.' It is the only category above M. Adults-only.
- ACB X 18+ — classification.gov.au and the ALRC classification-categories report: X 18+ covers sexually explicit material, actual sexual activity between consenting adults, and is a FILMS-ONLY category. Australian computer games are classified G, PG, M, MA 15+, R 18+ or RC, so a game cannot legally carry it. Kept in the adults-only set because if the token ever arrives it can only mean sexually explicit content, but it is near-inert in practice: esrb:ao and the Steam descriptor carry the real weight.
- ACB R 18+ — same source: legally restricted to adults for high-impact content INCLUDING violence. This is the token that was hiding GTA V. Restricted18, not adults-only.
- PEGI 18 — pegi.info: awarded where violence is 'gross violence, apparently motiveless killing, or violence towards defenceless characters'. Violence alone reaches it. Restricted18.
- USK 18 / CERO Z — each board's top age category, awarded for violence: Cyberpunk 2077 is USK 18; every GTA game is CERO Z. Restricted18.
- ClassInd 18 (Brazil) — top age band; its criteria cover gratuitous violence, torture, mutilation. Restricted18.
- GRAC 18 (South Korea) — top age band (now 19). Restricted18.
- ACB RC — refused classification, unsellable in Australia, but not necessarily for sexual content. Restricted18, deliberately not adults-only.
- Steam adult_only_sexual_content — Steam's own store-preferences wording is 'sexual content that is explicit or graphic' intended for adults only, and Steam separates it from Frequent Violence or Gore, Nudity or Sexual Content and General Mature Content. It is the only one of the four Steam hides by default. An independent storefront drawing the same line, which is the strongest corroboration of the split.

Implementation.

src/Winnow.Core/Queries/Maturity.cs is the only behaviour change. It gains an ordered MaturityTier enum and a MaturityTiers class; MaturityRules.IsExplicit is redefined on top of them.

MaturityTier, ascending: Unrated, Everyone, Preteen, Teen, Mature, Restricted18, AdultsOnly. Anchored on the minimum age each board states, which is what makes each placement checkable rather than a matter of taste — Everyone 0-7, Preteen 10-11, Teen 12-15, Mature 16-17, Restricted18 the legally-restricted 18+ boards, AdultsOnly the adults-only sexual signals. MaturityTiers.RatingTiers covers the whole vocabulary IgdbAgeRatingTokens can emit (39 legacy enum values plus acb:x18), grouped by board.

The gate: MaturityRules.ExplicitTier = MaturityTier.AdultsOnly, and IsExplicit is MaturityTiers.Highest(ratings, descriptors) >= ExplicitTier. ExplicitRatingCodes and ExplicitDescriptors are DERIVED from the tier map (MaturityTiers.RatingCodesAt / DescriptorsAt) rather than listed separately, so the gate and the scale cannot drift apart. The explicit set is now exactly acb:x18, esrb:ao and adult_only_sexual_content.

Descriptors: only adult_only_sexual_content reaches the scale. nudity_or_sexual_content, general_mature_content and violence_or_gore contribute no tier at all and stay pure evidence — giving them a maturity level would re-create the same over-reach from the other direction.

THE CONTRACT TASK-103 CONSUMES (all in Winnow.Core.Queries, no IO, referenced by Winnow.Core alone):
- enum MaturityTier — ordered, comparable with < and >.
- MaturityTiers.Ordered — IReadOnlyList<MaturityTier>, ascending, the cap-eligible levels. Deliberately EXCLUDES Unrated: absence of data is not a level.
- MaturityTiers.ForRating(string? token) / ForDescriptor(string? token) — one token to its tier; Unrated for unknown, blank, esrb:rp and grac:testing.
- MaturityTiers.Highest(string? ratings, string? descriptors) — the highest tier across the comma-joined stored columns, verbatim from work_maturity. Unrated when there is no evidence.
- MaturityTiers.IsWithinCap(MaturityTier tier, MaturityTier cap) — encodes the rule TASK-103 AC#3 needs in one place: Unrated is inside every cap, otherwise tier <= cap.
- MaturityRules.ExplicitTier — the single level the settings toggle gates on, so the cap and the toggle compose without either restating the other (TASK-103 AC#4).

No migration, and none should be needed: work_maturity stores tokens verbatim and the verdict is taken in C# at read time, which is precisely what migration 0024 was designed for. The broad 18+ tokens already on disk are untouched and are re-read into the new scale on the next query. Migration 0024's header comment still enumerates the old eight-code list; it is append-only so it was NOT edited, and game-library-design.md 6.4 now names MaturityTiers/MaturityRules as the authority instead.

Naming observation, not a defect: Winnow.Recommend already uses the phrase 'maturity tier' in prose (RecommendationEngine.cs, RecommendationTuning.cs, tests/Winnow.Recommend.Tests/MaturityTierTests.cs) for something unrelated — how established the user's LIBRARY is, F33 in docs/recommendation-engine.md. That concept has no C# symbol, so there is no collision and the solution builds clean; but the phrase now means two things in one repository. The new enum keeps the name MaturityTier because it sits in Winnow.Core.Queries beside MaturitySources, MaturityRatingCodes, MaturityDescriptors and MaturityRules, where Maturity already means content maturity. Flagging it rather than renaming either side.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Split maturity evidence into an ordered MaturityTier scale (Unrated, Everyone, Preteen, Teen, Mature, Restricted18, AdultsOnly) and moved the explicit gate to AdultsOnly only. Previously pegi:18, usk:18, cero:z, acb:r18, classind:18 and grac:18 were all treated as explicit; because IGDB returns every board for a work, excluding esrb:m accomplished nothing and mainstream violent titles were hidden anyway. Those six now sit at Restricted18 — stored evidence, not a hiding trigger — leaving esrb:ao, acb:x18 and the Steam adult_only_sexual_content descriptor as the gate. No migration: the evidence rows were already stored and the rule was always decided in C#, which is what made this a one-file change. MaturityTiers.Ordered, ForRating, ForDescriptor, Highest and IsWithinCap are the contract TASK-103 consumes for the rating cap. Toggle copy now names what it filters. Verified by 105 passing tests, including A_mainstream_violent_game_is_shown_when_the_setting_is_off (reproducing the GTA V / Doom Eternal / Witcher 3 / Cyberpunk rating profile), An_adults_only_rating_still_hides_the_game_when_the_setting_is_off, The_explicit_set_is_exactly_the_adults_only_signals as a guard against silent re-widening, and Every_rating_token_the_igdb_reader_can_emit_has_a_tier against silent Unrated fallthrough. Clean build, 0 warnings. Caveat recorded: migration 0024 header comment still lists the old codes and is not edited, per the append-only rule; MaturityTiers and MaturityRules are the authority.
<!-- SECTION:FINAL_SUMMARY:END -->
