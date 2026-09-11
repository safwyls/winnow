using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Http;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests.Igdb;

/// <summary>
/// TASK-121. An IGDB id typed into the wrong-game search returned a candidate
/// row showing a year and no platforms, while every title-search row showed
/// both: the id lookup rides the shared <c>games</c> query, which did not ask
/// for <c>platforms.name</c>, and the search has its own body that always has.
/// The shared query asks for it now.
///
/// <para>An Apicalypse <c>fields</c> clause costs the same request whatever it
/// lists, so platforms ride calls the metadata pass was already making. What
/// they do cost is the cached shape, so the payload version moved with them —
/// and these tests hold both halves: the field reaches the domain model, and
/// the bump neither loses cached data nor exceeds the measured refetch.</para>
/// </summary>
public sealed class IgdbPlatformFieldTests
{
    /// <summary>The author's library: 967 games, so the refetch cost is measured against a real number.</summary>
    private const int LibrarySize = 967;

    private static readonly string[] FixturePlatforms = ["PC (Microsoft Windows)", "PlayStation 4"];

    /// <summary>
    /// The field joins the shared body rather than displacing anything already
    /// on it. A <c>fields</c> clause that gained platforms and dropped genres
    /// would trade one empty column in the UI for another.
    /// </summary>
    [Fact]
    public void The_shared_game_query_asks_for_platforms()
    {
        var query = Apicalypse.Games([1, 2, 3], limit: 500, offset: 0);

        Assert.Contains("platforms.name", query, StringComparison.Ordinal);

        Assert.Contains("name,summary,first_release_date", query, StringComparison.Ordinal);
        Assert.Contains("genres.name", query, StringComparison.Ordinal);
        Assert.Contains("themes.name", query, StringComparison.Ordinal);
        Assert.Contains("game_modes.name", query, StringComparison.Ordinal);
        Assert.Contains("player_perspectives.name", query, StringComparison.Ordinal);
        Assert.Contains("involved_companies.company.name", query, StringComparison.Ordinal);
        Assert.Contains("game_type.type", query, StringComparison.Ordinal);
    }

    /// <summary>
    /// The names reach the domain model, not just the wire model, and they
    /// arrive on the one request the metadata pass was already making.
    /// </summary>
    [Fact]
    public async Task Game_metadata_carries_the_platforms()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Equal(FixturePlatforms, game.Platforms);
        Assert.Equal(1, host.Handler.CountFor("games"));
    }

    /// <summary>
    /// The warm read answers with what the cold read fetched. The cached
    /// payload is where a new field silently goes missing, because the second
    /// read never touches the network to notice.
    /// </summary>
    [Fact]
    public async Task Platforms_survive_the_cache_round_trip()
    {
        var cache = new InMemoryMetadataCache();
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache);

        var cold = Assert.Single(await host.Client.GetGamesAsync([100_440]));
        var warm = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Equal(1, host.Handler.CountFor("games"));
        Assert.Equal(FixturePlatforms, cold.Platforms);
        Assert.Equal(FixturePlatforms, warm.Platforms);
    }

    /// <summary>
    /// The reported defect, stated as an assertion. The candidate list draws
    /// id-matched and title-matched rows side by side, so the two must shape
    /// the same facts — which is why both paths go through
    /// <c>IgdbJson.PlatformNames</c> rather than shaping platforms each their
    /// own way.
    /// </summary>
    [Fact]
    public async Task An_id_matched_row_carries_the_same_facts_as_a_title_search_row()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());
        var assignment = new IgdbManualAssignment(
            host.Client, new NoPinRepository(), new UnusedIgdbObservationWriter(), NullLogger<IgdbManualAssignment>.Instance);

        IReadOnlyList<IgdbSearchResult> searchResults = await assignment.SearchAsync("Prey");
        var searched = searchResults[0];
        var byId = await assignment.GetByIdAsync(100_440);

        Assert.NotEmpty(searched.Platforms);

        Assert.NotNull(byId);
        Assert.NotNull(byId.Name);
        Assert.NotNull(byId.CoverUrl);
        Assert.NotNull(byId.FirstReleaseYear);
        Assert.NotEmpty(byId.Platforms);

        Assert.Equal(searched.Platforms, byId.Platforms);
    }

    /// <summary>
    /// Version 2 is the shape on every current install. A payload written
    /// under it is refetched rather than served with platforms silently empty
    /// for the rest of the 30-day TTL, and the answer is stored under version
    /// 3 so the next read is a hit again — one refetch per game, not one per
    /// run.
    /// </summary>
    [Fact]
    public async Task A_payload_written_under_the_previous_version_is_refetched_and_rewritten()
    {
        var cache = new InMemoryMetadataCache();
        await cache.SetAsync(
            IgdbClient.CacheProvider,
            IgdbClient.GameCacheKey(100_440),
            PreviousVersionEnvelope,
            DateTime.UtcNow);

        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache);

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Equal(1, host.Handler.CountFor("games"));
        Assert.Equal("Game 100440", game.Name);
        Assert.Equal(FixturePlatforms, game.Platforms);

        var stored = await cache.GetAsync(IgdbClient.CacheProvider, IgdbClient.GameCacheKey(100_440));
        Assert.NotNull(stored);
        using var document = JsonDocument.Parse(stored.Value.PayloadJson!);
        Assert.Equal(
            IgdbClient.GamePayloadVersion, document.RootElement.GetProperty("version").GetInt32());
    }

    /// <summary>
    /// The bump is the whole mechanism, so it is asserted as a value.
    /// Widening the query without moving this number leaves the cache serving
    /// platform-less rows for the rest of the TTL (§4.4).
    /// </summary>
    [Fact]
    public void The_game_payload_version_moved_past_the_shape_that_had_no_platforms()
        => Assert.True(IgdbClient.GamePayloadVersion >= 3);

    /// <summary>
    /// The guarantee the bump must not repeal, and the trap it nearly sprang.
    /// The superseded-payload fallback read only the bare pre-envelope shape,
    /// and a version-2 envelope read that way yields <c>IgdbId</c> 0 and is
    /// dropped — so on an install with no Twitch credentials and no network
    /// the bump would have turned every cached game into nothing at all
    /// rather than into a row missing one field. The stale row is served, with
    /// platforms honestly empty.
    /// </summary>
    [Fact]
    public async Task A_payload_written_under_the_previous_version_is_still_served_when_no_refetch_is_possible()
    {
        var cache = new InMemoryMetadataCache();
        await cache.SetAsync(
            IgdbClient.CacheProvider,
            IgdbClient.GameCacheKey(100_440),
            PreviousVersionEnvelope,
            DateTime.UtcNow);

        using var host = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), cache: cache, clientId: null, clientSecret: null);

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Empty(host.Handler.Requests);
        Assert.Equal("Stale Title", game.Name);
        Assert.Equal("main_game", game.GameType);
        Assert.Empty(game.Platforms);
    }

    /// <summary>
    /// Half of the recorded cost of the version bump: the whole 967-game
    /// library asked for in one call, the way <c>FacetSyncService</c> asks.
    /// Three requests — batches of 400, 400 and 167, because
    /// <c>BatchSize</c> is 400 and a batch fits inside one 500-row page — and
    /// three requests fit inside the limiter's 4-permit bucket, so the rate
    /// limiter adds no delay at all. Measured at 158 ms end to end through the
    /// real client and the real limiter against canned fixtures.
    /// </summary>
    [Fact]
    public async Task The_whole_library_refetches_in_three_requests_when_it_is_asked_for_at_once()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());
        var limiter = host.Resolve<IgdbRateLimiter>();

        var stopwatch = Stopwatch.StartNew();
        var games = await host.Client.GetGamesAsync(
            Enumerable.Range(1, LibrarySize).Select(i => (long)(100_000 + i)));
        stopwatch.Stop();

        Assert.Equal(LibrarySize, games.Count);
        Assert.Equal(3, host.Handler.CountFor("games"));

        var batches = host.Handler.Requests
            .Where(r => r.Endpoint == "games")
            .Select(r => IgdbFixtures.RequestedIds(r.Body).Count)
            .ToArray();
        Assert.Equal([400, 400, 167], batches);

        Assert.Equal(0, limiter.QueuedRequests);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"three requests fit inside the 4-permit bucket, but the refetch took "
            + $"{stopwatch.ElapsedMilliseconds} ms.");
    }

    /// <summary>
    /// The other half: the same library asked for in 40-target slices, the way
    /// <c>EnrichmentSyncService</c> asks. 25 requests, measured at 6 seconds
    /// wall clock. Those six seconds are the token bucket's own arithmetic and
    /// not a slow test — a 4-permit bucket refilled 4 tokens a second puts the
    /// 25th permit at t=6s. The two passes share the cache, so the one-time
    /// cost of the bump is between 3 and 25 requests, whichever pass reaches
    /// an id first.
    /// </summary>
    [Fact]
    public async Task The_whole_library_refetches_in_twenty_five_requests_when_it_is_asked_for_in_slices()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var stopwatch = Stopwatch.StartNew();
        var seen = 0;
        foreach (var slice in Enumerable.Range(1, LibrarySize)
                     .Select(i => (long)(100_000 + i))
                     .Chunk(EnrichmentSliceSize))
        {
            seen += (await host.Client.GetGamesAsync(slice)).Count;
        }

        stopwatch.Stop();

        Assert.Equal(LibrarySize, seen);
        Assert.Equal(25, host.Handler.CountFor("games"));

        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromSeconds(5),
            $"25 requests at 4/s cannot finish in {stopwatch.ElapsedMilliseconds} ms — "
            + "the limiter did not space them.");
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"25 requests at 4/s took {stopwatch.ElapsedMilliseconds} ms.");
    }

    private const int EnrichmentSliceSize = 40;

    /// <summary>
    /// The exact shape a game payload carries on a current install: a
    /// version-2 envelope around the domain record. Written out by hand rather
    /// than serialized from <c>IgdbGame</c>, so a later change to the record
    /// cannot quietly redefine what "the previous version" was.
    /// </summary>
    private const string PreviousVersionEnvelope =
        """
        {"version":2,"game":{"igdb_id":100440,"name":"Stale Title","cover_url":null,
        "first_release_year":2008,"summary":"A canned summary.","genres":["Shooter"],
        "themes":["Action"],"publishers":["Valve"],"game_modes":[],"player_perspectives":[],
        "game_type":"main_game","parent_game_id":null,"version_parent_id":null,"version_title":null}}
        """;

    private sealed class NoPinRepository : IWorkIgdbPinRepository
    {
        public Task<WorkIgdbPinOutcome> PinAsync(
            WorkIgdbPinAssignment assignment, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ClearAsync(long workId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<WorkIgdbPin?> GetAsync(long workId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlySet<long>> GetLivePinnedWorkIdsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
