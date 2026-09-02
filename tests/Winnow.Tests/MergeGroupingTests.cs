using Winnow.Core.Identity;
using Winnow.Core.Merging;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The grouper, on its own. It is pure and BCL-only, so the whole shape of
/// the queue (what becomes a card, who arrives checked, which title is
/// proposed and why) is testable without a database, a matcher or a screen.
/// </summary>
public sealed class MergeGroupingTests
{
    // Release ids are work id * 10 throughout, so a failure names the work it
    // came from rather than an anonymous number.
    private static readonly Dictionary<long, long> WorkOfRelease = new()
    {
        [10] = 1, [20] = 2, [30] = 3, [40] = 4, [50] = 5, [60] = 6,
    };

    [Fact]
    public void Three_proposals_over_three_works_are_one_group()
    {
        var groups = Build(
            Edge(1, 1, 2, 0.94, priority: true),
            Edge(2, 1, 3, 0.93, priority: true),
            Edge(3, 2, 3, 0.92, priority: true));

        var group = Assert.Single(groups);
        Assert.Equal([1, 2, 3], group.Members.Select(m => m.WorkId).Order());
        Assert.Equal(3, group.Edges.Count);
        Assert.False(group.IsPair);
        Assert.Equal(0.94, group.Score);
        Assert.True(group.IsPriority);
    }

    [Fact]
    public void Disjoint_proposals_stay_separate_cards()
    {
        var groups = Build(
            Edge(1, 1, 2, 0.80, priority: true),
            Edge(2, 3, 4, 0.95, priority: true));

        Assert.Equal(2, groups.Count);

        // Strongest first, so the 0.95 card leads.
        Assert.Equal([3, 4], groups[0].Members.Select(m => m.WorkId).Order());
        Assert.True(groups[0].IsPair);
        Assert.True(groups[1].IsPair);
    }

    /// <summary>
    /// The whole of the "already one game" question, answered by dropping the
    /// edge rather than by rendering a card that cannot be acted on.
    /// </summary>
    [Fact]
    public void A_proposal_whose_sides_resolve_to_one_work_is_dropped()
    {
        var linked = Resolution(child: 2, parent: 1);

        Assert.Empty(Build(linked, Edge(1, 1, 2, 0.94, priority: true)));
    }

    [Fact]
    public void A_link_can_collapse_two_cards_into_one()
    {
        // Without the link these are two separate components: 1-2 and 3-4.
        // Linking 3 under 2 joins them, and the result is one card of three
        // members, because work 3 no longer exists as a distinct member.
        var linked = Resolution(child: 3, parent: 2);

        var group = Assert.Single(Build(
            linked,
            Edge(1, 1, 2, 0.94, priority: true),
            Edge(2, 3, 4, 0.93, priority: true)));

        Assert.Equal([1, 2, 4], group.Members.Select(m => m.WorkId).Order());
    }

    [Fact]
    public void Two_proposals_naming_one_work_are_one_member_not_two()
    {
        // Releases 10 and 11 both sit on work 1. Two proposals name it, and the
        // card must show one member carrying both entries.
        var workOfRelease = new Dictionary<long, long>(WorkOfRelease) { [11] = 1 };

        var group = Assert.Single(MergeGrouping.Build(
            [
                new MergeGroupProposal
                {
                    CandidateId = 1, LeftReleaseId = 10, RightReleaseId = 20,
                    Score = 0.94, IsPriority = true,
                },
                new MergeGroupProposal
                {
                    CandidateId = 2, LeftReleaseId = 11, RightReleaseId = 30,
                    Score = 0.93, IsPriority = true,
                },
            ],
            workOfRelease,
            Works(1, 2, 3),
            SameGameResolution.Empty));

        Assert.Equal(3, group.Members.Count);
        var member = group.Members.Single(m => m.WorkId == 1);
        Assert.Equal([10, 11], member.ReleaseIds);
    }

    // ── Who arrives checked ──────────────────────────────────────────────────

    [Fact]
    public void A_complete_top_band_group_arrives_wholly_checked()
    {
        var group = Assert.Single(Build(
            Edge(1, 1, 2, 0.94, priority: true),
            Edge(2, 1, 3, 0.93, priority: true),
            Edge(3, 2, 3, 0.92, priority: true)));

        Assert.All(group.Members, m => Assert.True(m.IsDefaultIncluded));
    }

    /// <summary>
    /// Prey (2006) and Prey (2017). Two members can each match a third without
    /// matching each other, and the transitive closure must not turn that into
    /// one game on the user's behalf.
    /// </summary>
    [Fact]
    public void A_member_reachable_only_through_a_sibling_arrives_unchecked()
    {
        var group = Assert.Single(Build(
            Edge(1, 1, 2, 0.94, priority: true),
            Edge(2, 2, 3, 0.93, priority: true)));

        Assert.Equal(1, group.PrimaryWorkId);
        Assert.True(group.Members.Single(m => m.WorkId == 2).IsDefaultIncluded);

        // Work 3 has no proposal naming it and work 1 together, so nothing
        // says the two are the same game.
        Assert.False(group.Members.Single(m => m.WorkId == 3).IsDefaultIncluded);
    }

    [Fact]
    public void A_below_band_edge_arrives_unchecked()
    {
        var group = Assert.Single(Build(
            Edge(1, 1, 2, 0.94, priority: true),
            Edge(2, 1, 3, 0.93, priority: true),
            Edge(3, 2, 3, 0.61, priority: false)));

        Assert.True(group.Members.Single(m => m.WorkId == 2).IsDefaultIncluded);

        // Work 3 clears the band against the primary but not against work 2,
        // which is already in. The checked set is a clique, not a component.
        Assert.False(group.Members.Single(m => m.WorkId == 3).IsDefaultIncluded);
    }

    // ── Which title is proposed, and why ─────────────────────────────────────

    [Fact]
    public void The_ladder_runs_across_the_whole_group_not_pairwise()
    {
        var works = new Dictionary<long, SurvivorCandidate>
        {
            [1] = new() { WorkId = 1 },
            [2] = new() { WorkId = 2 },
            [3] = new() { WorkId = 3, HasIgdbId = true },
        };

        var group = Assert.Single(MergeGrouping.Build(
            [
                Proposal(1, 10, 20, 0.94, true),
                Proposal(2, 10, 30, 0.93, true),
            ],
            WorkOfRelease,
            works,
            SameGameResolution.Empty));

        Assert.Equal(3, group.PrimaryWorkId);
        Assert.Equal(MergeSurvivorReason.IgdbMatch, group.PrimaryReason);
        Assert.Equal(3, group.Members[0].WorkId);
    }

    [Fact]
    public void The_reason_is_the_rung_that_separated_the_winner_from_the_rest()
    {
        // Nothing discriminates, so the lowest id wins and the card says so
        // rather than leaving the choice to look arbitrary.
        var group = Assert.Single(Build(
            Edge(1, 1, 2, 0.94, priority: true),
            Edge(2, 1, 3, 0.93, priority: true)));

        Assert.Equal(1, group.PrimaryWorkId);
        Assert.Equal(MergeSurvivorReason.AddedFirst, group.PrimaryReason);
    }

    [Fact]
    public void Choosing_a_title_names_the_user_as_the_reason()
    {
        var decision = MergeGrouping.ChoosePrimary(
            [new SurvivorCandidate { WorkId = 1 }, new SurvivorCandidate { WorkId = 2 }],
            preferredWorkId: 2);

        Assert.Equal(2, decision.SurvivingWorkId);
        Assert.Equal(MergeSurvivorReason.ChosenByYou, decision.Reason);
    }

    [Fact]
    public void Choosing_a_title_outside_the_group_is_refused_not_ignored()
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => MergeGrouping.ChoosePrimary(
            [new SurvivorCandidate { WorkId = 1 }, new SurvivorCandidate { WorkId = 2 }],
            preferredWorkId: 9));

        Assert.Equal(9L, thrown.ActualValue);
    }

    [Fact]
    public void The_order_of_the_proposals_does_not_change_the_result()
    {
        var forwards = Build(
            Edge(1, 1, 2, 0.94, priority: true),
            Edge(2, 2, 3, 0.93, priority: true),
            Edge(3, 1, 3, 0.92, priority: true));

        var backwards = Build(
            Edge(3, 1, 3, 0.92, priority: true),
            Edge(2, 2, 3, 0.93, priority: true),
            Edge(1, 1, 2, 0.94, priority: true));

        Assert.Equal(
            forwards.Single().Members.Select(m => m.WorkId),
            backwards.Single().Members.Select(m => m.WorkId));
        Assert.Equal(forwards.Single().PrimaryWorkId, backwards.Single().PrimaryWorkId);
        Assert.Equal(forwards.Single().PrimaryReason, backwards.Single().PrimaryReason);
    }

    [Fact]
    public void A_proposal_naming_a_release_the_snapshot_cannot_place_is_skipped()
    {
        Assert.Empty(MergeGrouping.Build(
            [Proposal(1, 10, 99, 0.94, true)],
            WorkOfRelease,
            Works(1),
            SameGameResolution.Empty));
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    private static MergeGroupProposal Edge(
        long candidateId, long leftWorkId, long rightWorkId, double score, bool priority)
        => Proposal(candidateId, leftWorkId * 10, rightWorkId * 10, score, priority);

    private static MergeGroupProposal Proposal(
        long candidateId, long leftReleaseId, long rightReleaseId, double score, bool priority)
        => new()
        {
            CandidateId = candidateId,
            LeftReleaseId = leftReleaseId,
            RightReleaseId = rightReleaseId,
            Score = score,
            IsPriority = priority,
        };

    private static IReadOnlyList<MergeGroup> Build(params MergeGroupProposal[] proposals)
        => Build(SameGameResolution.Empty, proposals);

    private static IReadOnlyList<MergeGroup> Build(
        SameGameResolution resolution, params MergeGroupProposal[] proposals)
        => MergeGrouping.Build(
            proposals, WorkOfRelease, Works(1, 2, 3, 4, 5, 6), resolution);

    private static Dictionary<long, SurvivorCandidate> Works(params long[] workIds)
    {
        var works = new Dictionary<long, SurvivorCandidate>(workIds.Length);
        foreach (var workId in workIds)
        {
            works[workId] = new SurvivorCandidate { WorkId = workId };
        }

        return works;
    }

    private static SameGameResolution Resolution(long child, long parent)
        => IdentityResolution.FromLiveLinks(
        [
            new IdentityLink
            {
                Id = 1,
                ActId = 1,
                ChildWorkId = child,
                ParentWorkId = parent,
                Kind = IdentityLinkKinds.SameGame,
                Source = IdentityLinkSources.User,
                AppliedAt = DateTime.UnixEpoch,
            },
        ]).SameGame;
}
