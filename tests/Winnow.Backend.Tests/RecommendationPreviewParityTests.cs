using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.App.Services;
using Winnow.Covers;
using Winnow.Electron.Fixtures;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class RecommendationPreviewParityTests
{
    [Theory]
    [InlineData("ratings", 3)]
    [InlineData("library", 2)]
    [InlineData("missing", 0)]
    public async Task Preview_populations_preserve_each_original_score_count_label_and_empty_result(string kind, int count)
    {
        await using var host = await Host.StartAsync(kind);
        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal(1, game.WorkId);
        Assert.Equal("Hades", game.Title);
        Assert.Equal(1, Assert.Single(game.Entries).OwnershipId);
        var details = await host.Api.GetAsync<GameDetailsResponse>("games/1/details");
        Assert.Equal(count, details.Ratings.Count);
        if (count > 0)
        {
            var steam = Assert.Single(details.Ratings, row => row.Source == "steam");
            Assert.Equal((1L, 93d, 1500, "Very Positive", host.Fixture.Now),
                (steam.WorkId, steam.Score, steam.RatingCount, steam.Label, steam.ObservedAt));
            var critics = Assert.Single(details.Ratings, row => row.Source == "igdb_critics");
            Assert.Equal((88d, 12, (string?)null), (critics.Score, critics.RatingCount, critics.Label));
            if (kind == "ratings")
            {
                var users = Assert.Single(details.Ratings, row => row.Source == "igdb_users");
                Assert.Equal((81d, 100, (string?)null), (users.Score, users.RatingCount, users.Label));
            }
        }
    }

    [Fact]
    public async Task Visual_fixture_has_exactly_two_five_card_shelves_with_original_titles_reasons_and_Hades_summary()
    {
        await using var host = await Host.StartAsync("shelf");
        var feed = await host.Api.GetAsync<FeedSnapshot>("feed");
        Assert.Equal(0, feed.CandidateCount);
        Assert.Equal(FeedConfidence.EarlyDays, feed.Confidence);
        Assert.Equal(new[] { "visual-0", "visual-1" }, feed.Shelves.Select(shelf => shelf.Id));
        Assert.Equal(new[] { "Worth another look", "Still waiting for their first session" }, feed.Shelves.Select(shelf => shelf.Title));
        Assert.Equal(new[] { "Games you started, with a reason to come back.", "A fresh start, already in your library." }, feed.Shelves.Select(shelf => shelf.Blurb));
        Assert.Equal(10, feed.Shelves.Sum(shelf => shelf.Items.Count));
        foreach (var shelf in feed.Shelves)
        {
            Assert.Equal(RecommendationPreviewFixture.Titles, shelf.Items.Select(item => item.Title));
            Assert.Equal(RecommendationPreviewFixture.Reasons, shelf.Items.Select(item => item.Reason));
            Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, shelf.Items.Select(item => item.ReleaseId));
        }
        var library = await host.Api.GetLibraryAsync();
        Assert.Equal(5, library.Games.Count);
        var hades = Assert.Single(library.Games, game => game.Title == "Hades");
        Assert.Equal("Defy the god of the dead as you battle out of the Underworld.", hades.Summary);
        Assert.All(library.Games, game => Assert.True(Assert.Single(game.Entries).Installed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_rating_response_is_held_without_a_SQLite_lease_and_late_release_preserves_original_bytes(bool cancel)
    {
        await using var host = await Host.StartAsync("library");
        host.Controls.Arm(new("details", WorkId: 1));
        using var cancellation = new CancellationTokenSource();
        var pending = host.Api.GetAsync<GameDetailsResponse>("games/1/details", cancellation.Token);
        var call = await host.HeldAsync();
        Assert.Equal(200, call.StatusCode);
        Assert.Equal(2, JsonDocument.Parse(call.Json!).RootElement.GetProperty("ratings").GetArrayLength());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(call.Json!))), call.ResponseSha256);
        await host.Fixture.ChangeAsync(new("replacement", Publish: false));
        Assert.Equal("Replacement", Assert.Single((await host.Api.GetLibraryAsync()).Games).Title);
        Assert.False(pending.IsCompleted);
        if (cancel)
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            await WaitAsync(() => call.Canceled);
            Assert.False(call.Completed);
        }
        host.Controls.Release(call.GateId);
        if (!cancel) Assert.Equal(2, (await pending).Ratings.Count);
        await WaitAsync(() => call.Completed);
        Assert.Equal(!cancel, call.Delivered);
    }

    [Fact]
    public async Task Detached_artwork_metadata_keeps_staleart_separate_from_the_current_reattached_selection()
    {
        await using var host = await Host.StartAsync("backdrop");
        host.Controls.Arm(new("artworkState", WorkId: 1, Slot: "Hero"));
        using var cancellation = new CancellationTokenSource();
        var pending = host.Api.GetAsync<JsonElement>("works/1/artwork/Hero", cancellation.Token);
        var old = await host.HeldAsync();
        Assert.Equal("staleart", JsonDocument.Parse(old.Json!).RootElement.GetProperty("current").GetProperty("previewKey").GetProperty("id").GetString());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await WaitAsync(() => old.Canceled);
        await host.Fixture.ChangeAsync(new("currentart", Publish: false));
        var current = await host.Api.GetAsync<JsonElement>("works/1/artwork/Hero");
        Assert.Equal("igdb-backdrop", current.GetProperty("current").GetProperty("previewKey").GetProperty("provider").GetString());
        Assert.Equal("currentart", current.GetProperty("current").GetProperty("previewKey").GetProperty("id").GetString());
        Assert.False(old.Completed);
        host.Controls.Release(old.GateId);
        await WaitAsync(() => old.Completed);
        Assert.False(old.Delivered);
        Assert.Contains("staleart", old.Json);
        Assert.DoesNotContain("currentart", old.Json);
    }

    [Fact]
    public async Task Canceled_hero_pixels_can_be_reacquired_from_the_real_pipeline_with_identical_encoded_bytes()
    {
        await using var host = await Host.StartAsync("ratings");
        host.Controls.Arm(new("image", Provider: "steam-hero", Id: "123"));
        using var cancellation = new CancellationTokenSource();
        var pending = host.Http.GetByteArrayAsync("api/v1/artwork/image?provider=steam-hero&id=123&width=440", cancellation.Token);
        var old = await host.HeldAsync();
        Assert.True(old.ResponseBytes > 0);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await WaitAsync(() => old.Canceled);
        var current = await host.Http.GetByteArrayAsync("api/v1/artwork/image?provider=steam-hero&id=123&width=440");
        Assert.Equal(old.ResponseSha256, Convert.ToHexString(SHA256.HashData(current)));
        using var bitmap = SKBitmap.Decode(current);
        Assert.True(bitmap.Width >= 440);
        Assert.False(old.Completed);
        host.Controls.Release(old.GateId);
        await WaitAsync(() => old.Completed);
        Assert.False(old.Delivered);
    }

    [Fact]
    public async Task Fixture_control_is_authenticated_and_supplied_source_artwork_has_portrait_and_landscape_dimensions()
    {
        await using var host = await Host.StartAsync("shelf");
        using var anonymous = new HttpClient { BaseAddress = host.Http.BaseAddress };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("__fixture/recommendation-preview/state")).StatusCode);
        var output = Environment.GetEnvironmentVariable("WINNOW_FEED_CAPTURE_COVERS");
        foreach (var id in RecommendationPreviewFixture.SteamIds)
        foreach (var provider in new[] { "steam", "steam-hero" })
        {
            var bytes = PreviewCoverSource.Image(new(provider, id));
            using var image = SKBitmap.Decode(bytes);
            Assert.Equal(provider == "steam" ? (400, 600) : (800, 450), (image.Width, image.Height));
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                await File.WriteAllBytesAsync(Path.Combine(output, $"{provider}_{id}.src.jpg"), bytes);
            }
        }
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) await Task.Delay(10, deadline.Token);
    }
    private sealed class Host(string directory, WebApplication app, WinnowApiClient api, HttpClient http) : IAsyncDisposable
    {
        public WinnowApiClient Api => api;
        public HttpClient Http => http;
        public RecommendationPreviewFixture Fixture => app.Services.GetRequiredService<RecommendationPreviewFixture>();
        public PreviewResponseControls Controls => app.Services.GetRequiredService<PreviewResponseControls>();
        public async Task<PreviewResponseControls.Call> HeldAsync()
        {
            await WaitAsync(() => Controls.Calls.Any(call => call.GateId is not null && call.ResponseReady));
            return Assert.Single(Controls.Calls, call => call.GateId is not null);
        }
        public static async Task<Host> StartAsync(string kind)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-recommendation-preview-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], RecommendationPreviewFixture.Register);
            RecommendationPreviewFixture.Map(app);
            await app.StartAsync();
            await app.Services.GetRequiredService<RecommendationPreviewFixture>().SeedAsync(new(kind, Publish: false));
            var connection = await BackendConnection.ReadAsync(directory);
            var http = new HttpClient { BaseAddress = new(connection.Address) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
            return new(directory, app, WinnowApiClient.Attach(directory), http);
        }
        public async ValueTask DisposeAsync()
        {
            Controls.Release(); api.Dispose(); http.Dispose();
            await app.StopAsync(); await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
