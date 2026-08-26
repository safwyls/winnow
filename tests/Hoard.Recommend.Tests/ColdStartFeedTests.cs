using Hoard.Core.Queries;
using Xunit;

namespace Hoard.Recommend.Tests;

/// <summary>
/// The charter's acceptance bar made executable: a library with ONE snapshot
/// per ownership and ZERO sessions — the measured shape of the real database
/// today — must still produce a sensible ranked feed, because a model that
/// only works after six months of history is a model nobody ever sees work.
/// </summary>
public class ColdStartFeedTests : IAsyncLifetime, IDisposable
{
    private readonly RecommendHarness _harness = new();
    private RecommendationFeed _feed = null!;

    private SeededGame _patchedBounce = null!;
    private SeededGame _freshBounce = null!;
    private SeededGame _oldBounce = null!;
    private SeededGame _sampled = null!;
    private SeededGame _shelfware = null!;
    private SeededGame _retired = null!;
    private SeededGame _playedYesterday = null!;
    private SeededGame _ancientSentinel = null!;

    public async Task InitializeAsync()
    {
        var asOf = RecommendHarness.AsOf;

        // The cast, one per §6.1 pile. Every ownership gets exactly one
        // snapshot (the harness seeds it), and no sessions exist anywhere.
        _patchedBounce = await _harness.SeedGameAsync("Empyrion-alike", minutes: 40, lastPlayed: asOf.AddYears(-3));
        await _harness.SeedMajorUpdateAsync(_patchedBounce, asOf.AddMonths(-1), "v2.0 Overhaul");

        _freshBounce = await _harness.SeedGameAsync("Bounced Recently", minutes: 300, lastPlayed: asOf.AddYears(-1));
        _oldBounce = await _harness.SeedGameAsync("Bounced Long Ago", minutes: 300, lastPlayed: asOf.AddYears(-7));
        _sampled = await _harness.SeedGameAsync("Forty Minutes Once", minutes: 40, lastPlayed: asOf.AddYears(-2));
        _shelfware = await _harness.SeedGameAsync("Never Opened");

        // Retired AND patched — the §6.1 precedence trap: retired must win.
        _retired = await _harness.SeedGameAsync("Finished 200h Game", minutes: 12_000, lastPlayed: asOf.AddYears(-2));
        await _harness.SeedMajorUpdateAsync(_retired, asOf.AddMonths(-1), "Anniversary Update");

        _playedYesterday = await _harness.SeedGameAsync("Playing It Now", minutes: 600, lastPlayed: asOf.AddDays(-1));

        // Steam's pre-timestamp sentinel: real minutes, no date.
        _ancientSentinel = await _harness.SeedGameAsync("Pre-2009 Relic", minutes: 500, lastPlayed: null);

        _feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void The_feed_is_not_blank_until_ready()
    {
        Assert.NotEmpty(_feed.Items);
        Assert.Equal(7, _feed.CandidateCount); // 8 seeded, retired excluded
    }

    [Fact]
    public void One_snapshot_and_no_sessions_is_detected_as_cold_start()
        => Assert.Equal(DataTier.ColdStart, _feed.Tier);

    [Fact]
    public void The_patched_bounce_leads_the_feed()
        => Assert.Equal(_patchedBounce.ReleaseId, _feed.Items[0].ReleaseId);

    [Fact]
    public void The_retired_game_is_absent_even_though_it_was_patched()
        => Assert.DoesNotContain(_feed.Items, i => i.ReleaseId == _retired.ReleaseId);

    [Fact]
    public void The_game_played_yesterday_ranks_last_among_surfaced_items()
    {
        // Not excluded — the feed does not pretend it isn't owned — but the
        // fresh-play penalty must sink it below every dormant candidate.
        var index = _feed.Items.ToList().FindIndex(i => i.ReleaseId == _playedYesterday.ReleaseId);
        Assert.Equal(_feed.Items.Count - 1, index);
    }

    [Fact]
    public void Dormant_bounces_outrank_shelfware_and_the_sampled_taste()
    {
        var order = _feed.Items.Select(i => i.ReleaseId).ToList();
        Assert.True(order.IndexOf(_freshBounce.ReleaseId) < order.IndexOf(_shelfware.ReleaseId),
            "a committed-then-abandoned game says more than a never-opened one");
        Assert.True(order.IndexOf(_oldBounce.ReleaseId) < order.IndexOf(_shelfware.ReleaseId));
        Assert.True(order.IndexOf(_sampled.ReleaseId) < order.IndexOf(_shelfware.ReleaseId),
            "forty minutes of intent still beats zero");
    }

    [Fact]
    public void The_ancient_sentinel_is_ranked_as_deeply_dormant_not_dropped()
    {
        var order = _feed.Items.Select(i => i.ReleaseId).ToList();
        var sentinel = order.IndexOf(_ancientSentinel.ReleaseId);
        Assert.True(sentinel >= 0, "unknown-date rows must not vanish from the feed");
        Assert.True(sentinel < order.IndexOf(_shelfware.ReleaseId),
            "played-but-undated is a bounced game with maximal dormancy");
    }

    [Fact]
    public void Every_item_explains_itself()
    {
        foreach (var item in _feed.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Reason));
            Assert.NotEmpty(item.Signals);
            Assert.Equal(item.Signals.Sum(s => s.Contribution), item.Score, precision: 10);
        }
    }

    [Fact]
    public void The_patched_lead_reason_reads_like_the_charter_example()
    {
        var reason = _feed.Items[0].Reason;
        // "You put 40 minutes into this in 2023 and it has had an update
        // since, most recently 'v2.0 Overhaul'." — minutes, year, and the
        // patch fact fused into one interrogable sentence.
        Assert.Contains("40 minutes", reason);
        Assert.Contains("2023", reason);
        Assert.Contains("v2.0 Overhaul", reason);
    }

    [Fact]
    public async Task The_feed_is_deterministic_for_identical_requests()
    {
        var again = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());
        Assert.Equal(_feed.Items.Select(i => i.ReleaseId), again.Items.Select(i => i.ReleaseId));
        Assert.Equal(_feed.Items.Select(i => i.Score), again.Items.Select(i => i.Score));
    }
}
