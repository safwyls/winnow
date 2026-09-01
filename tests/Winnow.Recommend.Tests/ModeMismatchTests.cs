using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// The mode-mismatch signal: a library whose committed games sit
/// overwhelmingly on one side of the single-player/online line should see
/// candidates from the other side demoted, with the sentence that says why.
/// Measured motivation: the real library is 93% single-player by committed
/// game count and holds 12 never-opened multiplayer-only titles — every one
/// of them a wrong thing to lead a feed with.
/// </summary>
public class ModeMismatchTests : IDisposable
{
    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static DateTime AsOf => RecommendHarness.AsOf;

    /// <summary>Evidence floor lowered so the fixture stays readable; the arithmetic under test is identical.</summary>
    private static RecommendationRequest Request() => RecommendHarness.Request() with
    {
        Tuning = new RecommendationTuning { ModeEvidenceMinGames = 3 },
    };

    private async Task SeedSoloDominatedLibraryAsync()
    {
        for (var i = 0; i < 4; i++)
        {
            var game = await _harness.SeedGameAsync($"Solo Story {i}", minutes: 1_000, lastPlayed: AsOf.AddYears(-1));
            await _harness.SeedModesAsync(game, GameModes.SinglePlayer);
        }
    }

    [Fact]
    public async Task Online_only_games_are_demoted_in_a_single_player_library()
    {
        await SeedSoloDominatedLibraryAsync();
        var mmo = await _harness.SeedGameAsync("Sealed MMO");
        await _harness.SeedModesAsync(mmo, GameModes.Multiplayer, GameModes.Mmo);
        var solo = await _harness.SeedGameAsync("Sealed Story");
        await _harness.SeedModesAsync(solo, GameModes.SinglePlayer);

        var feed = await _harness.Engine.GetFeedAsync(Request());

        var mmoItem = feed.Items.Single(i => i.ReleaseId == mmo.ReleaseId);
        var soloItem = feed.Items.Single(i => i.ReleaseId == solo.ReleaseId);

        var penalty = mmoItem.Signals.Single(s => s.Signal == SignalNames.ModeMismatch);
        Assert.True(penalty.Contribution < 0);
        Assert.Contains("single-player", penalty.Explanation);
        // The demotion is honest out loud: the one sentence still carries it.
        Assert.Equal(ReasonSignal.OnlineOnlyMismatch, mmoItem.Explanation.Secondary);
        Assert.NotEqual(
            ReasonBuilder.Build(
                mmoItem.Explanation with { Secondary = ReasonSignal.None },
                RecommendationTuning.Default),
            mmoItem.Reason);

        Assert.DoesNotContain(soloItem.Signals, s => s.Signal == SignalNames.ModeMismatch);
        Assert.True(soloItem.Score > mmoItem.Score);
    }

    [Fact]
    public async Task Mismatched_games_are_kept_off_the_taste_shelf()
    {
        await SeedSoloDominatedLibraryAsync();

        // Both sealed games carry the beloved genre; only the solo one may
        // wear "right up your alley".
        var anchor = await _harness.SeedGameAsync("Beloved Survival", minutes: 3_000, lastPlayed: AsOf.AddYears(-1));
        await _harness.SeedGenreAsync(anchor, "Survival");

        var mmo = await _harness.SeedGameAsync("Sealed Survival MMO");
        await _harness.SeedGenreAsync(mmo, "Survival");
        await _harness.SeedModesAsync(mmo, GameModes.Multiplayer, GameModes.Mmo);

        var solo = await _harness.SeedGameAsync("Sealed Survival Story");
        await _harness.SeedGenreAsync(solo, "Survival");
        await _harness.SeedModesAsync(solo, GameModes.SinglePlayer);

        var feed = await _harness.Engine.GetShelvesAsync(Request());

        var shelf = feed.Shelves.Single(s => s.Id == ShelfIds.OnYourTaste);
        Assert.Contains(shelf.Items, i => i.ReleaseId == solo.ReleaseId);
        Assert.DoesNotContain(shelf.Items, i => i.ReleaseId == mmo.ReleaseId);
    }

    [Fact]
    public async Task No_dominance_no_penalty()
    {
        // Two solo, two online among the committed: no side dominates, so
        // neither kind of candidate is punished — a mixed library holds
        // both tastes for real.
        for (var i = 0; i < 2; i++)
        {
            var solo = await _harness.SeedGameAsync($"Solo Story {i}", minutes: 1_000, lastPlayed: AsOf.AddYears(-1));
            await _harness.SeedModesAsync(solo, GameModes.SinglePlayer);
            var online = await _harness.SeedGameAsync($"Online Nights {i}", minutes: 1_000, lastPlayed: AsOf.AddYears(-1));
            await _harness.SeedModesAsync(online, GameModes.Multiplayer);
        }

        var mmo = await _harness.SeedGameAsync("Sealed MMO");
        await _harness.SeedModesAsync(mmo, GameModes.Multiplayer, GameModes.Mmo);

        var feed = await _harness.Engine.GetFeedAsync(Request());

        var item = feed.Items.Single(i => i.ReleaseId == mmo.ReleaseId);
        Assert.DoesNotContain(item.Signals, s => s.Signal == SignalNames.ModeMismatch);
    }

    [Fact]
    public async Task Co_op_without_versus_is_not_online_only()
    {
        // Couch co-op beside a solo library is a maybe, not a mistake: the
        // penalty is reserved for the competitive trio.
        await SeedSoloDominatedLibraryAsync();
        var coop = await _harness.SeedGameAsync("Two Crowns");
        await _harness.SeedModesAsync(coop, GameModes.CoOperative);

        var feed = await _harness.Engine.GetFeedAsync(Request());

        var item = feed.Items.Single(i => i.ReleaseId == coop.ReleaseId);
        Assert.DoesNotContain(item.Signals, s => s.Signal == SignalNames.ModeMismatch);
    }

    [Fact]
    public async Task Below_the_evidence_floor_nothing_fires()
    {
        await SeedSoloDominatedLibraryAsync(); // 4 games — under the default floor of 20
        var mmo = await _harness.SeedGameAsync("Sealed MMO");
        await _harness.SeedModesAsync(mmo, GameModes.Multiplayer, GameModes.Mmo);

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request()); // default tuning

        var item = feed.Items.Single(i => i.ReleaseId == mmo.ReleaseId);
        Assert.DoesNotContain(item.Signals, s => s.Signal == SignalNames.ModeMismatch);
    }

    [Fact]
    public async Task The_mirror_direction_fires_for_online_dominated_libraries()
    {
        for (var i = 0; i < 4; i++)
        {
            var online = await _harness.SeedGameAsync($"Online Nights {i}", minutes: 1_000, lastPlayed: AsOf.AddYears(-1));
            await _harness.SeedModesAsync(online, GameModes.Multiplayer);
        }

        var solo = await _harness.SeedGameAsync("Sealed Story");
        await _harness.SeedModesAsync(solo, GameModes.SinglePlayer);

        var feed = await _harness.Engine.GetFeedAsync(Request());

        var item = feed.Items.Single(i => i.ReleaseId == solo.ReleaseId);
        var penalty = item.Signals.Single(s => s.Signal == SignalNames.ModeMismatch);
        Assert.Contains("online", penalty.Explanation);
    }
}
