using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Identity;
using Winnow.Api.Contracts.Preferences;
using Winnow.App.Services;
using Winnow.Covers;
using Winnow.Electron.Fixtures;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class CoverBehaviorParityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("fit")]
    [InlineData("fill")]
    public async Task Reading_each_source_cover_mode_preserves_raw_storage_without_writing_a_default(string? mode)
    {
        await using var host = await Host.StartAsync(new("fit", Mode: mode));
        var first = await host.Api.GetAsync<PresentationPreferenceValue[]>("preferences/presentation");
        using var other = WinnowApiClient.Attach(host.Directory);
        var second = await other.GetAsync<PresentationPreferenceValue[]>("preferences/presentation");
        Assert.Equal(mode, Assert.Single(first, item => item.Preference == PresentationPreference.CoverArtMode).Value);
        Assert.Equal(mode, Assert.Single(second, item => item.Preference == PresentationPreference.CoverArtMode).Value);
        Assert.Equal(mode, host.State().GetProperty("storedMode").GetString());
        Assert.Empty(host.State().GetProperty("modeWrites").EnumerateArray());
    }

    [Fact]
    public async Task Fill_then_fit_persists_and_is_visible_to_a_new_frontend_without_read_side_writes()
    {
        await using var host = await Host.StartAsync(new("fit"));
        await host.Api.SendAsync(HttpMethod.Put, "preferences/presentation/CoverArtMode", new SetPresentationPreference("fill"));
        using var reopened = WinnowApiClient.Attach(host.Directory);
        var values = await reopened.GetAsync<PresentationPreferenceValue[]>("preferences/presentation");
        Assert.Equal("fill", Assert.Single(values, item => item.Preference == PresentationPreference.CoverArtMode).Value);
        await reopened.SendAsync(HttpMethod.Put, "preferences/presentation/CoverArtMode", new SetPresentationPreference("fit"));
        Assert.Equal("fit", host.State().GetProperty("storedMode").GetString());
        Assert.Equal(new[] { "fill", "fit" }, host.State().GetProperty("modeWrites").EnumerateArray().Select(item => item.GetString()));
    }

    [Theory]
    [InlineData("plugin")]
    [InlineData("unavailable")]
    [InlineData("user")]
    [InlineData("igdb")]
    [InlineData("steam")]
    public async Task Actual_library_and_merge_workspace_share_the_source_selected_key_and_actual_image_path(string art)
    {
        await using var host = await Host.StartAsync(new("selection", Art: art));
        var library = await host.Api.GetLibraryAsync();
        Assert.Equal(new[] { "Game", "Game edition" }, library.Games.Select(game => game.Title));
        Assert.All(library.Games, game => Assert.Equal("epic", Assert.Single(game.Entries).Store));
        var review = await host.Api.GetAsync<IdentityReviewResponse>("identity/review/");
        var candidate = Assert.Single(review.Candidates);
        Assert.Equal((1L, 2L, .95), (candidate.LeftReleaseId, candidate.RightReleaseId, candidate.Score));
        Assert.Equal(library.Games[0].CoverUrl, Assert.Single(review.Workspace.Works, work => work.Id == 1).CoverUrl);
        var actual = await host.Api.GetAsync<ArtworkState>("works/1/artwork/Cover");
        CoverKey? expected = art switch
        {
            "unavailable" => null,
            "user" => CoverKey.User("fixtureuser"),
            "igdb" => CoverKey.Igdb("co42"),
            "steam" => CoverKey.Steam("42"),
            _ => PluginArtRef.Key("fixture", "https://images.example.test/cover.jpg"),
        };
        Assert.Equal(expected, actual.Current?.PreviewKey);
        if (expected is { } key)
        {
            var bytes = await host.Http.GetByteArrayAsync($"api/v1/artwork/image?provider={key.Provider}&id={key.Id}&width=160");
            using var image = SKBitmap.Decode(bytes);
            Assert.Equal((160, 240), (image.Width, image.Height));
            Assert.Equal(SKColors.SlateBlue, image.GetPixel(0, 0));
        }
        else Assert.DoesNotContain(host.Responses.Calls, call => call.Operation == "image");
    }

    [Fact]
    public async Task Four_held_network_fetches_do_not_block_a_cached_43_HTTP_image()
    {
        await using var host = await Host.StartAsync(new("conversion"));
        foreach (var id in new[] { "42", "44", "45", "46" })
            host.Source.Arm(new("fetch", Provider: "steam", Id: id, IgnoreCancellation: false));
        var held = new[] { "42", "44", "45", "46" }.Select(id => host.Http.GetByteArrayAsync($"api/v1/artwork/image?provider=steam&id={id}&width=160")).ToArray();
        try
        {
            await WaitAsync(() => host.Source.Calls.Count == 4);
            var cached = await host.Http.GetByteArrayAsync("api/v1/artwork/image?provider=steam&id=43&width=160")
                .WaitAsync(TimeSpan.FromSeconds(1));
            using var image = SKBitmap.Decode(cached);
            Assert.Equal(SKColors.SlateBlue, image.GetPixel(0, 0));
            Assert.All(held, pending => Assert.False(pending.IsCompleted));
            Assert.DoesNotContain(host.Source.Calls, call => call.Id == "43");
        }
        finally { host.Source.Release(); }
        Assert.All(await Task.WhenAll(held).WaitAsync(TimeSpan.FromSeconds(3)), bytes => Assert.NotEmpty(bytes));
    }

    [Fact]
    public async Task A_throwing_source_callback_keeps_HTTP_cancellation_and_releases_the_fetch_for_a_retry()
    {
        await using var host = await Host.StartAsync(new("conversion"));
        host.Source.Arm(new("fetch", Provider: "steam", Id: "42", IgnoreCancellation: false, ThrowOnCancel: true));
        using var cancellation = new CancellationTokenSource();
        var pending = host.Http.GetByteArrayAsync("api/v1/artwork/image?provider=steam&id=42&width=160", cancellation.Token);
        await WaitAsync(() => host.Source.Calls.Count == 1);
        var old = Assert.Single(host.Source.Calls);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await WaitAsync(() => old.Canceled && old.Completed && old.CancellationCallbackFailures == 1);
        var recovered = await host.Http.GetByteArrayAsync("api/v1/artwork/image?provider=steam&id=42&width=160");
        Assert.NotEmpty(recovered);
        Assert.Equal(2, host.Source.Calls.Count);
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) await Task.Delay(10, deadline.Token);
    }
    private sealed class Host(string directory, WebApplication app, WinnowApiClient api, HttpClient http) : IAsyncDisposable
    {
        public string Directory => directory;
        public WinnowApiClient Api => api;
        public HttpClient Http => http;
        public CoverBehaviorFixture Fixture => app.Services.GetRequiredService<CoverBehaviorFixture>();
        public CoverBehaviorSource Source => app.Services.GetRequiredService<CoverBehaviorSource>();
        public PreviewResponseControls Responses => app.Services.GetRequiredService<PreviewResponseControls>();
        public JsonElement State() => JsonSerializer.SerializeToElement(Fixture.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        public static async Task<Host> StartAsync(CoverBehaviorSeed seed)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-cover-behavior-parity", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], CoverBehaviorFixture.Register);
            CoverBehaviorFixture.Map(app);
            await app.StartAsync();
            await app.Services.GetRequiredService<CoverBehaviorFixture>().SeedAsync(seed);
            var connection = await BackendConnection.ReadAsync(directory);
            var http = new HttpClient { BaseAddress = new(connection.Address) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
            return new(directory, app, WinnowApiClient.Attach(directory), http);
        }
        public async ValueTask DisposeAsync()
        {
            Source.Release(); Responses.Release(); api.Dispose(); http.Dispose();
            await app.StopAsync(); await app.DisposeAsync();
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }
}
