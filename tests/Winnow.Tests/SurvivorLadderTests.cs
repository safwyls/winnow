using Winnow.Core.Merging;
using Xunit;

/// <summary>
/// The survivor ladder (TASK-70.1). Lifted out of
/// <c>MergeExecutionRepository.ChooseWork</c> so it is BCL-only, testable
/// without a database, and survives the retirement of the destructive executor
/// in TASK-70.7. Every rung reports its own reason, the result is
/// order-independent, and a chosen survivor overrides every rung or throws.
/// </summary>
namespace Winnow.Tests;

public class SurvivorLadderTests
{
    private static SurvivorCandidate Work(
        long id, bool igdb = false, bool provisional = false, int releases = 1)
        => new()
        {
            WorkId = id,
            HasIgdbId = igdb,
            NameIsProvisional = provisional,
            ReleaseCount = releases,
        };

    /// <summary>
    /// The first rung: <c>works.igdb_id</c> is UNIQUE, so it cannot be copied
    /// onto the other row. The holder wins even when the other side would win
    /// every later rung.
    /// </summary>
    [Fact]
    public void Holding_an_igdb_id_wins_and_says_so()
    {
        // The older row would win every later rung; the igdb_id outranks all of
        // them, because works.igdb_id is UNIQUE and cannot be copied across.
        var decision = SurvivorLadder.Choose(
            Work(1, provisional: false, releases: 9),
            Work(2, igdb: true, provisional: true, releases: 1));

        Assert.Equal(2, decision.SurvivingWorkId);
        Assert.Equal(1, decision.AbsorbedWorkId);
        Assert.Equal(MergeSurvivorReason.IgdbMatch, decision.Reason);
    }

    /// <summary>
    /// Second rung: a real store title beats a machine-minted placeholder.
    /// </summary>
    [Fact]
    public void A_real_name_beats_a_placeholder_and_says_so()
    {
        var decision = SurvivorLadder.Choose(
            Work(1, provisional: true, releases: 9),
            Work(2, provisional: false, releases: 1));

        Assert.Equal(2, decision.SurvivingWorkId);
        Assert.Equal(MergeSurvivorReason.NamedByStore, decision.Reason);
    }

    /// <summary>
    /// Third rung: the side with more releases already hanging off it wins.
    /// </summary>
    [Fact]
    public void More_store_entries_wins_and_says_so()
    {
        var decision = SurvivorLadder.Choose(
            Work(1, releases: 1),
            Work(2, releases: 3));

        Assert.Equal(2, decision.SurvivingWorkId);
        Assert.Equal(MergeSurvivorReason.MostStoreEntries, decision.Reason);
    }

    /// <summary>
    /// Last rung: nothing else discriminated, so the lower id (ingestion order)
    /// wins. The reason says so honestly rather than staying silent.
    /// </summary>
    [Fact]
    public void A_pair_discriminated_only_by_id_admits_it_was_added_first()
    {
        var decision = SurvivorLadder.Choose(Work(7), Work(3));

        Assert.Equal(3, decision.SurvivingWorkId);
        Assert.Equal(7, decision.AbsorbedWorkId);
        Assert.Equal(MergeSurvivorReason.AddedFirst, decision.Reason);
    }

    /// <summary>
    /// Each rung's reason is distinct from every other's, and none is
    /// <see cref="MergeSurvivorReason.None"/>. A UI that maps reason to copy
    /// can rely on the enum alone.
    /// </summary>
    [Fact]
    public void Every_rung_reports_a_reason_of_its_own()
    {
        var reasons = new[]
        {
            SurvivorLadder.Choose(Work(1), Work(2, igdb: true)).Reason,
            SurvivorLadder.Choose(Work(1, provisional: true), Work(2)).Reason,
            SurvivorLadder.Choose(Work(1), Work(2, releases: 2)).Reason,
            SurvivorLadder.Choose(Work(1), Work(2)).Reason,
            SurvivorLadder.Choose(Work(1), Work(2), preferredWorkId: 2).Reason,
        };

        Assert.Equal(reasons.Length, reasons.Distinct().Count());
        Assert.DoesNotContain(MergeSurvivorReason.None, reasons);
    }

    /// <summary>
    /// Swapping a and b gives the same survivor, the same absorbed side, and
    /// the same reason. The ladder is a function of the pair, not of
    /// presentation order.
    /// </summary>
    [Fact]
    public void The_ladder_is_order_independent()
    {
        SurvivorCandidate[] pool =
        [
            Work(1), Work(2, igdb: true), Work(3, provisional: true), Work(4, releases: 5),
        ];

        foreach (var a in pool)
        {
            foreach (var b in pool)
            {
                if (a.WorkId == b.WorkId)
                {
                    continue;
                }

                var forward = SurvivorLadder.Choose(a, b);
                var reversed = SurvivorLadder.Choose(b, a);

                Assert.Equal(forward.SurvivingWorkId, reversed.SurvivingWorkId);
                Assert.Equal(forward.AbsorbedWorkId, reversed.AbsorbedWorkId);
                Assert.Equal(forward.Reason, reversed.Reason);
            }
        }
    }

    /// <summary>
    /// Both sides name the same work. The absorbed side is null and the reason
    /// is <see cref="MergeSurvivorReason.AlreadyOneGame"/>.
    /// </summary>
    [Fact]
    public void One_work_on_both_sides_is_already_one_game()
    {
        var decision = SurvivorLadder.Choose(Work(5), Work(5));

        Assert.Equal(5, decision.SurvivingWorkId);
        Assert.Null(decision.AbsorbedWorkId);
        Assert.Equal(MergeSurvivorReason.AlreadyOneGame, decision.Reason);
    }

    // ── The survivor-choice contract (the picker lands in TASK-70.3) ─────────

    /// <summary>
    /// A preferred work id overrides the ladder entirely and reports
    /// <see cref="MergeSurvivorReason.ChosenByYou"/>. The ladder would have
    /// kept the other side on the igdb rung.
    /// </summary>
    [Fact]
    public void A_chosen_survivor_overrides_every_rung_and_is_named_as_the_choice()
    {
        // The ladder would keep work 2 on the igdb_id. The user says otherwise.
        var decision = SurvivorLadder.Choose(
            Work(1), Work(2, igdb: true), preferredWorkId: 1);

        Assert.Equal(1, decision.SurvivingWorkId);
        Assert.Equal(2, decision.AbsorbedWorkId);
        Assert.Equal(MergeSurvivorReason.ChosenByYou, decision.Reason);
    }

    /// <summary>
    /// A preferred work that is neither side of the pair throws rather than
    /// being silently ignored. <see cref="SurvivorLadder.NamesOneOf"/> is
    /// the same check as a boolean, for callers on a read path.
    /// </summary>
    [Fact]
    public void A_choice_naming_neither_side_is_refused_not_ignored()
    {
        Assert.False(SurvivorLadder.NamesOneOf(99, 1, 2));
        Assert.True(SurvivorLadder.NamesOneOf(null, 1, 2));
        Assert.True(SurvivorLadder.NamesOneOf(2, 1, 2));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SurvivorLadder.Choose(Work(1), Work(2), preferredWorkId: 99));
    }

    /// <summary>
    /// The validation runs before the same-work shortcut: a preference must
    /// name one of the two works even when they are already one, and a value
    /// naming neither still throws. Naming the shared work is fine and
    /// returns <see cref="MergeSurvivorReason.AlreadyOneGame"/>.
    /// </summary>
    [Fact]
    public void A_choice_must_name_one_of_the_two_works_even_when_they_are_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SurvivorLadder.Choose(Work(5), Work(5), preferredWorkId: 99));

        var decision = SurvivorLadder.Choose(Work(5), Work(5), preferredWorkId: 5);
        Assert.Equal(5, decision.SurvivingWorkId);
        Assert.Equal(MergeSurvivorReason.AlreadyOneGame, decision.Reason);
    }
}
