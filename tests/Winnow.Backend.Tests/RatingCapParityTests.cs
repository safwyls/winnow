using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class RatingCapParityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pretty tame please")]
    public async Task Missing_or_unreadable_caps_load_as_no_cap_without_writing_back(string? stored)
    {
        await using var fixture = await Fixture.Start();
        if (stored is not null)
            await fixture.Settings.SetAsync(BucketThresholds.MaturityCapSettingKey, stored);
        var preferences = await fixture.Api.GetAsync<LibraryPreferences>("preferences/library");
        Assert.Equal("adults_only", preferences.MaturityCap);
        Assert.False(preferences.ShowExplicitContent);
        Assert.Equal(stored, await fixture.Settings.GetAsync(BucketThresholds.MaturityCapSettingKey));
    }

    [Fact]
    public async Task Saving_a_cap_changes_the_visible_library_and_counts_only_its_own_exclusions()
    {
        await using var fixture = await Fixture.Start();
        await fixture.Seed("Nameless Game", MaturityRatingCodes.EsrbAdultsOnly);
        await fixture.Seed("Outer Wilds", MaturityRatingCodes.Pegi18);
        await fixture.Seed("Unrated game", null);
        Assert.Equal(new[] { "Outer Wilds", "Unrated game" }, (await fixture.Api.GetLibraryAsync()).Games.Select(game => game.Title).Order());
        Assert.Equal(0, (await fixture.Counts()).RatingCapHidden);
        await fixture.Save(new(false, false, "teen"));
        Assert.Equal("teen", await fixture.Settings.GetAsync(BucketThresholds.MaturityCapSettingKey));
        Assert.Equal("Unrated game", Assert.Single((await fixture.Api.GetLibraryAsync()).Games).Title);
        Assert.Equal(1, (await fixture.Counts()).RatingCapHidden);
        await fixture.Save(new(false, true, "teen"));
        Assert.Equal(2, (await fixture.Counts()).RatingCapHidden);
        Assert.Equal("Unrated game", Assert.Single((await fixture.Api.GetLibraryAsync()).Games).Title);
    }

    [Fact]
    public async Task A_saved_cap_survives_backend_restart_and_every_tier_round_trips()
    {
        await using var fixture = await Fixture.Start();
        foreach (var tier in MaturityTiers.Ordered)
        {
            var token = MaturityTiers.Token(tier);
            await fixture.Save(new(false, false, token));
            Assert.Equal(token, (await fixture.Api.GetAsync<LibraryPreferences>("preferences/library")).MaturityCap);
        }
        await fixture.Save(new(false, false, "mature"));
        await fixture.Restart();
        Assert.Equal("mature", (await fixture.Api.GetAsync<LibraryPreferences>("preferences/library")).MaturityCap);
    }

    [Fact]
    public async Task The_adult_toggle_and_cap_read_the_same_saved_rows_without_conflating_eighteen_plus()
    {
        await using var fixture = await Fixture.Start();
        await fixture.Seed("Adults only", MaturityRatingCodes.EsrbAdultsOnly);
        await fixture.Seed("18 plus", MaturityRatingCodes.Pegi18);
        var initial = await fixture.Api.GetAsync<LibraryPreferences>("preferences/library");
        Assert.False(initial.ShowExplicitContent);
        Assert.Equal("18 plus", Assert.Single((await fixture.Api.GetLibraryAsync()).Games).Title);
        await fixture.Settings.SetAsync(BucketThresholds.ShowExplicitContentSettingKey, "true");
        Assert.True((await fixture.Api.GetAsync<LibraryPreferences>("preferences/library")).ShowExplicitContent);
        Assert.Equal(2, (await fixture.Api.GetLibraryAsync()).Games.Count);
        await fixture.Save(new(false, true, "restricted18"));
        Assert.Equal("18 plus", Assert.Single((await fixture.Api.GetLibraryAsync()).Games).Title);
        Assert.Equal(1, (await fixture.Counts()).RatingCapHidden);
    }

    private sealed class Fixture(string directory, WebApplication app, WinnowApiClient api) : IAsyncDisposable
    {
        private WebApplication _app = app;
        public WinnowApiClient Api { get; private set; } = api;
        public ISettingsRepository Settings => _app.Services.GetRequiredService<ISettingsRepository>();
        public Task Save(LibraryPreferences preferences) => Api.SendAsync(HttpMethod.Put, "preferences/library", preferences);
        public Task<VisibilityCountsResponse> Counts() => Api.GetAsync<VisibilityCountsResponse>("library/visibility-counts");
        public static async Task<Fixture> Start()
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-rating-cap", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            try
            {
                await app.StartAsync();
                return new(directory, app, WinnowApiClient.Attach(directory));
            }
            catch
            {
                await app.DisposeAsync();
                Directory.Delete(directory, true);
                throw;
            }
        }
        public async Task Seed(string title, string? rating)
        {
            var work = await _app.Services.GetRequiredService<IWorkRepository>().InsertAsync(new Work { Name = title });
            var release = await _app.Services.GetRequiredService<IReleaseRepository>().InsertAsync(new Release { WorkId = work, Name = title });
            await _app.Services.GetRequiredService<IOwnershipRepository>().InsertAsync(new Ownership { ReleaseId = release, Store = "manual" });
            if (rating is not null)
                await _app.Services.GetRequiredService<IWorkMaturityRepository>().UpsertAsync(new WorkMaturity
                { WorkId = work, Source = MaturitySources.Igdb, Ratings = rating, ObservedAt = DateTime.UtcNow });
        }
        public async Task Restart()
        {
            Api.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await _app.StartAsync();
            Api = WinnowApiClient.Attach(directory);
        }
        public async ValueTask DisposeAsync()
        {
            Api.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }
}
