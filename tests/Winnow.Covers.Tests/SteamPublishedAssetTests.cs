using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Winnow.Covers.Tests;

public sealed class SteamPublishedAssetTests
{
    [Fact]
    public void Registration_injects_published_lookup_into_both_Steam_sources()
    {
        var services = new ServiceCollection();
        services.AddCoverCache();
        services.AddSingleton<ISteamLibraryAssetLookup>(new Lookup([]));
        using var provider = services.BuildServiceProvider();
        var sources = provider.GetServices<ICoverSource>().ToArray();
        Assert.True(Assert.Single(sources.OfType<SteamCapsuleSource>()).CanRefreshCachedFallback);
        Assert.Equal("steam-library-hero-published-v1", Assert.Single(sources.OfType<SteamHeroSource>()).SourceSetId);
    }

    private static CoverCacheOptions Options() => new()
    {
        SteamCdnBaseUrl = "https://legacy.invalid/apps",
        SteamAssetCdnBaseUrl = "https://assets.invalid/steam/apps"
    };

    [Theory]
    [InlineData("f72e5a29377820dd3d1d76e0c2eb5a8fcfa34c7d/library_capsule_2x.jpg")]
    [InlineData("30259337ac341e9fbe5410650e7aabf1b63796b6/library_600x900_2x.jpg")]
    [InlineData("hash/library_capsule_schinese.png")]
    public async Task Legacy_miss_resolves_published_asset(string path)
    {
        var bytes = TestArt.Capsule(60, 90);
        using var cdn = new Cdn(url => url.Contains("assets.invalid") ? Ok(bytes) : Missing());
        var lookup = new Lookup([path]);
        var source = new SteamCapsuleSource(cdn, Options(), assets: lookup);
        Assert.Equal(bytes, await source.TryFetchAsync(CoverKey.Steam("1374490")));
        Assert.Equal(3, cdn.Requests.Count);
        Assert.Equal($"https://assets.invalid/steam/apps/1374490/{path}", cdn.Requests[^1]);
        Assert.Equal(1, lookup.Calls);
        Assert.True(source.CanRefreshCachedFallback);
    }

    [Fact]
    public async Task Existing_legacy_cover_does_not_request_appinfo()
    {
        var bytes = TestArt.Capsule(60, 90);
        using var cdn = new Cdn(_ => Ok(bytes));
        var lookup = new Lookup(null);
        Assert.Equal(bytes, await new SteamCapsuleSource(cdn, Options(), assets: lookup).TryFetchAsync(CoverKey.Steam("220")));
        Assert.Equal(0, lookup.Calls);
        Assert.Single(cdn.Requests);
    }

    [Fact]
    public async Task Missing_published_high_rendition_tries_next_path()
    {
        var bytes = TestArt.Capsule(30, 45);
        using var cdn = new Cdn(url => url.EndsWith("/hash/library_capsule.jpg") ? Ok(bytes) : Missing());
        var lookup = new Lookup(["hash/library_capsule_2x.jpg", "hash/library_capsule.jpg"]);
        Assert.Equal(bytes, await new SteamCapsuleSource(cdn, Options(), assets: lookup).TryFetchAsync(CoverKey.Steam("220")));
        Assert.Equal(4, cdn.Requests.Count);
    }

    [Theory]
    [InlineData("../outside.jpg")]
    [InlineData("https://evil.invalid/image.jpg")]
    [InlineData("//evil.invalid/image.jpg")]
    [InlineData("hash/%2e%2e/image.jpg")]
    [InlineData("hash\\image.jpg")]
    [InlineData("hash/image.jpg?redirect=other")]
    [InlineData("hash/../image.jpg")]
    public async Task Unsafe_metadata_never_becomes_a_request_or_missing_marker(string path)
    {
        using var cdn = new Cdn(_ => Missing());
        var source = new SteamCapsuleSource(cdn, Options(), assets: new Lookup([path]));
        await Assert.ThrowsAsync<InvalidDataException>(() => source.TryFetchAsync(CoverKey.Steam("220")));
        Assert.Equal(2, cdn.Requests.Count);
        Assert.All(cdn.Requests, url => Assert.StartsWith("https://legacy.invalid/", url));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Published_transport_errors_are_not_absence(HttpStatusCode status)
    {
        using var cdn = new Cdn(url => new HttpResponseMessage(url.Contains("assets.invalid") ? status : HttpStatusCode.NotFound));
        var source = new SteamCapsuleSource(cdn, Options(), assets: new Lookup(["hash/library_capsule.jpg"]));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.TryFetchAsync(CoverKey.Steam("220")));
    }

    [Fact]
    public async Task Unavailable_metadata_does_not_poison_negative_cache()
    {
        using var directory = new TempCoverDirectory();
        var options = directory.Options();
        using var cdn = new Cdn(_ => Missing());
        var lookup = new Lookup(null);
        var source = new SteamCapsuleSource(cdn, options, assets: lookup);
        using var pipeline = directory.Pipeline(options, source);
        var key = CoverKey.Steam("220");
        Assert.Null(await pipeline.GetAsync(key, 160));
        Assert.False(pipeline.IsKnownMissing(key));
        lookup.Paths = [];
        Assert.Null(await pipeline.GetAsync(key, 160));
        Assert.True(pipeline.IsKnownMissing(key));
    }

    [Fact]
    public async Task Non_image_success_is_a_failure_not_cacheable_art()
    {
        using var cdn = new Cdn(url => url.Contains("assets.invalid") ? Ok([1, 2, 3]) : Missing());
        var source = new SteamCapsuleSource(cdn, Options(), assets: new Lookup(["hash/library_capsule.jpg"]));
        await Assert.ThrowsAsync<InvalidDataException>(() => source.TryFetchAsync(CoverKey.Steam("220")));
    }

    [Theory]
    [InlineData(CoverProviders.SteamHero, "hash/library_hero_2x.jpg")]
    [InlineData(CoverProviders.SteamHeroStandard, "hash/library_hero.jpg")]
    public async Task Heroes_resolve_published_paths_for_the_requested_key(string provider, string path)
    {
        var bytes = TestArt.Capsule(96, 31);
        using var cdn = new Cdn(url => url.Contains("assets.invalid") ? Ok(bytes) : Missing());
        var lookup = new Lookup([path]);
        var key = new CoverKey(provider, "220");
        Assert.Equal(bytes, await new SteamHeroSource(cdn, Options(), lookup).TryFetchAsync(key));
        Assert.Equal(key, lookup.LastKey);
        Assert.Equal($"https://assets.invalid/steam/apps/220/{path}", cdn.Requests[^1]);
        Assert.Equal(2, cdn.Requests.Count);
    }

    [Fact]
    public void Published_capability_reopens_legacy_negative_markers()
    {
        using var cdn = new Cdn(_ => Missing());
        Assert.NotEqual(new SteamCapsuleSource(cdn, Options()).SourceSetId,
            new SteamCapsuleSource(cdn, Options(), assets: new Lookup([])).SourceSetId);
        Assert.NotEqual(new SteamHeroSource(cdn, Options()).SourceSetId,
            new SteamHeroSource(cdn, Options(), new Lookup([])).SourceSetId);
    }

    private static HttpResponseMessage Missing() => new(HttpStatusCode.NotFound);
    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class Lookup(IReadOnlyList<string>? paths) : ISteamLibraryAssetLookup
    {
        public IReadOnlyList<string>? Paths { get; set; } = paths;
        public int Calls { get; private set; }
        public CoverKey LastKey { get; private set; }
        public Task<IReadOnlyList<string>?> GetPathsAsync(CoverKey key, CancellationToken ct = default)
        { Calls++; LastKey = key; return Task.FromResult(Paths); }
    }
    private sealed class Cdn(Func<string, HttpResponseMessage> respond) : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Requests { get; } = [];
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            return Task.FromResult(respond(url));
        }
    }
}
