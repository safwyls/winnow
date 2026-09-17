using System.Net;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Covers;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Model;
using Xunit;

namespace Winnow.Tests;

public sealed class SteamBrowserArtworkSourceTests
{
    [Theory]
    [InlineData(ArtworkSlot.Cover, false, "library_600x900_2x.jpg")]
    [InlineData(ArtworkSlot.Hero, false, "library_hero_2x.jpg")]
    [InlineData(ArtworkSlot.Hero, true, "library_hero.jpg")]
    public async Task Browser_keys_fetch_the_requested_Steam_rendition(ArtworkSlot slot, bool standard, string file)
    {
        var handler = new RecordingHandler();
        var source = Source(handler);
        var key = SteamBrowserArtworkSource.Key("620", slot, standard);
        Assert.NotEqual(CoverProviders.Steam, key.Provider);
        Assert.Equal(new byte[] { 1, 2, 3 }, await source.TryFetchAsync(key));
        Assert.EndsWith("/620/" + file, Assert.Single(handler.Urls));
    }

    [Fact]
    public async Task Icon_uses_validated_common_hash_on_the_fixed_Steam_host()
    {
        var handler = new RecordingHandler();
        var source = Source(handler);
        await source.TryFetchAsync(SteamBrowserArtworkSource.Key("620", ArtworkSlot.Icon));
        Assert.Equal("https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/620/25a5a16b2423bf7487ac5340b5b0948cef48c5f8.jpg", Assert.Single(handler.Urls));
    }

    [Fact]
    public async Task Missing_icon_makes_no_download_and_unavailable_metadata_remains_retryable()
    {
        var handler = new RecordingHandler();
        var key = SteamBrowserArtworkSource.Key("620", ArtworkSlot.Icon);
        Assert.Null(await Source(handler, AppInfoFetch.NoData).TryFetchAsync(key));
        Assert.Empty(handler.Urls);
        await Assert.ThrowsAsync<HttpRequestException>(() => Source(handler, AppInfoFetch.Unavailable).TryFetchAsync(key));
        Assert.Empty(handler.Urls);
    }

    [Fact]
    public async Task Icon_downloads_enforce_the_shared_encoded_body_limit()
    {
        var handler = new RecordingHandler { DeclaredLength = CoverDownload.MaxBytes + 1L };
        await Assert.ThrowsAsync<InvalidDataException>(() => Source(handler).TryFetchAsync(
            SteamBrowserArtworkSource.Key("620", ArtworkSlot.Icon)));
    }

    [Fact]
    public void Source_refuses_non_browser_and_malformed_keys()
    {
        var source = Source(new RecordingHandler());
        Assert.False(source.CanHandle(CoverKey.Steam("620")));
        Assert.False(source.CanHandle(new CoverKey(SteamBrowserArtworkSource.IconProvider, "../620")));
    }

    private static SteamBrowserArtworkSource Source(RecordingHandler handler, AppInfoFetch? info = null)
        => new(new Clients(handler), new CoverCacheOptions(), new Assets(), new AppInfo(info ?? AppInfoFetch.Ok(
            new SteamAppInfo("620", "Portal 2", "game", null) { IconHash = "25a5a16b2423bf7487ac5340b5b0948cef48c5f8" })));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        public long? DeclaredLength { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            if (DeclaredLength is { } length) response.Content.Headers.ContentLength = length;
            return Task.FromResult(response);
        }
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Assets : ISteamLibraryAssetLookup
    {
        public Task<IReadOnlyList<string>?> GetPathsAsync(CoverKey key, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>?>([]);
    }

    private sealed class AppInfo(AppInfoFetch result) : IBuildInfoClient
    {
        public Task<AppInfoFetch> GetAppInfoAsync(string appId, TimeSpan? cacheTtl = null, bool cachedOnly = false, CancellationToken ct = default)
            => Task.FromResult(result);
        public Task<BuildInfoFetch> GetPublicBranchAsync(string appId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(BuildInfoFetch.NoData);
    }
}
