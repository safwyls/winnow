using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Winnow.Covers.Tests;

public sealed class SteamGridDbHeroSourceTests
{
    private const string Hash = "61ba87bf4177f576150389d84d14bb01";
    private const string Asset = Hash + ".png";
    private const string Url = "https://cdn2.steamgriddb.com/hero/" + Asset;

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("webp")]
    public async Task Canonical_asset_url_round_trips_to_a_trusted_download(string extension)
    {
        var url = "https://cdn2.steamgriddb.com/hero/" + Hash + "." + extension;
        var key = Assert.IsType<CoverKey>(SteamGridDbHeroUrl.Key(url));
        Assert.Equal(CoverKey.SteamGridDbHero(Hash + "." + extension), key);
        using var handler = new Cdn(_ => Ok([1, 2, 3]));
        Assert.Equal(new byte[] { 1, 2, 3 }, await new SteamGridDbHeroSource(handler).TryFetchAsync(key));
        Assert.Equal(url, Assert.Single(handler.Requests));
        Assert.Equal(CoverKey.SteamGridDbHero(Asset), SteamGridDbHeroUrl.Key(Url.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://cdn2.steamgriddb.com/hero/" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com.evil.test/hero/" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com@evil.test/hero/" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com:443/hero/" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com/grid/" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com/hero/../" + Asset)]
    [InlineData("https://cdn2.steamgriddb.com/hero/%2e%2e/" + Asset)]
    [InlineData(Url + "?redirect=other")]
    [InlineData(Url + "#fragment")]
    [InlineData(Url + "/")]
    [InlineData("https://cdn2.steamgriddb.com/hero/" + Hash + ".gif")]
    [InlineData("https://cdn2.steamgriddb.com/hero/short.png")]
    public void Unsafe_or_unrecognized_urls_have_no_key(string? url)
        => Assert.Null(SteamGridDbHeroUrl.Key(url));

    [Theory]
    [InlineData(CoverProviders.Steam, Asset)]
    [InlineData(CoverProviders.SteamHero, Asset)]
    [InlineData(CoverProviders.IgdbBackdrop, Asset)]
    [InlineData(CoverProviders.SteamGridDbHero, "../../host.png")]
    [InlineData(CoverProviders.SteamGridDbHero, "50584")]
    public async Task Invalid_direct_keys_cannot_make_requests(string provider, string id)
    {
        using var handler = new Cdn(_ => Ok([1]));
        var source = new SteamGridDbHeroSource(handler);
        var key = new CoverKey(provider, id);
        Assert.False(source.CanHandle(key));
        Assert.Null(await source.TryFetchAsync(key));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Unavailable_cdn_is_not_an_absent_asset(HttpStatusCode status)
    {
        using var handler = new Cdn(_ => new HttpResponseMessage(status));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new SteamGridDbHeroSource(handler).TryFetchAsync(CoverKey.SteamGridDbHero(Asset)));
    }

    [Fact]
    public async Task Missing_asset_and_other_provider_cache_entries_stay_isolated()
    {
        using var directory = new TempCoverDirectory();
        var options = directory.Options();
        using var handler = new Cdn(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var key = CoverKey.SteamGridDbHero(Asset);
        var disk = new CoverDiskCache(options);
        var steam = CoverKey.SteamHero("50584");
        disk.WriteSource(steam, TestArt.Capsule(640, 200));
        using var pipeline = directory.Pipeline(options, new SteamGridDbHeroSource(handler));
        Assert.Null(await pipeline.GetAsync(key, 320));
        Assert.True(pipeline.IsKnownMissing(key));
        Assert.False(pipeline.IsKnownMissing(steam));
        Assert.True(disk.TryReadSource(steam, out _));
        Assert.Null(await pipeline.GetAsync(key, 320));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Failed_download_can_recover_without_a_negative_cache_marker()
    {
        using var directory = new TempCoverDirectory();
        var available = false;
        var bytes = TestArt.Capsule(640, 200);
        using var handler = new Cdn(_ => available ? Ok(bytes) : new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var pipeline = directory.Pipeline(directory.Options(), new SteamGridDbHeroSource(handler));
        var key = CoverKey.SteamGridDbHero(Asset);
        Assert.Null(await pipeline.GetAsync(key, 320));
        Assert.False(pipeline.IsKnownMissing(key));
        available = true;
        using var recovered = await pipeline.GetAsync(key, 320);
        Assert.NotNull(recovered);
        Assert.NotEqual(key.CacheStem, CoverKey.SteamGridDbHero(Hash + ".jpg").CacheStem);
    }

    [Fact]
    public async Task Download_limit_and_factory_client_apply_to_every_request()
    {
        using var handler = new Cdn(_ =>
        {
            var response = Ok([1]);
            response.Content.Headers.ContentLength = CoverDownload.MaxBytes + 1L;
            return response;
        });
        var source = new SteamGridDbHeroSource(handler);
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<InvalidDataException>(() => source.TryFetchAsync(CoverKey.SteamGridDbHero(Asset)));
        Assert.Equal(2, handler.ClientsCreated);
        Assert.All(handler.ClientNames, name => Assert.Equal(SteamCapsuleSource.HttpClientName, name));
    }

    [Fact]
    public void Registration_includes_the_hero_source()
    {
        var services = new ServiceCollection();
        services.AddCoverCache();
        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<ICoverSource>().OfType<SteamGridDbHeroSource>());
    }

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class Cdn(Func<string, HttpResponseMessage> respond) : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Requests { get; } = [];
        public List<string> ClientNames { get; } = [];
        public int ClientsCreated => ClientNames.Count;
        public HttpClient CreateClient(string name)
        {
            ClientNames.Add(name);
            return new(this, disposeHandler: false);
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            return Task.FromResult(respond(url));
        }
    }
}
