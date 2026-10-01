using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Preferences;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Electron.Fixtures;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class ArtworkStateParityTests
{
    [Fact]
    public async Task Projected_cover_changes_preserve_canonical_revision_and_saved_manual_choice_until_group_reset()
    {
        await using var host = await Host.StartAsync("browser");
        var canonical = await host.State(1);
        Assert.Equal(CoverKey.Steam("620"), canonical.Current!.PreviewKey);
        Assert.Equal(CoverKey.Igdb("preferred"), (await host.State(2)).Current!.PreviewKey);
        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal(1, game.WorkId);
        Assert.Equal(new[] { 1L, 2L }, game.Entries.Select(entry => entry.WorkId));
        Assert.Equal("epic", (await host.Api.GetWorkspaceAsync()).PreferredHeaderStores[1]);
        var browser = new ApiArtworkBrowserService(host.Api);
        await browser.GetCurrentAsync(1, ArtworkSlot.Cover);
        var page = await browser.BrowseAsync(1, ArtworkSlot.Cover, "igdb");
        var manual = Assert.Single(page.Items, item => item.AssetId == "manual");
        Assert.Equal("igdb", manual.SourceId);
        Assert.NotNull(manual.OfferId);
        Assert.True((await browser.SaveAsync(1, ArtworkSlot.Cover, manual)).Success);
        var saved = await host.State(1);
        var key = saved.Current!.PreviewKey;
        Assert.Equal("user", key.Provider);
        Assert.Equal("igdb", saved.Current.SourceId);
        var choice = Assert.Single((await host.Fixture.StateAsync()).AsJson().GetProperty("choices").EnumerateArray());
        Assert.Equal(1, choice.GetProperty("workId").GetInt64());
        Assert.Equal("manual", choice.GetProperty("assetId").GetString());
        Assert.Equal(ArtworkStateSource.Image(CoverKey.Igdb("manual")), await host.Image(key));
        await host.Fixture.ChangeAsync("newprojection");
        Assert.Equal(saved, await host.State(1));
        Assert.Equal(key, (await host.State(2)).Current!.PreviewKey);
        using var reopened = WinnowApiClient.Attach(host.Directory);
        var other = new ApiArtworkBrowserService(reopened);
        Assert.Equal(key, (await other.GetCurrentAsync(1, ArtworkSlot.Cover))!.PreviewKey);
        Assert.True((await other.ResetAsync(1, ArtworkSlot.Cover)).Success);
        Assert.Equal(canonical, await host.State(1));
        Assert.Equal(CoverKey.Igdb("newprojection"), (await host.State(2)).Current!.PreviewKey);
        Assert.Empty((await host.Fixture.StateAsync()).AsJson().GetProperty("choices").EnumerateArray());
        await host.Fixture.ChangeAsync("steam440");
        Assert.Equal(CoverKey.Steam("440"), (await host.State(2)).Current!.PreviewKey);
        Assert.NotEqual(canonical.Revision, (await host.State(1)).Revision);
    }

    [Theory]
    [InlineData("igdb,steam,steamgriddb")]
    [InlineData("igdb,steamgriddb,steam")]
    public async Task HTTP_source_order_updates_the_shared_candidate_policy_and_survives_a_fresh_client(string order)
    {
        await using var host = await Host.StartAsync("live");
        Assert.Equal(new[] { CoverKey.SteamHero("42"), CoverKey.IgdbBackdrop("art"), CoverKey.SteamHeroStandard("42") }, (await host.Backdrop()).Candidates.Select(candidate => candidate.Key));
        await host.Api.SendAsync(HttpMethod.Put, "preferences/presentation/ArtworkSourceOrder", new SetPresentationPreference(order));
        Assert.Equal(CoverKey.IgdbBackdrop("art"), (await host.Backdrop()).Candidates[0].Key);
        Assert.Equal(order.Split(','), (await host.Fixture.StateAsync()).AsJson().GetProperty("sourceOrder").EnumerateArray().Select(item => item.GetString()));
        using var reopened = WinnowApiClient.Attach(host.Directory);
        var values = await reopened.GetAsync<PresentationPreferenceValue[]>("preferences/presentation");
        Assert.Equal(order, Assert.Single(values, value => value.Preference == PresentationPreference.ArtworkSourceOrder).Value);
    }

    [Fact]
    public async Task Unowned_group_root_background_precedes_child_Steam_and_grid_art_without_using_child_saved_background()
    {
        await using var host = await Host.StartAsync("root");
        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal(1, game.WorkId);
        Assert.Equal(2, Assert.Single(game.Entries).WorkId);
        var candidates = (await host.Backdrop()).Candidates.Select(candidate => candidate.Key).ToArray();
        Assert.Equal(new[] { CoverKey.User("rootsaved"), CoverKey.SteamHero("42"), SteamGridDbHeroUrl.Key(ArtworkStateFixture.GridUrl)!.Value, CoverKey.SteamHeroStandard("42") }, candidates);
        Assert.DoesNotContain(CoverKey.User("childsaved"), candidates);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync(ImageRoute(candidates[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync(ImageRoute(candidates[1]))).StatusCode);
        using var image = SKBitmap.Decode(await host.Image(candidates[2]));
        Assert.Equal((32, 10, SKColors.Teal), (image.Width, image.Height, image.GetPixel(0, 0)));
        Assert.DoesNotContain(host.Responses.Calls, call => call.Id == "childsaved");
    }

    [Fact]
    public async Task Installed_GOG_primary_keeps_the_known_Steam_hero_through_the_real_library_boundary()
    {
        await using var host = await Host.StartAsync("grouped");
        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal(new[] { 1L, 2L }, game.Entries.Select(entry => entry.OwnershipId));
        var installed = Assert.Single(game.Entries, entry => entry.Installed);
        Assert.Equal("gog", installed.Store);
        Assert.Equal(1, installed.ReleaseId);
        Assert.Equal(new[] { CoverKey.SteamHero("42"), CoverKey.SteamHeroStandard("42") }, (await host.Backdrop()).Candidates.Select(candidate => candidate.Key));
    }

    [Fact]
    public async Task Repeated_Steam42_repository_observations_produce_one_hero_while_GOG_stays_installed_primary()
    {
        await using var host = await Host.StartAsync("grouped", repeatSteamIds: true);
        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal("gog", Assert.Single(game.Entries, entry => entry.Installed).Store);
        Assert.Equal(new[] { "42", "42" }, (await host.Releases.GetExternalIdsAsync(2)).Select(id => id.ProviderId));
        Assert.Equal(new[] { CoverKey.SteamHero("42"), CoverKey.SteamHeroStandard("42") }, (await host.Backdrop()).Candidates.Select(candidate => candidate.Key));
    }

    [Fact]
    public async Task Immutable_replacement_image_keeps_source_dimensions_and_records_canceled_late_delivery()
    {
        await using var host = await Host.StartAsync("live");
        using (var old = SKBitmap.Decode(await host.Image(CoverKey.SteamHero("42"))))
            Assert.Equal((32, 10, SKColors.Teal), (old.Width, old.Height, old.GetPixel(0, 0)));
        var gate = host.Responses.Arm(new("image", Provider: "igdb-backdrop", Id: "art")).AsJson().GetProperty("gateId").GetString();
        using var cancellation = new CancellationTokenSource();
        var pending = host.Http.GetByteArrayAsync(ImageRoute(CoverKey.IgdbBackdrop("art")), cancellation.Token);
        await WaitAsync(() => host.Responses.Calls.Any(call => call.GateId == gate && call.ResponseReady));
        var call = Assert.Single(host.Responses.Calls, item => item.GateId == gate);
        Assert.False(call.Completed);
        Assert.Equal(200, call.StatusCode);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await WaitAsync(() => call.Canceled);
        host.Responses.Release(gate);
        await WaitAsync(() => call.Completed);
        Assert.False(call.Delivered);
        using var fresh = SKBitmap.Decode(await host.Image(CoverKey.IgdbBackdrop("art")));
        Assert.Equal((32, 18, SKColors.MediumPurple), (fresh.Width, fresh.Height, fresh.GetPixel(0, 0)));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await host.Image(CoverKey.IgdbBackdrop("art")))), call.ResponseSha256);
    }

    private static string ImageRoute(CoverKey key) => $"api/v1/artwork/image?provider={key.Provider}&id={key.Id}&width=160";
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
        public ArtworkStateFixture Fixture => app.Services.GetRequiredService<ArtworkStateFixture>();
        public PreviewResponseControls Responses => app.Services.GetRequiredService<PreviewResponseControls>();
        public IReleaseRepository Releases => app.Services.GetRequiredService<IReleaseRepository>();
        public Task<ArtworkState> State(long work) => api.GetAsync<ArtworkState>($"works/{work}/artwork/Cover");
        public Task<BackdropArtwork> Backdrop() => api.GetAsync<BackdropArtwork>("works/1/backdrop?aspectRatio=1.7777777777777777");
        public Task<byte[]> Image(CoverKey key) => http.GetByteArrayAsync(ImageRoute(key));
        public static async Task<Host> StartAsync(string kind, bool repeatSteamIds = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-artwork-state-parity", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services =>
            {
                ArtworkStateFixture.Register(services);
                if (repeatSteamIds) services.AddSingleton<IReleaseRepository>(provider =>
                    new RepeatedSteamIds(new ReleaseRepository(provider.GetRequiredService<ISqliteConnectionFactory>())));
            });
            ArtworkStateFixture.Map(app);
            await app.StartAsync();
            await app.Services.GetRequiredService<ArtworkStateFixture>().SeedAsync(new(kind));
            var connection = await BackendConnection.ReadAsync(directory);
            var http = new HttpClient { BaseAddress = new(connection.Address) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
            return new(directory, app, WinnowApiClient.Attach(directory), http);
        }
        public async ValueTask DisposeAsync()
        {
            Responses.Release(); api.Dispose(); http.Dispose();
            await app.StopAsync(); await app.DisposeAsync();
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    // SQLite rejects duplicate release/store ownerships. Repeat only the source observation
    // at the repository boundary; the actual service must still deduplicate its result.
    private sealed class RepeatedSteamIds(IReleaseRepository inner) : IReleaseRepository
    {
        public Task<long> InsertAsync(Release release, CancellationToken ct = default) => inner.InsertAsync(release, ct);
        public Task UpdateNameAsync(long id, string name, CancellationToken ct = default) => inner.UpdateNameAsync(id, name, ct);
        public Task<Release?> GetAsync(long id, CancellationToken ct = default) => inner.GetAsync(id, ct);
        public Task<IReadOnlyList<Release>> GetByWorkAsync(long workId, CancellationToken ct = default) => inner.GetByWorkAsync(workId, ct);
        public Task AddExternalIdAsync(ExternalId externalId, CancellationToken ct = default) => inner.AddExternalIdAsync(externalId, ct);
        public async Task<IReadOnlyList<ExternalId>> GetExternalIdsAsync(long releaseId, CancellationToken ct = default)
        {
            var ids = await inner.GetExternalIdsAsync(releaseId, ct);
            return ids.Concat(ids.Where(id => id.Provider == "steam")).ToArray();
        }
        public Task<IReadOnlyList<ExternalId>> GetAllExternalIdsAsync(CancellationToken ct = default) => inner.GetAllExternalIdsAsync(ct);
        public Task<Release?> FindByExternalIdAsync(string provider, string providerId, CancellationToken ct = default) => inner.FindByExternalIdAsync(provider, providerId, ct);
        public Task<IReadOnlyList<Winnow.Core.Queries.ReleaseIdentity>> GetIdentitiesAsync(CancellationToken ct = default) => inner.GetIdentitiesAsync(ct);
    }
}

internal static class ArtworkStateFixtureJson
{
    public static JsonElement AsJson(this object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
