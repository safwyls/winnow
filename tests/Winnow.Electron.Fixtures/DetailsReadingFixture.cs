using System.Collections.Concurrent;
using Dapper;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;

namespace Winnow.Electron.Fixtures;

// Source UI facts enter through real repositories; only external search answers
// are supplied locally. Details, account selection and publication stay production.
internal sealed class DetailsReadingFixture(ISqliteConnectionFactory database, LibraryChangePublisher publisher,
    DetailsReadingIgdbClient igdb, PluginActionShellGuard shell)
{
    public async Task<object> SeedAsync(string kind, int state)
    {
        if (kind is not ("sparse" or "long" or "gallery" or "achievements" or "prose") || state is < 0 or > 4)
            throw new ArgumentException("Unknown Details reading fixture.");
        using (var check = database.Open())
            if (check.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
                throw new InvalidOperationException("Details reading requires an empty fixture library.");
        var title = kind switch
        {
            "achievements" => "Achievement fixture",
            "long" => "A game with a long description",
            "prose" => "Fullscreen prose fixture",
            _ => "An unplayed game",
        };
        var summary = kind switch
        {
            "long" => string.Join("\n\n", Enumerable.Repeat(
                "This paragraph explains the places to explore and the choices to make in the game.", 40)),
            "prose" => string.Join(' ', Enumerable.Repeat(
                "Readable television paragraphs use the available layout width.", 8)),
            _ => null,
        };
        var work = await new WorkRepository(database).InsertAsync(new Work { Name = title, Summary = summary });
        var release = await new ReleaseRepository(database).InsertAsync(new Release { WorkId = work, Name = title });
        await new OwnershipRepository(database).InsertAsync(new Ownership { ReleaseId = release, Store = "steam" });
        if (kind == "achievements")
        {
            await new SettingsRepository(database).SetAsync(SteamOwnedAccount.RefSettingKey, "12345");
            var now = DateTime.UtcNow;
            var repository = new AchievementRepository(database);
            var known = new AchievementFetch
            {
                AttemptedAt = now, Schema = [new("A", "First", null, false)],
                Unlocks = new Dictionary<string, DateTime?>(),
            };
            if (state == 1) await repository.SaveAsync(release, "12345", known with { Schema = [], Unlocks = null });
            if (state == 2) await repository.SaveAsync(release, "12345", known);
            if (state == 3) await repository.SaveAsync(release, "12345", new AchievementFetch { AttemptedAt = now });
            if (state == 4)
            {
                await repository.SaveAsync(release, "12345", known with
                    { Unlocks = new Dictionary<string, DateTime?> { ["A"] = null } });
                await repository.SaveAsync(release, "12345", new AchievementFetch { AttemptedAt = now.AddSeconds(1) });
            }
        }
        if (kind == "gallery")
            await new WorkImageRepository(database).UpsertAsync(new WorkImages
            {
                WorkId = work, Source = ImageSources.Igdb, Kind = ImageKinds.Screenshot,
                ImageIds = string.Join(',', Enumerable.Range(1, 5).Select(index => "detailshot" + index)),
                ObservedAt = DateTime.UtcNow,
            });
        await publisher.PublishAsync(default);
        return new { WorkId = work };
    }

    public object Snapshot()
    {
        using var read = database.Open();
        var tables = new[] { "works", "releases", "ownerships", "achievements", "account_achievement_unlocks",
            "achievement_observations", "work_igdb_pins", "work_field_sources", "work_images" };
        return new
        {
            Searches = igdb.Searches.ToArray(), MetadataRequests = igdb.MetadataRequests.ToArray(),
            OsDispatchAttempts = shell.Attempts.ToArray(),
            Rows = tables.ToDictionary(table => table, table => read.Query("SELECT * FROM " + table).ToArray()),
        };
    }
}

internal sealed class DetailsReadingIgdbClient : IIgdbClient
{
    public ConcurrentQueue<string> Searches { get; } = new();
    public ConcurrentQueue<long[]> MetadataRequests { get; } = new();
    public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
    public Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(string title, int limit = 0,
        TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        Searches.Enqueue(title);
        return Task.FromResult<IReadOnlyList<IgdbSearchResult>>(Enumerable.Range(1, 5).Select(index =>
            new IgdbSearchResult(index, $"The Astral Cartographers {index}", null, 2019 + index,
                ["PC (Microsoft Windows)", "PlayStation 5"])).ToArray());
    }
    public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(IEnumerable<long> igdbIds,
        TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        MetadataRequests.Enqueue(igdbIds.ToArray());
        return Task.FromResult<IReadOnlyList<IgdbGame>>([]);
    }
    public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(IEnumerable<string> appIds,
        TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(int externalGameSourceId,
        IEnumerable<string> uids, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(IEnumerable<long> igdbIds,
        TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
}
