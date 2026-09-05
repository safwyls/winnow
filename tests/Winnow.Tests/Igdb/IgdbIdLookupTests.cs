using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Xunit;

namespace Winnow.Tests.Igdb;

/// <summary>
/// Covers the IGDB id lookup at both layers:
/// <see cref="IgdbManualAssignment.GetByIdAsync"/> in the enrichment
/// module and <see cref="IgdbAssignmentService.GetCandidateByIdAsync"/>,
/// the App-layer seam in front of it (TASK-120, acceptance criterion 5).
///
/// <para>The load-bearing assertion is which client call is made: the
/// lookup must ride the shared <see cref="IIgdbClient.GetGamesAsync"/>
/// path and its cache, never <see cref="IIgdbClient.SearchGamesAsync"/>,
/// so the recording client fails the test if the id lookup ever grows a
/// query of its own.</para>
///
/// <para>A miss, a non-positive id and a transport failure are all null;
/// a cancellation on the caller's own token is not. No socket is opened:
/// the client is a recording stand-in, per the charter's rule that HTTP
/// clients are tested against canned responses.</para>
/// </summary>
public sealed class IgdbIdLookupTests
{
    private const long KnownId = 103298;

    [Fact]
    public async Task An_id_IGDB_knows_comes_back_as_that_one_game()
    {
        var client = new RecordingIgdbClient();
        client.Games[KnownId] = Game(KnownId, "Prey", 2017);
        var assignment = NewAssignment(client);

        var result = await assignment.GetByIdAsync(KnownId);

        Assert.NotNull(result);
        Assert.Equal(KnownId, result.IgdbId);
        Assert.Equal("Prey", result.Name);
        Assert.Equal(2017, result.FirstReleaseYear);
        Assert.Equal(
            "https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg", result.CoverUrl);
        Assert.Empty(result.Platforms);
    }

    [Fact]
    public async Task An_id_IGDB_has_no_record_for_is_null()
    {
        var client = new RecordingIgdbClient();
        var assignment = NewAssignment(client);

        Assert.Null(await assignment.GetByIdAsync(KnownId));
        Assert.Equal([KnownId], client.GameIdsAsked);
    }

    [Fact]
    public async Task An_id_the_client_answers_with_a_different_game_is_null()
    {
        var client = new RecordingIgdbClient();
        client.Games[KnownId] = Game(4242, "Something else", 2011);
        var assignment = NewAssignment(client);

        // The method takes the element whose id actually matches, so a batch
        // response carrying some other game is a miss — not a wrong candidate.
        Assert.Null(await assignment.GetByIdAsync(KnownId));
    }

    [Fact]
    public async Task A_failed_lookup_is_null_rather_than_an_exception()
    {
        var client = new RecordingIgdbClient { Throw = new HttpRequestException("no route to host") };
        var assignment = NewAssignment(client);

        Assert.Null(await assignment.GetByIdAsync(KnownId));
    }

    [Fact]
    public async Task Cancellation_on_the_callers_own_token_still_propagates()
    {
        var client = new RecordingIgdbClient();
        client.Games[KnownId] = Game(KnownId, "Prey", 2017);
        var assignment = NewAssignment(client);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => assignment.GetByIdAsync(KnownId, cts.Token));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public async Task A_non_positive_id_never_reaches_the_client(long igdbId)
    {
        var client = new RecordingIgdbClient();
        var assignment = NewAssignment(client);

        Assert.Null(await assignment.GetByIdAsync(igdbId));
        Assert.Empty(client.GameIdsAsked);
        Assert.Empty(client.TitlesSearched);
    }

    [Fact]
    public async Task The_id_lookup_rides_the_shared_game_query_not_the_title_search()
    {
        var client = new RecordingIgdbClient();
        client.Games[KnownId] = Game(KnownId, "Prey", 2017);
        var assignment = NewAssignment(client);

        await assignment.GetByIdAsync(KnownId);

        Assert.Equal([KnownId], client.GameIdsAsked);
        Assert.Empty(client.TitlesSearched);
    }

    [Fact]
    public async Task The_App_seam_maps_a_hit_onto_a_candidate()
    {
        var client = new RecordingIgdbClient();
        client.Games[KnownId] = Game(KnownId, "Prey", 2017);
        IIgdbAssignmentService service = new IgdbAssignmentService(NewAssignment(client));

        var candidate = await service.GetCandidateByIdAsync(KnownId);

        Assert.NotNull(candidate);
        Assert.Equal(KnownId, candidate.IgdbId);
        Assert.Equal("Prey", candidate.Name);
        Assert.Equal(2017, candidate.FirstReleaseYear);
        Assert.Equal(
            "https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg", candidate.CoverUrl);
        Assert.Empty(candidate.Platforms);
    }

    [Fact]
    public async Task The_App_seam_answers_null_for_a_miss_and_for_a_failure()
    {
        var empty = new RecordingIgdbClient();
        var broken = new RecordingIgdbClient { Throw = new HttpRequestException("no route to host") };

        IIgdbAssignmentService missed = new IgdbAssignmentService(NewAssignment(empty));
        IIgdbAssignmentService failed = new IgdbAssignmentService(NewAssignment(broken));

        Assert.Null(await missed.GetCandidateByIdAsync(KnownId));
        Assert.Null(await failed.GetCandidateByIdAsync(KnownId));
    }

    private static IgdbManualAssignment NewAssignment(IIgdbClient client)
        => new(client, new NoPinRepository(), NullLogger<IgdbManualAssignment>.Instance);

    private static IgdbGame Game(long igdbId, string name, int year)
        => new(
            igdbId,
            name,
            "https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg",
            year,
            "Morgan Yu wakes on Talos I.",
            ["Shooter"],
            ["Horror"],
            ["Bethesda Softworks"]);

    /// <summary>
    /// Stand-in that answers from a canned game table and records what it
    /// was asked. "Went through the id lookup" and "went through the title
    /// search" are the same endpoint at the transport level; only the call
    /// log tells them apart. <see cref="Throw"/> stands for a dead endpoint.
    /// </summary>
    private sealed class RecordingIgdbClient : IIgdbClient
    {
        public Dictionary<long, IgdbGame> Games { get; } = [];

        public List<long> GameIdsAsked { get; } = [];

        public List<string> TitlesSearched { get; } = [];

        public Exception? Throw { get; set; }

        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(
            IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, IgdbExternalMatch>>(
                new Dictionary<string, IgdbExternalMatch>(StringComparer.Ordinal));

        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(
            int externalGameSourceId,
            IEnumerable<string> uids,
            TimeSpan? cacheTtl = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, IgdbExternalMatch>>(
                new Dictionary<string, IgdbExternalMatch>(StringComparer.Ordinal));

        public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(
            IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var wanted = igdbIds.ToArray();
            GameIdsAsked.AddRange(wanted);

            if (Throw is { } boom)
            {
                return Task.FromException<IReadOnlyList<IgdbGame>>(boom);
            }

            return Task.FromResult<IReadOnlyList<IgdbGame>>(
                [.. wanted.Where(Games.ContainsKey).Select(id => Games[id])]);
        }

        public Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(
            IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<long, IgdbAgeRatings>>(
                new Dictionary<long, IgdbAgeRatings>());

        public Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(
            string title, int limit = 0, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            TitlesSearched.Add(title);
            return Task.FromResult<IReadOnlyList<IgdbSearchResult>>([]);
        }
    }

    /// <summary>
    /// <see cref="IgdbManualAssignment.GetByIdAsync"/> reads IGDB and never
    /// touches the pin table, so every member throws — if one is ever
    /// called, that is the bug this stub is here to catch.
    /// </summary>
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
