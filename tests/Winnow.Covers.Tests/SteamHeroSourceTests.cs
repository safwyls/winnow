using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Winnow.Covers.Tests;

public sealed class SteamHeroSourceTests
{
    [Theory]
    [InlineData(CoverProviders.SteamHero, "library_hero_2x.jpg")]
    [InlineData(CoverProviders.SteamHeroStandard, "library_hero.jpg")]
    public async Task Each_key_fetches_only_its_own_rendition(string provider, string file)
    {
        using var handler = new HeroCdn(_ => Ok([1, 2, 3]));
        var source = new SteamHeroSource(handler, Options());
        Assert.Equal(new byte[] { 1, 2, 3 }, await source.TryFetchAsync(new CoverKey(provider, "220")));
        Assert.Equal($"https://cdn.test.invalid/steam/apps/220/{file}", Assert.Single(handler.Requests));
    }

    [Theory]
    [InlineData(CoverProviders.Steam, "220")]
    [InlineData(CoverProviders.IgdbBackdrop, "220")]
    [InlineData(CoverProviders.SteamHero, "")]
    [InlineData(CoverProviders.SteamHero, "../220")]
    [InlineData(CoverProviders.SteamHeroStandard, "220?other=1")]
    [InlineData(CoverProviders.SteamHeroStandard, "２２０")]
    public async Task Unsupported_keys_and_unsafe_ids_make_no_request(string provider, string id)
    {
        using var handler = new HeroCdn(_ => Ok([1]));
        var source = new SteamHeroSource(handler, Options());
        Assert.False(source.CanHandle(new CoverKey(provider, id)));
        Assert.Null(await source.TryFetchAsync(new CoverKey(provider, id)));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(CoverProviders.SteamHero)]
    [InlineData(CoverProviders.SteamHeroStandard)]
    public async Task Missing_rendition_does_not_probe_another_file(string provider)
    {
        using var handler = new HeroCdn(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await new SteamHeroSource(handler, Options()).TryFetchAsync(new CoverKey(provider, "220")));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Transient_failure_is_not_reported_as_missing(HttpStatusCode status)
    {
        using var handler = new HeroCdn(_ => new HttpResponseMessage(status));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new SteamHeroSource(handler, Options()).TryFetchAsync(CoverKey.SteamHero("220")));
    }

    [Fact]
    public async Task Oversized_response_uses_the_shared_download_limit()
    {
        using var handler = new HeroCdn(_ =>
        {
            var response = Ok([1]);
            response.Content.Headers.ContentLength = CoverDownload.MaxBytes + 1L;
            return response;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SteamHeroSource(handler, Options()).TryFetchAsync(CoverKey.SteamHero("220")));
    }

    [Fact]
    public async Task Missing_high_hero_and_cached_capsule_do_not_hide_standard_hero()
    {
        using var directory = new TempCoverDirectory();
        var options = directory.Options();
        var hero = TestArt.Capsule(640, 200);
        using var handler = new HeroCdn(url => url.EndsWith("/library_hero.jpg", StringComparison.Ordinal)
            ? Ok(hero) : new HttpResponseMessage(HttpStatusCode.NotFound));
        var source = new SteamHeroSource(handler, options);
        var disk = new CoverDiskCache(options);
        var capsuleKey = CoverKey.Steam("220");
        var highKey = CoverKey.SteamHero("220");
        var standardKey = CoverKey.SteamHeroStandard("220");
        disk.WriteSource(capsuleKey, TestArt.Capsule(200, 300));

        using (var pipeline = directory.Pipeline(options, source))
        {
            Assert.Null(await pipeline.GetAsync(highKey, 320));
            Assert.True(pipeline.IsKnownMissing(highKey));
            Assert.False(pipeline.IsKnownMissing(standardKey));
            using var art = await pipeline.GetAsync(standardKey, 320);
            Assert.NotNull(art);
            Assert.True(art.Vivid.Width > art.Vivid.Height);
        }
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(disk.TryReadSource(capsuleKey, out _));
        Assert.True(disk.TryReadSource(standardKey, out var saved));
        Assert.Equal(hero, saved);
        Assert.False(disk.TryReadSource(highKey, out _));
        using (var fresh = directory.Pipeline(options, source))
        {
            Assert.Null(await fresh.GetAsync(highKey, 320));
            using var cached = await fresh.GetAsync(standardKey, 320);
            Assert.NotNull(cached);
        }
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Failed_request_does_not_poison_the_hero_cache()
    {
        using var directory = new TempCoverDirectory();
        var available = false;
        var hero = TestArt.Capsule(640, 200);
        using var handler = new HeroCdn(_ => available ? Ok(hero) : new HttpResponseMessage(HttpStatusCode.Forbidden));
        var source = new SteamHeroSource(handler, directory.Options());
        var key = CoverKey.SteamHero("220");
        using var pipeline = directory.Pipeline(directory.Options(), source);
        Assert.Null(await pipeline.GetAsync(key, 320));
        Assert.False(pipeline.IsKnownMissing(key));
        Assert.False(File.Exists(new CoverDiskCache(directory.Options()).NegativePath(key)));
        available = true;
        using var recovered = await pipeline.GetAsync(key, 320);
        Assert.NotNull(recovered);
    }

    [Fact]
    public async Task Registration_uses_the_existing_cover_http_pipeline()
    {
        var services = new ServiceCollection();
        services.AddCoverCache(options => options.SteamCdnBaseUrl = Options().SteamCdnBaseUrl);
        var identified = false;
        using var handler = new HeroCdn(_ => Ok([1]));
        services.AddHttpClient(SteamCapsuleSource.HttpClientName, client =>
        {
            identified = client.DefaultRequestHeaders.UserAgent.ToString().Contains("Winnow", StringComparison.Ordinal);
        }).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var source = Assert.Single(provider.GetServices<ICoverSource>().OfType<SteamHeroSource>());
        Assert.NotNull(await source.TryFetchAsync(CoverKey.SteamHero("220")));
        Assert.True(identified);
        Assert.Single(handler.Requests);
    }

    private static CoverCacheOptions Options() => new() { SteamCdnBaseUrl = "https://cdn.test.invalid/steam/apps" };

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class HeroCdn(Func<string, HttpResponseMessage> respond) : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Requests { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            return Task.FromResult(respond(url));
        }
    }
}
