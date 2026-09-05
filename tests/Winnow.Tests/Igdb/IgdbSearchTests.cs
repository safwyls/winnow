using System.Net;
using Winnow.Enrich.Igdb;
using Xunit;

namespace Winnow.Tests.Igdb;

/// <summary>
/// Integration tests for IGDB title search: the query carries its own
/// fields and rides its own cache namespace, a rejected search costs
/// only the search, and a title that would break the quoted clause is
/// sanitized rather than passed through.
/// </summary>
public sealed class IgdbSearchTests
{
    [Fact]
    public async Task Search_posts_its_own_query_naming_cover_year_and_platforms()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var results = await host.Client.SearchGamesAsync("Prey");

        var query = host.Handler.Requests.Single(r => r.Endpoint == "games");
        Assert.Equal(HttpMethod.Post, query.Method);
        Assert.Equal("text/plain", query.ContentType);
        Assert.Contains("search \"Prey\";", query.Body, StringComparison.Ordinal);
        Assert.Contains("fields name,cover.image_id,cover.url,first_release_date,platforms.name;",
            query.Body, StringComparison.Ordinal);

        Assert.DoesNotContain("genres", query.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("involved_companies", query.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("age_ratings", query.Body, StringComparison.Ordinal);

        var first = results[0];
        Assert.Equal(IgdbFixtures.IgdbIdForSearchHit(1), first.IgdbId);
        Assert.Equal("Prey", first.Name);
        Assert.Equal(2008, first.FirstReleaseYear);
        Assert.Equal(
            "https://images.igdb.com/igdb/image/upload/t_cover_big/cosearch1.jpg", first.CoverUrl);
        Assert.Equal(["PC (Microsoft Windows)", "PlayStation 4"], first.Platforms);
    }

    [Fact]
    public async Task A_repeated_search_is_served_from_its_own_cache_namespace()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var first = await host.Client.SearchGamesAsync("Prey");
        var second = await host.Client.SearchGamesAsync("prey");

        Assert.Equal(1, host.Handler.CountFor("games"));
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first[0].IgdbId, second[0].IgdbId);

        var cached = await host.Cache.GetAsync(
            IgdbClient.CacheProvider, IgdbClient.SearchCacheKey("Prey", 20));
        Assert.NotNull(cached);

        Assert.StartsWith("search:", IgdbClient.SearchCacheKey("Prey", 20), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_search_writes_nothing_into_the_game_or_maturity_namespaces()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        await host.Client.SearchGamesAsync("Prey");

        var hitId = IgdbFixtures.IgdbIdForSearchHit(1);
        Assert.Null(await host.Cache.GetAsync(
            IgdbClient.CacheProvider, IgdbClient.GameCacheKey(hitId)));
        Assert.Null(await host.Cache.GetAsync(
            IgdbClient.CacheProvider, IgdbClient.AgeRatingsCacheKey(hitId)));
    }

    [Fact]
    public async Task A_rejected_search_query_costs_only_the_search()
    {
        using var host = new IgdbTestHost((request, _) => request.Endpoint switch
        {
            "token" => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, IgdbFixtures.TokenResponse("token")),
            "games" when IgdbFixtures.SearchedTerm(request.Body) is not null
                => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "[]"),
            "games" => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, IgdbFixtures.Games(request.Body)),
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "[]"),
        });

        var results = await host.Client.SearchGamesAsync("Prey");
        Assert.Empty(results);

        Assert.Null(await host.Cache.GetAsync(
            IgdbClient.CacheProvider, IgdbClient.SearchCacheKey("Prey", 20)));

        var games = await host.Client.GetGamesAsync([1020]);
        Assert.Single(games);
    }

    [Fact]
    public async Task An_unconfigured_client_returns_no_candidates_and_costs_no_request()
    {
        using var host = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), clientId: null, clientSecret: null);

        Assert.Empty(await host.Client.SearchGamesAsync("Prey"));
        Assert.Equal(0, host.Handler.CountFor("games"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\";")]
    public async Task A_title_that_carries_no_searchable_text_costs_no_request(string title)
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        Assert.Empty(await host.Client.SearchGamesAsync(title));
        Assert.Equal(0, host.Handler.CountFor("games"));
    }

    [Fact]
    public async Task A_title_cannot_break_out_of_the_quoted_search_clause()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        await host.Client.SearchGamesAsync("Prey\"; where id = 1; search \"Doom");

        var query = host.Handler.Requests.Single(r => r.Endpoint == "games");
        Assert.Equal("Prey where id = 1 search Doom", IgdbFixtures.SearchedTerm(query.Body));
        Assert.DoesNotContain("where id", query.Body[..query.Body.IndexOf("search", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_result_limit_is_the_one_the_caller_asked_for()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var results = await host.Client.SearchGamesAsync("Prey", limit: 2);

        Assert.Equal(2, results.Count);
        var query = host.Handler.Requests.Single(r => r.Endpoint == "games");
        Assert.Contains("limit 2;", query.Body, StringComparison.Ordinal);
    }
}
