using System.Net;
using System.Net.Http.Headers;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Data;
using Winnow.Enrich.SteamGridDb;
using Xunit;

namespace Winnow.Tests;

public sealed class SteamGridDbClientTests
{
    private const string Hero = """{"id":7,"width":3840,"height":2160,"url":"https://cdn2.steamgriddb.com/hero/0123456789abcdef0123456789abcdef.png","mime":"image/png","tags":[]}""";
    private static string Success(string assets) => "{\"success\":true,\"data\":[" + assets + "]}";

    [Fact]
    public async Task Known_id_request_filters_assets_and_caches_projected_metadata()
    {
        using var host = new Host(Success(Hero + "," + Hero.Replace("\"id\":7", "\"id\":8").Replace("\"tags\":[]", "\"tags\":[\"nsfw\"]")
            + "," + Hero.Replace("cdn2.steamgriddb.com", "untrusted.example")));
        var image = Assert.Single((await host.Client.GetHeroesAsync("220"))!);
        Assert.Equal("7", image.ImageId);
        Assert.Equal(3840, image.Width);
        Assert.False(image.Animated);
        Assert.EndsWith(".png", image.Url, StringComparison.Ordinal);
        Assert.Equal("Bearer fixture-key", host.Handler.Authorization);
        Assert.Equal("/api/v2/heroes/steam/220?types=static&nsfw=false&humor=false&epilepsy=false", host.Handler.Path);
        Assert.Equal(image, Assert.Single((await host.Client.GetHeroesAsync("220"))!));
        Assert.Equal(1, host.Handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Confirmed_empty_result_is_cached(HttpStatusCode status)
    {
        using var host = new Host(Success(""));
        host.Handler.Status = status;
        Assert.Empty((await host.Client.GetHeroesAsync("220"))!);
        Assert.Empty((await host.Client.GetHeroesAsync("220"))!);
        Assert.Equal(1, host.Handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Failures_create_no_negative_cache_and_pause_a_batch_until_key_changes(HttpStatusCode status)
    {
        using var host = new Host(Success(Hero));
        host.Handler.Status = status;
        Assert.Null(await host.Client.GetHeroesAsync("220"));
        Assert.Null(await host.Client.GetHeroesAsync("400"));
        Assert.Equal(1, host.Handler.Count);
        using var lease = host.Database.Factory.Lease();
        Assert.Equal(0, await lease.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM metadata_cache WHERE provider='steamgriddb';"));
        host.Keys.Key = "replacement-fixture-key";
        host.Handler.Status = HttpStatusCode.OK;
        Assert.Single((await host.Client.GetHeroesAsync("220"))!);
        Assert.Equal(2, host.Handler.Count);
    }

    [Fact]
    public async Task Expired_positive_cache_survives_missing_key_and_failed_refetch()
    {
        using var host = new Host(Success(Hero));
        var image = Assert.Single((await host.Client.GetHeroesAsync("220"))!);
        using (var lease = host.Database.Factory.Lease())
            await lease.Connection.ExecuteAsync("UPDATE metadata_cache SET fetched_at=@old;", new { old = DateTime.UtcNow.AddDays(-40) });
        host.Keys.Key = null;
        Assert.Equal(image, Assert.Single((await host.Client.GetHeroesAsync("220"))!));
        Assert.Equal(1, host.Handler.Count);
        host.Keys.Key = "fixture-key";
        host.Handler.Status = HttpStatusCode.Unauthorized;
        Assert.Equal(image, Assert.Single((await host.Client.GetHeroesAsync("220"))!));
        Assert.Equal(2, host.Handler.Count);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"success\":false,\"data\":[]}")]
    [InlineData("{\"success\":true}")]
    public async Task Invalid_envelopes_do_not_become_absence(string body)
    {
        using var host = new Host(body);
        Assert.Null(await host.Client.GetHeroesAsync("220"));
        using var lease = host.Database.Factory.Lease();
        Assert.Equal(0, await lease.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM metadata_cache WHERE provider='steamgriddb';"));
    }

    [Fact]
    public async Task Missing_key_invalid_id_and_oversized_json_are_soft_failures()
    {
        using var host = new Host(Success(Hero));
        host.Keys.Key = null;
        Assert.False(await host.Client.IsConfiguredAsync());
        Assert.Null(await host.Client.GetHeroesAsync("220"));
        host.Keys.Key = "fixture-key";
        Assert.Null(await host.Client.GetHeroesAsync("../220"));
        Assert.Equal(0, host.Handler.Count);
        host.Options.MaxResponseBytes = 16;
        Assert.Null(await host.Client.GetHeroesAsync("220"));
    }

    [Fact]
    public async Task Transport_failure_preserves_stale_success_without_advancing_its_cache_time()
    {
        using var host = new Host(Success(Hero));
        var image = Assert.Single((await host.Client.GetHeroesAsync("220"))!);
        var old = DateTime.UtcNow.AddDays(-40);
        using var lease = host.Database.Factory.Lease();
        await lease.Connection.ExecuteAsync("UPDATE metadata_cache SET fetched_at=@old;", new { old });
        host.Handler.ThrowTransport = true;
        Assert.Equal(image, Assert.Single((await host.Client.GetHeroesAsync("220"))!));
        Assert.Equal(old, await lease.Connection.ExecuteScalarAsync<DateTime>(
            "SELECT fetched_at FROM metadata_cache WHERE provider='steamgriddb';"));
    }

    [Theory]
    [InlineData("humor")]
    [InlineData("epilepsy")]
    [InlineData("animated")]
    public async Task Unsafe_asset_flags_are_filtered_locally(string flag)
    {
        using var host = new Host(Success(Hero.Replace("\"tags\":[]", $"\"tags\":[\"{flag}\"]")));
        Assert.Empty((await host.Client.GetHeroesAsync("220"))!);
    }

    [Fact]
    public async Task Typed_registration_retries_429_and_preserves_request_authentication()
    {
        using var db = new TempDatabase();
        var services = new ServiceCollection();
        services.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        services.AddSingleton<ISteamGridDbKeyProvider>(new Keys());
        services.AddSteamGridDb(options => options.RetryDelay = TimeSpan.FromMilliseconds(1));
        using var handler = new Handler(Success(Hero)) { FirstRateLimited = true };
        services.AddHttpClient(SteamGridDbClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        Assert.Single((await provider.GetRequiredService<ISteamGridDbClient>().GetHeroesAsync("220"))!);
        Assert.Equal(2, handler.Count);
        Assert.Equal("Bearer fixture-key", handler.Authorization);
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromSeconds(30), SteamGridDbResilience.RetryAfter(response, TimeSpan.FromSeconds(30)));
    }

    private sealed class Host : IDisposable
    {
        public TempDatabase Database { get; } = new();
        public Keys Keys { get; } = new();
        public Handler Handler { get; }
        public SteamGridDbOptions Options { get; } = new();
        private readonly HttpClient _http;
        public SteamGridDbClient Client { get; }
        public Host(string body)
        {
            Handler = new Handler(body);
            _http = new HttpClient(Handler);
            Client = new(_http, Keys, Database.Factory, Options, new SteamGridDbAvailability(), TimeProvider.System);
        }
        public void Dispose() { _http.Dispose(); Database.Dispose(); }
    }

    private sealed class Keys : ISteamGridDbKeyProvider
    {
        public string? Key { get; set; } = "fixture-key";
        public ValueTask<string?> GetApiKeyAsync(CancellationToken ct = default) => ValueTask.FromResult(Key);
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? Authorization { get; private set; }
        public string? Path { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool FirstRateLimited { get; set; }
        public bool ThrowTransport { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Count++;
            if (ThrowTransport) throw new HttpRequestException("Canned transport failure");
            Authorization = request.Headers.Authorization?.ToString();
            Path = request.RequestUri!.PathAndQuery;
            var response = new HttpResponseMessage(FirstRateLimited && Count == 1 ? HttpStatusCode.TooManyRequests : Status)
            { Content = new StringContent(body) };
            if (response.StatusCode == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }
}
