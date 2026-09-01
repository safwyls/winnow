using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// F33: the maturity tier is a claim about the LIBRARY, so it may not be read
/// off a sample chosen for a different purpose. The candidate shortlist
/// excludes, by design, exactly the games being played; the recently-played
/// rows are the densest in sessions. Neither is the library.
/// </summary>
public class MaturityTierTests : IDisposable
{
    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Sessions_spread_across_more_titles_than_any_probe_still_read_as_established()
    {
        var asOf = RecommendHarness.AsOf;

        // Seventy patched bounces: the strongest signal in the model, so they
        // occupy the whole candidate shortlist, and the most recently played
        // rows in the library, so they occupy the recent probe too. Not one of
        // them has a session.
        for (var i = 0; i < 70; i++)
        {
            var loud = await _harness.SeedGameAsync(
                $"Loud Candidate {i:00}", minutes: 200, lastPlayed: asOf.AddYears(-1));
            await _harness.SeedMajorUpdateAsync(loud, asOf.AddMonths(-1), $"Update {i}");
        }

        // The user's actual history: one session each on a hundred older games,
        // spread over seven months. A hundred sessions across a hundred titles
        // is a settled, months-in library — and every one of them sits on a row
        // the feed ranks below the patched pile.
        for (var i = 0; i < 100; i++)
        {
            var quiet = await _harness.SeedGameAsync(
                $"Quiet History {i:00}", minutes: 150, lastPlayed: asOf.AddYears(-5));
            await _harness.SeedSessionAsync(quiet, asOf.AddDays(-300 + (i * 2)));
        }

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request(maxResults: 20));

        // The candidate probe saw none of it: the shortlist is patched games.
        Assert.All(
            feed.Items,
            item => Assert.StartsWith("Loud Candidate", item.Title, StringComparison.Ordinal));

        Assert.Equal(DataTier.Established, feed.Tier);
    }

    [Fact]
    public async Task A_global_aggregate_decides_the_tier_when_one_is_available()
    {
        // The exact answer, when the data layer can give one: no sampling, no
        // scaling, no dependence on which rows the feed happened to rank.
        await _harness.SeedGameAsync("Anything", minutes: 200, lastPlayed: RecommendHarness.AsOf.AddYears(-2));

        var engine = _harness.EngineWith(new FixedHistoryStats(new LibraryHistoryStats
        {
            SessionCount = 400,
            FirstSessionAt = RecommendHarness.AsOf.AddDays(-200),
            LastSessionAt = RecommendHarness.AsOf.AddDays(-1),
            OwnershipsWithSnapshotRises = 120,
        }));

        var feed = await engine.GetFeedAsync(RecommendHarness.Request());

        Assert.Equal(DataTier.Established, feed.Tier);
    }

    [Fact]
    public async Task An_empty_global_aggregate_is_cold_start_however_the_feed_ranks()
    {
        await _harness.SeedGameAsync("Anything", minutes: 200, lastPlayed: RecommendHarness.AsOf.AddYears(-2));

        var engine = _harness.EngineWith(new FixedHistoryStats(LibraryHistoryStats.Empty));

        Assert.Equal(DataTier.ColdStart, (await engine.GetFeedAsync(RecommendHarness.Request())).Tier);
    }

    /// <summary>Stands in for the data layer's single aggregate query until one is wired up.</summary>
    private sealed class FixedHistoryStats : ILibraryHistoryStatsRepository
    {
        private readonly LibraryHistoryStats _stats;

        public FixedHistoryStats(LibraryHistoryStats stats) => _stats = stats;

        public Task<LibraryHistoryStats> GetAsync(CancellationToken ct = default)
            => Task.FromResult(_stats);
    }
}
