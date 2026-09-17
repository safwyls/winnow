using System.Text;
using System.Text.Json;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Plugin.SteamGridDb.Tests;

public sealed partial class SteamGridDbPluginTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef";
    private static string BrowserAsset(PluginArtworkKind kind, int id = 7, bool prefix = false)
    {
        var type = kind switch { PluginArtworkKind.Background => "hero", PluginArtworkKind.Cover => "grid", _ => "icon" };
        var thumb = kind == PluginArtworkKind.Cover ? "thumb" : type + "_thumb";
        return JsonSerializer.Serialize(new
        {
            id, width = kind switch { PluginArtworkKind.Background => 3840, PluginArtworkKind.Cover => 600, _ => 256 },
            height = kind switch { PluginArtworkKind.Background => 1240, PluginArtworkKind.Cover => 900, _ => 256 },
            url = $"https://cdn2.steamgriddb.com/{(prefix ? "file/sgdb-cdn/" : "")}{type}/{Hash}.png",
            thumb = $"https://cdn2.steamgriddb.com/{thumb}/{Hash}.png", author = new { name = "Fixture artist" },
            mime = "image/png", tags = Array.Empty<string>(),
        });
    }

    private static string BrowserResponse(string assets, int page = 0, int limit = 50, int total = 1)
        => $"{{\"success\":true,\"page\":{page},\"limit\":{limit},\"total\":{total},\"data\":[{assets}]}}";

    [Theory]
    [InlineData(PluginArtworkKind.Background, "heroes", "hero")]
    [InlineData(PluginArtworkKind.Cover, "grids", "grid")]
    [InlineData(PluginArtworkKind.Icon, "icons", "icon")]
    public async Task Browser_pages_all_slots_with_exact_ids_attribution_and_isolated_warm_cache(PluginArtworkKind kind, string endpoint, string type)
    {
        var host = await Host.CreateAsync(BrowserResponse(BrowserAsset(kind, prefix: true), limit: 1, total: 2));
        Assert.Contains(kind, host.Plugin.SupportedArtworkKinds);
        var first = await host.Plugin.BrowseArtworkAsync(Game, kind);
        Assert.Equal(PluginArtworkAvailability.Available, first.Availability);
        var image = Assert.Single(first.Items);
        Assert.Equal(kind, image.Kind);
        Assert.Equal(type, image.ImageType);
        Assert.Equal("Fixture artist", image.Creator);
        Assert.Equal($"https://www.steamgriddb.com/{type}/7", image.PageUrl);
        Assert.NotNull(image.ThumbnailUrl);
        Assert.Equal($"v1:220:{type}:1", first.NextCursor);
        var request = Assert.Single(host.Requests);
        Assert.StartsWith($"https://www.steamgriddb.com/api/v2/{endpoint}/steam/220?types=static&nsfw=false&humor=false&epilepsy=false&mimes=", request.Url);
        Assert.EndsWith("&limit=50&page=0", request.Url);
        Assert.Equal("Bearer fixture-key", request.Headers["Authorization"]);
        if (kind == PluginArtworkKind.Cover) Assert.Contains("dimensions=600x900,342x482,660x930", request.Url);
        if (kind == PluginArtworkKind.Icon) Assert.Contains("mimes=image/png&", request.Url);

        host.Body = Encoding.UTF8.GetBytes(BrowserResponse(BrowserAsset(kind, 8), page: 1, limit: 1, total: 2));
        var second = await host.Plugin.BrowseArtworkAsync(Game, kind, first.NextCursor);
        Assert.Null(second.NextCursor);
        Assert.Equal("8", Assert.Single(second.Items).Id);
        Assert.EndsWith("&page=1", host.Requests[1].Url);
        Assert.Equal(2, host.Entries.Count);
        Assert.All(host.Entries.Values, entry => Assert.Equal(host.Clock.Now.AddDays(30), entry.ExpiresAt));
        host.Key = null;
        Assert.Equal(image, Assert.Single((await host.Plugin.BrowseArtworkAsync(Game, kind)).Items));
        Assert.Equal(2, host.Requests.Count);
        Assert.Equal(2, host.SecretReads);
    }

    [Fact]
    public async Task Browser_cache_is_separate_for_kinds_and_from_automatic_hero_cache()
    {
        var host = await Host.CreateAsync(Success(Hero));
        await host.Plugin.GetArtworkAsync(Game);
        await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Background);
        host.Body = Encoding.UTF8.GetBytes(BrowserResponse(BrowserAsset(PluginArtworkKind.Icon)));
        await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon);
        Assert.Equal(3, host.Requests.Count);
        Assert.Equal(3, host.Entries.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad key")]
    public async Task Browser_missing_key_is_actionable_without_confirming_a_miss(string? key)
    {
        var host = await Host.CreateAsync(BrowserResponse(BrowserAsset(PluginArtworkKind.Icon)));
        host.Key = key;
        var result = await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon);
        Assert.Equal(PluginArtworkAvailability.SetupRequired, result.Availability);
        Assert.Contains("Settings", result.Message);
        Assert.Empty(host.Requests);
        Assert.Empty(host.Entries);
    }

    [Theory]
    [InlineData(401, PluginArtworkAvailability.SetupRequired)]
    [InlineData(403, PluginArtworkAvailability.SetupRequired)]
    [InlineData(429, PluginArtworkAvailability.Unavailable)]
    [InlineData(503, PluginArtworkAvailability.Unavailable)]
    public async Task Browser_failures_pause_the_same_credential_without_caching_misses(int status, PluginArtworkAvailability expected)
    {
        var host = await Host.CreateAsync(BrowserResponse(BrowserAsset(PluginArtworkKind.Icon)));
        host.Status = status;
        var result = await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon);
        Assert.Equal(expected, result.Availability);
        Assert.DoesNotContain("fixture-key", result.Message);
        await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Cover);
        Assert.Single(host.Requests);
        Assert.Empty(host.Entries);
        host.Key = "replacement-key";
        host.Status = 200;
        Assert.Single((await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon)).Items);
        Assert.Equal(2, host.Requests.Count);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    public async Task Browser_confirmed_empty_pages_cache_but_expired_empty_pages_do_not_hide_missing_setup(int status)
    {
        var host = await Host.CreateAsync(BrowserResponse("", total: 0));
        host.Status = status;
        var result = await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon);
        Assert.Equal(PluginArtworkAvailability.Available, result.Availability);
        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
        await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon);
        Assert.Single(host.Requests);
        host.Clock.Now = host.Clock.Now.AddDays(31);
        host.Key = null;
        Assert.Equal(PluginArtworkAvailability.SetupRequired, (await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon)).Availability);
    }

    [Fact]
    public async Task Browser_stale_positive_pages_survive_missing_keys_and_http_failures_without_extending_expiry()
    {
        var host = await Host.CreateAsync(BrowserResponse(BrowserAsset(PluginArtworkKind.Cover)));
        var first = await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Cover);
        var original = Assert.Single(host.Entries).Value;
        host.Clock.Now = host.Clock.Now.AddDays(31);
        host.Key = null;
        var offline = await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Cover);
        Assert.Equal(first.Items, offline.Items);
        Assert.Contains("cached", offline.Message);
        host.Key = "fixture-key";
        host.Failure = new HttpRequestException("secret-response-body");
        var failure = await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Cover);
        Assert.Equal(first.Items, failure.Items);
        Assert.DoesNotContain("secret-response-body", failure.Message);
        Assert.Same(original, Assert.Single(host.Entries).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("v1:220:icon:0")]
    [InlineData("v1:220:icon:01")]
    [InlineData("v1:220:icon:-1")]
    [InlineData("v1:220:icon:2147483647")]
    [InlineData("v1:220:icon:1&other=2")]
    [InlineData("v1:400:icon:1")]
    [InlineData("v1:220:hero:1")]
    public async Task Browser_cursors_are_scoped_and_validated_before_cache_secrets_or_network(string cursor)
    {
        var host = await Host.CreateAsync(BrowserResponse(BrowserAsset(PluginArtworkKind.Icon)));
        var result = await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon, cursor);
        Assert.Equal(PluginArtworkAvailability.Unavailable, result.Availability);
        Assert.Empty(host.Requests);
        Assert.Empty(host.Entries);
        Assert.Equal(0, host.SecretReads);
    }

    [Fact]
    public async Task Browser_missing_exact_id_and_unsupported_kind_never_search_names()
    {
        var host = await Host.CreateAsync(Success(Hero));
        Assert.Equal(PluginArtworkAvailability.Unsupported,
            (await host.Plugin.BrowseArtworkAsync(Game with { ExternalIds = new Dictionary<string, string> { ["gog"] = "220" } }, PluginArtworkKind.Icon)).Availability);
        Assert.Equal(PluginArtworkAvailability.Unsupported,
            (await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Screenshot)).Availability);
        Assert.Empty(host.Requests);
    }

    [Theory]
    [InlineData("nsfw")]
    [InlineData("humor")]
    [InlineData("epilepsy")]
    [InlineData("animated")]
    public async Task Browser_filters_flags_and_tags_and_keeps_paging_when_a_whole_page_is_filtered(string flag)
    {
        var asset = BrowserAsset(PluginArtworkKind.Icon);
        var host = await Host.CreateAsync(BrowserResponse(asset.Replace("\"tags\":[]", $"\"{flag}\":true") + ","
            + asset.Replace("\"tags\":[]", $"\"tags\":[\"{flag.ToUpperInvariant()}\"]"), limit: 2, total: 3));
        var result = await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon);
        Assert.Empty(result.Items);
        Assert.Equal("v1:220:icon:1", result.NextCursor);
    }

    [Theory]
    [InlineData("https://cdn2.steamgriddb.com/icon/", "http://cdn2.steamgriddb.com/icon/")]
    [InlineData("cdn2.steamgriddb.com", "untrusted.example")]
    [InlineData("/icon/", "/grid/")]
    [InlineData(".png\"", ".ico\"")]
    [InlineData(".png\"", ".png?token=secret\"")]
    [InlineData("\"width\":256", "\"width\":128")]
    [InlineData("\"height\":256", "\"height\":0")]
    [InlineData("\"mime\":\"image/png\"", "\"mime\":\"image/vnd.microsoft.icon\"")]
    [InlineData("\"tags\":[]", "\"type\":\"animated\"")]
    public async Task Browser_rejects_invalid_assets(string before, string after)
    {
        var host = await Host.CreateAsync(BrowserResponse(BrowserAsset(PluginArtworkKind.Icon).Replace(before, after)));
        Assert.Empty((await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon)).Items);
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("{\"success\":false,\"data\":[]}")]
    [InlineData("{\"success\":true,\"page\":1,\"data\":[]}")]
    [InlineData("{\"success\":true,\"page\":\"0\",\"data\":[]}")]
    [InlineData("{\"success\":true,\"limit\":0,\"data\":[]}")]
    [InlineData("{\"success\":true,\"limit\":51,\"data\":[]}")]
    [InlineData("{\"success\":true,\"total\":-1,\"data\":[]}")]
    public async Task Browser_malformed_paging_is_unavailable_and_not_cached(string response)
    {
        var host = await Host.CreateAsync(response);
        Assert.Equal(PluginArtworkAvailability.Unavailable, (await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon)).Availability);
        Assert.Empty(host.Entries);
    }

    [Fact]
    public async Task Browser_bounds_response_bytes_and_asset_count()
    {
        var host = await Host.CreateAsync(Success(string.Join(',', Enumerable.Repeat(BrowserAsset(PluginArtworkKind.Icon), 51))));
        Assert.Equal(PluginArtworkAvailability.Unavailable, (await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon)).Availability);
        Assert.Empty(host.Entries);
        host.Clock.Now = host.Clock.Now.AddMinutes(1);
        host.Body = new byte[2097153];
        Assert.Equal(PluginArtworkAvailability.Unavailable, (await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon)).Availability);
        Assert.Empty(host.Entries);
    }

    [Fact]
    public async Task Browser_deduplicates_assets_and_discards_untrusted_attribution_urls()
    {
        var asset = BrowserAsset(PluginArtworkKind.Cover).Replace($"https://cdn2.steamgriddb.com/thumb/{Hash}.png", "https://untrusted.example/thumb.png");
        var host = await Host.CreateAsync(BrowserResponse(asset + "," + asset, total: 2));
        var result = await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Cover);
        Assert.Null(Assert.Single(result.Items).ThumbnailUrl);
    }

    [Fact]
    public async Task Browser_revalidates_cached_image_urls_and_cursor_before_using_a_warm_page()
    {
        var host = await Host.CreateAsync(BrowserResponse(BrowserAsset(PluginArtworkKind.Cover)));
        await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Cover);
        var pair = Assert.Single(host.Entries);
        host.Entries[pair.Key] = pair.Value with { Payload = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(pair.Value.Payload).Replace("cdn2.steamgriddb.com", "untrusted.example")) };
        Assert.Single((await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Cover)).Items);
        Assert.Equal(2, host.Requests.Count);
        var entry = host.Entries[pair.Key];
        host.Entries[pair.Key] = entry with { Payload = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(entry.Payload).Replace("\"nextCursor\":null", "\"nextCursor\":\"untrusted\"")) };
        await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Cover);
        Assert.Equal(3, host.Requests.Count);
    }

    [Fact]
    public async Task Browser_cancellation_during_transport_propagates_without_cache_or_credential_pause()
    {
        var host = await Host.CreateAsync(BrowserResponse(BrowserAsset(PluginArtworkKind.Icon)));
        using var cancellation = new CancellationTokenSource();
        host.RequestStarted = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon, cancellationToken: cancellation.Token));
        Assert.Empty(host.Entries);
        host.RequestStarted = null;
        Assert.Single((await host.Plugin.BrowseArtworkAsync(Game, PluginArtworkKind.Icon)).Items);
        Assert.Equal(2, host.Requests.Count);
    }

    [Fact]
    public async Task Automatic_heroes_accept_the_canonical_file_prefix_without_browser_cache_changes()
    {
        var host = await Host.CreateAsync(Success(Hero.Replace("/hero/", "/file/sgdb-cdn/hero/")));
        Assert.Single((await host.Plugin.GetArtworkAsync(Game))!);
        Assert.Equal(CacheKey, Assert.Single(host.Entries).Key);
    }
}
