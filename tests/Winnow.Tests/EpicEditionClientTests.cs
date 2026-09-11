using System.Net;
using Winnow.Enrich.Stores;
using Xunit;

namespace Winnow.Tests;

public sealed class EpicEditionClientTests
{
    [Fact]
    public async Task Fresh_exact_cms_response_is_cached_with_a_source_hash_and_does_not_refetch()
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        var now = DateTime.UtcNow;
        await cache.SaveAsync("epic", "{\"" + EditionEvidenceFixture.Namespace + "\":\"fez\"}", now);
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(EditionEvidenceFixture.Cms) });
        using var http = new HttpClient(handler);
        var client = new StorefrontClient(http, cache, TimeProvider.System);
        var evidence = Assert.IsType<EpicEditionPageEvidence>(await client.GetEpicEditionIdsAsync(Target()));
        Assert.Equal(2, evidence.NativeIds.Count);
        Assert.False(evidence.Conflicting);
        Assert.NotNull(evidence.Source.PayloadSha256);
        var repeat = Assert.IsType<EpicEditionPageEvidence>(await client.GetEpicEditionIdsAsync(Target()));
        Assert.Equal(evidence.Source, repeat.Source);
        Assert.Single(handler.Requests);
        Assert.EndsWith("/products/fez", handler.Requests[0]);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(0)]
    [InlineData(200)]
    public async Task Expired_positive_cms_evidence_cannot_survive_an_unanswered_refresh(int status)
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        await cache.SaveAsync("epic", "{\"" + EditionEvidenceFixture.Namespace + "\":\"fez\"}", DateTime.UtcNow);
        await cache.SaveAsync("epic-edition-v1:fez", EditionEvidenceFixture.Cms, DateTime.UtcNow.AddDays(-2));
        using var handler = new Handler(_ => status == 0 ? throw new HttpRequestException()
            : new((HttpStatusCode)status) { Content = new StringContent("{\"pages\":null}") });
        using var http = new HttpClient(handler);
        var client = new StorefrontClient(http, cache, TimeProvider.System);
        Assert.Null(await client.GetEpicEditionIdsAsync(Target()));
        Assert.Single(handler.Requests);
        if (status is 403 or 404)
        {
            var retry = await client.GetEpicEditionIdsAsync(Target());
            Assert.Empty(Assert.IsType<EpicEditionPageEvidence>(retry).NativeIds);
            Assert.Single(handler.Requests);
        }
    }

    [Fact]
    public async Task Future_cms_timestamp_requires_a_valid_new_response()
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        await cache.SaveAsync("epic", "{\"" + EditionEvidenceFixture.Namespace + "\":\"fez\"}", DateTime.UtcNow);
        await cache.SaveAsync("epic-edition-v1:fez", EditionEvidenceFixture.Cms, DateTime.UtcNow.AddDays(1));
        using var handler = new Handler(_ => new(HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);
        Assert.Null(await new StorefrontClient(http, cache, TimeProvider.System).GetEpicEditionIdsAsync(Target()));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("{\"pages\":[],\"pages\":[]}")]
    [InlineData("null")]
    [InlineData("{\"pages\":[null]}")]
    public void Malformed_or_duplicate_cms_fields_cannot_supply_identifiers(string payload)
    {
        Assert.False(StorefrontClient.TryParseEpicEditionIds(payload, Target(), out var ids, out _));
        Assert.Empty(ids);
    }

    private static EpicEditionTarget Target() => new(EditionEvidenceFixture.Namespace, EditionEvidenceFixture.CatalogId, "Bluebird");
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests.Add(request.RequestUri!.AbsoluteUri); return Task.FromResult(respond(request)); }
    }
}
