---
id: TASK-122
title: Offer to link when an IGDB entry is already used by another game
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 04:58'
updated_date: '2026-09-05 16:22'
labels:
  - ui
  - data
dependencies:
  - TASK-89
priority: medium
type: feature
ordinal: 149000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User request: "if someone tries to set the metadata for something to a game that is already using that metadata elsewhere in the library then we should prompt them to merge instead".

The refusal already exists and is already distinguished. WorkIgdbPinRepository returns WorkIgdbPinOutcome.IgdbIdClaimedByAnotherWork when another work holds the id, IgdbManualAssignment maps it to IgdbAssignmentStatus.IgdbIdClaimedByAnotherWork, and GameIgdbMatchCopy gives it its own sentence. A comment in IgdbMatchViewModelTests already calls it out as "the one the user can act on" against three they cannot — but the UI gives them nothing to act with, so it dead-ends.

The meaning of the collision is the point: works.igdb_id is UNIQUE, so two works claiming one IGDB entry ARE the same game. That is exactly what the identity-link system models — kind same_game, built across the TASK-70 series and surfaced in the Merges queue. So the honest response to the refusal is not an error but an offer: these two are the same game, link them.

Design the offer rather than bolting a button on. The user should be told which game already holds the entry, be able to see it, and confirm the link — links are reviewed deliberately in the Merges queue and a hard external-id join is the one case AGENTS.md says may auto-merge, so decide and record whether this path confirms in place or routes to the queue.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Assigning an IGDB entry another work already holds offers to link the two as the same game instead of only refusing
- [x] #2 The offer names and shows the game that already holds the entry, so the user can tell whether it is really the same game
- [x] #3 Accepting produces the same same_game link the Merges queue produces, through the existing identity-link path and not a second mechanism
- [x] #4 Declining leaves both works exactly as they were, with nothing pinned
- [x] #5 Whether this confirms in place or routes to the Merges queue is decided and recorded
- [x] #6 Tests cover the offer, the accept producing a link, and the decline changing nothing
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
DECIDED (user, 2026-09-05): confirm in place, do not route to the Merges queue. Recorded in docs/decisions.md.

1. DATA HALF (delegated to the data-layer agent). IWorkRepository.GetByIgdbIdAsync(long) -> Work?, implemented on WorkRepository in GetAsync's shape; a non-positive id returns null without a query. Added to IdentityReadInventoryTests on the DO NOT RESOLVE list: it asks which row literally holds that igdb_id, the question the UNIQUE constraint answers, so resolving would name a work that does not hold it. Repository tests for hit, miss, NULL igdb_id and non-positive id. No migration; no schema change.

2. THE SHAPE OF THE OFFER. The refusal stays a refusal when nothing can be shown, and becomes an offer when the claiming game can be named. IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork comes back from AssignAsync exactly as today; the view model then asks who holds the entry and, if it can name them, draws the offer instead of the bare Amber sentence. No claimant, no identity-link repository, or a claimant that resolves to this same work: the control degrades to the sentence it draws today, which is why the existing refusal Theory keeps passing untouched.

3. SEAM. IIgdbAssignmentService gains FindClaimingGameAsync(long igdbId) -> IgdbClaimingGame? (work id, title, cover URL, first release year) - an App-layer read model, like IgdbCandidate, because the architecture tests enforce 5.1 on what a view model NAMES. IgdbAssignmentService implements it over an optional IWorkRepository; a null repository or a failed read returns null, like every other call on the seam.

4. THE LINK. Handed in as a delegate from LibraryViewModel, the precedent BuildCoverageAsync sets by handing SeparateAsync to GameCoverageViewModel. LibraryViewModel calls _identityLinks.LinkAsync with ParentWorkId = the claiming work, ChildWorkIds = [this work], Kind = same_game, Source = user - the same request MergeQueueViewModel.LinkAsync builds, so it is the existing path and not a second mechanism. The claimant is the PARENT because it holds the igdb_id, which is the first rung of the queue's own ChooseWork ladder, and because the parent's metadata is the IGDB entry the user was reaching for. Nothing is pinned: works.igdb_id is UNIQUE, so pinning the child is impossible, and the link is the whole answer.

5. AFTER THE LINK. Reload and reopen through the existing AfterIgdbChangeAsync, carrying a confirmation note. ReopenDetailsAsync currently matches only a tile's PRIMARY ownership; a newly linked child folds into the parent tile as a non-primary entry, so it gains a fallback to any tile whose OwnershipIds contain the one that was open. Without it the modal would close silently on the one action that folds the game.

6. DECLINING. Dismisses the offer, writes nothing, and restores the plain refusal sentence so the user still sees why the assignment did not land. No pin, no link, no candidate list lost.

7. VIEW. The offer draws inside the existing disclosure in the right column's rest band, where the Amber refusal sentence already draws. Cover, name and year in the candidate-row idiom, so the user can judge it really is the same game, over two controls. The cover machinery IgdbCandidateViewModel already has is lifted into a shared base rather than copied.

8. PROSE. Every string, comment, XML doc comment, the design-system.md 10.9 addition and the docs/decisions.md entry authored by docs-writer.

9. TESTS. IgdbMatchViewModelTests: the offer appears and names the holder; accepting calls the link delegate with the claiming work as parent and pins nothing; declining writes nothing and restores the sentence; no claimant or no link delegate degrades to today's sentence. IgdbAssignmentModalTests: the accept produces a live same_game row through the real IdentityLinkRepository over a migrated database.

10. VERIFY. PowerShell, dotnet build -p:BaseOutputPath=C:\Temp\winnow-u1\ -m:1, then dotnet test per project --no-build against the same path. Baseline 3201 / 152 / 70.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
DECIDED BY THE USER, 2026-09-05: "confirm in place, dont route to queue". The offer is accepted inside the details modal — show which game already holds the entry so the user can judge it is really the same game, then link on confirmation. Do not send them to the Merges queue. This is consistent with AGENTS.md, which permits a hard external-id join to auto-merge; naming an exact IGDB id is a hard join, and the in-place confirmation is what supplies the review that a queue would otherwise provide.

IMPLEMENTED (UI + identity-link wiring). The data half was delegated to the data-layer agent; all prose to docs-writer.

WHAT LANDED

Data half (data-layer agent): IWorkRepository.GetByIgdbIdAsync(long) -> Work?, implemented on WorkRepository in GetAsync's shape, non-positive id short-circuits to null without a query. Repository tests in RepositoryRoundTripTests. No migration.

Seam (src/Winnow.App/Services): IIgdbAssignmentService gains FindClaimingGameAsync(long igdbId) -> IgdbClaimingGame?, an App-layer read model beside IgdbCandidate for the reason TASK-89 already learned the hard way - the architecture-boundary tests enforce 5.1 on what a view model NAMES, not only on what it holds. IgdbAssignmentService takes an optional IWorkRepository; a null repository, a non-positive id, no holder or a failed read all return null, which is the same soft-failing contract every other call on the seam has. No registration change was needed: IWorkRepository was already registered, so the container fills the new constructor parameter.

View model (GameIgdbMatchViewModel): AssignAsync now routes IgdbIdClaimedByAnotherWork through OfferToLinkAsync before falling back to ProblemFor. The offer is a Claim (IgdbClaimViewModel) carrying the holder's work id, name, year and cover; LinkClaimCommand accepts, DeclineClaimCommand declines. The cover machinery IgdbCandidateViewModel already had was lifted into a shared IgdbCoverRowViewModel base rather than copied, and both rows derive from it.

THE OFFER IS ADDITIVE, WHICH IS WHY NOTHING EXISTING MOVED. OfferToLinkAsync returns false - and the control draws exactly the Amber sentence it drew before - when no link delegate is wired, when nothing holds the entry, or when the holder resolves to this same work. That is what let the pre-existing refusal Theory (A_refusal_is_stated_and_nothing_is_pinned, all four outcomes) keep passing untouched.

The link (LibraryViewModel.LinkIgdbClaimAsync): builds the same IdentityLinkRequest MergeQueueViewModel.LinkAsync builds - ParentWorkId = the holder, ChildWorkIds = [this work], Kind = same_game, Source = user - and calls the same IIdentityLinkRepository.LinkAsync. Not a second mechanism. Handed to the view model as a delegate, the precedent BuildCoverageAsync set by handing SeparateAsync to GameCoverageViewModel.

THE HOLDER IS THE PARENT, and NOTHING IS PINNED. The holder carries the igdb_id, which is the first rung of the Merges queue's own ChooseWork ladder, and its metadata is the entry the user was reaching for. Pinning the child is not declined, it is impossible: works.igdb_id is UNIQUE, and pinning the child to an id another row holds is exactly what the constraint refused. The link is the whole answer.

ONE THING THAT HAD TO CHANGE UNDERNEATH. ReopenDetailsAsync matched only a tile's PRIMARY ownership. A link written from this modal folds the open game into the holder's tile as a NON-primary entry, so the modal would have closed silently on the one action that unifies the two. It now falls back to any tile whose OwnershipIds contain the one that was open.

View (GameDetailsView.axaml): the offer draws inside the existing inline disclosure in the right column's rest band, where the Amber refusal already drew. Holder cover 34x51 at RadiusControl, name, year in Plex - the candidate row's own idiom, because it is the same judgement. Line border, not Amber: a question, not a failure (2).

Inventory: IgdbAssignmentService.FindClaimingGameAsync and WorkRepository.GetByIgdbIdAsync are both on IdentityReadInventoryTests' DO NOT RESOLVE list. Resolving either would name a group parent that does not hold the id, in the one place whose whole purpose is to be judged correct by the user.

All prose - the seven copy strings, every comment and XML doc comment, design-system.md 10.9's correction and its new 'The same-game offer' subsection, game-library-design.md 5.3's paragraph and the docs/decisions.md entry - authored by docs-writer.

VERIFICATION, PowerShell, scratch output path.
  dotnet build -p:BaseOutputPath=C:\Temp\winnow-u1\ -m:1
    Build succeeded. 0 Warning(s) 0 Error(s)
  dotnet test tests\Winnow.Recommend.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-u1    Passed! - Failed: 0, Passed: 152, Skipped: 0, Total: 152
  dotnet test tests\Winnow.Covers.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-u1    Passed! - Failed: 0, Passed: 78, Skipped: 0, Total: 78
  dotnet test tests\Winnow.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-u1    Failed! - Failed: 2, Passed: 3243, Skipped: 0, Total: 3245
  dotnet test tests\Winnow.Tests ... --filter IgdbMatchViewModelTests|IgdbAssignmentModalTests
    Passed! - Failed: 0, Passed: 38, Skipped: 0, Total: 38

BOTH Winnow.Tests FAILURES BELONG TO TASK-119, WHICH IS BUILDING THE METADATA EDITOR IN THE SAME WORKING TREE, AND NEITHER NAMES A FILE THIS TASK TOUCHED.
- IdentityReadInventoryTests names four unclassified readers, all of them TASK-119's: WorkMetadataEditService.GetAsync and three in WorkFieldSourceRepository. This task's two readers - WorkRepository.GetByIgdbIdAsync and IgdbAssignmentService.FindClaimingGameAsync - are both on the DO NOT RESOLVE list and no longer named.
- DocumentationConsistencyTests names game-library-design.md:757, inside TASK-119's new 'Per-field sources' paragraph about migration 0027. This task's addition to that file is at line 526.
The library and covers projects are green, and every test over this task's own surface passes.

AC EVIDENCE
AC 1: IgdbMatchViewModelTests.A_claimed_entry_offers_to_link_and_names_the_holder drives a real IgdbIdClaimedByAnotherWork refusal and asserts an offer stands with no Amber sentence beside it; IgdbAssignmentModalTests.Accepting_the_offer_writes_a_same_game_link_and_pins_nothing does the same over the real LibraryViewModel, the real WorkIgdbPinRepository and a migrated SQLite database, so the refusal it turns into an offer is the shipped UNIQUE-constraint refusal and not a fake.
AC 2: the same two tests assert the offer carries the holder's work id, name, headline naming it, year and IGDB cover key. Compiled bindings (AvaloniaUseCompiledBindingsByDefault) make the build a check that every binding in the new markup resolves. WHAT NO TEST COVERS IS HOW IT LOOKS ON SCREEN - that needs a run.
AC 3: Accepting_the_offer_writes_a_same_game_link_and_pins_nothing reads the row back through the real IdentityLinkRepository.GetHistoryAsync and asserts one live link, parent = the holder, child = this work, kind = same_game, source = user - the same request MergeQueueViewModel.LinkAsync builds - and asserts the grid folds to one tile.
AC 4: Declining_the_offer_leaves_both_games_as_they_were asserts no link row, no pin on either work, both igdb_id values unchanged and two tiles still on the grid; IgdbMatchViewModelTests.Declining_the_offer_writes_nothing_and_restores_the_refusal asserts the link delegate was never called and the refusal sentence came back.
AC 5: recorded in docs/decisions.md (2026-09-05, quoting the user's words and the superseded design-system text), design-system.md 10.9 and game-library-design.md 5.3.
AC 6: the offer, the accept, the decline, the failed link write, and all three degrade-to-a-plain-refusal paths are covered; The_offer_has_copy_of_its_own also asserts the seven new strings are non-blank, distinct, and contain neither TODO nor PLACEHOLDER.

NO PLACEHOLDERS REMAIN. Swept every file this task touched for TODO(docs-writer) and PLACEHOLDER: none.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The one IGDB refusal a user could act on now gives them something to act with. works.igdb_id is UNIQUE, so two works claiming one IGDB entry ARE the same game; the refusal is therefore not an error but an answer, and the details modal now says so. When an assignment collides, the control names the game that already holds the entry and draws it in the candidate row's own idiom - 34x51 cover, name, year in Plex - because judging whether two entries are one game is exactly what a candidate row is drawn for. The border is Line rather than Amber: a question, not a failure.

Accepting confirms IN PLACE and never routes to the Merges queue, which is the user's own decision ('confirm in place, dont route to queue'), consistent with game-library-design.md 5.3 permitting a hard external-id join to auto-merge - naming an exact IGDB id is a hard join, and the in-place confirmation, which names and shows the other game, supplies the review a queue would otherwise provide. The write is the same IdentityLinkRequest MergeQueueViewModel.LinkAsync builds, through the same IIdentityLinkRepository.LinkAsync: kind same_game, source user, holder as parent because it carries the igdb_id, the first rung of the queue's own ChooseWork ladder. Nothing is pinned, and that is not a choice - pinning the child to an id another row holds is precisely what the UNIQUE constraint refused, so the link is the whole answer. Declining writes nothing and restores the refusal sentence.

The offer is additive, which is why nothing that already worked moved: with no identity-link repository, no holder to name, or a holder that resolves to this same work, the collision draws exactly the Amber sentence it drew before, and the pre-existing four-outcome refusal Theory passes untouched. One thing did have to change underneath - ReopenDetailsAsync matched only a tile's PRIMARY ownership, and a link written here folds the open game into the holder's tile as a NON-primary entry, so the modal would have closed silently on the one action that unifies the two; it now falls back to any tile whose entries contain the open ownership.

The data half is one read - IWorkRepository.GetByIgdbIdAsync - delegated to the data-layer agent and on IdentityReadInventoryTests' DO NOT RESOLVE list along with its App-layer caller, because resolving either would name a group parent that does not hold the id, in the one place whose whole purpose is to be judged correct by the user. All prose was authored by docs-writer.

Verified with dotnet build -p:BaseOutputPath=C:\Temp\winnow-u1\ -m:1 (succeeded, 0 warnings, 0 errors) and dotnet test per project against that path: Winnow.Recommend.Tests 152 passed, Winnow.Covers.Tests 78 passed, Winnow.Tests 3243 passed against a 3201 baseline. Winnow.Tests reports 2 failures, both belonging to TASK-119's concurrent metadata-editor work in the same tree - four unclassified readers in WorkMetadataEditService and WorkFieldSourceRepository, and a 'superseded' wording at game-library-design.md:757 inside its new per-field-sources paragraph. Neither names a file this task touched, and the 38 tests over this task's own surface all pass. What no test covers is how the offer looks on screen, which needs a run.
<!-- SECTION:FINAL_SUMMARY:END -->
