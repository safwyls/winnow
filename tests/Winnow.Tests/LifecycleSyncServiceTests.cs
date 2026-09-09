using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Steam;
using Winnow.Tests.Igdb;
using Xunit;

namespace Winnow.Tests;

public sealed class LifecycleSyncServiceTests
{
    private sealed class IgdbStub(IgdbTestClock clock) : IIgdbLifecycleClient
    {
        public List<long> Calls { get; } = [];
        public bool Fail { get; set; }
        public DateTime ObservedAt { get; } = clock.GetUtcNow().UtcDateTime;
        public Task<IgdbLifecycleSnapshot?> GetAsync(long gameId, CancellationToken ct = default)
        {
            Calls.Add(gameId);
            if (Fail) throw new HttpRequestException();
            return Task.FromResult<IgdbLifecycleSnapshot?>(new(ObservedAt, new() { IgdbStatus = "offline" }, "{}"));
        }
    }
    private sealed class SteamStub : ISteamLifecycleClient
    {
        public Task<IReadOnlyList<SteamLifecycleSnapshot>> GetAsync(string appId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SteamLifecycleSnapshot>>([]);
    }

    [Fact]
    public async Task Batch_cap_and_persisted_attempts_prevent_failed_titles_starving_others()
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        for (var i = 1; i <= 51; i++)
        {
            var work = await works.InsertAsync(new Work { Name = "Game " + i, IgdbId = i });
            await releases.InsertAsync(new Release { WorkId = work, Name = "Game " + i });
        }
        var clock = new IgdbTestClock(new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var client = new IgdbStub(clock) { Fail = true };
        LifecycleSyncService Service() => new(new LibraryQueryRepository(db.Factory), new LifecycleRepository(db.Factory),
            new SettingsRepository(db.Factory), client, new SteamStub(), clock, NullLogger<LifecycleSyncService>.Instance);
        Assert.Equal(0, await Service().SyncAsync());
        Assert.Equal(50, client.Calls.Count);
        await Service().SyncAsync();
        Assert.Equal(51, client.Calls.Count);
        Assert.Equal(51, client.Calls.Distinct().Count());
    }

    [Fact]
    public async Task Cached_observation_is_not_duplicated_or_given_a_new_time_next_day()
    {
        using var db = new TempDatabase();
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = "Game", IgdbId = 42 });
        var release = await new ReleaseRepository(db.Factory).InsertAsync(new Release { WorkId = work, Name = "Game" });
        var clock = new IgdbTestClock(new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var client = new IgdbStub(clock);
        var repository = new LifecycleRepository(db.Factory);
        var service = new LifecycleSyncService(new LibraryQueryRepository(db.Factory), repository,
            new SettingsRepository(db.Factory), client, new SteamStub(), clock, NullLogger<LifecycleSyncService>.Instance);
        Assert.Equal(1, await service.SyncAsync());
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await service.SyncAsync());
        Assert.Equal(client.ObservedAt, Assert.Single(await repository.GetForReleaseAsync(release)).ObservedAt);
    }
}
