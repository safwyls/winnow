using System.Text.Json;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests.Igdb;

/// <summary>
/// The IGDB half of TASK-70.10. An Apicalypse
/// <c>fields</c> clause costs the same request whatever it lists, so
/// <c>game_type</c>, <c>parent_game</c>, <c>version_parent</c> and
/// <c>version_title</c> ride the call that was already fetching the publisher —
/// no new request, and IGDB's own answer to the question the title heuristic
/// was guessing at.
/// </summary>
public sealed class IgdbRelationFieldTests
{
    /// <summary>
    /// The query names the replacement, not the deprecation, and asks for the
    /// label field IGDB actually publishes it under.
    /// </summary>
    [Fact]
    public void The_games_query_asks_for_game_type_parent_and_version_parent()
    {
        var query = Apicalypse.Games([1, 2, 3], limit: 500, offset: 0);

        Assert.Contains("game_type.type", query, StringComparison.Ordinal);
        Assert.Contains("parent_game", query, StringComparison.Ordinal);
        Assert.Contains("version_parent", query, StringComparison.Ordinal);
        Assert.Contains("version_title", query, StringComparison.Ordinal);

        // `category` is deprecated in favour of game_type and must not be
        // requested; game_type's label is `type`, so `game_type.name` would
        // come back empty rather than failing loudly.
        Assert.DoesNotContain("category", query, StringComparison.Ordinal);
        Assert.DoesNotContain("game_type.name", query, StringComparison.Ordinal);
    }

    /// <summary>
    /// The 918 works on the author's library that carry an
    /// igdb_id, at Apicalypse's documented ceiling of 500 rows — two pages of
    /// ids, plus the empty page that proves the full first page was not hiding
    /// a third. That is what "the IGDB half adds no requests beyond the
    /// existing enrichment pass" means in numbers: a `fields` clause costs the
    /// same request whatever it lists, so game_type, parent_game and
    /// version_parent ride calls the pass was already making.
    /// </summary>
    [Fact]
    public async Task The_whole_library_costs_a_handful_of_requests()
    {
        using var host = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(),
            configure: options => options.BatchSize = Apicalypse.MaxLimit);

        var games = await host.Client.GetGamesAsync(Enumerable.Range(1, 918).Select(i => (long)i));

        Assert.Equal(918, games.Count);
        Assert.Equal(3, host.Handler.CountFor("games"));

        // Every request carried the relation fields, so nothing has to be
        // re-asked for them later.
        Assert.All(
            host.Handler.Requests.Where(r => r.Endpoint == "games"),
            r => Assert.Contains("game_type.type", r.Body, StringComparison.Ordinal));
    }

    /// <summary>The three fields reach the domain model, not just the wire model.</summary>
    [Fact]
    public async Task Game_metadata_carries_the_relation_fields()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Equal("main_game", game.GameType);
        Assert.Null(game.ParentGameId);
        Assert.Null(game.VersionParentId);
        Assert.Null(game.RelationParentId);
    }

    /// <summary>
    /// A reference field arrives as a bare id when it is not expanded and as an
    /// object when it is. The query never expands these, but a shape that
    /// throws would take a whole batch down with it, so both are read.
    /// </summary>
    [Theory]
    [InlineData("""{"id":1,"name":"X","parent_game":4242,"version_parent":77}""")]
    [InlineData("""{"id":1,"name":"X","parent_game":{"id":4242},"version_parent":{"id":77}}""")]
    public async Task A_reference_field_reads_as_an_id_however_it_arrives(string row)
    {
        using var host = new IgdbTestHost((request, prior) => request.Endpoint switch
        {
            "games" => FakeHttpMessageHandler.Json(System.Net.HttpStatusCode.OK, "[" + row + "]"),
            _ => IgdbTestHost.DefaultResponder()(request, prior),
        });

        var game = Assert.Single(await host.Client.GetGamesAsync([1]));

        Assert.Equal(4242, game.ParentGameId);
        Assert.Equal(77, game.VersionParentId);

        // parent_game wins: an edition of an expansion belongs under the
        // expansion, not under the thing the expansion extends.
        Assert.Equal(4242, game.RelationParentId);
    }

    /// <summary>
    /// F29, and the reason TASK-70.10 depends on TASK-18.
    /// The 1,923 payloads on the author's disk were written before these fields
    /// were requested, so without a payload version they would answer with the
    /// fields silently empty for the whole 30-day TTL rather than triggering a
    /// refetch. Bumping the version makes every stored entry a miss.
    /// </summary>
    [Fact]
    public async Task A_payload_written_under_an_older_version_is_refetched()
    {
        var cache = new InMemoryMetadataCache();

        // The shape every entry in a pre-TASK-70.10 cache has: the game
        // serialised bare, with no envelope and no version.
        await cache.SetAsync(
            IgdbClient.CacheProvider,
            IgdbClient.GameCacheKey(100_440),
            """{"igdb_id":100440,"name":"Stale Title","genres":[],"themes":[],"publishers":[]}""",
            DateTime.UtcNow);

        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache);

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        // Refetched, not served: the new shape came back with the fields on it.
        Assert.Equal(1, host.Handler.CountFor("games"));
        Assert.Equal("Game 100440", game.Name);
        Assert.Equal("main_game", game.GameType);

        // And the entry is now stored under the current version, so a second
        // read is a cache hit again.
        var warm = Assert.Single(await host.Client.GetGamesAsync([100_440]));
        Assert.Equal(1, host.Handler.CountFor("games"));
        Assert.Equal("main_game", warm.GameType);

        var stored = await cache.GetAsync(IgdbClient.CacheProvider, IgdbClient.GameCacheKey(100_440));
        Assert.NotNull(stored);
        using var document = JsonDocument.Parse(stored.Value.PayloadJson!);
        Assert.Equal(IgdbClient.GamePayloadVersion, document.RootElement.GetProperty("version").GetInt32());
    }

    /// <summary>
    /// The guarantee the version must not repeal. An install with no Twitch
    /// credentials cannot refetch anything, and 1,923 entries that stop
    /// deserializing would be a worse bug than a missing field — so a
    /// superseded payload is still served when no refetch is possible.
    /// </summary>
    [Fact]
    public async Task A_superseded_payload_is_still_served_when_no_refetch_is_possible()
    {
        var cache = new InMemoryMetadataCache();
        await cache.SetAsync(
            IgdbClient.CacheProvider,
            IgdbClient.GameCacheKey(7),
            """{"igdb_id":7,"name":"Thief II","genres":["Adventure"],"themes":[],"publishers":[]}""",
            DateTime.UtcNow);

        using var host = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), cache: cache, clientId: null, clientSecret: null);

        var game = Assert.Single(await host.Client.GetGamesAsync([7]));

        Assert.Empty(host.Handler.Requests);
        Assert.Equal("Thief II", game.Name);
        Assert.Equal(["Adventure"], game.Genres);

        // The field the version exists for is genuinely absent, which is the
        // honest answer: nothing invented one.
        Assert.Null(game.GameType);
    }
}
