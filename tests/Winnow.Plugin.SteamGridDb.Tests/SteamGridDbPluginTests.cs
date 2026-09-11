using System.Text;
using System.Text.Json;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Plugin.SteamGridDb.Tests;

public sealed class SteamGridDbPluginTests
{
    private const string Hero = """{"id":7,"width":3840,"height":2160,"url":"https://cdn2.steamgriddb.com/hero/0123456789abcdef0123456789abcdef.png","mime":"image/png","tags":[]}""";
    private const string CacheKey = "heroes:steam:220";
    private static readonly PluginGame Game = new("opaque-work-handle", "Fixture game", new Dictionary<string, string> { ["steam"] = "220" });
    private static string Success(string assets) => "{\"success\":true,\"data\":[" + assets + "]}";

    [Fact]
    public async Task Exact_id_request_filters_assets_and_caches_without_a_secret_read_on_a_warm_hit()
    {
        var host = await Host.CreateAsync(Success(Hero + "," + Hero + "," + Hero.Replace("cdn2.steamgriddb.com", "untrusted.example")));
        var image = Assert.Single((await host.Plugin.GetArtworkAsync(Game))!);
        Assert.Equal("7", image.Id);
        Assert.Equal(3840, image.Width);
        Assert.Equal(2160, image.Height);
        Assert.Equal(PluginArtworkKind.Background, image.Kind);
        Assert.Equal("hero", image.ImageType);
        Assert.False(image.Animated);
        Assert.False(image.Transparent);
        var request = Assert.Single(host.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("https://www.steamgriddb.com/api/v2/heroes/steam/220?types=static&nsfw=false&humor=false&epilepsy=false", request.Url);
        Assert.Equal("Bearer fixture-key", request.Headers["Authorization"]);
        Assert.Equal(image, Assert.Single((await host.Plugin.GetArtworkAsync(Game))!));
        Assert.Single(host.Requests);
        Assert.Equal(1, host.SecretReads);
        Assert.Equal(host.Clock.Now.AddDays(30), host.Entries[CacheKey].ExpiresAt);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    public async Task Confirmed_absence_is_cached_and_rechecked_after_expiry(int status)
    {
        var host = await Host.CreateAsync(Success(""));
        host.Status = status;
        Assert.Empty((await host.Plugin.GetArtworkAsync(Game))!);
        Assert.Empty((await host.Plugin.GetArtworkAsync(Game))!);
        Assert.Single(host.Requests);
        host.Clock.Now = host.Clock.Now.AddDays(31);
        host.Key = null;
        Assert.Null(await host.Plugin.GetArtworkAsync(Game));
        host.Key = "fixture-key";
        Assert.Empty((await host.Plugin.GetArtworkAsync(Game))!);
        Assert.Equal(2, host.Requests.Count);
    }

    [Theory]
    [InlineData(401, 120)]
    [InlineData(403, 120)]
    [InlineData(429, 30)]
    [InlineData(503, 30)]
    public async Task Failed_requests_do_not_write_misses_and_pause_other_games_for_the_same_key(int status, int pauseSeconds)
    {
        var host = await Host.CreateAsync(Success(Hero));
        host.Status = status;
        Assert.Null(await host.Plugin.GetArtworkAsync(Game));
        Assert.Null(await host.Plugin.GetArtworkAsync(Game with { ExternalIds = new Dictionary<string, string> { ["steam"] = "400" } }));
        Assert.Single(host.Requests);
        Assert.Empty(host.Entries);
        host.Clock.Now = host.Clock.Now.AddSeconds(pauseSeconds - 1);
        Assert.Null(await host.Plugin.GetArtworkAsync(Game));
        Assert.Single(host.Requests);
        host.Clock.Now = host.Clock.Now.AddSeconds(1);
        host.Status = 200;
        Assert.Single((await host.Plugin.GetArtworkAsync(Game))!);
        Assert.Equal(2, host.Requests.Count);
    }

    [Fact]
    public async Task Replacement_key_bypasses_a_previous_keys_failure_pause()
    {
        var host = await Host.CreateAsync(Success(Hero));
        host.Status = 401;
        Assert.Null(await host.Plugin.GetArtworkAsync(Game));
        host.Key = "replacement-fixture-key";
        host.Status = 200;
        Assert.Single((await host.Plugin.GetArtworkAsync(Game))!);
        Assert.Equal(2, host.Requests.Count);
        Assert.Equal("Bearer replacement-fixture-key", host.Requests[1].Headers["Authorization"]);
    }

    [Fact]
    public async Task Stale_artwork_survives_missing_credentials_or_failed_refetch_without_extending_expiry()
    {
        var host = await Host.CreateAsync(Success(Hero));
        var image = Assert.Single((await host.Plugin.GetArtworkAsync(Game))!);
        var originalEntry = host.Entries[CacheKey];
        host.Clock.Now = host.Clock.Now.AddDays(40);
        host.Key = null;
        Assert.Equal(image, Assert.Single((await host.Plugin.GetArtworkAsync(Game))!));
        Assert.Single(host.Requests);
        host.Key = "fixture-key";
        host.Failure = new HttpRequestException("Canned transport failure");
        Assert.Equal(image, Assert.Single((await host.Plugin.GetArtworkAsync(Game))!));
        Assert.Same(originalEntry, host.Entries[CacheKey]);
    }

    [Fact]
    public async Task Original_built_in_cache_envelope_remains_readable_without_network()
    {
        var host = await Host.CreateAsync(Success(""));
        host.Key = null;
        host.Entries[CacheKey] = new(Encoding.UTF8.GetBytes("""
            {"version":1,"images":[{"imageId":"7","width":3840,"height":2160,"alphaChannel":null,"animated":false,"imageType":"hero","url":"https://cdn2.steamgriddb.com/hero/0123456789abcdef0123456789abcdef.png"}]}
            """), host.Clock.Now.AddDays(1));
        var image = Assert.Single((await host.Plugin.GetArtworkAsync(Game))!);
        Assert.Equal("7", image.Id);
        Assert.Equal("hero", image.ImageType);
        Assert.Empty(host.Requests);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"success\":false,\"data\":[]}")]
    [InlineData("{\"success\":true}")]
    [InlineData("[]")]
    public async Task Malformed_responses_do_not_confirm_absence(string body)
    {
        var host = await Host.CreateAsync(body);
        Assert.Null(await host.Plugin.GetArtworkAsync(Game));
        Assert.Empty(host.Entries);
    }

    [Theory]
    [InlineData("../220")]
    [InlineData("220?other=1")]
    [InlineData("0")]
    [InlineData("4294967296")]
    [InlineData("")]
    public async Task Invalid_steam_ids_never_become_http_requests(string appId)
    {
        var host = await Host.CreateAsync(Success(Hero));
        Assert.Null(await host.Plugin.GetArtworkAsync(Game with { ExternalIds = new Dictionary<string, string> { ["steam"] = appId } }));
        Assert.Empty(host.Requests);
    }

    [Fact]
    public async Task Titles_and_other_stores_do_not_trigger_fuzzy_identity_search()
    {
        var host = await Host.CreateAsync(Success(Hero));
        Assert.Null(await host.Plugin.GetArtworkAsync(Game with { ExternalIds = new Dictionary<string, string> { ["gog"] = "220" } }));
        Assert.Empty(host.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad key")]
    [InlineData("key\r\nextra-header: value")]
    public async Task Missing_or_invalid_credentials_do_not_reach_http(string? key)
    {
        var host = await Host.CreateAsync(Success(Hero));
        host.Key = key;
        Assert.Null(await host.Plugin.GetArtworkAsync(Game));
        Assert.Empty(host.Requests);
    }

    [Theory]
    [InlineData("nsfw")]
    [InlineData("humor")]
    [InlineData("epilepsy")]
    [InlineData("animated")]
    public async Task Unsafe_flags_and_tags_are_checked_even_if_the_service_ignores_filters(string flag)
    {
        var host = await Host.CreateAsync(Success(Hero.Replace("\"tags\":[]", $"\"{flag}\":true") + "," + Hero.Replace("\"tags\":[]", $"\"tags\":[\"{flag.ToUpperInvariant()}\"]")));
        Assert.Empty((await host.Plugin.GetArtworkAsync(Game))!);
    }

    [Theory]
    [InlineData("\"width\":3840", "\"width\":1080")]
    [InlineData("\"height\":2160", "\"height\":0")]
    [InlineData("\"width\":3840", "\"width\":9000")]
    [InlineData("\"id\":7", "\"id\":0")]
    [InlineData("https://cdn2.steamgriddb.com", "http://cdn2.steamgriddb.com")]
    [InlineData("/hero/", "/grid/")]
    [InlineData(".png\"", ".png?token=private\"")]
    [InlineData(".png\"", ".gif\"")]
    [InlineData("image/png", "image/gif")]
    public async Task Invalid_dimensions_ids_formats_and_noncanonical_urls_are_rejected(string before, string after)
    {
        var host = await Host.CreateAsync(Success(Hero.Replace(before, after)));
        Assert.Empty((await host.Plugin.GetArtworkAsync(Game))!);
    }

    [Fact]
    public async Task Oversized_payloads_do_not_create_observations()
    {
        var host = await Host.CreateAsync(Success(Hero));
        host.Body = new byte[2 * 1024 * 1024 + 1];
        Assert.Null(await host.Plugin.GetArtworkAsync(Game));
        Assert.Empty(host.Entries);
    }

    [Fact]
    public async Task Invalid_cached_urls_are_refetched_instead_of_bypassing_validation()
    {
        var host = await Host.CreateAsync(Success(Hero));
        host.Entries[CacheKey] = new(Encoding.UTF8.GetBytes("""
            {"version":1,"images":[{"imageId":"7","width":3840,"height":2160,"url":"https://untrusted.example/hero.png"}]}
            """), host.Clock.Now.AddDays(30));
        Assert.Single((await host.Plugin.GetArtworkAsync(Game))!);
        Assert.Single(host.Requests);
    }

    [Fact]
    public async Task Cancellation_is_not_downgraded_to_missing_artwork()
    {
        var host = await Host.CreateAsync(Success(Hero));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Plugin.GetArtworkAsync(Game, cancellation.Token));
        Assert.Empty(host.Requests);
        Assert.Empty(host.Entries);
    }

    [Fact]
    public void Plugin_depends_on_the_public_sdk_without_host_or_enrichment_assemblies()
    {
        var references = typeof(SteamGridDbPlugin).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name).Where(name => name!.StartsWith("Winnow", StringComparison.Ordinal));
        Assert.Equal(["Winnow.PluginSdk"], references);
    }

    [Fact]
    public void Manifest_declares_a_secret_and_the_host_http_budget()
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "steamgriddb.plugin.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("steamgriddb", manifest.Id);
        Assert.Equal(typeof(SteamGridDbPlugin).FullName, manifest.EntryType);
        Assert.Equal("Winnow.Plugin.SteamGridDb.dll", manifest.EntryAssembly);
        Assert.Equal(PluginApi.Version, manifest.ApiVersion);
        Assert.Equal([PluginCapabilities.Artwork], manifest.Capabilities);
        var setting = Assert.Single(manifest.Settings);
        Assert.Equal("apikey", setting.Key);
        Assert.True(setting.Secret);
        Assert.True(setting.Required);
        Assert.Equal(1, manifest.Network.RequestsPerSecond);
        Assert.Equal(2, manifest.Network.MaxRetries);
        Assert.Equal(2097152, manifest.Network.MaxResponseBytes);
        Assert.Equal(["www.steamgriddb.com", "cdn2.steamgriddb.com"], manifest.Network.AllowedHosts);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Host : IPluginContext, IPluginSecrets, IPluginCache, IPluginHttp, IPluginSettings
    {
        public string PluginId => "steamgriddb";
        public IPluginSettings Settings => this;
        public IPluginSecrets Secrets => this;
        public IPluginCache Cache => this;
        public IPluginHttp Http => this;
        public Clock Clock { get; } = new();
        public SteamGridDbPlugin Plugin { get; private set; } = null!;
        public string? Key { get; set; } = "fixture-key";
        public int SecretReads { get; private set; }
        public Dictionary<string, PluginCacheEntry> Entries { get; } = [];
        public List<PluginHttpRequest> Requests { get; } = [];
        public int Status { get; set; } = 200;
        public byte[] Body { get; set; } = [];
        public Exception? Failure { get; set; }

        public static async Task<Host> CreateAsync(string body)
        {
            var host = new Host { Body = Encoding.UTF8.GetBytes(body) };
            host.Plugin = new SteamGridDbPlugin(host.Clock);
            await host.Plugin.InitializeAsync(host);
            return host;
        }

        ValueTask<string?> IPluginSecrets.GetAsync(string key, CancellationToken cancellationToken)
        {
            Assert.Equal("apikey", key);
            SecretReads++;
            return ValueTask.FromResult(Key);
        }

        ValueTask<PluginCacheEntry?> IPluginCache.GetAsync(string key, CancellationToken cancellationToken)
            => ValueTask.FromResult(Entries.GetValueOrDefault(key));

        ValueTask IPluginCache.SetAsync(string key, PluginCacheEntry entry, CancellationToken cancellationToken)
        {
            Entries[key] = entry;
            return ValueTask.CompletedTask;
        }

        public Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Failure is not null) throw Failure;
            return Task.FromResult(new PluginHttpResponse(Status, Body, new Dictionary<string, string>()));
        }

        ValueTask<string?> IPluginSettings.GetAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        ValueTask IPluginSettings.SetAsync(string key, string? value, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
