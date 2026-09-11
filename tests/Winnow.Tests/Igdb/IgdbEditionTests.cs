using System.Net;
using System.Text.Json.Nodes;
using Winnow.Core.Identity;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests.Igdb;

public sealed class IgdbEditionTests
{
    private const string Edition = """{"uid":"440","external_game_source":1,"game":{"id":200,"version_parent":100,"version_title":"Deluxe"}}""";
    private const string Envelope = """{"version":1,"source_id":1,"uid":"440","status":3,"game_id":200,"parent_id":100,"title":"Deluxe"}""";

    private static IgdbTestHost Host(string json, IMetadataCache? cache = null)
        => new((request, _) => request.Endpoint == "token"
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, IgdbFixtures.TokenResponse("fake-token"))
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, json), cache: cache);

    [Fact]
    public async Task Unique_edition_has_exact_query_correlation_and_cached_payload_provenance()
    {
        using var host = Host("[" + Edition + "]");
        var match = Assert.Single(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440", "440"])).Value;
        Assert.Equal(IgdbEditionMatchStatus.Edition, match.Status);
        Assert.Equal(200, match.EditionGameId);
        Assert.Equal(100, match.VersionParentId);
        Assert.Equal("Deluxe", match.VersionTitle);
        Assert.Equal(host.Clock.Now.UtcDateTime.AddDays(30), match.ValidUntilUtc);
        var cache = await host.Cache.GetAsync(IgdbClient.EditionCacheProvider, "1:440");
        Assert.NotNull(cache);
        Assert.Equal(CachedEvidenceSource.FromPayload(IgdbClient.EditionCacheProvider, "1:440", cache.Value.PayloadJson), match.Source);
        var query = Assert.Single(host.Handler.Requests, request => request.Endpoint == "external_games");
        Assert.Contains("fields uid,external_game_source,game.id,game.version_parent,game.version_title;", query.Body);
        Assert.Contains("where external_game_source = 1 & uid = (\"440\");", query.Body);
        Assert.Equal("text/plain", query.ContentType);
        Assert.Equal(HttpMethod.Post, query.Method);
    }

    [Theory]
    [InlineData("[]", IgdbEditionMatchStatus.Missing)]
    [InlineData("""[{"uid":"other","external_game_source":1,"game":{"id":200}}]""", IgdbEditionMatchStatus.Missing)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":{"id":200}}]""", IgdbEditionMatchStatus.NotEdition)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":{"id":200,"version_parent":null,"version_title":null}}]""", IgdbEditionMatchStatus.NotEdition)]
    [InlineData("""[{"uid":"440","external_game_source":5,"game":{"id":200}}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","game":{"id":200}}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":"1","game":{"id":200}}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":200}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":null}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":{"id":0}}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":{"id":200,"version_parent":200,"version_title":"Deluxe"}}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":{"id":200,"version_parent":-1,"version_title":"Deluxe"}}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":{"id":200,"version_parent":100,"version_title":" "}}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":{"id":200,"version_title":"Deluxe"}}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":{"id":200,"version_parent":100,"version_title":42}}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":5,"external_game_source":1,"game":{"id":200,"version_parent":100,"version_title":"Deluxe"}}]""", IgdbEditionMatchStatus.Ambiguous)]
    [InlineData("""[{"uid":"440","external_game_source":1,"game":{"id":300,"id":200,"version_parent":100,"version_title":"Deluxe"}}]""", IgdbEditionMatchStatus.Ambiguous)]
    public async Task Complete_responses_classify_without_promoting_partial_or_mismatched_rows(string json, IgdbEditionMatchStatus status)
    {
        using var host = Host(json);
        var match = Assert.Single(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"])).Value;
        Assert.Equal(status, match.Status);
        Assert.Null(match.EditionGameId);
        Assert.Null(match.VersionParentId);
        Assert.Null(match.VersionTitle);
        Assert.NotNull(match.Source.PayloadSha256);
        var again = Assert.Single(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"])).Value;
        Assert.Equal(match, again);
        Assert.Equal(1, host.Handler.CountFor("external_games"));
    }

    [Theory]
    [InlineData("id", "201")]
    [InlineData("version_parent", "101")]
    [InlineData("version_title", "\"Gold\"")]
    public async Task Conflicting_game_parent_or_title_is_ambiguous(string property, string replacement)
    {
        var second = JsonNode.Parse(Edition)!;
        second["game"]![property] = JsonNode.Parse(replacement);
        using var host = Host("[" + Edition + "," + second.ToJsonString() + "]");
        Assert.Equal(IgdbEditionMatchStatus.Ambiguous,
            Assert.Single(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"])).Value.Status);
    }

    [Fact]
    public async Task Repeated_identical_rows_are_one_mapping()
    {
        using var host = Host("[" + Edition + "," + Edition + "]");
        Assert.Equal(IgdbEditionMatchStatus.Edition,
            Assert.Single(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"])).Value.Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[42]")]
    [InlineData("[")]
    [InlineData("""[{"uid":440}]""")]
    [InlineData("""[{"uid":"other","uid":"440","external_game_source":1,"game":{"id":200,"version_parent":100,"version_title":"Deluxe"}}]""")]
    public async Task Uncorrelatable_or_invalid_response_cannot_create_even_a_negative_cache(string json)
    {
        using var host = Host(json);
        Assert.Empty(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"]));
        Assert.Null(await host.Cache.GetAsync(IgdbClient.EditionCacheProvider, "1:440"));
    }

    [Fact]
    public async Task Uncorrelatable_row_alongside_positive_cannot_establish_exclusivity()
    {
        using var host = Host("[" + Edition + ",{}]");
        Assert.Empty(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440", "570"]));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("source_id")]
    [InlineData("uid")]
    [InlineData("status")]
    [InlineData("game_id")]
    [InlineData("parent_id")]
    [InlineData("title")]
    public async Task Every_cache_envelope_field_is_required_even_for_a_negative(string missing)
    {
        var node = JsonNode.Parse(Envelope)!.AsObject();
        node["status"] = 0;
        node["game_id"] = null;
        node["parent_id"] = null;
        node["title"] = null;
        node.Remove(missing);
        using var host = new IgdbTestHost((_, _) => throw new InvalidOperationException(), clientId: null, clientSecret: null);
        await host.Cache.SetAsync(IgdbClient.EditionCacheProvider, "1:440", node.ToJsonString(), host.Clock.Now.UtcDateTime);
        Assert.Empty(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"]));
        Assert.Empty(host.Handler.Requests);
    }

    [Theory]
    [InlineData("version", "99")]
    [InlineData("source_id", "5")]
    [InlineData("uid", "\"570\"")]
    [InlineData("status", "99")]
    [InlineData("status", "0")]
    [InlineData("game_id", "null")]
    [InlineData("parent_id", "200")]
    [InlineData("title", "\" \"")]
    public async Task Incompatible_or_inconsistent_cache_has_no_offline_authority(string property, string replacement)
    {
        var node = JsonNode.Parse(Envelope)!;
        node[property] = JsonNode.Parse(replacement);
        using var host = new IgdbTestHost((_, _) => throw new InvalidOperationException(), clientId: null, clientSecret: null);
        await host.Cache.SetAsync(IgdbClient.EditionCacheProvider, "1:440", node.ToJsonString(), host.Clock.Now.UtcDateTime);
        Assert.Empty(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"]));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("""{"version":1,"source_id":1,"uid":"440","status":0,"status":3,"game_id":200,"parent_id":100,"title":"Deluxe"}""")]
    public async Task Malformed_or_duplicate_cache_fields_are_refetched(string json)
    {
        using var host = Host("[" + Edition + "]");
        await host.Cache.SetAsync(IgdbClient.EditionCacheProvider, "1:440", json, host.Clock.Now.UtcDateTime);
        Assert.Equal(IgdbEditionMatchStatus.Edition, Assert.Single(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"])).Value.Status);
        Assert.Equal(1, host.Handler.CountFor("external_games"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-29, true)]
    [InlineData(-30, false)]
    [InlineData(-31, false)]
    [InlineData(1, false)]
    public async Task Only_fresh_compatible_evidence_is_available_without_credentials(int ageDays, bool usable)
    {
        using var host = new IgdbTestHost((_, _) => throw new InvalidOperationException(), clientId: null, clientSecret: null);
        await host.Cache.SetAsync(IgdbClient.EditionCacheProvider, "1:440", Envelope, host.Clock.Now.UtcDateTime.AddDays(ageDays));
        var result = await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"]);
        Assert.Equal(usable ? 1 : 0, result.Count);
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task Legacy_mapping_cache_is_never_accepted_as_edition_evidence()
    {
        var cache = new InMemoryMetadataCache();
        using (var old = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache))
            Assert.Single(await old.Client.ResolveBySteamAppIdsAsync(["440"]));
        using var host = new IgdbTestHost((_, _) => throw new InvalidOperationException(), cache: cache, clientId: null, clientSecret: null);
        Assert.Empty(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"]));
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task Transport_failure_cannot_return_or_refresh_expired_positive_evidence()
    {
        using var host = new IgdbTestHost((request, _) => request.Endpoint == "token"
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, IgdbFixtures.TokenResponse("fake-token"))
            : throw new HttpRequestException("Canned network failure"), configure: options => options.MaxRetryAttempts = 1);
        var expired = host.Clock.Now.UtcDateTime.AddDays(-31);
        await host.Cache.SetAsync(IgdbClient.EditionCacheProvider, "1:440", Envelope, expired);
        Assert.Empty(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440", "570"]));
        Assert.Equal(expired, (await host.Cache.GetAsync(IgdbClient.EditionCacheProvider, "1:440"))!.Value.FetchedAt);
        Assert.Null(await host.Cache.GetAsync(IgdbClient.EditionCacheProvider, "1:570"));
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_caching_an_answer()
    {
        using var cancelled = new CancellationTokenSource();
        using var host = new IgdbTestHost((request, _) =>
        {
            if (request.Endpoint == "token") return FakeHttpMessageHandler.Json(HttpStatusCode.OK, IgdbFixtures.TokenResponse("fake-token"));
            cancelled.Cancel();
            throw new OperationCanceledException(cancelled.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"], ct: cancelled.Token));
        Assert.Null(await host.Cache.GetAsync(IgdbClient.EditionCacheProvider, "1:440"));
    }

    [Fact]
    public async Task Zero_ttl_forces_a_new_request_and_grants_no_future_identity_authority()
    {
        using var host = Host("[" + Edition + "]");
        await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"]);
        var forced = Assert.Single(await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"], TimeSpan.Zero)).Value;
        Assert.Equal(2, host.Handler.CountFor("external_games"));
        Assert.Equal(host.Clock.Now.UtcDateTime, forced.ValidUntilUtc);
    }

    [Fact]
    public async Task Safe_distinct_native_ids_are_batched_without_query_injection()
    {
        using var host = Host("[]");
        var safe = Enumerable.Range(1, 401).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var matches = await host.Client.ResolveEditionsByExternalIdsAsync(1, safe.Concat(["1", "bad\";", "", "bad\\value"]));
        Assert.Equal(401, matches.Count);
        Assert.Equal(2, host.Handler.CountFor("external_games"));
        Assert.All(matches.Values, match => Assert.Equal(IgdbEditionMatchStatus.Missing, match.Status));
        Assert.All(host.Handler.Requests, request => Assert.DoesNotContain("bad", request.Body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_later_page_discards_partial_positive_and_preserves_existing_cache(bool failPage)
    {
        var first = "[" + string.Join(',', Enumerable.Repeat(Edition, 500)) + "]";
        using var host = new IgdbTestHost((request, count) => request.Endpoint == "token"
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, IgdbFixtures.TokenResponse("fake-token"))
            : count == 0 ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, first)
            : FakeHttpMessageHandler.Json(failPage ? HttpStatusCode.BadRequest : HttpStatusCode.OK,
                failPage ? "{}" : "[" + Edition.Replace("200", "201", StringComparison.Ordinal) + "]"));
        var expired = host.Clock.Now.UtcDateTime.AddDays(-31);
        await host.Cache.SetAsync(IgdbClient.EditionCacheProvider, "1:440", Envelope, expired);
        var result = await host.Client.ResolveEditionsByExternalIdsAsync(1, ["440"]);
        Assert.Equal(2, host.Handler.CountFor("external_games"));
        Assert.Contains("offset 500;", host.Handler.Requests.Last().Body);
        if (failPage)
        {
            Assert.Empty(result);
            Assert.Equal(expired, (await host.Cache.GetAsync(IgdbClient.EditionCacheProvider, "1:440"))!.Value.FetchedAt);
        }
        else Assert.Equal(IgdbEditionMatchStatus.Ambiguous, Assert.Single(result).Value.Status);
    }
}
