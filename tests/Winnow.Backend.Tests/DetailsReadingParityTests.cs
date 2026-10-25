using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class DetailsReadingParityTests
{
    [Theory]
    [InlineData(0, AchievementAvailability.Unknown, 0, 0, false, false, null)]
    [InlineData(1, AchievementAvailability.NoSchema, 0, 0, false, false, null)]
    [InlineData(2, AchievementAvailability.Available, 1, 0, true, false, 0d)]
    [InlineData(3, AchievementAvailability.Unavailable, 0, 0, false, false, null)]
    [InlineData(4, AchievementAvailability.Unavailable, 1, 1, true, true, 100d)]
    public async Task Exact_ingested_achievement_states_reach_Details_without_inventing_progress_over_HTTP(
        int state, AchievementAvailability availability, int total, int unlocked, bool known, bool stale, double? percentage)
    {
        await using var host = await Fixture.StartAsync();
        var database = host.Get<ISqliteConnectionFactory>();
        using (var seed = database.Open()) seed.Execute("""
            INSERT INTO works(id,name,sort_name) VALUES(1,'Achievement fixture','Achievement fixture');
            INSERT INTO releases(id,work_id,name) VALUES(1,1,'Achievement fixture');
            INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0);
            """);
        await host.Get<ISettingsRepository>().SetAsync(SteamOwnedAccount.RefSettingKey, "12345");
        var now = DateTime.UtcNow;
        var repository = new AchievementRepository(database);
        var complete = new AchievementFetch
        {
            AttemptedAt = now, Schema = [new("A", "First", null, false)],
            Unlocks = new Dictionary<string, DateTime?>(),
        };
        if (state == 1) await repository.SaveAsync(1, "12345", complete with { Schema = [], Unlocks = null });
        if (state == 2) await repository.SaveAsync(1, "12345", complete);
        if (state == 3) await repository.SaveAsync(1, "12345", new AchievementFetch { AttemptedAt = now });
        if (state == 4)
        {
            await repository.SaveAsync(1, "12345", complete with
                { Unlocks = new Dictionary<string, DateTime?> { ["A"] = null } });
            await repository.SaveAsync(1, "12345", new AchievementFetch { AttemptedAt = now.AddSeconds(1) });
        }

        var details = await host.Details.GetAsync(1);
        var summary = Assert.Single(details.Achievements);
        Assert.Equal(1, details.WorkId);
        Assert.Equal(1, summary.ReleaseId);
        Assert.Equal("12345", summary.AccountRef);
        Assert.Equal(availability, summary.Availability);
        Assert.Equal(total, summary.Total);
        Assert.Equal(unlocked, summary.Unlocked);
        Assert.Equal(known, summary.HasKnownProgress);
        Assert.Equal(stale, summary.IsStale);
        Assert.Equal(percentage, summary.PercentComplete);
        Assert.Equal(total != 0, summary.HasAny);
        Assert.Equal(known ? now : (DateTime?)null, summary.ObservedAt);
        Assert.Equal(state is 1 or 2 or 4 ? now : (DateTime?)null, summary.SchemaObservedAt);
        Assert.Equal(state == 0 ? null : state == 4 ? now.AddSeconds(1) : now, summary.LastAttemptAt);
        Assert.Null(summary.GlobalObservedAt);
        Assert.Equal(Assert.Single(await repository.GetForAccountAsync([1], "12345", now.AddSeconds(1))), summary);

        // A read from another account must not adopt the first account's retained
        // unlocks; selecting the original account again exposes the original facts.
        await host.Get<ISettingsRepository>().SetAsync(SteamOwnedAccount.RefSettingKey, "67890");
        var other = Assert.Single((await host.Details.GetAsync(1)).Achievements);
        Assert.Equal("67890", other.AccountRef);
        Assert.Equal(AchievementAvailability.Unknown, other.Availability);
        Assert.Equal(total, other.Total);
        Assert.Equal(0, other.Unlocked);
        Assert.False(other.HasKnownProgress);
        Assert.False(other.IsStale);
        Assert.Null(other.PercentComplete);
        Assert.Null(other.ObservedAt);
        await host.Get<ISettingsRepository>().SetAsync(SteamOwnedAccount.RefSettingKey, "12345");
        Assert.Equal(summary, Assert.Single((await host.Details.GetAsync(1)).Achievements));

        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal("Achievement fixture", game.Title);
        Assert.Equal(0, game.PlaytimeMinutes);
        Assert.Equal((1L, 1L, "steam"), (game.WorkId, Assert.Single(game.Entries).ReleaseId, game.Entries[0].Store));
        using var read = database.Open();
        var schema = read.Query<(string Key, string Name, string? Description, bool Hidden)>(
            "SELECT provider_key,name,description,hidden FROM achievements;").ToArray();
        if (total == 1) Assert.Equal(("A", "First", (string?)null, false), Assert.Single(schema));
        else Assert.Empty(schema);
    }

    private sealed class Fixture(string directory, WebApplication app, WinnowApiClient api) : IAsyncDisposable
    {
        public WinnowApiClient Api => api;
        public DetailsClient Details { get; } = new(api);
        public T Get<T>() where T : notnull => app.Services.GetRequiredService<T>();
        public static async Task<Fixture> StartAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-details-reading-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await app.StartAsync();
            return new(directory, app, WinnowApiClient.Attach(directory));
        }
        public async ValueTask DisposeAsync()
        {
            api.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
